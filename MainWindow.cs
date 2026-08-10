using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LurchTracer;

public sealed class MainWindow : Form
{
    private WebView2? browser;
    private RawInputListener? inputListener;
    private byte[]? pageBytes;
    private bool pageReady;
    private bool starting;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ClassName = "Chrome_LurchTracer_Window";
            return parameters;
        }
    }

    public MainWindow()
    {
        Text = "Lurch Tracer";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 840);
        MinimumSize = new Size(760, 620);
        BackColor = Color.FromArgb(1, 2, 3);
        Shown += MainWindowShown;
    }

    private async void MainWindowShown(object? sender, EventArgs eventArgs)
    {
        if (starting)
            return;

        starting = true;

        try
        {
            await StartAppAsync();
        }
        catch (Exception error)
        {
            Program.ShowFatalError(error);
            Close();
        }
    }

    private async Task StartAppAsync()
    {
        browser = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.Transparent,
            ZoomFactor = 1.25
        };
        Controls.Add(browser);

        string userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LurchTracer",
            "WebView2");

        Directory.CreateDirectory(userDataFolder);

        CoreWebView2Environment environment;
        try
        {
            environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder,
                options: null);
            await browser.EnsureCoreWebView2Async(environment);
        }
        catch (Exception error)
        {
            throw new InvalidOperationException(
                "Microsoft Edge WebView2 could not be initialized. Install or repair the WebView2 Runtime and try again.",
                error);
        }

        CoreWebView2 webView = browser.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 initialized without a CoreWebView2 instance.");

        webView.Settings.AreDefaultContextMenusEnabled = false;
        webView.Settings.AreDevToolsEnabled = false;
        webView.Settings.IsZoomControlEnabled = false;
        webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.Settings.IsStatusBarEnabled = false;

        using Stream? source = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LurchTracer.Web.index.html");

        if (source is null)
            throw new InvalidOperationException("Embedded UI is missing from the executable.");

        using var copy = new MemoryStream();
        await source.CopyToAsync(copy);
        pageBytes = copy.ToArray();

        webView.AddWebResourceRequestedFilter(
            "https://app.lurchtracer/*",
            CoreWebView2WebResourceContext.All);
        webView.WebResourceRequested += ServeAppResource;
        webView.NavigationCompleted += AppNavigationCompleted;
        webView.Navigate("https://app.lurchtracer/index.html");
    }

    private void ServeAppResource(object? sender, CoreWebView2WebResourceRequestedEventArgs eventArgs)
    {
        CoreWebView2? webView = browser?.CoreWebView2;
        if (pageBytes is null || webView is null)
            return;

        Uri requestUri = new(eventArgs.Request.Uri);
        bool isPage = requestUri.AbsolutePath.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
            requestUri.AbsolutePath == "/";

        Stream stream = isPage
            ? new MemoryStream(pageBytes, false)
            : new MemoryStream(Array.Empty<byte>(), false);

        eventArgs.Response = webView.Environment.CreateWebResourceResponse(
            stream,
            isPage ? 200 : 404,
            isPage ? "OK" : "Not Found",
            isPage
                ? "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store"
                : "Cache-Control: no-store");
    }

    private void AppNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (pageReady)
            return;

        if (!eventArgs.IsSuccess)
        {
            Program.ShowFatalError(new InvalidOperationException(
                $"The embedded UI failed to load. WebView2 error: {eventArgs.WebErrorStatus}."));
            Close();
            return;
        }

        try
        {
            inputListener = new RawInputListener(PostInputMessage);
            inputListener.Register(Handle);
            TransparencyKey = BackColor;
            pageReady = true;
        }
        catch (Exception error)
        {
            Program.ShowFatalError(error);
            Close();
        }
    }

    private void PostInputMessage(string json)
    {
        CoreWebView2? webView = browser?.CoreWebView2;
        if (!pageReady || webView is null || IsDisposed || Disposing)
            return;

        void Send()
        {
            CoreWebView2? current = browser?.CoreWebView2;
            if (!IsDisposed && !Disposing && current is not null)
                current.PostWebMessageAsJson(json);
        }

        try
        {
            if (InvokeRequired)
                BeginInvoke(Send);
            else
                Send();
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override void WndProc(ref Message message)
    {
        inputListener?.ProcessMessage(message);
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inputListener?.Dispose();
            browser?.Dispose();
        }

        base.Dispose(disposing);
    }
}
