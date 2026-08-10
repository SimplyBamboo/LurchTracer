using System.Reflection;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LurchTracer;

public sealed class MainWindow : Form
{
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill };
    private RawInputListener? inputListener;
    private bool pageReady;

    public MainWindow()
    {
        Text = "Lurch Tracer";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1080, 840);
        MinimumSize = new Size(760, 620);
        BackColor = Color.FromArgb(1, 2, 3);
        TransparencyKey = BackColor;

        browser.DefaultBackgroundColor = Color.Transparent;
        browser.ZoomFactor = 1.25;

        Controls.Add(browser);
        Shown += async (_, _) => await StartAppAsync();
    }

    private async Task StartAppAsync()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions("--disable-gpu");
            var environment = await CoreWebView2Environment.CreateAsync(options: options);
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

        string webFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LurchTracer",
            "Web");
        Directory.CreateDirectory(webFolder);

        string indexPath = Path.Combine(webFolder, "index.html");
        using Stream? source = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("LurchTracer.Web.index.html");

        if (source is null)
            throw new InvalidOperationException("Embedded UI is missing.");

        await using (FileStream output = File.Create(indexPath))
            await source.CopyToAsync(output);

        webView.SetVirtualHostNameToFolderMapping(
            "app.lurchtracer",
            webFolder,
            CoreWebView2HostResourceAccessKind.Allow);

        webView.Navigate("https://app.lurchtracer/index.html");
        pageReady = true;

        inputListener = new RawInputListener(PostInputMessage);
        inputListener.Register(Handle);
    }

    private void PostInputMessage(string json)
    {
        if (!pageReady || browser.CoreWebView2 is null)
            return;

        void Send() => browser.CoreWebView2.PostWebMessageAsJson(json);
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
}
