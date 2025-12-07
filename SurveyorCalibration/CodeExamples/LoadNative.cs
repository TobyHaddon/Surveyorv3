using Microsoft.UI.Xaml;

public static Window? MainWindow { get; private set; }

/// <summary>
/// Initializes the singleton application object.  This is the first line of authored code
/// executed, and as such is the logical equivalent of main() or WinMain().
/// </summary>
public App()
{
    this.InitializeComponent();
}
