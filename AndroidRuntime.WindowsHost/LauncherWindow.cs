#nullable enable
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AndroidRuntime.Core.Hosting;

namespace AndroidRuntime.WindowsHost;

/// <summary>
/// Minimal package-manager launcher (docs\installer-launcher-design.md): lists
/// installed apps (open / uninstall) and installs new .apk files. Double-click
/// the host executable with no arguments to open this window. "Open" spawns a
/// fresh host process via --launch &lt;package&gt; so a crashed app never takes the
/// launcher down.
/// </summary>
public sealed class LauncherWindow : Window
{
    private readonly ListBox _list;

    public LauncherWindow()
    {
        Title = "Android Runtime — Launcher";
        Width = 420;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new TextBlock
        {
            Text = "Installed apps",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        _list = new ListBox();
        Grid.SetRow(_list, 1);
        grid.Children.Add(_list);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var installButton = new Button { Content = "Install APK…", Width = 110, Margin = new Thickness(0, 0, 6, 0) };
        installButton.Click += (_, _) => InstallApk();
        var openButton = new Button { Content = "Open", Width = 80, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        openButton.Click += (_, _) => OpenSelected();
        var uninstallButton = new Button { Content = "Uninstall", Width = 90 };
        uninstallButton.Click += (_, _) => UninstallSelected();
        buttons.Children.Add(installButton);
        buttons.Children.Add(openButton);
        buttons.Children.Add(uninstallButton);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
        Refresh();
    }

    private void Refresh()
    {
        _list.Items.Clear();
        foreach (string package in AndroidInstaller.ListInstalled())
            _list.Items.Add(package);
    }

    private void InstallApk()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an APK to install",
            Filter = "Android packages (*.apk)|*.apk|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) != true)
            return;
        try
        {
            InstalledApp app = AndroidInstaller.Install(dialog.FileName);
            FileAssociation.Register();
            MessageBox.Show(this, $"Installed {app.Package}.\nLauncher: {app.LauncherFilePath}", "Android Runtime", MessageBoxButton.OK, MessageBoxImage.Information);
            Refresh();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "Install failed:\n" + error.Message, "Android Runtime", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OpenSelected()
    {
        if (_list.SelectedItem is not string package)
            return;
        string exe = Environment.ProcessPath ?? throw new InvalidOperationException("Unable to resolve the host executable path.");
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, ArgumentList = { "--launch", package } });
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "Launch failed:\n" + error.Message, "Android Runtime", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallSelected()
    {
        if (_list.SelectedItem is not string package)
            return;
        if (MessageBox.Show(this, $"Uninstall {package}? This removes the app and its data.", "Android Runtime", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        try
        {
            AndroidInstaller.Uninstall(package);
            Refresh();
        }
        catch (Exception error)
        {
            MessageBox.Show(this, "Uninstall failed:\n" + error.Message, "Android Runtime", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
