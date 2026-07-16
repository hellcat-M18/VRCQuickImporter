using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace VRCQuickImporter.WebView2Host
{

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
    private CoreWebView2DownloadOperation _currentDownload;
    private TaskCompletionSource<bool> _downloadCompletionSource;
    private int _blockedLightweightResourceCount;

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
            ConfigureLightweightLibrarySyncResources();

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
                else if (_options.IsDownloadMode && e.IsSuccess)
                {
                    await TryStartDownloadAsync();
                }
            };
            _webView.CoreWebView2.HistoryChanged += (_, _) => UpdateNavigationButtonState();
            _webView.CoreWebView2.DownloadStarting += (_, e) =>
            {
                if (_options.IsDownloadMode && !string.IsNullOrEmpty(_options.OutputPath))
                {
                    e.ResultFilePath = _options.OutputPath;
                    _currentDownload = e.DownloadOperation;
                    _currentDownload.StateChanged += OnDownloadStateChanged;
                    _currentDownload.BytesReceivedChanged += OnDownloadBytesReceivedChanged;
                    WriteProgress(0, 0, 0, "started");
                    _statusLabel.Text = "ダウンロード開始: " + e.ResultFilePath;
                }
                else
                {
                    var fileName = Path.GetFileName(e.ResultFilePath);
                    if (string.IsNullOrWhiteSpace(fileName))
                    {
                        fileName = "download.bin";
                    }
                    e.ResultFilePath = MakeUniquePath(Path.Combine(_options.DownloadDirectory, fileName));
                    _statusLabel.Text = "ダウンロード開始: " + e.ResultFilePath;
                }
            };
            _webView.CoreWebView2.ProcessFailed += (_, e) =>
            {
                _statusLabel.Text = "WebView2プロセスで問題が発生しました: " + e.ProcessFailedKind;
            };

            UpdateNavigationButtonState();

            if (_options.IsDownloadMode)
            {
                Navigate(_options.DownloadUrl);
            }
            else if (_options.SyncLibrary)
            {
                await NavigateLibraryPageAsync(_options.InitialUrl);
            }
            else
            {
                Navigate(_options.InitialUrl);
            }
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

    private void ConfigureLightweightLibrarySyncResources()
    {
        if (!_options.SyncLibrary || _webView.CoreWebView2 == null)
        {
            return;
        }

        _webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Image);
        _webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Media);
        _webView.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Font);
        _webView.CoreWebView2.WebResourceRequested += (_, e) =>
        {
            if (!IsBlockedLightweightResource(e.ResourceContext) || _webView.CoreWebView2 == null)
            {
                return;
            }

            _blockedLightweightResourceCount++;
            e.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(
                new MemoryStream(Array.Empty<byte>()),
                204,
                "No Content",
                "Cache-Control: no-store\r\nContent-Length: 0");
        };
    }

    private static bool IsBlockedLightweightResource(CoreWebView2WebResourceContext context)
    {
        return context == CoreWebView2WebResourceContext.Image
            || context == CoreWebView2WebResourceContext.Media
            || context == CoreWebView2WebResourceContext.Font;
    }

    private async Task TryAutoSyncAfterNavigationAsync()
    {
        if (_autoSyncAttempted || _syncRunning) return;

        var url = _webView.Source?.ToString() ?? string.Empty;
        if (!IsBoothLibraryUrl(url))
        {
            if (url.IndexOf("sign_in", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _statusLabel.Text = "BOOTHにログインしてください。ログイン後に「同期」を押してください。";
            }
            return;
        }

        _autoSyncAttempted = true;
        await SyncSingleLibraryPageAsync();
    }

    private async Task SyncSingleLibraryPageAsync()
    {
        if (_syncRunning) return;

        if (_webView.CoreWebView2 == null)
        {
            _statusLabel.Text = "まだWebView2が初期化されていません。";
            return;
        }

        var page = Math.Max(1, _options.Page);
        var pageUrl = page == 1 ? LibraryUrl : $"{LibraryUrl}?page={page}";

        _syncRunning = true;
        _syncButton.Enabled = false;

        try
        {
            if (!string.Equals(_webView.Source?.ToString() ?? string.Empty, pageUrl, StringComparison.OrdinalIgnoreCase))
            {
                _statusLabel.Text = $"BOOTHライブラリ {page}ページ目を読み込み中...";
                await NavigateLibraryPageAsync(pageUrl);
                await WaitForLibraryNavigationAsync();
            }

            var urlAfterWait = _webView.Source?.ToString() ?? string.Empty;
            if (!IsBoothLibraryUrl(urlAfterWait))
            {
                _statusLabel.Text = "BOOTHライブラリにアクセスできませんでした。ログインが必要です。";
                return;
            }

            _statusLabel.Text = $"BOOTHライブラリ {page}ページ目を抽出中...";
            var rawJson = await _webView.CoreWebView2.ExecuteScriptAsync(LibraryExtractionScript);

            var outputPath = string.IsNullOrWhiteSpace(_options.OutputPath)
                ? Path.Combine(_options.LogDirectory, "booth-library.database.json")
                : _options.OutputPath;

            using var document = JsonDocument.Parse(rawJson);
            var formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            // アトミック書き込み: テンポラリファイルに書き込んでからリネーム
            var tmpPath = outputPath + ".tmp";
            await WriteAllTextAsync(tmpPath, formatted);
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
            File.Move(tmpPath, outputPath);

            await WriteAllTextAsync(Path.Combine(_options.LogDirectory, "last-library-sync.raw.json"), formatted);
            await SaveCurrentHtmlAsync($"last-library-page-{page:D3}-html.json.txt", showStatus: false);

            var count = document.RootElement.TryGetProperty("Products", out var products) && products.ValueKind == JsonValueKind.Array
                ? products.GetArrayLength()
                : 0;

            _statusLabel.Text = $"同期データを保存しました: {count}件 / ページ{page} / 軽量化で {_blockedLightweightResourceCount} 件抑制";

            if (_options.ExitAfterSync)
            {
                await Task.Delay(1000);
                BeginInvoke(new MethodInvoker(Close));
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

    private async Task NavigateLibraryPageAsync(string url)
    {
        await WaitAndMarkLibraryAccessAsync();
        Navigate(url);
    }

    private async Task WaitAndMarkLibraryAccessAsync()
    {
        if (_options.SkipRateLimit || _options.MinAccessIntervalMs <= 0 || string.IsNullOrWhiteSpace(_options.RateLimitFilePath))
        {
            return;
        }

        try
        {
            if (File.Exists(_options.RateLimitFilePath))
            {
                var text = await ReadAllTextAsync(_options.RateLimitFilePath);
                if (DateTimeOffset.TryParse(text.Trim(), out var lastAccessUtc))
                {
                    var nextAllowedUtc = lastAccessUtc.ToUniversalTime().AddMilliseconds(_options.MinAccessIntervalMs);
                    var delay = nextAllowedUtc - DateTimeOffset.UtcNow;
                    if (delay > TimeSpan.Zero)
                    {
                        _statusLabel.Text = $"BOOTHへの次のアクセスまで {delay.TotalSeconds:F1} 秒待機中...";
                        await Task.Delay(delay);
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_options.RateLimitFilePath) ?? _options.LogDirectory);
            await WriteAllTextAsync(_options.RateLimitFilePath, DateTimeOffset.UtcNow.ToString("o"));
        }
        catch
        {
            // レート制限ファイルの読み書きに失敗しても同期自体は続行する。
        }
    }

    private async Task WaitForLibraryNavigationAsync()
    {
        var tcs = new TaskCompletionSource<bool>();
        var completed = false;

        void Handler(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (completed) return;
            completed = true;
            _webView.CoreWebView2.NavigationCompleted -= Handler;
            tcs.TrySetResult(true);
        }

        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.NavigationCompleted += Handler;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using (cts.Token.Register(() => tcs.TrySetCanceled()))
        {
            await tcs.Task;
        }
    }

    private async Task TryStartDownloadAsync()
    {
        var url = _webView.Source?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            _statusLabel.Text = "ダウンロードURLが無効です。";
            return;
        }

        _downloadCompletionSource = new TaskCompletionSource<bool>();

        // WebView2がダウンロードを開始するのを待つ（NavigationCompleted後にダウンロードがトリガされる）
        // DownloadStartingイベントで_downloadCompletionSourceが設定される
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        using (cts.Token.Register(() => _downloadCompletionSource.TrySetCanceled()))
        {
            try
            {
                await _downloadCompletionSource.Task;
                _statusLabel.Text = "ダウンロード完了: " + _options.OutputPath;
            }
            catch (OperationCanceledException)
            {
                _statusLabel.Text = "ダウンロードがタイムアウトしました。";
            }
        }

        await Task.Delay(500);
        BeginInvoke(new MethodInvoker(Close));
    }

    private void OnDownloadStateChanged(object sender, object e)
    {
        if (_currentDownload == null) return;

        var state = _currentDownload.State;
        if (state == CoreWebView2DownloadState.Completed)
        {
            _statusLabel.Text = "ダウンロード完了: " + _options.OutputPath;
            WriteProgress(100, 0, 100, "completed");
            _downloadCompletionSource?.TrySetResult(true);
            BeginInvoke(new MethodInvoker(Close));
        }
        else if (state == CoreWebView2DownloadState.Interrupted)
        {
            _statusLabel.Text = "ダウンロードが中断されました。";
            WriteProgress(0, 0, 0, "interrupted");
            _downloadCompletionSource?.TrySetResult(false);
            BeginInvoke(new MethodInvoker(Close));
        }
    }

    private void OnDownloadBytesReceivedChanged(object sender, object e)
    {
        if (_currentDownload == null) return;
        try
        {
            dynamic br = _currentDownload.BytesReceived;
            dynamic tt = _currentDownload.TotalBytesToReceive;
            long received = System.Convert.ToInt64(br);
            long total = tt == null ? 0 : System.Convert.ToInt64(tt);
            var pct = total > 0 ? (int)(received * 100 / total) : -1;
            WriteProgress(pct, received, total, "downloading");
        }
        catch { }
    }

    private void WriteProgress(int pct, long received, long total, string status)
    {
        try
        {
            var progressPath = Path.Combine(_options.LogDirectory, "download-progress.json");
            var json = System.Text.Json.JsonSerializer.Serialize(new
            {
                status,
                percent = pct,
                bytesReceived = received,
                bytesTotal = total,
                outputPath = _options.OutputPath,
                updatedAt = DateTimeOffset.Now.ToString("o")
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
            File.WriteAllText(progressPath, json);
        }
        catch { }
    }

    private async Task SyncLibraryAsync(bool manual)
    {
        await SyncSingleLibraryPageAsync();
    }

    private static bool IsBoothLibraryUrl(string url)
    {
        return url.IndexOf("accounts.booth.pm/library", StringComparison.OrdinalIgnoreCase) >= 0;
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
            await WriteAllTextAsync(filePath, htmlJson);

            var meta = new
            {
                savedAt = DateTimeOffset.Now,
                url = _webView.Source?.ToString(),
                htmlJsonLength = htmlJson.Length,
                filePath
            };
            await WriteAllTextAsync(
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

    private static Task<string> ReadAllTextAsync(string path)
    {
        return Task.Run(() => File.ReadAllText(path));
    }

    private static Task WriteAllTextAsync(string path, string contents)
    {
        return Task.Run(() => File.WriteAllText(path, contents));
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
  const downloadSelector = '.js-download-button[data-test=""downloadable""][data-href]';
  const itemLinkSelector = 'a[href*=""/items/""]';
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
  const elementText = element => normalize(element && (element.innerText || element.textContent));
  const productIdForAnchor = anchor => itemIdFromUrl(absoluteUrl(anchor && anchor.getAttribute('href')));
  const productIdsWithin = element => new Set(Array.from(element.querySelectorAll(itemLinkSelector))
    .map(productIdForAnchor)
    .filter(Boolean));

  // 商品リンクから、DLボタンを持ち他商品を含まない最小祖先を商品ブロックとして特定する。
  // 表示用のCSSクラスには依存しない。
  const findProductBlock = (anchor, productId) => {
    let element = anchor.parentElement;
    while (element && element !== document.body) {
      if (element.querySelector(downloadSelector)) {
        const productIds = productIdsWithin(element);
        if (productIds.size === 1 && productIds.has(productId)) {
          return element;
        }
      }
      element = element.parentElement;
    }
    return null;
  };

  // 1個のDLボタンだけを含む最も外側の祖先を、対応するファイル行として使う。
  const findFileRow = (button, block) => {
    let element = button.parentElement;
    let row = null;
    while (element && element !== block) {
      if (element.querySelectorAll(downloadSelector).length === 1) {
        row = element;
      }
      element = element.parentElement;
    }
    return row || button.parentElement;
  };

  const fileNamePattern = /\.(?:unitypackage|zip|png|jpe?g|webp|gif|blend|fbx|obj|vrm|vrca|vpm|rar|7z|tar|gz|txt|pdf|mp3|wav|mp4|mov|avi|psd|ai|svg)$/i;
  const findFileName = (button, block) => {
    const row = findFileRow(button, block);
    if (!row) return '';
    const copy = row.cloneNode(true);
    copy.querySelectorAll(downloadSelector).forEach(element => element.remove());
    const lines = (copy.innerText || copy.textContent || '')
      .split(/\r?\n/)
      .map(normalize)
      .filter(Boolean);
    return lines.find(line => fileNamePattern.test(line)) || normalize(lines.join(' '));
  };

  const itemAnchorsById = new Map();
  for (const anchor of Array.from(document.querySelectorAll(itemLinkSelector))) {
    const productId = productIdForAnchor(anchor);
    if (!productId) continue;
    if (!itemAnchorsById.has(productId)) itemAnchorsById.set(productId, []);
    itemAnchorsById.get(productId).push(anchor);
  }

  const products = [];
  for (const [productId, anchors] of itemAnchorsById) {
    const anchor = anchors.find(item => elementText(item).length > 0) || anchors[0];
    const block = anchors.map(item => findProductBlock(item, productId)).find(Boolean);
    if (!block) continue;

    const href = absoluteUrl(anchor.getAttribute('href'));
    const name = elementText(anchor);
    if (!name) continue;

    const shopAnchor = Array.from(block.querySelectorAll('a[href]'))
      .find(item => {
        const itemHref = absoluteUrl(item.getAttribute('href'));
        return itemHref &&
          !itemIdFromUrl(itemHref) &&
          /(^|\.)booth\.pm$/i.test((new URL(itemHref)).hostname) &&
          elementText(item).length > 0;
      });
    let shopName = elementText(shopAnchor);
    if (shopName === name) shopName = '';

    const image = Array.from(block.querySelectorAll('img'))
      .find(item => productIdForAnchor(item.closest(itemLinkSelector)) === productId) || block.querySelector('img');
    const files = [];
    const seenDownloadUrls = new Set();
    for (const button of Array.from(block.querySelectorAll(downloadSelector))) {
      const downloadUrl = absoluteUrl(button.getAttribute('data-href'));
      if (!downloadUrl || seenDownloadUrls.has(downloadUrl)) continue;
      const fileName = findFileName(button, block);
      if (!fileName) continue;
      const downloadId = (downloadUrl.match(/\/downloadables\/(\d+)/) || ['', ''])[1];
      files.push({
        FileId: downloadId || (productId + ':file:' + files.length),
        Name: fileName,
        SizeText: '',
        Kind: kindFromName(fileName),
        DownloadUrl: downloadUrl
      });
      seenDownloadUrls.add(downloadUrl);
    }

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
  }

  const parserError = itemAnchorsById.size > 0 && products.length === 0
    ? '商品リンクは検出されましたが、商品ブロックを抽出できませんでした。BOOTHのページ構造が変更された可能性があります。'
    : '';
  return {
    SchemaVersion: '1',
    ParserVersion: '2',
    ParserError: parserError,
    SyncedAt: new Date().toLocaleString(),
    SourceUrl: location.href,
    Products: products.slice(0, 200)
  };
})()
";
}

class HostOptions
{
    public string ProfileDirectory { get; private set; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "webview-profile");
    public string LogDirectory { get; private set; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "logs");
    public string DownloadDirectory { get; private set; } = Path.Combine(Path.GetTempPath(), "VRCQuickImporter", "downloads");
    public string InitialUrl { get; private set; } = "https://accounts.booth.pm/users/sign_in";
    public string OutputPath { get; private set; } = string.Empty;
    public bool SyncLibrary { get; private set; }
    public bool ExitAfterSync { get; private set; }
    public bool Headless { get; private set; }
    public int Page { get; private set; } = 1;
    public string DownloadUrl { get; private set; } = string.Empty;
    public bool IsDownloadMode { get; private set; }
    public string RateLimitFilePath { get; private set; } = string.Empty;
    public int MinAccessIntervalMs { get; private set; }
    public bool SkipRateLimit { get; private set; }

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
                case "--page":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var pageNumber) && pageNumber >= 1)
                    {
                        options = options.WithPage(pageNumber);
                    }
                    break;
                case "--download":
                    if (i + 1 < args.Length)
                    {
                        options = options.WithDownloadUrl(args[++i]);
                    }
                    break;
                case "--min-access-interval-ms":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out var intervalMs) && intervalMs >= 0)
                    {
                        options = options.WithMinAccessIntervalMs(intervalMs);
                    }
                    break;
                case "--skip-rate-limit":
                    options = options.WithSkipRateLimit();
                    break;
                case "--profile":
                case "--logs":
                case "--downloads":
                case "--url":
                case "--output":
                case "--rate-limit-file":
                    if (i + 1 >= args.Length) break;
                    var value = args[++i];
                    options = key switch
                    {
                        "--profile" => options.WithProfile(value),
                        "--logs" => options.WithLogs(value),
                        "--downloads" => options.WithDownloads(value),
                        "--url" => options.WithUrl(value),
                        "--output" => options.WithOutput(value),
                        "--rate-limit-file" => options.WithRateLimitFile(value),
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
    private HostOptions WithPage(int value) => Copy(page: value);
    private HostOptions WithDownloadUrl(string value) => Copy(downloadUrl: value, isDownloadMode: true);
    private HostOptions WithRateLimitFile(string value) => Copy(rateLimitFilePath: value);
    private HostOptions WithMinAccessIntervalMs(int value) => Copy(minAccessIntervalMs: value);
    private HostOptions WithSkipRateLimit() => Copy(skipRateLimit: true);

    private HostOptions Copy(
        string profileDirectory = null,
        string logDirectory = null,
        string downloadDirectory = null,
        string initialUrl = null,
        string outputPath = null,
        bool? syncLibrary = null,
        bool? exitAfterSync = null,
        bool? headless = null,
        int? page = null,
        string downloadUrl = null,
        bool? isDownloadMode = null,
        string rateLimitFilePath = null,
        int? minAccessIntervalMs = null,
        bool? skipRateLimit = null) => new()
    {
        ProfileDirectory = profileDirectory ?? ProfileDirectory,
        LogDirectory = logDirectory ?? LogDirectory,
        DownloadDirectory = downloadDirectory ?? DownloadDirectory,
        InitialUrl = initialUrl ?? InitialUrl,
        OutputPath = outputPath ?? OutputPath,
        SyncLibrary = syncLibrary ?? SyncLibrary,
        ExitAfterSync = exitAfterSync ?? ExitAfterSync,
        Headless = headless ?? Headless,
        Page = page ?? Page,
        DownloadUrl = downloadUrl ?? DownloadUrl,
        IsDownloadMode = isDownloadMode ?? IsDownloadMode,
        RateLimitFilePath = rateLimitFilePath ?? RateLimitFilePath,
        MinAccessIntervalMs = minAccessIntervalMs ?? MinAccessIntervalMs,
        SkipRateLimit = skipRateLimit ?? SkipRateLimit
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
}
