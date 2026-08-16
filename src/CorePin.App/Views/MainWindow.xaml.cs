using System.Windows;
using CorePin.App.Themes;

namespace CorePin.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(ThemeController theme)
    {
        InitializeComponent();
        theme.RegisterWindow(this);   // colour the title bar as soon as the HWND exists (S03 §6.4)
    }
}
