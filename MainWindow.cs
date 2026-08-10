using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LurchTracer;

public sealed class MainWindow : Form
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private RawInputListener? inputListener;
    private byte[]? pageBytes;
    private bool pageReady;

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
        TransparencyKey = BackColor;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

        browser.DefaultBackgroundColor = Color.Transparent;
        browser.ZoomFactor = 1.25;

        Controls.Add(browser);
        Shown += async (_, _) => await StartAppAsync();
    }

    private async Task StartAppAsync()
    {
        try
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LurchTracer",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await browser.EnsureCoreWebView2Async(environment);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                this,
                "Microsoft Edge WebView2 Runtime is required to run Lurch Tracer.\n\n" + error.Message,
                "Lurch Tracer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
            return;
        }

        CoreWebView2 webView = browser.CoreWebView2;
        webView.Settings.AreDefaultContextMenusEnabled = false;
        webView.Settings.AreDevToolsEnabled = false;
        webView.Settings.IsZoomControlEnabled = false;
        webView.Settings.AreBrowserAcceleratorKeysEnabled = false;
        webView.Settings.IsStatusBarEnabled = false;

        using Stream? source = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LurchTracer.Web.index.html");

        if (source is null)
            throw new InvalidOperationException("Embedded UI is missing.");

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
        if (pageBytes is null || browser.CoreWebView2 is null)
            return;

        Uri requestUri = new(eventArgs.Request.Uri);
        bool isPage = requestUri.AbsolutePath.Equals("/index.html", StringComparison.OrdinalIgnoreCase) ||
            requestUri.AbsolutePath == "/";
        Stream stream = isPage
            ? new MemoryStream(pageBytes, false)
            : new MemoryStream(Array.Empty<byte>(), false);
        eventArgs.Response = browser.CoreWebView2.Environment.CreateWebResourceResponse(
            stream,
            isPage ? 200 : 404,
            isPage ? "OK" : "Not Found",
            isPage
                ? "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store"
                : "Cache-Control: no-store");
    }

    private void AppNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs eventArgs)
    {
        if (!eventArgs.IsSuccess || pageReady)
            return;

        pageReady = true;
        inputListener = new RawInputListener(PostInputMessage);
        inputListener.Register(Handle);
    }

    private void PostInputMessage(string json)
    {
        if (!pageReady || browser.CoreWebView2 is null || IsDisposed)
            return;

        void Send()
        {
            if (!IsDisposed && browser.CoreWebView2 is not null)
                browser.CoreWebView2.PostWebMessageAsJson(json);
        }

        if (InvokeRequired)
            BeginInvoke(Send);
        else
            Send();
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
            browser.Dispose();
        }

        base.Dispose(disposing);
    }
}
