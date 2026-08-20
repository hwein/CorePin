using System.ComponentModel;

namespace CorePin.App.Views;

/// One line of the picker; Icon and ResolvedPath arrive late from the loader. UI thread only.
public sealed class FlyoutProcessRow(int pid, string exeName) : INotifyPropertyChanged
{
    private object? _icon;
    private string? _resolvedPath;
    private bool _resolved;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Pid { get; } = pid;

    public string ExeName { get; } = exeName;

    /// A BitmapSource once loaded; null keeps the placeholder via the binding's TargetNullValue.
    public object? Icon
    {
        get => _icon;
        set
        {
            if (ReferenceEquals(_icon, value)) return;

            _icon = value;
            Raise(nameof(Icon));
        }
    }

    public string? ResolvedPath
    {
        get => _resolvedPath;
        set
        {
            if (_resolvedPath == value) return;

            _resolvedPath = value;
            Raise(nameof(ResolvedPath));
        }
    }

    /// True once the loader callback has run, whether or not it found a path.
    public bool Resolved
    {
        get => _resolved;
        set
        {
            if (_resolved == value) return;

            _resolved = value;
            Raise(nameof(Resolved));
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
