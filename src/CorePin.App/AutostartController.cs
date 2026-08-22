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
    AutostartMode Mode, RunKeyState RunKey, bool OwnTask,
    bool TaskEnabled, string? TaskExePath, long ReadMs)
{
    internal bool On => Mode != AutostartMode.Off;
}

/// Windows is the truth: every change reads it back, mirrors the mode and logs the state.
internal sealed class AutostartController
{
    private const string Category = "app";

    private readonly IRunKeyAutostart _runKey;
    private readonly Func<IAutostartTask> _taskFactory;
    private readonly ILog _log;
    private readonly string? _exePath;
    private readonly string _userName;
    private readonly string? _userSid;
    private readonly bool _isElevated;
    private readonly Action<string> _mirror;

    private IAutostartTask? _task;
    private bool _foreignTaskReported;

    internal AutostartController(
        IRunKeyAutostart runKey, Func<IAutostartTask> taskFactory, ILog log,
        string? exePath, string userName, string? userSid,
        bool isElevated, Action<string> mirror)
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
        _mirror = mirror;
    }

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

        return new AutostartSnapshot(AutostartTruth.Resolve(runKey, task, OwnSid), runKey, ownTask,
                                     task.Enabled, task.ExePath, watch.ElapsedMilliseconds);
    }

    /// Runs behind the tray icon: the first late binding must not delay the start.
    internal void Reconcile()
    {
        try
        {
            var snapshot = Read();

            // Only an enabled task supersedes the run key — next to a disabled one it starts CorePin.
            if (snapshot.RunKey.Value is not null && snapshot.OwnTask && snapshot.TaskEnabled
                && TryRemoveRunKey())
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

    /// The tick is a switch: selecting the entry always means the opposite of what it showed.
    internal void Toggle(AutostartSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        try
        {
            var choice = AutostartTruth.Switch(snapshot.Mode, _isElevated);
            if (choice == AutostartSwitch.RefuseNeedsAdmin)
            {
                RefuseNeedsAdmin();
                return;
            }

            Apply(snapshot, choice);
            Publish(Read());
        }
        catch (Exception ex)
        {
            Fail(AutostartTexts.DescribeFailure(ex));
        }
    }

    /// The task step runs first, the run key step only on its success; nothing is ever taken back.
    private void Apply(AutostartSnapshot snapshot, AutostartSwitch choice)
    {
        switch (choice)
        {
            case AutostartSwitch.TurnOnRunKey:
                if (ExePathOrFail() is { } exePath) TryWriteRunKey(exePath);
                break;
            case AutostartSwitch.TurnOnTask:
                if (RegisterTask()) TryRemoveRunKey();
                break;
            case AutostartSwitch.TurnOff:
                // Only the elevated instance may delete the task; the normal one owns the run key alone.
                if (_isElevated && snapshot.OwnTask && !TryTask(task => task.Delete())) break;
                TryRemoveRunKey();
                break;
            default:
                throw new UnreachableException();
        }
    }

    /// Reads the task fresh: a menu snapshot seconds old could hide a foreign task of the same name.
    private bool RegisterTask()
    {
        var task = ReadTask();
        if (task.Present && !AutostartTruth.IsOwnTask(task, OwnSid))
        {
            Fail(AutostartTexts.TaskBelongsToAnotherUser);
            return false;
        }

        return ExePathOrFail() is { } exePath && TryRegisterOwnTask(exePath);
    }

    /// Registers unconditionally: the caller already confirmed ownership, from this reading or a fresh one.
    private bool TryRegisterOwnTask(string exePath)
    {
        if (_userSid is not { } sid)
        {
            Fail(AutostartTexts.NoSid);
            return false;
        }

        return TryTask(task => task.Register(exePath, _userName, sid));
    }

    /// Nothing was changed, so nothing is read back, mirrored or reported as a new state.
    private void RefuseNeedsAdmin()
    {
        _log.Warning(Category,
            "autostart task needs administrator rights to be removed, left unchanged");
        MessageBoxes.ShowAutostartFailed(AutostartTexts.NeedsAdmin);
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
                "autostart task points to a missing file, start CorePin as administrator to repair");
            return;
        }

        if (TryRegisterOwnTask(exePath))
            _log.Information(Category, "autostart task updated to the current location");
    }

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
            Fail(AutostartTexts.DescribeFailure(ex));
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

        Fail(AutostartTexts.NoExePath);
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
