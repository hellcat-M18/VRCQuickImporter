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
        if (!string.IsNullOrEmpty(options.OutputPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath) ?? options.LogDirectory);
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new BrowserForm(options));
    }
}

internal sealed class BrowserForm : Form
{
    private const string LibraryUrl = "https://accounts.booth.pm/library";

    private readonly HostOptions _options;
    private readonly WebView2 _webView;
    private readonly TextBox _addressBar;
    private readonly Label _statusLabel;
    private readonly Button _backButton;
    private readonly Button _forwardButton;
    private readonly Button _syncButton;
    private bool _autoSyncAttempted;
    private bool _syncRunning;

    public BrowserForm(HostOptions options)
    {
        _options = options;

        Text = options.SyncLibrary
            ? "VRCQuickImporter - BOOTHライブラリ同期"
            : "VRCQuickImporter - BOOTHログイン専用";
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

        _syncButton = MakeButton("同期", 70, async (_, _) => await SyncLibraryAsync(manual: true));
        _syncButton.ToolTipText("BOOTHライブラリページから商品候補を抽出してUnity用database.jsonへ保存します");
        toolbar.Controls.Add(_syncButton);

        toolbar.Controls.Add(MakeButton("HTML保存", 90, async (_, _) => await SaveCurrentHtmlAsync()));
        toolbar.Controls.Add(MakeButton("DevTools", 90, (_, _) => _webView.CoreWebView2?.OpenDevToolsWindow()));

        _addressBar = new TextBox
        {
            Width = 520,
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
            _webView.CoreWebView2.NavigationCompleted += async (_, e) =>
            {
                _statusLabel.Text = e.IsSuccess ? "読み込み完了" : $"読み込み失敗: {e.WebErrorStatus}";
                _addressBar.Text = _webView.Source?.ToString() ?? string.Empty;
                UpdateNavigationButtonState();

                if (_options.SyncLibrary && e.IsSuccess)
                {
                    await TryAutoSyncAfterNavigationAsync();
                }
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

    private async Task TryAutoSyncAfterNavigationAsync()
    {
        if (_autoSyncAttempted || _syncRunning) return;

        var url = _webView.Source?.ToString() ?? string.Empty;
        if (!IsBoothLibraryUrl(url))
        {
            if (url.Contains("sign_in", StringComparison.OrdinalIgnoreCase))
            {
                _statusLabel.Text = "BOOTHにログインしてください。ログイン後に「同期」を押してください。";
            }
            return;
        }

        _autoSyncAttempted = true;
        await Task.Delay(1800);
        await SyncLibraryAsync(manual: false);
    }

    private async Task SyncLibraryAsync(bool manual)
    {
        if (_syncRunning) return;

        if (_webView.CoreWebView2 == null)
        {
            _statusLabel.Text = "まだWebView2が初期化されていません。";
            return;
        }

        var url = _webView.Source?.ToString() ?? string.Empty;
        if (!IsBoothLibraryUrl(url))
        {
            _statusLabel.Text = "BOOTHライブラリへ移動します。ログイン画面が出た場合はログイン後にもう一度「同期」を押してください。";
            Navigate(LibraryUrl);
            return;
        }

        _syncRunning = true;
        _syncButton.Enabled = false;
        _statusLabel.Text = "BOOTHライブラリを抽出中...";

        try
        {
            await Task.Delay(manual ? 800 : 0);
            var rawJson = await _webView.CoreWebView2.ExecuteScriptAsync(LibraryExtractionScript);
            await File.WriteAllTextAsync(Path.Combine(_options.LogDirectory, "last-library-sync.raw.json"), rawJson);

            var outputPath = string.IsNullOrWhiteSpace(_options.OutputPath)
                ? Path.Combine(_options.LogDirectory, "booth-library.database.json")
                : _options.OutputPath;

            using var document = JsonDocument.Parse(rawJson);
            var formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(outputPath, formatted);
            await SaveCurrentHtmlAsync("last-library-page-html.json.txt", showStatus: false);

            var count = document.RootElement.TryGetProperty("Products", out var products) && products.ValueKind == JsonValueKind.Array
                ? products.GetArrayLength()
                : 0;

            _statusLabel.Text = $"同期データを保存しました: {count}件 / {outputPath}";

            if (_options.ExitAfterSync)
            {
                await Task.Delay(1200);
                BeginInvoke(Close);
            }
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "同期失敗: " + ex.Message;
            MessageBox.Show(this, ex.ToString(), "BOOTHライブラリ同期失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _syncRunning = false;
            _syncButton.Enabled = true;
        }
    }

    private static bool IsBoothLibraryUrl(string url)
    {
        return url.Contains("accounts.booth.pm/library", StringComparison.OrdinalIgnoreCase);
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
        await SaveCurrentHtmlAsync("last-booth-page-html.json.txt", showStatus: true);
    }

    private async Task SaveCurrentHtmlAsync(string fileName, bool showStatus)
    {
        try
        {
            if (_webView.CoreWebView2 == null)
            {
                _statusLabel.Text = "まだWebView2が初期化されていません。";
                return;
            }

            var htmlJson = await _webView.CoreWebView2.ExecuteScriptAsync("document.documentElement.outerHTML");
            var filePath = Path.Combine(_options.LogDirectory, fileName);
            await File.WriteAllTextAsync(filePath, htmlJson);

            var meta = new
            {
                savedAt = DateTimeOffset.Now,
                url = _webView.Source?.ToString(),
                htmlJsonLength = htmlJson.Length,
                filePath
            };
            await File.WriteAllTextAsync(
                Path.Combine(_options.LogDirectory, Path.GetFileNameWithoutExtension(fileName) + ".meta.json"),
                JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));

            if (showStatus)
            {
                _statusLabel.Text = "HTMLを保存しました: " + filePath;
            }
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

    private const string LibraryExtractionScript = @"
(() => {
  const normalize = value => (value || '').replace(/\s+/g, ' ').trim();
  const absoluteUrl = value => {
    try { return value ? new URL(value, location.href).href : ''; } catch { return ''; }
  };
  const itemIdFromUrl = href => {
    const match = (href || '').match(/\/items\/(\d+)/);
    return match ? match[1] : '';
  };
  const kindFromName = name => {
    const lower = (name || '').toLowerCase();
    if (lower.endsWith('.unitypackage')) return 1;
    if (lower.endsWith('.zip')) return 2;
    if (/\.(png|jpg|jpeg|webp|gif)$/.test(lower)) return 3;
    return 0;
  };
  const findCard = anchor => anchor.closest('article, li, [class*=""item""], [class*=""product""], [class*=""card""], [class*=""library""]') || anchor.parentElement || anchor;
  const bestText = card => Array.from(card.querySelectorAll('a, h1, h2, h3, h4, p, span, div'))
    .map(el => normalize(el.innerText || el.textContent))
    .filter(text => text.length >= 2 && text.length <= 160);

  const anchors = Array.from(document.querySelectorAll('a[href*=""/items/""]'));
  const productsById = new Map();

  for (const anchor of anchors) {
    const href = absoluteUrl(anchor.getAttribute('href'));
    const productId = itemIdFromUrl(href);
    if (!productId || productsById.has(productId)) continue;

    const card = findCard(anchor);
    const image = card.querySelector('img');
    const texts = bestText(card);
    const anchorText = normalize(anchor.innerText || anchor.textContent);
    const imageAlt = normalize(image && image.getAttribute('alt'));
    const name = anchorText || imageAlt || texts[0] || ('BOOTH item ' + productId);
    const price = texts.find(text => /(?:¥|￥|無料|free)/i.test(text)) || '';
    const likeText = texts.find(text => /(?:♥|♡|いいね|like)/i.test(text)) || '';
    const likeMatch = likeText.replace(/,/g, '').match(/(\d{1,7})/);
    const category = texts.find(text => /(?:3D|衣装|アバター|キャラクター|アクセサリ|テクスチャ|素材|ツール|イラスト)/.test(text)) || '';
    const shopCandidate = texts.find(text => text !== name && text !== price && text !== category && !/VRCHAT/i.test(text) && text.length <= 48) || '';
    const downloadButtons = Array.from(card.querySelectorAll('a, button'))
      .map(el => normalize(el.innerText || el.textContent))
      .filter(text => /(?:download|ダウンロード|\.zip|\.unitypackage)/i.test(text));

    const files = downloadButtons.length > 0
      ? downloadButtons.slice(0, 8).map((text, index) => ({
          FileId: productId + ':candidate:' + index,
          Name: text,
          SizeText: '',
          Kind: kindFromName(text),
          DownloadUrl: ''
        }))
      : [{
          FileId: productId + ':detail',
          Name: 'ファイル一覧は次フェーズで取得',
          SizeText: '',
          Kind: 0,
          DownloadUrl: ''
        }];

    productsById.set(productId, {
      ProductId: productId,
      Name: name,
      ShopName: shopCandidate,
      ThumbnailUrl: absoluteUrl(image && (image.currentSrc || image.src || image.getAttribute('src'))),
      ProductUrl: href,
      Files: files,
      CategoryLabel: category || 'BOOTH',
      BadgeText: /vrchat/i.test(texts.join(' ')) ? 'VRCHAT' : '',
      PriceText: price,
      LikeCount: likeMatch ? parseInt(likeMatch[1], 10) : 0
    });
  }

  return {
    SchemaVersion: '1',
    SyncedAt: new Date().toLocaleString(),
    SourceUrl: location.href,
    Products: Array.from(productsById.values()).slice(0, 200)
  };
})()
";
}

internal sealed class HostOptions
{
    public string ProfileDirectory { get; private init; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "webview-profile");
    public string LogDirectory { get; private init; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "logs");
    public string DownloadDirectory { get; private init; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "downloads");
    public string InitialUrl { get; private init; } = "https://accounts.booth.pm/users/sign_in";
    public string OutputPath { get; private init; } = string.Empty;
    public bool SyncLibrary { get; private init; }
    public bool ExitAfterSync { get; private init; }

    public static HostOptions Parse(string[] args)
    {
        var options = new HostOptions();

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (!key.StartsWith("--", StringComparison.Ordinal)) continue;

            switch (key)
            {
                case "--sync-library":
                    options = options.WithSyncLibrary();
                    break;
                case "--exit-after-sync":
                    options = options.WithExitAfterSync();
                    break;
                case "--profile":
                case "--logs":
                case "--downloads":
                case "--url":
                case "--output":
                    if (i + 1 >= args.Length) break;
                    var value = args[++i];
                    options = key switch
                    {
                        "--profile" => options.WithProfile(value),
                        "--logs" => options.WithLogs(value),
                        "--downloads" => options.WithDownloads(value),
                        "--url" => options.WithUrl(value),
                        "--output" => options.WithOutput(value),
                        _ => options
                    };
                    break;
            }
        }

        return options;
    }

    private HostOptions WithProfile(string value) => Copy(profileDirectory: value);
    private HostOptions WithLogs(string value) => Copy(logDirectory: value);
    private HostOptions WithDownloads(string value) => Copy(downloadDirectory: value);
    private HostOptions WithUrl(string value) => Copy(initialUrl: value);
    private HostOptions WithOutput(string value) => Copy(outputPath: value);
    private HostOptions WithSyncLibrary() => Copy(syncLibrary: true);
    private HostOptions WithExitAfterSync() => Copy(exitAfterSync: true);

    private HostOptions Copy(
        string? profileDirectory = null,
        string? logDirectory = null,
        string? downloadDirectory = null,
        string? initialUrl = null,
        string? outputPath = null,
        bool? syncLibrary = null,
        bool? exitAfterSync = null) => new()
    {
        ProfileDirectory = profileDirectory ?? ProfileDirectory,
        LogDirectory = logDirectory ?? LogDirectory,
        DownloadDirectory = downloadDirectory ?? DownloadDirectory,
        InitialUrl = initialUrl ?? InitialUrl,
        OutputPath = outputPath ?? OutputPath,
        SyncLibrary = syncLibrary ?? SyncLibrary,
        ExitAfterSync = exitAfterSync ?? ExitAfterSync
    };
}

internal static class WinFormsExtensions
{
    private static readonly ToolTip ToolTip = new();

    public static void ToolTipText(this Control control, string text)
    {
        ToolTip.SetToolTip(control, text);
    }
}
