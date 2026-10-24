using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Coclico.Installer;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Portage de App.xaml.cs original :
        //   --uninstall  -> UninstallWindow
        //   --silent     -> géré AVANT le démarrage Avalonia dans Program.cs (headless)
        //   défaut       -> MainWindow (assistant 7 étapes)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Window main;
            try
            {
                main = Program.IsUninstallMode ? new Views.UninstallWindow() : new MainWindow();
            }
            catch (Exception ex)
            {
                Program.LogCrash(ex);
                Environment.Exit(1);
                return;
            }

            desktop.MainWindow = main;
            main.Show();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
