using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;

namespace Clocteck.CubicCenter;

public partial class App : System.Windows.Application
{
    private Mutex? _instanceMutex;
    private NotifyIcon? _trayIcon;
    private MainWindow? _window;
    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _instanceMutex = new Mutex(true, "Clocteck.CubicCenter.Singleton", out var created);
        if (!created)
        {
            System.Windows.MessageBox.Show("Clocteck Cubic Center 已经在运行。", "Clocteck", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _window = new MainWindow();
        MainWindow = _window;
        CreateTrayIcon();
        _window.Show();
    }

    private void CreateTrayIcon()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开控制中心", null, (_, _) => ShowMainWindow());
        menu.Items.Add("打开设备控制页", null, (_, _) => _window?.OpenControlPage());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, async (_, _) => await ExitApplicationAsync());
        _trayIcon = new NotifyIcon
        {
            Text = "Clocteck Cubic Center",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu,
        };
        _trayIcon.DoubleClick += (_, _) => ShowMainWindow();
    }

    public void ShowMainWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public async Task ExitApplicationAsync()
    {
        if (IsExiting) return;
        IsExiting = true;
        _trayIcon?.Dispose();
        if (_window is not null) await _window.ShutdownAsync();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        Shutdown();
    }
}
