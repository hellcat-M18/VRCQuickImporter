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

        if (options.Headless)
        {
            Text = "VRCQuickImporter - バックグラウンド同期";
            Width = 1;
            Height = 1;
            StartPosition = FormStartPosition.Manual;
            Left = -32000;
            Top = -32000;
            ShowInTaskbar = false;
            Opacity = 0;
            WindowState = FormWindowState.Minimized;
        }
        else
        {
            Text = options.SyncLibrary
                ? "VRCQuickImporter - BOOTHライブラリ同期"
                : "VRCQuickImporter - BOOTHログイン専用";
            Width = 1200;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;
        }

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
        await SyncAllLibraryPagesAsync();
    }

    private async Task SyncAllLibraryPagesAsync()
    {
        if (_syncRunning) return;

        if (_webView.CoreWebView2 == null)
        {
            _statusLabel.Text = "まだWebView2が初期化されていません。";
            return;
        }

        var currentUrl = _webView.Source?.ToString() ?? string.Empty;
        if (!IsBoothLibraryUrl(currentUrl))
        {
            _statusLabel.Text = "BOOTHライブラリへ移動します。ログイン画面が出た場合はログイン後にもう一度「同期」を押してください。";
            Navigate(LibraryUrl);
            return;
        }

        _syncRunning = true;
        _syncButton.Enabled = false;

        try
        {
            var allProducts = new List<JsonElement>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var page = 1;
            var maxPages = 100;

            while (page <= maxPages)
            {
                var pageUrl = page == 1 ? LibraryUrl : $"{LibraryUrl}?page={page}";
                _statusLabel.Text = $"BOOTHライブラリ {page}ページ目を読み込み中...";

                if (!string.Equals(_webView.Source?.ToString() ?? string.Empty, pageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    Navigate(pageUrl);
                    await WaitForLibraryNavigationAsync();
                }

                var urlAfterWait = _webView.Source?.ToString() ?? string.Empty;
                if (!IsBoothLibraryUrl(urlAfterWait))
                {
                    _statusLabel.Text = "BOOTHライブラリではないページに移動しました。ログインが必要な可能性があります。";
                    break;
                }

                await Task.Delay(1200);
                var rawJson = await _webView.CoreWebView2.ExecuteScriptAsync(LibraryExtractionScript);
                var pageDocument = JsonDocument.Parse(rawJson);
                var pageCount = 0;

                if (pageDocument.RootElement.TryGetProperty("Products", out var products) && products.ValueKind == JsonValueKind.Array)
                {
                    foreach (var product in products.EnumerateArray())
                    {
                        if (product.TryGetProperty("ProductId", out var idElement) && idElement.ValueKind == JsonValueKind.String)
                        {
                            var id = idElement.GetString() ?? string.Empty;
                            if (!string.IsNullOrEmpty(id) && seenIds.Add(id))
                            {
                                allProducts.Add(product.Clone());
                            }
                        }
                    }

                    pageCount = products.GetArrayLength();
                }

                await SaveCurrentHtmlAsync($"last-library-page-{page:D3}-html.json.txt", showStatus: false);

                if (pageCount == 0)
                {
                    break;
                }

                page++;
            }

            var outputPath = string.IsNullOrWhiteSpace(_options.OutputPath)
                ? Path.Combine(_options.LogDirectory, "booth-library.database.json")
                : _options.OutputPath;

            var finalDocument = new
            {
                SchemaVersion = "1",
                SyncedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                SourceUrl = LibraryUrl,
                Products = allProducts
            };

            var formatted = JsonSerializer.Serialize(finalDocument, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(outputPath, formatted);

            var rawLogPath = Path.Combine(_options.LogDirectory, "last-library-sync.raw.json");
            await File.WriteAllTextAsync(rawLogPath, formatted);

            _statusLabel.Text = $"同期データを保存しました: {allProducts.Count}件 / {outputPath}";

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

    private async Task WaitForLibraryNavigationAsync()
    {
        var tcs = new TaskCompletionSource();
        var completed = false;

        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (completed) return;
            completed = true;
            _webView.CoreWebView2.NavigationCompleted -= Handler;
            tcs.TrySetResult();
        }

        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.NavigationCompleted += Handler;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using (cts.Token.Register(() => tcs.TrySetCanceled()))
        {
            await tcs.Task;
        }
    }

    private async Task SyncLibraryAsync(bool manual)
    {
        await SyncAllLibraryPagesAsync();
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
  const hasClasses = (element, ...classes) => element && classes.every(className => element.classList.contains(className));
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
  const directElementChildren = element => Array.from(element ? element.children : []).filter(child => child instanceof HTMLElement);
  const findProductBlocks = () => Array.from(document.querySelectorAll('main div'))
    .filter(element => hasClasses(element, 'mb-16', 'bg-white', 'p-16'))
    .filter(element => element.querySelector('a[href*=""/items/""] img.l-library-item-thumbnail'));
  const findProductRow = block => directElementChildren(block)
    .find(element => hasClasses(element, 'flex', 'border-b', 'pb-16')) || block;
  const findTitleAnchor = row => Array.from(row.querySelectorAll('a[href*=""/items/""]'))
    .find(anchor => normalize(anchor.innerText || anchor.textContent).length > 0);
  const findShopAnchor = row => Array.from(row.querySelectorAll('a[href]'))
    .find(anchor => !/\/items\//.test(anchor.getAttribute('href') || '') && /\.booth\.pm\/?/.test(anchor.href || '') && normalize(anchor.innerText || anchor.textContent).length > 0);
  const findFileRows = block => Array.from(block.querySelectorAll('div'))
    .filter(element => hasClasses(element, 'mt-16', 'desktop:flex', 'desktop:justify-between', 'desktop:items-center'));
  const findFileName = row => {
    const nameContainer = Array.from(row.querySelectorAll('div')).find(element => hasClasses(element, 'min-w-0', 'break-words', 'whitespace-pre-line'));
    const text14 = nameContainer && Array.from(nameContainer.querySelectorAll('div')).find(element => element.classList.contains('text-14'));
    return normalize((text14 || nameContainer || row).innerText || (text14 || nameContainer || row).textContent);
  };

  const products = [];
  const seen = new Set();

  for (const block of findProductBlocks()) {
    const row = findProductRow(block);
    const image = row.querySelector('img.l-library-item-thumbnail');
    const imageAnchor = image && image.closest('a[href*=""/items/""]');
    const titleAnchor = findTitleAnchor(row) || imageAnchor;
    const href = absoluteUrl(titleAnchor && titleAnchor.getAttribute('href'));
    const productId = itemIdFromUrl(href);
    if (!productId || seen.has(productId)) continue;

    const titleElement = titleAnchor && (titleAnchor.querySelector('.font-bold') || titleAnchor);
    const name = normalize(titleElement && (titleElement.innerText || titleElement.textContent));
    if (!name) continue;

    const shopAnchor = findShopAnchor(row);
    const shopElement = shopAnchor && (shopAnchor.querySelector('.text-text-gray600') || shopAnchor);
    let shopName = normalize(shopElement && (shopElement.innerText || shopElement.textContent));
    if (shopName === name) shopName = '';

    const files = findFileRows(block)
      .map((fileRow, index) => {
        const fileName = findFileName(fileRow);
        if (!fileName) return null;
        return {
          FileId: productId + ':file:' + index,
          Name: fileName,
          SizeText: '',
          Kind: kindFromName(fileName),
          DownloadUrl: ''
        };
      })
      .filter(Boolean);

    products.push({
      ProductId: productId,
      Name: name,
      ShopName: shopName,
      ThumbnailUrl: absoluteUrl(image && (image.currentSrc || image.src || image.getAttribute('src'))),
      ProductUrl: href,
      Files: files.length > 0 ? files : [{
        FileId: productId + ':detail',
        Name: 'ファイル一覧未取得',
        SizeText: '',
        Kind: 0,
        DownloadUrl: ''
      }],
      CategoryLabel: '',
      BadgeText: '',
      PriceText: '',
      LikeCount: 0
    });
    seen.add(productId);
  }

  return {
    SchemaVersion: '1',
    SyncedAt: new Date().toLocaleString(),
    SourceUrl: location.href,
    Products: products.slice(0, 200)
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
    public bool Headless { get; private init; }

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
                case "--headless":
                    options = options.WithHeadless();
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
    private HostOptions WithHeadless() => Copy(headless: true);

    private HostOptions Copy(
        string? profileDirectory = null,
        string? logDirectory = null,
        string? downloadDirectory = null,
        string? initialUrl = null,
        string? outputPath = null,
        bool? syncLibrary = null,
        bool? exitAfterSync = null,
        bool? headless = null) => new()
    {
        ProfileDirectory = profileDirectory ?? ProfileDirectory,
        LogDirectory = logDirectory ?? LogDirectory,
        DownloadDirectory = downloadDirectory ?? DownloadDirectory,
        InitialUrl = initialUrl ?? InitialUrl,
        OutputPath = outputPath ?? OutputPath,
        SyncLibrary = syncLibrary ?? SyncLibrary,
        ExitAfterSync = exitAfterSync ?? ExitAfterSync,
        Headless = headless ?? Headless
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
