using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using Clocteck.CubicCenter.Core;
using Microsoft.Web.WebView2.Core;

namespace Clocteck.CubicCenter;

public partial class MainWindow : Window
{
    private readonly AppController _controller = new();
    private readonly List<BrowserWindow> _browserWindows = [];
    private bool _initialized;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var webRoot = Path.Combine(AppContext.BaseDirectory, "Web");
            var userData = Path.Combine(AppContext.BaseDirectory, "data", "webview2");
            Directory.CreateDirectory(userData);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await WebView.EnsureCoreWebView2Async(environment);
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.clocteck.local",
                webRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(EmbeddedDeviceDarkThemeScript);
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            WebView.CoreWebView2.NavigationCompleted += (_, _) => LoadingOverlay.Visibility = Visibility.Collapsed;

            _controller.SendEventAsync = SendEventAsync;
            _controller.BrowserRequested += (_, url) => Dispatcher.Invoke(() => OpenBrowser(url));
            _controller.ExitRequested += async (_, _) => await ((App)System.Windows.Application.Current).ExitApplicationAsync();
            await _controller.InitializeAsync();
            WebView.CoreWebView2.Navigate("http://app.clocteck.local/index.html");
        }
        catch (Exception error)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            System.Windows.MessageBox.Show($"启动失败：{error.Message}", "Clocteck Cubic Center", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        await _controller.HandleCommandAsync(e.WebMessageAsJson);
    }

    private Task SendEventAsync(string type, object? payload)
    {
        return Dispatcher.InvokeAsync(async () =>
        {
            if (WebView.CoreWebView2 is null) return;
            var envelope = JsonSerializer.Serialize(new { type, payload }, JsonOptions);
            await WebView.CoreWebView2.ExecuteScriptAsync($"window.Cubic && window.Cubic.receive({envelope});");
        }).Task.Unwrap();
    }

    private void OpenBrowser(string url)
    {
        var existing = _browserWindows.FirstOrDefault(window => window.IsLoaded);
        if (existing is null)
        {
            existing = new BrowserWindow { Owner = this };
            existing.Closed += (_, _) => _browserWindows.Remove(existing);
            _browserWindows.Add(existing);
        }
        existing.Navigate(url);
        existing.Show();
        existing.Activate();
    }

    public void OpenControlPage() => _ = _controller.HandleCommandAsync("{\"action\":\"device.openControl\",\"payload\":{}}");

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (((App)System.Windows.Application.Current).IsExiting) return;
        e.Cancel = true;
        Hide();
    }

    public async Task ShutdownAsync()
    {
        foreach (var browser in _browserWindows.ToArray()) browser.Close();
        await _controller.DisposeAsync();
        Close();
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string EmbeddedDeviceDarkThemeScript = """
        (() => {
          const applyTheme = () => {
            if (window.top === window.self || !/^(?:\d{1,3}\.){3}\d{1,3}$/.test(location.hostname)) return;
            if (document.getElementById('cubic-center-dark-theme')) return;
            const style = document.createElement('style');
            style.id = 'cubic-center-dark-theme';
            style.textContent = `
              :root {
                color-scheme: dark !important;
                --bg: #070909 !important;
                --bg-soft: #0b0f0d !important;
                --surface: #101412 !important;
                --surface-soft: #151a17 !important;
                --surface-tint: #111613 !important;
                --panel: #101412 !important;
                --panel2: #151a17 !important;
                --line: #2b342e !important;
                --line-strong: #3a453e !important;
                --text: #edf3ef !important;
                --muted: #8c9991 !important;
                --accent: #c6fa38 !important;
                --accent-strong: #a9d75d !important;
                --accent-soft: #1a2117 !important;
                --blue: #9ccc3c !important;
                --blue-strong: #b9e264 !important;
                --blue2: #1a2117 !important;
                --blue-soft: #182018 !important;
                --green: #9ccc3c !important;
                --danger: #ff8383 !important;
                --danger-soft: #291719 !important;
                --success: #9ccc3c !important;
                --shadow-sm: 0 8px 20px rgba(0,0,0,.2) !important;
                --shadow: 0 16px 40px rgba(0,0,0,.28) !important;
              }
              html, body { background: #070909 !important; color: #edf3ef !important; }
              body { background-image: radial-gradient(circle at 55% -20%, rgba(113,162,55,.08), transparent 36%) !important; }
              .panel, .card, .box, .topbar, header, section, aside { border-color: #2b342e !important; }
              .panel, .card, .box, .hero-card, .wifi-card, .summary-item, .metric, .app-card, .wifi-field-box,
              .service-card, .store-app-card, .store-toolbar, .store-topbar, .store-hero-main, .store-hero-side-copy {
                border-color: #2b342e !important;
                background: linear-gradient(135deg,#101412,#151a17) !important;
                color: #edf3ef !important;
                box-shadow: 0 16px 40px rgba(0,0,0,.24) !important;
              }
              .app-card.current, .media-switch-btn.active { background: #182018 !important; color: #dfff91 !important; }
              input, select, textarea, button, .main-link { border-color: #303933 !important; background: #111512 !important; color: #e3eae5 !important; }
              .primary, button.primary { border-color: transparent !important; background: #c6fa38 !important; color: #111806 !important; }
              .download { border-color: #40512b !important; background: #182018 !important; color: #c6fa38 !important; }
              .note { border-color: #8cb52c !important; background: #151b13 !important; color: #aebcae !important; }
              .num { background: #1a2117 !important; color: #c6fa38 !important; }
              .code { background: #090c0a !important; color: #dbe7de !important; }
            `;
            (document.head || document.documentElement).appendChild(style);
          };
          if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', applyTheme, { once: true });
          else applyTheme();
        })();
        """;
}
