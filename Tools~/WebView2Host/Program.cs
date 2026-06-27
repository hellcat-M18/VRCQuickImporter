using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace VRCQuickImporter.WebView2Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var options = HostOptions.Parse(args);

        Directory.CreateDirectory(options.ProfileDirectory);
        Directory.CreateDirectory(options.LogDirectory);
        Directory.CreateDirectory(options.DownloadDirectory);

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new BrowserForm(options));
    }
}

internal sealed class BrowserForm : Form
{
    private readonly HostOptions _options;
    private readonly WebView2 _webView;
    private readonly TextBox _addressBar;
    private readonly Label _statusLabel;
    private readonly Button _backButton;
    private readonly Button _forwardButton;

    public BrowserForm(HostOptions options)
    {
        _options = options;

        Text = "VRCQuickImporter - BOOTHログイン専用";
        Width = 1200;
        Height = 820;
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        Controls.Add(root);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(4)
        };
        root.Controls.Add(toolbar, 0, 0);

        _backButton = MakeButton("戻る", 60, (_, _) => GoBack());
        _backButton.Enabled = false;
        toolbar.Controls.Add(_backButton);

        _forwardButton = MakeButton("進む", 60, (_, _) => GoForward());
        _forwardButton.Enabled = false;
        toolbar.Controls.Add(_forwardButton);

        toolbar.Controls.Add(MakeButton("再読込", 70, (_, _) => Reload()));
        toolbar.Controls.Add(MakeButton("ログイン", 80, (_, _) => Navigate("https://accounts.booth.pm/users/sign_in")));
        toolbar.Controls.Add(MakeButton("HTML保存", 90, async (_, _) => await SaveCurrentHtmlAsync()));
        toolbar.Controls.Add(MakeButton("DevTools", 90, (_, _) => _webView.CoreWebView2?.OpenDevToolsWindow()));

