namespace G0Theming;

/// Weg B: own dictionaries only, no ThemeMode anywhere.
public partial class MainWindow : DemoWindow
{
    public MainWindow(RunOptions options, ThemeController theme)
        : base(options, theme)
    {
        InitializeComponent();
        AttachPanel(DemoContent);
    }
}

