using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Clocteck.CubicCenter;

public partial class BrowserWindow : Window
{
    private string _pendingUrl = "about:blank";
    private bool _ready;

    public BrowserWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    public void Navigate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https")) return;
        _pendingUrl = parsed.ToString();
        AddressBox.Text = _pendingUrl;
        if (_ready) Browser.CoreWebView2.Navigate(_pendingUrl);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_ready) return;
        var userData = Path.Combine(AppContext.BaseDirectory, "data", "webview2");
        Directory.CreateDirectory(userData);
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
        await Browser.EnsureCoreWebView2Async(environment);
        Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;
        Browser.CoreWebView2.NavigationStarting += (_, args) => AddressBox.Text = args.Uri;
        Browser.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            AddressBox.Text = Browser.Source?.ToString() ?? _pendingUrl;
            BackButton.IsEnabled = Browser.CoreWebView2.CanGoBack;
        };
        _ready = true;
        Browser.CoreWebView2.Navigate(_pendingUrl);
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ready && Browser.CoreWebView2.CanGoBack) Browser.CoreWebView2.GoBack();
    }

    private void ReloadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_ready) Browser.CoreWebView2.Reload();
    }

    private void ExternalButton_OnClick(object sender, RoutedEventArgs e)
    {
        var url = Browser.Source?.ToString() ?? _pendingUrl;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