        _addressBar = new TextBox
        {
            Width = 600,
            Anchor = AnchorStyles.Left | AnchorStyles.Right
        };
        _addressBar.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Navigate(_addressBar.Text);
            }
        };
        toolbar.Controls.Add(_addressBar);
        toolbar.Controls.Add(MakeButton("移動", 60, (_, _) => Navigate(_addressBar.Text)));

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = _options.ProfileDirectory
            }
        };
        root.Controls.Add(_webView, 0, 1);

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0)
        };
        root.Controls.Add(_statusLabel, 0, 2);

        Shown += async (_, _) => await InitializeAsync();
    }

    private static Button MakeButton(string text, int width, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 28,
            Margin = new Padding(2)
        };
        button.Click += onClick;
        return button;
    }

    private async Task InitializeAsync()
    {
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _options.ProfileDirectory);
            await _webView.EnsureCoreWebView2Async(environment);

            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.IsStatusBarEnabled = true;

            _webView.CoreWebView2.NavigationStarting += (_, e) =>
            {
                _statusLabel.Text = "読み込み中: " + e.Uri;
                _addressBar.Text = e.Uri;
            };
            _webView.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                _statusLabel.Text = e.IsSuccess ? "読み込み完了" : $"読み込み失敗: {e.WebErrorStatus}";
                _addressBar.Text = _webView.Source?.ToString() ?? string.Empty;
                UpdateNavigationButtonState();
            };
            _webView.CoreWebView2.HistoryChanged += (_, _) => UpdateNavigationButtonState();
            _webView.CoreWebView2.DownloadStarting += (_, e) =>
            {
                var fileName = Path.GetFileName(e.ResultFilePath);
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    fileName = "download.bin";
                }

                e.ResultFilePath = MakeUniquePath(Path.Combine(_options.DownloadDirectory, fileName));
                _statusLabel.Text = "ダウンロード開始: " + e.ResultFilePath;
            };
            _webView.CoreWebView2.ProcessFailed += (_, e) =>
            {
                _statusLabel.Text = "WebView2プロセスで問題が発生しました: " + e.ProcessFailedKind;
            };

            UpdateNavigationButtonState();
            Navigate(_options.InitialUrl);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "初期化失敗: " + ex.Message;
            MessageBox.Show(this,
                "WebView2の初期化に失敗しました。\n\n" + ex,
                "VRCQuickImporter",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void GoBack()
    {
        if (_webView.CoreWebView2?.CanGoBack == true)
        {
            _webView.CoreWebView2.GoBack();
        }

        UpdateNavigationButtonState();
    }

    private void GoForward()
    {
        if (_webView.CoreWebView2?.CanGoForward == true)
        {
            _webView.CoreWebView2.GoForward();
        }

        UpdateNavigationButtonState();
    }

    private void Reload()
    {
        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.Reload();
            return;
        }

        if (_webView.Source != null)
        {
            _webView.Source = _webView.Source;
        }
    }

    private void UpdateNavigationButtonState()
    {
        _backButton.Enabled = _webView.CoreWebView2?.CanGoBack == true;
        _forwardButton.Enabled = _webView.CoreWebView2?.CanGoForward == true;
    }

    private void Navigate(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (!Uri.TryCreate("https://" + url, UriKind.Absolute, out uri)) return;
        }

        _addressBar.Text = uri.ToString();
        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.Navigate(uri.ToString());
        }
        else
        {
            _webView.Source = uri;
        }
    }

    private async Task SaveCurrentHtmlAsync()
    {
        try
        {
            if (_webView.CoreWebView2 == null)
            {
                _statusLabel.Text = "まだWebView2が初期化されていません。";
                return;
            }

            var htmlJson = await _webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var filePath = Path.Combine(_options.LogDirectory, "last-booth-page-html.json.txt");
            await File.WriteAllTextAsync(filePath, htmlJson);

            var meta = new
            {
                savedAt = DateTimeOffset.Now,
                url = _webView.Source?.ToString(),
                htmlJsonLength = htmlJson.Length,
                filePath
            };
            await File.WriteAllTextAsync(
                Path.Combine(_options.LogDirectory, "last-booth-page-html.meta.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            _statusLabel.Text = "HTMLを保存しました: " + filePath;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "HTML保存失敗: " + ex.Message;
            MessageBox.Show(this, ex.ToString(), "HTML保存失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string MakeUniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; i < 10000; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(directory, $"{name}-{Guid.NewGuid():N}{extension}");
    }
}

internal sealed class HostOptions
{
    public string ProfileDirectory { get; private init; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "webview-profile");
    public string LogDirectory { get; private init; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "logs");
    public string DownloadDirectory { get; private init; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "downloads");
    public string InitialUrl { get; private init; } = "https://accounts.booth.pm/users/sign_in";

    public static HostOptions Parse(string[] args)
    {
        var options = new HostOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal)) continue;
            if (i + 1 >= args.Length) break;
            var value = args[++i];

            options = key switch
            {
                "--profile" => options.WithProfile(value),
                "--logs" => options.WithLogs(value),
                "--downloads" => options.WithDownloads(value),
                "--url" => options.WithUrl(value),
                _ => options
            };
        }

        return options;
    }

    private HostOptions WithProfile(string value) => new()
    {
        ProfileDirectory = value,
        LogDirectory = LogDirectory,
        DownloadDirectory = DownloadDirectory,
        InitialUrl = InitialUrl
    };

    private HostOptions WithLogs(string value) => new()
    {
        ProfileDirectory = ProfileDirectory,
        LogDirectory = value,
        DownloadDirectory = DownloadDirectory,
        InitialUrl = InitialUrl
    };

    private HostOptions WithDownloads(string value) => new()
    {
        ProfileDirectory = ProfileDirectory,
        LogDirectory = LogDirectory,
        DownloadDirectory = value,
        InitialUrl = InitialUrl
    };

    private HostOptions WithUrl(string value) => new()
    {
        ProfileDirectory = ProfileDirectory,
        LogDirectory = LogDirectory,
        DownloadDirectory = DownloadDirectory,
        InitialUrl = value
    };
}
