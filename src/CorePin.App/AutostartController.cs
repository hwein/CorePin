using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using CorePin.Core.Autostart;
using CorePin.Core.Diagnostics;
using CorePin.Core.Platform;
using CorePin.Interop;

namespace CorePin.App;

/// One reading of Windows: what the menu showed and what a selection then acts on.
internal sealed record AutostartSnapshot(
    TrayMenuState Menu, RunKeyState RunKey, bool OwnTask,
    bool TaskEnabled, string? TaskExePath, long ReadMs)
{
    internal AutostartMode Mode => Menu.Mode;
}

/// Windows is the truth: every change reads it back, mirrors the mode and logs the state.
internal sealed class AutostartController
{
    private const string Category = "app";
    private const int ErrorCancelled = 1223;

    private readonly IRunKeyAutostart _runKey;
    private readonly Func<IAutostartTask> _taskFactory;
    private readonly ILog _log;
    private readonly string? _exePath;
    private readonly string _userName;
    private readonly string? _userSid;
    private readonly bool _isElevated;
    private readonly bool _adminSelectable;
    private readonly Action<string> _mirror;

    private IAutostartTask? _task;
    private bool _foreignTaskReported;

    internal AutostartController(
        IRunKeyAutostart runKey, Func<IAutostartTask> taskFactory, ILog log,
        string? exePath, string userName, string? userSid,
        bool isElevated, bool canElevate, Action<string> mirror)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        ArgumentNullException.ThrowIfNull(taskFactory);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(mirror);

        _runKey = runKey;
        _taskFactory = taskFactory;
        _log = log;
        _exePath = exePath;
        _userName = userName;
        _userSid = userSid;
        _isElevated = isElevated;
        _adminSelectable = canElevate || isElevated;
        _mirror = mirror;
    }

    private enum StepResult { Done, Failed, Cancelled }

    /// The truth rule and the ownership check see the same string; no SID matches no task.
    private string OwnSid => _userSid ?? string.Empty;

    internal AutostartSnapshot Read()
    {
        var watch = Stopwatch.StartNew();
        var runKey = ReadRunKey();
        var task = ReadTask();
        watch.Stop();

        bool ownTask = AutostartTruth.IsOwnTask(task, OwnSid);
        if (task.Present && !ownTask) ReportForeignTask();

        var mode = AutostartTruth.Resolve(runKey, task, OwnSid);
        var menu = new TrayMenuState(mode, _adminSelectable, IsStale(mode, task.ExePath),
                                     runKey.Value is not null && runKey.Disabled);

        return new AutostartSnapshot(menu, runKey, ownTask,
                                     task.Enabled, task.ExePath, watch.ElapsedMilliseconds);
    }

    /// Runs behind the tray icon: the first late binding must not delay the start.
    internal void Reconcile()
    {
        try
        {
            var snapshot = Read();

            if (snapshot.RunKey.Value is not null && snapshot.OwnTask && TryRemoveRunKey())
            {
                snapshot = snapshot with { RunKey = new RunKeyState(null, false) };
                _log.Information(Category, "autostart: both mechanisms present, run key removed");
            }

            ReconcilePath(snapshot);
            Publish(snapshot);
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    internal void Select(AutostartSnapshot snapshot, AutostartMode selected)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var action = AutostartTruth.Target(snapshot.Mode, selected,
                                           snapshot.Menu.NormalDisabledInTaskManager,
                                           snapshot.Menu.AdminStale);
        if (action == AutostartAction.None) return;

        // Started, not awaited: ApplyAsync catches on its own so nothing can end the process.
        _ = ApplyAsync(snapshot, action);
    }

    private async Task ApplyAsync(AutostartSnapshot snapshot, AutostartAction action)
    {
        try
        {
            var step = await TaskStepAsync(snapshot, action);
            if (step == StepResult.Cancelled) return;

            if (step == StepResult.Done) RunKeyStep(action);
            Publish(Read());
        }
        catch (Exception ex)
        {
            Fail(AutostartHelper.DescribeFailure(ex));
        }
    }

    /// The step that may need elevation runs first; a failed one takes nothing back.
    private async Task<StepResult> TaskStepAsync(AutostartSnapshot snapshot, AutostartAction action)
    {
        if (action == AutostartAction.SetAdmin) return await CreateTask();
        if (!snapshot.OwnTask) return StepResult.Done;

        if (!_isElevated) return await RunHelper("delete");
        return TryTask(task => task.Delete()) ? StepResult.Done : StepResult.Failed;
    }

    /// Elevated in-process: reads the task fresh so a stale menu snapshot cannot hide a foreign one.
    private async Task<StepResult> CreateTask()
    {
        if (!_isElevated) return await RunHelper("create");

        var task = ReadTask();
        if (task.Present && !AutostartTruth.IsOwnTask(task, OwnSid))
        {
            Fail(AutostartHelper.TaskBelongsToAnotherUser);
            return StepResult.Failed;
        }

        if (ExePathOrFail() is not { } exePath) return StepResult.Failed;
        return TryRegister(exePath) ? StepResult.Done : StepResult.Failed;
    }

    private async Task<StepResult> RunHelper(string command)
    {
        if (ExePathOrFail() is not { } exePath) return StepResult.Failed;

        Process? helper;
        try
        {
            helper = Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = $"--autostart-task {command}",
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            _log.Information(Category, "autostart change cancelled at the UAC prompt");
            return StepResult.Cancelled;
        }
        catch (Win32Exception ex)
        {
            Fail($"elevation request: Win32 {ex.NativeErrorCode}");
            return StepResult.Failed;
        }

        if (helper is null)
        {
            Fail("The elevated helper did not start.");
            return StepResult.Failed;
        }

        using (helper)
        {
            await helper.WaitForExitAsync();
            return HelperResult(helper.ExitCode);
        }
    }

    private StepResult HelperResult(int exitCode)
    {
        if (exitCode == 0) return StepResult.Done;

        string detail = $"helper exit code {exitCode}";
        _log.Warning(Category, $"autostart change failed: {detail}");
        // The helper shows its own dialog before it returns Failed; any other code shows none.
        if (exitCode != AutostartHelper.Failed) MessageBoxes.ShowAutostartFailed(detail);
        return StepResult.Failed;
    }

    private void RunKeyStep(AutostartAction action)
    {
        if (action == AutostartAction.SetNormal)
        {
            if (ExePathOrFail() is { } exePath) TryWriteRunKey(exePath);
            return;
        }

        // Admin and Off both end without a run key, and removing a missing one is no error.
        TryRemoveRunKey();
    }

    private void ReconcilePath(AutostartSnapshot snapshot)
    {
        // Off writes nothing at all, or an entry the user removed would rise again on every start.
        if (snapshot.Mode == AutostartMode.Off) return;
        if (ExePathOrFail() is not { } exePath) return;

        if (snapshot.Mode == AutostartMode.Normal) ReconcileRunKeyPath(snapshot.RunKey.Value, exePath);
        else ReconcileTaskPath(snapshot.TaskExePath, exePath);
    }

    private void ReconcileRunKeyPath(string? value, string exePath)
    {
        if (RunKeyCommand.Matches(value, exePath)) return;

        if (File.Exists(RunKeyCommand.ParseExePath(value)))
        {
            _log.Information(Category, "autostart points to another copy of CorePin, left unchanged");
            return;
        }

        if (TryWriteRunKey(exePath))
            _log.Information(Category, "autostart run key updated to the current location");
    }

    private void ReconcileTaskPath(string? taskExePath, string exePath)
    {
        if (string.Equals(taskExePath, exePath, StringComparison.OrdinalIgnoreCase)) return;

        if (File.Exists(taskExePath))
        {
            _log.Information(Category, "autostart points to another copy of CorePin, left unchanged");
            return;
        }

        if (!_isElevated)
        {
            _log.Warning(Category,
                "autostart task points to a missing file, select it in the tray menu to repair");
            return;
        }

        if (TryRegister(exePath))
            _log.Information(Category, "autostart task updated to the current location");
    }

    /// A path that is merely different belongs to another copy; only a missing file needs repair.
    private bool IsStale(AutostartMode mode, string? taskExePath)
        => mode == AutostartMode.Admin
           && !(_exePath is { } own && string.Equals(taskExePath, own, StringComparison.OrdinalIgnoreCase))
           && !File.Exists(taskExePath);

    /// Late binding fails as RuntimeBinderException, the service as COM or IO errors — every
    /// one of them reads as "no task", and the next use builds the connection again.
    private TaskState ReadTask()
        => ReadSafely("task", () => ConnectTask().Read(), new TaskState(false, null, null, false),
                      () => _task = null);

    private RunKeyState ReadRunKey() => ReadSafely("run key", _runKey.Read, new RunKeyState(null, false));

    private T ReadSafely<T>(string what, Func<T> read, T fallback, Action? onFailure = null)
    {
        try
        {
            return read();
        }
        catch (Exception ex)
        {
            onFailure?.Invoke();
            _log.Warning(Category, $"autostart {what} could not be read: 0x{ex.HResult:X8} {ex.Message}");
            return fallback;
        }
    }

    private IAutostartTask ConnectTask() => _task ??= _taskFactory();

    private bool TryRegister(string exePath)
    {
        if (_userSid is not { } sid)
        {
            Fail(AutostartHelper.NoSid);
            return false;
        }

        return TryTask(task => task.Register(exePath, _userName, sid));
    }

    private bool TryTask(Action<IAutostartTask> change)
    {
        try
        {
            change(ConnectTask());
            return true;
        }
        catch (Exception ex)
        {
            _task = null;
            Fail(AutostartHelper.DescribeFailure(ex));
            return false;
        }
    }

    private bool TryWriteRunKey(string exePath) => TryRunKey(() => _runKey.Write(exePath), "write");

    private bool TryRemoveRunKey() => TryRunKey(_runKey.Remove, "remove");

    private bool TryRunKey(Action change, string what)
    {
        try
        {
            change();
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            Fail($"run key {what}: {ex.Message}");
            return false;
        }
    }

    private void Publish(AutostartSnapshot snapshot)
    {
        string mode = AutostartTruth.ModeName(snapshot.Mode);
        _mirror(mode);
        _log.Information(Category,
            $"autostart: {mode} (run key: {RunKeyWord(snapshot.RunKey)}, task: {TaskWord(snapshot)}, "
            + $"read {snapshot.ReadMs} ms)");
    }

    private static string RunKeyWord(RunKeyState runKey)
        => runKey.Value is null ? "absent" : runKey.Disabled ? "disabled" : "present";

    /// A task of another account is not ours to report on, so it reads as absent here as well.
    private static string TaskWord(AutostartSnapshot snapshot)
        => !snapshot.OwnTask ? "absent" : snapshot.TaskEnabled ? "present" : "disabled";

    private void ReportForeignTask()
    {
        if (_foreignTaskReported) return;

        _foreignTaskReported = true;
        _log.Warning(Category, "autostart task belongs to another user account, ignored");
    }

    private string? ExePathOrFail()
    {
        if (_exePath is { } path) return path;

        Fail(AutostartHelper.NoExePath);
        return null;
    }

    private void Fail(string detail)
    {
        _log.Warning(Category, $"autostart change failed: {detail}");
        MessageBoxes.ShowAutostartFailed(detail);
    }

    private void ReportUnexpected(Exception ex)
        => _log.Warning(Category, $"autostart change failed: {ex.GetType().Name}: {ex.Message}");
}
