using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRCQuickImporter.Editor.Library;
using VRCQuickImporter.Editor.Storage;
using VRCQuickImporter.Editor.Thumbnails;
using VRCQuickImporter.Editor.WebView;
using VRCQuickImporter.Editor.Import;
using Debug = UnityEngine.Debug;

namespace VRCQuickImporter.Editor.UI
{
    internal enum LibrarySyncMode
    {
        Incremental,
        InitialSetup,
        FullRefresh
    }

    public sealed class VRCQuickImporterWindow : EditorWindow
    {
        private Process _librarySyncProcess;
        private DateTime _librarySyncStartedAtUtc;
        private bool _librarySyncInProgress;
        private bool _fullRefreshInProgress;
        private int _fullRefreshCompletedPage;
        private int _fullRefreshProductCount;
        private string _fullRefreshProgressText = string.Empty;
        private ProgressBar _progressBar;
        private Label _progressLabel;
        private bool _showSyncWindow;
        private Vector2 _pendingScrollOffset;
        private bool _needsScrollRestore;
        private int _currentMaxPage;
        private bool _reachedLastPage;
        private int _syncRequestedPage = 1;
        private LibrarySyncMode _syncMode = LibrarySyncMode.Incremental;
        private HashSet<string> _knownProductIdsBeforeSync = new HashSet<string>();
        private readonly List<BoothProduct> _syncCollectedProducts = new List<BoothProduct>();
        private readonly HashSet<string> _syncNewProductIds = new HashSet<string>();
        private int _syncFetchedPageCount;
        private bool _librarySyncWaitingForRateLimit;
        private DateTime _librarySyncLaunchAtUtc;
        private bool _productNameSearchLoaded;
        private string _productNameSearchQuery = string.Empty;
        private Process _productVerificationProcess;
        private BoothProduct _productBeingVerified;
        private BoothDownloadFile _fileBeingVerified;
        private readonly List<int> _productVerificationPages = new List<int>();
        private int _productVerificationPageIndex;
        private string _productVerificationOutputPath = string.Empty;
        private DateTime _productVerificationPageStartedAtUtc;
        private bool _productVerificationInProgress;
        private VisualElement _updateBanner;
        private static bool _updateCheckDone;
        private static string _availableUpdateVersion;

        internal event System.Action<BoothProduct, BoothDownloadFile> OnImportRequested;

        private const float BackgroundSyncTimeoutSeconds = 120f;
        private const float ProductVerificationTimeoutSeconds = 120f;
        private const double BoothLibraryAccessIntervalSeconds = 5.0;
        private const int BoothLibraryPageSize = 10;
        private const string ConfirmedBoothAccessPrefKey = "VRCQuickImporter.confirmedBoothAccess";
        private const string ProductNameSearchPrefKey = "VRCQuickImporter.productNameSearch";
        private const int PreferredCardWidth = 228;
        private const int MinCardWidth = 190;
        private const int MaxCardWidth = 270;
        private const int CardSpacing = 10;
        private const int GridPadding = 10;
        private const int MaxSmartSyncPages = 50;
        private const string BoothProductPageUrl = "https://hellcat.booth.pm/items/8616786";
        private const string GitHubReleasesApiUrl = "https://api.github.com/repos/hellcat-M18/VRCQuickImporter/releases/latest";

        [Serializable]
        private sealed class LibraryExtractionStatus
        {
            public string ParserError = string.Empty;
        }

        [MenuItem("Tools/VRCQuickImporter")]
        public static void Open()
        {
            var window = GetWindow<VRCQuickImporterWindow>();
            window.titleContent = new GUIContent("VRCQuickImporter");
            window.minSize = new Vector2(780, 520);
            window.Show();
        }

        private void CreateGUI()
        {
            _updateBanner = null;
            VRCQuickImporterPaths.EnsureDirectories();

            OnImportRequested = OnImportButtonClicked;

            var root = rootVisualElement;
            BoothFontProvider.ApplyToRoot(root);
            root.style.paddingLeft = 14;
            root.style.paddingRight = 14;
            root.style.paddingTop = 14;
            root.style.paddingBottom = 14;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            root.Add(scroll);

            var container = scroll.contentContainer;
            container.style.paddingBottom = 20;

            container.Add(BuildHeader());
            container.Add(BuildLibrarySection());
            container.Add(BuildAdvancedSection());

            CheckForUpdateAsync();
        }

        private VisualElement BuildHeader()
        {
            var wrap = new VisualElement();

            var title = new Label("VRCQuickImporter");
            BoothFontProvider.Apply(title, FontStyle.Bold);
            title.style.fontSize = 20;
            wrap.Add(title);

            var subtitle = new Label("BOOTHライブラリの取得とUnityへの取り込みを支援します");
            subtitle.style.marginTop = 2;
            wrap.Add(subtitle);

            return wrap;
        }

        private async void CheckForUpdateAsync()
        {
            if (!string.IsNullOrEmpty(_availableUpdateVersion))
            {
                ShowUpdateBanner(_availableUpdateVersion);
            }

            if (_updateCheckDone)
            {
                return;
            }

            // 同じUnityセッション内では、成功・失敗を問わず一度だけ確認する。
            _updateCheckDone = true;
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("VRCQuickImporter");
                var json = await client.GetStringAsync(GitHubReleasesApiUrl);
                var tagName = ExtractJsonString(json, "tag_name");
                if (string.IsNullOrEmpty(tagName) || !tagName.StartsWith("v", StringComparison.Ordinal))
                {
                    return;
                }

                var latestVersion = tagName.Substring(1);
                if (IsNewerVersion(latestVersion, GetCurrentVersion()))
                {
                    _availableUpdateVersion = latestVersion;
                    ShowUpdateBanner(latestVersion);
                }
            }
            catch
            {
                // ネットワークエラー時もツールの通常動作には影響させない。
            }
        }

        private static string GetCurrentVersion()
        {
            try
            {
                var packageRoot = VRCQuickImporterPaths.GetPackageRoot();
                var versionPath = VRCQuickImporterPaths.ToAbsoluteAssetPath(Path.Combine(packageRoot, "VERSION"));
                if (File.Exists(versionPath))
                {
                    return File.ReadAllText(versionPath).Trim();
                }
            }
            catch
            {
                // VERSIONを読めない場合は更新通知を表示しない。
            }

            return null;
        }

        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(key))
            {
                return null;
            }

            var keyIndex = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (keyIndex < 0)
            {
                return null;
            }

            var valueStart = json.IndexOf('"', keyIndex + key.Length + 2);
            if (valueStart < 0)
            {
                return null;
            }

            var valueEnd = json.IndexOf('"', valueStart + 1);
            return valueEnd < 0 ? null : json.Substring(valueStart + 1, valueEnd - valueStart - 1);
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrWhiteSpace(latest) || string.IsNullOrWhiteSpace(current))
            {
                return false;
            }

            try
            {
                return new Version(latest).CompareTo(new Version(current)) > 0;
            }
            catch
            {
                return false;
            }
        }

        private void ShowUpdateBanner(string latestVersion)
        {
            if (_updateBanner != null)
            {
                return;
            }

            var banner = new VisualElement { name = "update-banner" };
            banner.style.flexDirection = FlexDirection.Row;
            banner.style.alignItems = Align.Center;
            banner.style.backgroundColor = VRCQuickImporterTheme.ChipBgAccent;
            VRCQuickImporterTheme.SetBorderRadius(banner, VRCQuickImporterTheme.RadiusCardOuter);
            VRCQuickImporterTheme.SetBorder(banner, VRCQuickImporterTheme.Accent);
            banner.style.paddingTop = VRCQuickImporterTheme.SpaceSm;
            banner.style.paddingBottom = VRCQuickImporterTheme.SpaceSm;
            banner.style.paddingLeft = VRCQuickImporterTheme.SpaceMd;
            banner.style.paddingRight = VRCQuickImporterTheme.SpaceMd;
            banner.style.marginTop = VRCQuickImporterTheme.SpaceMd;

            var icon = new Label("↻");
            icon.style.fontSize = 18;
            icon.style.color = VRCQuickImporterTheme.Accent;
            icon.style.marginRight = VRCQuickImporterTheme.SpaceMd;
            banner.Add(icon);

            var message = new Label($"最新版 v{latestVersion} がリリースされています！");
            BoothFontProvider.Apply(message, FontStyle.Bold);
            message.style.flexGrow = 1;
            message.style.alignSelf = Align.Center;
            banner.Add(message);

            var linkButton = new Button(() => Application.OpenURL(BoothProductPageUrl))
            {
                text = "BOOTHで見る"
            };
            linkButton.style.marginLeft = VRCQuickImporterTheme.SpaceMd;
            linkButton.tooltip = "BOOTH商品ページをブラウザで開きます";
            banner.Add(linkButton);

            var container = rootVisualElement.Q<ScrollView>()?.contentContainer;
            if (container == null)
            {
                return;
            }

            _updateBanner = banner;
            if (container.childCount >= 2)
            {
                container.Insert(1, _updateBanner);
            }
            else
            {
                container.Add(_updateBanner);
            }
        }

        private static void StylePrimarySyncButton(Button button)
        {
            BoothFontProvider.Apply(button, FontStyle.Bold);
            button.style.backgroundColor = VRCQuickImporterTheme.Accent;
            button.style.color = VRCQuickImporterTheme.AccentFg;
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            button.style.marginLeft = VRCQuickImporterTheme.SpaceMd;
            button.style.paddingLeft = VRCQuickImporterTheme.SpaceXl;
            button.style.paddingRight = VRCQuickImporterTheme.SpaceXl;
            button.style.paddingTop = VRCQuickImporterTheme.SpaceSm;
            button.style.paddingBottom = VRCQuickImporterTheme.SpaceSm;
            VRCQuickImporterTheme.SetBorderRadius(button, VRCQuickImporterTheme.RadiusImage);
        }

        private VisualElement BuildLibrarySection()
        {
            var section = new VisualElement();
            section.style.marginTop = 10;

            // 同期中はUI再描画でもインメモリのページ状態を維持し、そうでなければDBから読み直す
            if (!_librarySyncInProgress)
            {
                BoothLibraryStore.TryGetPageState(out _currentMaxPage, out _reachedLastPage);
            }

            EnsureProductNameSearchLoaded();
            var products = MergeProductsForDisplay(BoothLibraryStore.LoadProducts(out var dataState));
            var searchActive = IsProductNameSearchActive() && products.Count > 0;
            var filteredProducts = searchActive ? FilterProductsByName(products) : products;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;

            var heading = new Label("BOOTHライブラリ");
            BoothFontProvider.Apply(heading, FontStyle.Bold);
            heading.style.fontSize = 15;
            heading.style.flexGrow = 1;
            heading.style.alignSelf = Align.Center;
            headerRow.Add(heading);

            var syncButton = new Button(StartLibrarySync)
            {
                text = _librarySyncInProgress ? "同期中..." : (BoothLibraryStore.InitialFullSyncCompleted ? "BOOTHと同期" : "初回セットアップ"),
                name = "sync-button"
            };
            syncButton.SetEnabled(!_librarySyncInProgress);
            syncButton.tooltip = BoothLibraryStore.InitialFullSyncCompleted
                ? "BOOTHライブラリを増分同期します。既存商品と重なるページまで取得します。"
                : "BOOTHライブラリを最後まで取得し、ローカルJSONキャッシュを作成します。";
            StylePrimarySyncButton(syncButton);
            headerRow.Add(syncButton);

            if (_librarySyncInProgress)
            {
                var cancelButton = new Button(CancelLibrarySync)
                {
                    text = "キャンセル",
                    name = "sync-cancel-button"
                };
                cancelButton.tooltip = "進行中の同期を中断します。取得途中のページはローカルJSONキャッシュへ反映しません。";
                cancelButton.style.marginLeft = VRCQuickImporterTheme.SpaceSm;
                headerRow.Add(cancelButton);
            }

            var visibleToggle = new Toggle("同期ウインドウを表示")
            {
                value = _showSyncWindow
            };
            visibleToggle.style.marginLeft = 8;
            visibleToggle.style.alignSelf = Align.Center;
            visibleToggle.RegisterValueChangedCallback(evt => _showSyncWindow = evt.newValue);
            headerRow.Add(visibleToggle);

            section.Add(headerRow);

            section.Add(BuildLibraryStatePanel(products.Count, dataState));

            section.Add(VRCQuickImporterTheme.Spacer(8));
            section.Add(BuildFullRefreshProgress());

            // 完全リフレッシュ中は、古いキャッシュの商品カードを表示しない。
            if (_fullRefreshInProgress)
            {
                return section;
            }

            var grid = BuildProductGrid(filteredProducts, dataState, searchActive);
            section.Add(BuildProductNameSearchPanel(products, grid));
            section.Add(VRCQuickImporterTheme.Spacer(8));
            section.Add(grid);

            return section;
        }

        private VisualElement BuildFullRefreshProgress()
        {
            var container = new VisualElement { name = "sync-progress-container" };

            _progressLabel = new Label { name = "sync-progress-label" };
            BoothFontProvider.Apply(_progressLabel, FontStyle.Bold);
            _progressLabel.style.marginTop = 12;
            _progressLabel.style.marginBottom = 4;
            container.Add(_progressLabel);

            _progressBar = new ProgressBar { name = "sync-progress-bar" };
            _progressBar.lowValue = 0;
            _progressBar.style.height = 24;
            _progressBar.style.marginBottom = 12;
            container.Add(_progressBar);

            if (_fullRefreshInProgress)
            {
                ShowProgressBar(_fullRefreshProgressText);
            }
            else
            {
                HideProgressBar();
            }

            return container;
        }

        private void ShowProgressBar(string text)
        {
            if (_progressBar == null || _progressLabel == null)
            {
                return;
            }

            var estimatedMaxPages = _currentMaxPage > 0 ? _currentMaxPage : MaxSmartSyncPages;
            estimatedMaxPages = Math.Max(estimatedMaxPages, Math.Max(1, _fullRefreshCompletedPage));

            _progressBar.highValue = estimatedMaxPages;
            _progressBar.value = Math.Min(_fullRefreshCompletedPage, estimatedMaxPages);
            _progressBar.style.display = DisplayStyle.Flex;
            _progressLabel.text = string.IsNullOrEmpty(text)
                ? $"ページ {_syncRequestedPage} を取得中..."
                : text;
            _progressLabel.style.display = DisplayStyle.Flex;
        }

        private void UpdateProgressBar(int page, int productCount)
        {
            _fullRefreshCompletedPage = page;
            _fullRefreshProductCount = productCount;
            _fullRefreshProgressText = $"ページ {page} を取得完了（{productCount} 商品）";
            ShowProgressBar(_fullRefreshProgressText);
        }

        private void HideProgressBar()
        {
            if (_progressBar != null)
            {
                _progressBar.style.display = DisplayStyle.None;
            }

            if (_progressLabel != null)
            {
                _progressLabel.style.display = DisplayStyle.None;
            }
        }

        private VisualElement BuildLibraryStatePanel(int productCount, BoothLibraryDataState dataState)
        {
            var shell = VRCQuickImporterTheme.MakeShell(VRCQuickImporterTheme.RadiusCardOuter, VRCQuickImporterTheme.SpaceXs);
            shell.name = "library-state-panel";
            shell.style.marginTop = 8;
            shell.style.marginBottom = 0;

            var core = VRCQuickImporterTheme.MakeCore(VRCQuickImporterTheme.RadiusCardInner, VRCQuickImporterTheme.SpaceLg);
            core.style.flexDirection = FlexDirection.Row;
            core.style.alignItems = Align.Center;
            shell.Add(core);

            var accent = new VisualElement();
            accent.style.width = 4;
            accent.style.height = 48;
            accent.style.flexShrink = 0;
            accent.style.backgroundColor = GetLibraryStateAccent(dataState);
            VRCQuickImporterTheme.SetBorderRadius(accent, 4);
            core.Add(accent);

            var textColumn = new VisualElement();
            textColumn.style.flexGrow = 1;
            textColumn.style.marginLeft = VRCQuickImporterTheme.SpaceLg;
            core.Add(textColumn);

            var title = new Label(GetSimpleLibraryStateTitle(dataState));
            BoothFontProvider.Apply(title, FontStyle.Bold);
            title.style.fontSize = VRCQuickImporterTheme.FontSection;
            title.style.color = VRCQuickImporterTheme.TextPrimary;
            textColumn.Add(title);

            var body = new Label(BuildSimpleLibraryStatusText(productCount, dataState));
            BoothFontProvider.Apply(body, FontStyle.Normal);
            body.style.fontSize = VRCQuickImporterTheme.FontBody;
            body.style.color = VRCQuickImporterTheme.TextMuted;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginTop = VRCQuickImporterTheme.SpaceXs;
            textColumn.Add(body);

            return shell;
        }

        private VisualElement BuildGridStateBlock(BoothLibraryDataState dataState)
        {
            var shell = VRCQuickImporterTheme.MakeShell(VRCQuickImporterTheme.RadiusCardOuter, VRCQuickImporterTheme.SpaceSm);
            shell.style.minHeight = 220;
            shell.style.alignSelf = Align.Stretch;

            var core = VRCQuickImporterTheme.MakeCore(VRCQuickImporterTheme.RadiusCardInner, VRCQuickImporterTheme.SpaceXl);
            core.style.minHeight = 208;
            core.style.alignItems = Align.Center;
            core.style.justifyContent = Justify.Center;
            shell.Add(core);

            var badge = MakeStateChip(GetGridStateBadge(dataState));
            badge.style.marginBottom = VRCQuickImporterTheme.SpaceMd;
            core.Add(badge);

            var title = new Label(GetGridStateTitle(dataState));
            BoothFontProvider.Apply(title, FontStyle.Bold);
            title.style.fontSize = 16;
            title.style.color = VRCQuickImporterTheme.TextPrimary;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            core.Add(title);

            var body = new Label(GetGridStateBody(dataState));
            BoothFontProvider.Apply(body, FontStyle.Normal);
            body.style.fontSize = VRCQuickImporterTheme.FontBody;
            body.style.color = VRCQuickImporterTheme.TextMuted;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.style.marginTop = VRCQuickImporterTheme.SpaceSm;
            body.style.maxWidth = 520;
            core.Add(body);

            return shell;
        }

        private Label MakeStateChip(string text)
        {
            var chip = new Label(text);
            BoothFontProvider.Apply(chip, FontStyle.Bold);
            chip.style.backgroundColor = VRCQuickImporterTheme.ChipBg;
            chip.style.color = VRCQuickImporterTheme.TextMuted;
            chip.style.fontSize = VRCQuickImporterTheme.FontCaption;
            chip.style.paddingTop = 2;
            chip.style.paddingBottom = 2;
            chip.style.paddingLeft = VRCQuickImporterTheme.SpaceSm;
            chip.style.paddingRight = VRCQuickImporterTheme.SpaceSm;
            chip.style.marginLeft = VRCQuickImporterTheme.SpaceXs;
            chip.style.marginBottom = VRCQuickImporterTheme.SpaceXs;
            VRCQuickImporterTheme.SetBorderRadius(chip, VRCQuickImporterTheme.RadiusChip);
            return chip;
        }

        private Color GetLibraryStateAccent(BoothLibraryDataState dataState)
        {
            if (_librarySyncInProgress) return VRCQuickImporterTheme.Accent;
            if (IsLibraryProblemState(dataState)) return VRCQuickImporterTheme.Price;
            if (_reachedLastPage) return VRCQuickImporterTheme.Like;
            return VRCQuickImporterTheme.Accent;
        }

        private string GetSimpleLibraryStateTitle(BoothLibraryDataState dataState)
        {
            if (_librarySyncInProgress) return "同期中";
            if (dataState == BoothLibraryDataState.MissingDatabase) return "未同期";
            if (dataState == BoothLibraryDataState.Error) return "エラー";
            return "同期済み";
        }

        private bool IsLibraryProblemState(BoothLibraryDataState dataState)
        {
            return dataState == BoothLibraryDataState.MissingDatabase
                || dataState == BoothLibraryDataState.Empty
                || dataState == BoothLibraryDataState.Error;
        }

        private string GetGridStateBadge(BoothLibraryDataState dataState)
        {
            if (_librarySyncInProgress) return "取得中";
            if (dataState == BoothLibraryDataState.Error) return "エラー";
            if (dataState == BoothLibraryDataState.Empty) return "0件";
            return "未同期";
        }

        private string GetGridStateTitle(BoothLibraryDataState dataState)
        {
            if (_librarySyncInProgress) return "ライブラリを読み込んでいます";
            if (dataState == BoothLibraryDataState.Error) return "商品一覧を表示できません";
            if (dataState == BoothLibraryDataState.Empty) return "表示できる商品がありません";
            return "BOOTHライブラリを同期してください";
        }

        private string GetGridStateBody(BoothLibraryDataState dataState)
        {
            if (_librarySyncInProgress) return "WebView2 helperがBOOTHの購入履歴ページを取得しています。完了するとここにカードが表示されます。";
            if (dataState == BoothLibraryDataState.Error) return "database.json の読み込みに失敗しました。詳細設定からデータフォルダを開き、必要に応じて再同期してください。";
            if (dataState == BoothLibraryDataState.Empty) return "BOOTH側で購入履歴が見つからない、またはログイン状態が切れている可能性があります。同期ウインドウを表示して再実行すると確認しやすいです。";
            return "右上の「初回セットアップ」から最後のページまで取得し、ローカルJSONキャッシュを作成します。BOOTHアクセスは明示操作ごとに行われます。";
        }

        private string BuildSimpleLibraryStatusText(int productCount, BoothLibraryDataState dataState)
        {
            var lastSyncedAt = GetLastSyncedAtText();
            if (_librarySyncInProgress)
            {
                return "表示中: " + productCount + "件\n最終同期: " + lastSyncedAt;
            }

            if (dataState == BoothLibraryDataState.MissingDatabase)
            {
                return "表示中: 0件\n初回セットアップを開始してください";
            }

            if (dataState == BoothLibraryDataState.Error)
            {
                return "表示中: 0件\n詳細はConsoleまたはダイアログを確認してください";
            }

            return "表示中: " + productCount + "件\n最終同期: " + lastSyncedAt;
        }

        private static string GetLastSyncedAtText()
        {
            var document = BoothLibraryStore.LoadDatabaseDocument();
            return string.IsNullOrWhiteSpace(document?.SyncedAt) ? "-" : document.SyncedAt;
        }

        private void EnsureProductNameSearchLoaded()
        {
            if (_productNameSearchLoaded)
            {
                return;
            }

            _productNameSearchQuery = EditorPrefs.GetString(ProductNameSearchPrefKey, string.Empty);
            _productNameSearchLoaded = true;
        }

        private bool IsProductNameSearchActive()
        {
            return !string.IsNullOrWhiteSpace(_productNameSearchQuery);
        }

        private static List<BoothProduct> MergeProductsForDisplay(IEnumerable<BoothProduct> products)
        {
            var merged = new List<BoothProduct>();
            var representatives = new Dictionary<string, BoothProduct>(StringComparer.Ordinal);

            foreach (var product in products ?? Enumerable.Empty<BoothProduct>())
            {
                if (product == null)
                {
                    continue;
                }

                // database.jsonにはBOOTH上のカードスロットをそのまま保存する。
                // 表示時は同一ProductIdの初出を代表にし、各バリエーションの
                // ダウンロードファイルを代表カードへ統合する。
                if (string.IsNullOrEmpty(product.ProductId))
                {
                    merged.Add(CloneProduct(product));
                    continue;
                }

                if (!representatives.TryGetValue(product.ProductId, out var representative))
                {
                    representative = CloneProduct(product);
                    representatives.Add(product.ProductId, representative);
                    merged.Add(representative);
                    continue;
                }

                MergeDownloadFiles(representative.Files, product.Files);
            }

            return merged;
        }

        private static BoothProduct CloneProduct(BoothProduct source)
        {
            return new BoothProduct
            {
                ProductId = source.ProductId,
                Name = source.Name,
                ShopName = source.ShopName,
                ThumbnailUrl = source.ThumbnailUrl,
                ProductUrl = source.ProductUrl,
                Files = (source.Files ?? new List<BoothDownloadFile>())
                    .Where(file => file != null)
                    .Select(CloneDownloadFile)
                    .ToList(),
                CategoryLabel = source.CategoryLabel,
                BadgeText = source.BadgeText,
                PriceText = source.PriceText,
                LikeCount = source.LikeCount
            };
        }

        private static BoothDownloadFile CloneDownloadFile(BoothDownloadFile source)
        {
            return new BoothDownloadFile
            {
                FileId = source.FileId,
                Name = source.Name,
                SizeText = source.SizeText,
                Kind = source.Kind,
                DownloadUrl = source.DownloadUrl
            };
        }

        private static void MergeDownloadFiles(List<BoothDownloadFile> target, IEnumerable<BoothDownloadFile> incoming)
        {
            if (target == null || incoming == null)
            {
                return;
            }

            foreach (var file in incoming.Where(item => item != null))
            {
                var duplicate = target.Any(existing => existing != null &&
                    ((!string.IsNullOrEmpty(file.FileId) && existing.FileId == file.FileId) ||
                     (!string.IsNullOrEmpty(file.DownloadUrl) && existing.DownloadUrl == file.DownloadUrl)));
                if (!duplicate)
                {
                    target.Add(CloneDownloadFile(file));
                }
            }
        }

        private List<BoothProduct> FilterProductsByName(List<BoothProduct> products)
        {
            if (products == null)
            {
                return new List<BoothProduct>();
            }

            var query = (_productNameSearchQuery ?? string.Empty).Trim();
            if (query.Length == 0)
            {
                return products;
            }

            return products
                .Where(product => !string.IsNullOrEmpty(product?.Name) && product.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
        }

        private VisualElement BuildProductNameSearchPanel(List<BoothProduct> allProducts, VisualElement grid)
        {
            allProducts = allProducts ?? new List<BoothProduct>();

            var panel = VRCQuickImporterTheme.MakeShell(VRCQuickImporterTheme.RadiusCardOuter, VRCQuickImporterTheme.SpaceXs);
            panel.name = "product-name-search-panel";

            var core = VRCQuickImporterTheme.MakeCore(VRCQuickImporterTheme.RadiusCardInner, VRCQuickImporterTheme.SpaceMd);
            core.style.flexDirection = FlexDirection.Row;
            core.style.alignItems = Align.Center;
            panel.Add(core);

            var searchField = new TextField("商品名検索")
            {
                value = _productNameSearchQuery ?? string.Empty
            };
            searchField.name = "product-name-search-field";
            searchField.tooltip = "ローカルJSONキャッシュ内の商品名だけを検索します。BOOTHへの追加アクセスは行いません。";
            searchField.style.flexGrow = 1;
            searchField.style.minHeight = 28;
            BoothFontProvider.Apply(searchField, FontStyle.Normal);
            core.Add(searchField);

            var resultLabel = new Label(BuildSearchResultText(allProducts.Count, FilterProductsByName(allProducts).Count));
            resultLabel.name = "product-name-search-result";
            resultLabel.style.marginLeft = VRCQuickImporterTheme.SpaceMd;
            resultLabel.style.color = VRCQuickImporterTheme.TextMuted;
            resultLabel.style.fontSize = VRCQuickImporterTheme.FontCaption;
            BoothFontProvider.Apply(resultLabel, FontStyle.Normal);
            core.Add(resultLabel);

            var clearButton = new Button(() =>
            {
                _productNameSearchQuery = string.Empty;
                EditorPrefs.DeleteKey(ProductNameSearchPrefKey);
                searchField.SetValueWithoutNotify(string.Empty);
                ApplyProductNameSearchToGrid(allProducts, grid, resultLabel);
            })
            {
                text = "クリア"
            };
            clearButton.name = "product-name-search-clear";
            clearButton.style.marginLeft = VRCQuickImporterTheme.SpaceSm;
            core.Add(clearButton);

            searchField.RegisterValueChangedCallback(evt =>
            {
                _productNameSearchQuery = evt.newValue ?? string.Empty;
                if (string.IsNullOrWhiteSpace(_productNameSearchQuery))
                {
                    EditorPrefs.DeleteKey(ProductNameSearchPrefKey);
                }
                else
                {
                    EditorPrefs.SetString(ProductNameSearchPrefKey, _productNameSearchQuery);
                }

                ApplyProductNameSearchToGrid(allProducts, grid, resultLabel);
            });

            return panel;
        }

        private void ApplyProductNameSearchToGrid(List<BoothProduct> allProducts, VisualElement grid, Label resultLabel)
        {
            var filtered = FilterProductsByName(allProducts);
            resultLabel.text = BuildSearchResultText(allProducts?.Count ?? 0, filtered.Count);

            if (grid?.userData is ProductGridState state)
            {
                state.Products = filtered;
                state.IsNameFiltered = IsProductNameSearchActive() && (allProducts?.Count ?? 0) > 0;
                state.LastColumnCount = -1;
                state.LastCardWidth = -1f;
                RebuildProductGridRows(grid);
            }
        }

        private string BuildSearchResultText(int totalCount, int filteredCount)
        {
            if (!IsProductNameSearchActive() || totalCount <= 0)
            {
                return totalCount + "件";
            }

            return filteredCount + " / " + totalCount + "件";
        }

        private VisualElement BuildProductGrid(List<BoothProduct> products, BoothLibraryDataState dataState, bool isNameFiltered)
        {
            // UI ToolkitのWrap任せだと折り返し境界で余白が不自然になりやすいため、
            // 列数とカード幅をこちらで決め、行単位で明示的に配置する。
            var grid = new VisualElement { name = "product-grid" };
            grid.style.backgroundColor = VRCQuickImporterTheme.GridSurface;
            grid.style.paddingTop = GridPadding;
            grid.style.paddingBottom = GridPadding;
            grid.style.paddingLeft = GridPadding;
            grid.style.paddingRight = GridPadding;
            grid.style.alignSelf = Align.Stretch;
            grid.style.flexGrow = 1;
            VRCQuickImporterTheme.SetBorderRadius(grid, VRCQuickImporterTheme.RadiusImage);

            if ((products == null || products.Count == 0) && !isNameFiltered)
            {
                grid.Add(BuildGridStateBlock(dataState));
                return grid;
            }

            grid.userData = new ProductGridState(products, dataState, isNameFiltered);
            grid.RegisterCallback<GeometryChangedEvent>(_ => RebuildProductGridRows(grid));
            grid.schedule.Execute(() => RebuildProductGridRows(grid));
            return grid;
        }

        private void RebuildProductGridRows(VisualElement grid)
        {
            if (!(grid.userData is ProductGridState state))
            {
                return;
            }

            if (state.Products == null || state.Products.Count == 0)
            {
                grid.Clear();
                grid.Add(state.IsNameFiltered ? BuildNameSearchEmptyBlock() : BuildGridStateBlock(state.DataState));
                if (_needsScrollRestore)
                {
                    RestoreScrollOffsetAfterLayout();
                }
                return;
            }

            var availableWidth = grid.resolvedStyle.width - GridPadding * 2;
            if (availableWidth <= 0)
            {
                // レイアウト未確定: 後の GeometryChangedEvent で再試行される
                return;
            }

            var layout = CalculateProductGridLayout(availableWidth);
            if (state.LastColumnCount == layout.ColumnCount && Mathf.Approximately(state.LastCardWidth, layout.CardWidth))
            {
                // 列数変更なしでも初回スクロール復元が必要な場合
                if (_needsScrollRestore)
                {
                    RestoreScrollOffsetAfterLayout();
                }
                return;
            }

            state.LastColumnCount = layout.ColumnCount;
            state.LastCardWidth = layout.CardWidth;
            HideImportPathOverlays();
            grid.Clear();

            for (var index = 0; index < state.Products.Count;)
            {
                var row = new VisualElement { name = "product-row" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = CardSpacing;
                grid.Add(row);

                for (var column = 0; column < layout.ColumnCount && index < state.Products.Count; column++, index++)
                {
                    var card = ProductCard.Build(
                        state.Products[index],
                        (p, f) => OnImportRequested?.Invoke(p, f),
                        OpenProductPage,
                        OnProductCardDoubleClick);
                    ProductCard.ApplyCardWidth(card, layout.CardWidth);
                    card.style.marginRight = column == layout.ColumnCount - 1 ? 0 : CardSpacing;
                    row.Add(card);
                }
            }

            // 行追加完了後にスクロール位置を復元する
            if (_needsScrollRestore)
            {
                RestoreScrollOffsetAfterLayout();
            }
        }

        private void RestoreScrollOffsetAfterLayout()
        {
            _needsScrollRestore = false;
            var scroll = rootVisualElement.Q<ScrollView>();
            if (scroll == null) return;

            var content = scroll.contentContainer;
            if (content == null) return;

            // contentContainer の GeometryChangedEvent で
            // コンテンツ高さが確定した後にスクロール位置を復元する
            EventCallback<GeometryChangedEvent> handler = null;
            handler = (evt) =>
            {
                content.UnregisterCallback<GeometryChangedEvent>(handler);
                scroll.scrollOffset = _pendingScrollOffset;
            };
            content.RegisterCallback<GeometryChangedEvent>(handler);

            // フォールバック: GeometryChangedEvent が来ない場合に備えて遅延実行も行う
            rootVisualElement.schedule.Execute(() =>
            {
                scroll.scrollOffset = _pendingScrollOffset;
            }).StartingIn(200);
        }

        private VisualElement BuildNameSearchEmptyBlock()
        {
            var shell = VRCQuickImporterTheme.MakeShell(VRCQuickImporterTheme.RadiusCardOuter, VRCQuickImporterTheme.SpaceSm);
            shell.style.minHeight = 180;
            shell.style.alignSelf = Align.Stretch;

            var core = VRCQuickImporterTheme.MakeCore(VRCQuickImporterTheme.RadiusCardInner, VRCQuickImporterTheme.SpaceXl);
            core.style.minHeight = 168;
            core.style.alignItems = Align.Center;
            core.style.justifyContent = Justify.Center;
            shell.Add(core);

            var badge = MakeStateChip("検索結果 0件");
            badge.style.marginBottom = VRCQuickImporterTheme.SpaceMd;
            core.Add(badge);

            var title = new Label("一致する商品名がありません");
            BoothFontProvider.Apply(title, FontStyle.Bold);
            title.style.fontSize = 16;
            title.style.color = VRCQuickImporterTheme.TextPrimary;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            core.Add(title);

            var body = new Label("検索語を短くするか、表記ゆれを変えて試してください。検索はローカルJSONキャッシュ内の商品名だけを対象にしています。");
            BoothFontProvider.Apply(body, FontStyle.Normal);
            body.style.fontSize = VRCQuickImporterTheme.FontBody;
            body.style.color = VRCQuickImporterTheme.TextMuted;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.style.marginTop = VRCQuickImporterTheme.SpaceSm;
            body.style.maxWidth = 520;
            core.Add(body);

            return shell;
        }

        private static ProductGridLayout CalculateProductGridLayout(float availableWidth)
        {
            var maxColumns = Mathf.Max(1, Mathf.FloorToInt((availableWidth + CardSpacing) / (MinCardWidth + CardSpacing)));
            var bestColumns = 1;
            var bestWidth = Mathf.Clamp(availableWidth, MinCardWidth, MaxCardWidth);
            var bestScore = float.MaxValue;

            for (var columns = 1; columns <= maxColumns; columns++)
            {
                var width = Mathf.Floor((availableWidth - CardSpacing * (columns - 1)) / columns);
                if (width < MinCardWidth || width > MaxCardWidth)
                {
                    continue;
                }

                var score = Mathf.Abs(width - PreferredCardWidth);
                if (score < bestScore || Mathf.Approximately(score, bestScore) && columns > bestColumns)
                {
                    bestScore = score;
                    bestColumns = columns;
                    bestWidth = width;
                }
            }

            return new ProductGridLayout(bestColumns, bestWidth);
        }

        private VisualElement BuildAdvancedSection()
        {
            var foldout = new Foldout
            {
                text = "詳細設定 / トラブルシュート",
                value = false
            };
            foldout.style.marginTop = 18;
            foldout.tooltip = "ログイン画面、データフォルダ、ログイン用プロファイル削除などの補助機能です。";

            var summary = new Label("通常は開く必要はありません。ログインし直す、データ保存先を確認する、Helperを閉じる場合に使います。");
            summary.style.marginTop = 4;
            summary.style.marginBottom = 8;
            summary.style.whiteSpace = WhiteSpace.Normal;
            foldout.Add(summary);

            foldout.Add(BuildSyncMaintenanceSection());
            foldout.Add(BuildLoginSection());
            foldout.Add(BuildPathsSection());

            return foldout;
        }

        private VisualElement BuildSyncMaintenanceSection()
        {
            var section = new VisualElement();
            section.style.marginTop = 6;

            var heading = new Label("同期メンテナンス");
            BoothFontProvider.Apply(heading, FontStyle.Bold);
            heading.style.fontSize = 15;
            section.Add(heading);

            var help = new HelpBox(
                "通常は上部の「BOOTHと同期」で十分です。完全リフレッシュはローカルJSONキャッシュを全件取り直して置き換えるため、必要な時だけ使ってください。",
                HelpBoxMessageType.Info);
            help.style.marginTop = 6;
            help.style.marginBottom = 6;
            section.Add(help);

            var fullRefreshButton = new Button(StartFullRefresh)
            {
                text = "完全リフレッシュ（全件取り直し）",
                name = "full-refresh-button"
            };
            fullRefreshButton.SetEnabled(!_librarySyncInProgress && BoothLibraryStore.HasDatabase);
            fullRefreshButton.tooltip = "BOOTHライブラリを最後まで再取得し、ローカルJSONキャッシュを丸ごと置き換えます。";
            section.Add(fullRefreshButton);

            return section;
        }

        private VisualElement BuildLoginSection()
        {
            var section = new VisualElement();
            section.style.marginTop = 6;

            var heading = new Label("BOOTHログイン / Helper");
            BoothFontProvider.Apply(heading, FontStyle.Bold);
            heading.style.fontSize = 15;
            section.Add(heading);

            var openLoginButton = new Button(WebView2HostLauncher.OpenLogin) { text = "BOOTHログイン画面を開く" };
            section.Add(openLoginButton);

            var closeButton = new Button(WebView2HostLauncher.CloseAll) { text = "WebView2 helperをすべて閉じる" };
            section.Add(closeButton);

            var help = new HelpBox(
                "WebView2 helperはBOOTHへのログイン・同期・ダウンロードに使います。BOOTHのライブラリ画面をUnity内にそのまま表示する方針ではありません。" +
                "ログイン情報はプロジェクト内の専用プロファイルに保存されます。",
                HelpBoxMessageType.Info);
            help.style.marginTop = 6;
            section.Add(help);

            return section;
        }

        private VisualElement BuildPathsSection()
        {
            var section = new VisualElement();
            section.style.marginTop = 14;

            var heading = new Label("データ / 設定");
            BoothFontProvider.Apply(heading, FontStyle.Bold);
            heading.style.fontSize = 15;
            section.Add(heading);

            section.Add(VRCQuickImporterTheme.Spacer(4));
            section.Add(MakePathRow("データ保存先", VRCQuickImporterPaths.DataRoot));
            section.Add(MakePathRow("ログイン用プロファイル", VRCQuickImporterPaths.WebViewProfileDirectory));
            section.Add(MakePathRow("ログ", VRCQuickImporterPaths.LogsDirectory));

            section.Add(VRCQuickImporterTheme.Spacer(8));

            var openDataButton = new Button(() => EditorUtility.RevealInFinder(VRCQuickImporterPaths.DataRoot))
            {
                text = "VRCQuickImporterのデータフォルダを開く"
            };
            section.Add(openDataButton);

            var clearProfileButton = new Button(ClearWebViewProfile)
            {
                text = "ログイン用プロファイルを削除（WebViewを閉じてから実行）"
            };
            section.Add(clearProfileButton);

            return section;
        }

        private static VisualElement MakePathRow(string label, string path)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 6;

            var labelElement = new Label(label);
            BoothFontProvider.Apply(labelElement, FontStyle.Bold);
            row.Add(labelElement);

            var pathElement = new TextField { value = path, isReadOnly = true };
            row.Add(pathElement);

            return row;
        }

        private void StartLibrarySync()
        {
            if (_productVerificationInProgress)
            {
                EditorUtility.DisplayDialog("VRCQuickImporter", "商品の確認中です。完了後に再度お試しください。", "OK");
                return;
            }

            if (!ConfirmBoothAccess())
            {
                return;
            }

            if (!BoothLibraryStore.InitialFullSyncCompleted)
            {
                if (!ConfirmInitialSetup())
                {
                    return;
                }

                BeginLibrarySync(LibrarySyncMode.InitialSetup);
                return;
            }

            BeginLibrarySync(LibrarySyncMode.Incremental);
        }

        private void StartFullRefresh()
        {
            if (_productVerificationInProgress)
            {
                EditorUtility.DisplayDialog("VRCQuickImporter", "商品の確認中です。完了後に再度お試しください。", "OK");
                return;
            }

            if (!ConfirmBoothAccess())
            {
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "完全リフレッシュ",
                    "BOOTHライブラリを最後のページまで再取得し、ローカルJSONキャッシュを丸ごと置き換えます。\n\n" +
                    "ページ間は最低5秒待機します。商品数が多い場合は時間がかかります。続行しますか？",
                    "完全リフレッシュ",
                    "キャンセル"))
            {
                return;
            }

            BeginLibrarySync(LibrarySyncMode.FullRefresh);
        }

        private void CancelLibrarySync()
        {
            if (!_librarySyncInProgress)
            {
                return;
            }

            try
            {
                if (_librarySyncProcess != null && !_librarySyncProcess.HasExited)
                {
                    _librarySyncProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 同期キャンセル時のhelper終了に失敗しました: " + ex.Message);
            }

            BoothLibraryStore.DeletePendingPage();
            FinishLibrarySync(GetSyncModeLabel(_syncMode) + "をキャンセルしました。ローカルJSONキャッシュは変更していません。");
        }

        private static bool ConfirmInitialSetup()
        {
            return EditorUtility.DisplayDialog(
                "初回セットアップ",
                "BOOTHライブラリを最後のページまで取得し、このUnityプロジェクト内にローカルJSONキャッシュを作成します。\n\n" +
                "・BOOTH購入履歴の複数ページにアクセスします。\n" +
                "・ページ間は最低5秒待機します。\n" +
                "・取得データは Library/VRCQuickImporter/database.json に保存します。\n" +
                "・以後の同期は基本的に増分確認になります。\n\n続行しますか？",
                "初回セットアップを開始",
                "キャンセル");
        }

        private void BeginLibrarySync(LibrarySyncMode mode)
        {
            if (_librarySyncInProgress)
            {
                return;
            }

            VRCQuickImporterPaths.EnsureDirectories();
            BoothLibraryStore.DeletePendingPage();

            _syncMode = mode;
            _syncRequestedPage = 1;
            _knownProductIdsBeforeSync = BoothLibraryStore.LoadKnownProductIds();
            _syncCollectedProducts.Clear();
            _syncNewProductIds.Clear();
            _syncFetchedPageCount = 0;
            _fullRefreshInProgress = mode == LibrarySyncMode.FullRefresh || mode == LibrarySyncMode.InitialSetup;
            _fullRefreshCompletedPage = 0;
            _fullRefreshProductCount = 0;
            _fullRefreshProgressText = _fullRefreshInProgress ? "ページ 1 を取得中..." : string.Empty;
            _librarySyncInProgress = true;
            _librarySyncProcess = null;
            _librarySyncStartedAtUtc = DateTime.UtcNow;

            var wait = GetRemainingBoothLibraryAccessWait();
            if (wait > TimeSpan.Zero)
            {
                _librarySyncWaitingForRateLimit = true;
                _librarySyncLaunchAtUtc = DateTime.UtcNow.Add(wait);
            }
            else
            {
                _librarySyncWaitingForRateLimit = false;
                _librarySyncLaunchAtUtc = DateTime.MinValue;
                if (!LaunchLibrarySyncProcess())
                {
                    return;
                }
            }

            EditorApplication.update -= PollLibrarySync;
            EditorApplication.update += PollLibrarySync;
            RefreshWindow();
        }

        private bool LaunchLibrarySyncProcess()
        {
            _librarySyncWaitingForRateLimit = false;
            _librarySyncLaunchAtUtc = DateTime.MinValue;
            _librarySyncStartedAtUtc = DateTime.UtcNow;
            if (_fullRefreshInProgress)
            {
                _fullRefreshProgressText = _fullRefreshCompletedPage > 0
                    ? $"ページ {_syncRequestedPage} を取得中...（{_fullRefreshCompletedPage} ページ完了 / {_fullRefreshProductCount} 商品）"
                    : $"ページ {_syncRequestedPage} を取得中...";
                ShowProgressBar(_fullRefreshProgressText);
            }

            _librarySyncProcess = WebView2HostLauncher.StartLibrarySync(VRCQuickImporterPaths.PendingPagePath, headless: !_showSyncWindow, page: _syncRequestedPage);
            if (_librarySyncProcess == null)
            {
                _librarySyncInProgress = false;
                if (_fullRefreshInProgress)
                {
                    HideProgressBar();
                    _fullRefreshInProgress = false;
                    _fullRefreshCompletedPage = 0;
                    _fullRefreshProductCount = 0;
                    _fullRefreshProgressText = string.Empty;
                }
                RefreshWindow();
                return false;
            }

            return true;
        }

        private static string GetSyncModeLabel(LibrarySyncMode mode)
        {
            switch (mode)
            {
                case LibrarySyncMode.InitialSetup:
                    return "初回セットアップ";
                case LibrarySyncMode.FullRefresh:
                    return "完全リフレッシュ";
                default:
                    return "増分同期";
            }
        }

        private static TimeSpan GetRemainingBoothLibraryAccessWait()
        {
            try
            {
                var path = VRCQuickImporterPaths.BoothLibraryAccessStampPath;
                if (!File.Exists(path))
                {
                    return TimeSpan.Zero;
                }

                var text = File.ReadAllText(path).Trim();
                if (!DateTimeOffset.TryParse(text, out var lastAccessUtc))
                {
                    return TimeSpan.Zero;
                }

                var nextAllowedUtc = lastAccessUtc.ToUniversalTime().AddSeconds(BoothLibraryAccessIntervalSeconds);
                var wait = nextAllowedUtc - DateTimeOffset.UtcNow;
                return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private static bool ConfirmBoothAccess()
        {
            if (EditorPrefs.GetBool(ConfirmedBoothAccessPrefKey, false))
            {
                return true;
            }

            var accepted = EditorUtility.DisplayDialog(
                "BOOTH購入履歴へのアクセスについて",
                "VRCQuickImporterはBOOTH（pixiv社）の非公式支援ツールです。\n\n" +
                "・ログイン済みの「あなた自身の購入履歴」にアクセスします。\n" +
                "・取得したデータはこのUnityプロジェクト内にのみ保存します。\n" +
                "・BOOTHの利用規約・ガイドラインを尊重し、常識的な範囲でご利用ください。\n" +
                "・利用は自己責任です。\n\n続行しますか？",
                "続行",
                "キャンセル");

            if (accepted)
            {
                EditorPrefs.SetBool(ConfirmedBoothAccessPrefKey, true);
            }

            return accepted;
        }

        private void PollLibrarySync()
        {
            if (_librarySyncWaitingForRateLimit)
            {
                var remaining = _librarySyncLaunchAtUtc - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    return;
                }

                if (!LaunchLibrarySyncProcess())
                {
                    EditorApplication.update -= PollLibrarySync;
                    return;
                }

                RefreshWindow();
            }

            var elapsed = (float)(DateTime.UtcNow - _librarySyncStartedAtUtc).TotalSeconds;
            var pendingReady = File.Exists(VRCQuickImporterPaths.PendingPagePath) &&
                               File.GetLastWriteTimeUtc(VRCQuickImporterPaths.PendingPagePath) >= _librarySyncStartedAtUtc.AddSeconds(-1);

            if (pendingReady)
            {
                ProcessPendingLibraryPage();
                return;
            }

            if (_librarySyncProcess == null)
            {
                FinishLibrarySync("BOOTHライブラリの取得状態を確認できませんでした。");
                return;
            }

            try
            {
                if (_librarySyncProcess.HasExited)
                {
                    FinishLibrarySync("WebView2 helperが終了しました。データが作成されていない場合は、BOOTHにログイン済みか確認してもう一度お試しください。");
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 同期プロセス監視に失敗しました: " + ex.Message);
                FinishLibrarySync("同期プロセス監視に失敗しました。");
                return;
            }

            if (elapsed > BackgroundSyncTimeoutSeconds)
            {
                try
                {
                    if (!_librarySyncProcess.HasExited)
                    {
                        _librarySyncProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[VRCQuickImporter] 同期プロセスのタイムアウト終了に失敗しました: " + ex.Message);
                }

                FinishLibrarySync("BOOTHライブラリの取得がタイムアウトしました。ログイン状態を確認するため、「同期ウインドウを表示」をオンにして再実行してください。");
            }
        }

        private void ProcessPendingLibraryPage()
        {
            WaitForLibrarySyncProcessExitBriefly();

            var parserError = TryReadLibraryParserError(VRCQuickImporterPaths.PendingPagePath);
            if (!string.IsNullOrEmpty(parserError))
            {
                Debug.LogWarning("[VRCQuickImporter] BOOTHライブラリ抽出エラー: " + parserError);
                BoothLibraryStore.DeletePendingPage();
                FinishLibrarySync("BOOTHライブラリのページ構造を解析できませんでした。既存のローカルJSONキャッシュは変更していません。");
                return;
            }

            var pending = BoothLibraryStore.LoadPendingPageDocument();
            if (pending == null)
            {
                BoothLibraryStore.DeletePendingPage();
                FinishLibrarySync("同期データの読み込みに失敗しました。");
                return;
            }

            var pageProducts = pending.Products ?? new List<BoothProduct>();
            var pageHadProducts = pageProducts.Count > 0;
            var pageIds = pageProducts
                .Where(p => !string.IsNullOrEmpty(p.ProductId))
                .Select(p => p.ProductId)
                .ToList();
            var overlapsKnown = pageIds.Any(id => _knownProductIdsBeforeSync.Contains(id));
            _syncNewProductIds.UnionWith(pageIds.Where(id => !_knownProductIdsBeforeSync.Contains(id)));
            _syncFetchedPageCount = Math.Max(_syncFetchedPageCount, _syncRequestedPage);

            if (_syncMode == LibrarySyncMode.Incremental)
            {
                if (pageHadProducts)
                {
                    _syncCollectedProducts.AddRange(pageProducts);
                }

                BoothLibraryStore.DeletePendingPage();

                if (pageHadProducts && !overlapsKnown && _syncRequestedPage < MaxSmartSyncPages)
                {
                    QueueNextLibrarySyncPage();
                    return;
                }

                if (_syncCollectedProducts.Count > 0)
                {
                    var merged = BoothLibraryStore.MergeProductsIntoDatabase(_syncCollectedProducts, _syncFetchedPageCount);
                    _currentMaxPage = merged.MaxPage;
                    _reachedLastPage = merged.ReachedLastPage;
                }

                if (!pageHadProducts)
                {
                    BoothLibraryStore.UpdatePageState(Math.Max(0, _syncRequestedPage - 1), reachedLast: true);
                    _currentMaxPage = Math.Max(0, _syncRequestedPage - 1);
                    _reachedLastPage = true;
                }

                var reason = overlapsKnown
                    ? "既存商品と重なったため停止しました。"
                    : pageHadProducts
                        ? "安全上限に到達したため停止しました。"
                        : "空ページに到達したため停止しました。";
                FinishLibrarySync("増分同期が完了しました（新規候補 " + _syncNewProductIds.Count + "件 / 確認ページ " + _syncFetchedPageCount + "）。" + reason);
                return;
            }

            if (pageHadProducts)
            {
                _syncCollectedProducts.AddRange(pageProducts);
                if (_fullRefreshInProgress)
                {
                    UpdateProgressBar(_syncRequestedPage, _syncCollectedProducts
                        .Where(product => product != null && !string.IsNullOrEmpty(product.ProductId))
                        .Select(product => product.ProductId)
                        .Distinct()
                        .Count());
                }
            }

            BoothLibraryStore.DeletePendingPage();

            if (pageHadProducts && _syncRequestedPage < MaxSmartSyncPages)
            {
                QueueNextLibrarySyncPage();
                return;
            }

            if (pageHadProducts && _syncRequestedPage >= MaxSmartSyncPages)
            {
                FinishLibrarySync(GetSyncModeLabel(_syncMode) + "を安全上限で停止しました。既存のローカルJSONキャッシュは変更していません（取得済み " + _syncCollectedProducts.Count + "件 / ページ" + _syncRequestedPage + "まで）。");
                return;
            }

            var maxPage = Math.Max(0, _syncRequestedPage - 1);
            if (_syncMode == LibrarySyncMode.FullRefresh && _syncCollectedProducts.Count == 0 && BoothLibraryStore.HasDatabase)
            {
                FinishLibrarySync("完全リフレッシュで商品を取得できなかったため、既存のローカルJSONキャッシュは変更していません。ログイン状態やBOOTH側の表示を確認してください。");
                return;
            }

            var document = BoothLibraryStore.ReplaceDatabaseWithProducts(
                _syncCollectedProducts,
                maxPage,
                initialFullSync: _syncMode == LibrarySyncMode.InitialSetup,
                fullRefresh: _syncMode == LibrarySyncMode.FullRefresh);
            _currentMaxPage = document.MaxPage;
            _reachedLastPage = document.ReachedLastPage;

            FinishLibrarySync(GetSyncModeLabel(_syncMode) + "が完了しました（" + document.Products.Count + "件 / ページ" + document.MaxPage + "まで）。");
        }

        private void QueueNextLibrarySyncPage()
        {
            _syncRequestedPage++;
            _librarySyncProcess = null;

            var wait = GetRemainingBoothLibraryAccessWait();
            if (wait > TimeSpan.Zero)
            {
                _librarySyncWaitingForRateLimit = true;
                _librarySyncLaunchAtUtc = DateTime.UtcNow.Add(wait);
                RefreshWindow();
                return;
            }

            if (!LaunchLibrarySyncProcess())
            {
                EditorApplication.update -= PollLibrarySync;
                return;
            }

            RefreshWindow();
        }

        private void WaitForLibrarySyncProcessExitBriefly()
        {
            try
            {
                if (_librarySyncProcess != null && !_librarySyncProcess.HasExited)
                {
                    _librarySyncProcess.WaitForExit(3000);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 同期プロセス終了待機に失敗しました: " + ex.Message);
            }
        }

        private void FinishLibrarySync(string message)
        {
            EditorApplication.update -= PollLibrarySync;
            _librarySyncInProgress = false;
            _librarySyncWaitingForRateLimit = false;
            _librarySyncLaunchAtUtc = DateTime.MinValue;
            _librarySyncProcess = null;
            if (_fullRefreshInProgress)
            {
                HideProgressBar();
                _fullRefreshInProgress = false;
                _fullRefreshCompletedPage = 0;
                _fullRefreshProductCount = 0;
                _fullRefreshProgressText = string.Empty;
            }
            _syncCollectedProducts.Clear();
            _syncNewProductIds.Clear();
            Debug.Log("[VRCQuickImporter] " + message);

            if (ShouldShowSyncResultDialog(message))
            {
                EditorUtility.DisplayDialog("VRCQuickImporter", message, "OK");
            }

            RefreshWindow();
        }

        private static bool ShouldShowSyncResultDialog(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.Contains("失敗")
                || message.Contains("タイムアウト")
                || message.Contains("確認できません")
                || message.Contains("アクセスできません")
                || message.Contains("作成されていない")
                || message.Contains("取得できなかった")
                || message.Contains("安全上限")
                || message.Contains("終了しました");
        }

        private void RefreshWindow()
        {
            // 再構築でスクロール位置が先頭に戻らないよう、事前に保存して再適用する
            var existing = rootVisualElement.Q<ScrollView>();
            if (existing != null)
            {
                _pendingScrollOffset = existing.scrollOffset;
            }

            rootVisualElement.Clear();
            CreateGUI();

            // RebuildProductGridRows の行追加完了後にスクロール位置を復元する
            _needsScrollRestore = true;
            Repaint();
        }

        private void OnImportButtonClicked(BoothProduct product, BoothDownloadFile file)
        {
            if (!ConfirmBoothAccess())
            {
                return;
            }

            if (_librarySyncInProgress || WebView2HostLauncher.IsRunning)
            {
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "別のBOOTHアクセスが進行中です。完了後に再度お試しください。",
                    "OK");
                return;
            }

            VerifyAndUpdateProductBeforeDownload(product, file);
        }

        private void VerifyAndUpdateProductBeforeDownload(BoothProduct product, BoothDownloadFile file)
        {
            var document = BoothLibraryStore.LoadDatabaseDocument();
            var productIndex = document?.Products?.FindIndex(item => item != null && item.ProductId == product.ProductId) ?? -1;
            if (productIndex < 0)
            {
                ShowProductVerificationGuidance();
                return;
            }

            _productBeingVerified = product;
            _fileBeingVerified = file;
            _productVerificationPages.Clear();
            AddVerificationPage(productIndex / BoothLibraryPageSize + 1);
            AddVerificationPage(productIndex / BoothLibraryPageSize + 2);
            AddVerificationPage(productIndex / BoothLibraryPageSize);
            _productVerificationPageIndex = 0;
            _productVerificationInProgress = true;
            _productVerificationOutputPath = Path.Combine(VRCQuickImporterPaths.CacheDirectory, "download-verification-page.json");

            EditorApplication.update -= PollProductVerification;
            EditorApplication.update += PollProductVerification;
            LaunchProductVerificationPage();
        }

        private void AddVerificationPage(int page)
        {
            if (page >= 1 && !_productVerificationPages.Contains(page))
            {
                _productVerificationPages.Add(page);
            }
        }

        private void LaunchProductVerificationPage()
        {
            if (_productVerificationPageIndex >= _productVerificationPages.Count)
            {
                FinishProductVerification();
                ShowProductVerificationGuidance();
                return;
            }

            try
            {
                if (File.Exists(_productVerificationOutputPath))
                {
                    File.Delete(_productVerificationOutputPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 商品確認用データの削除に失敗しました: " + ex.Message);
            }

            var page = _productVerificationPages[_productVerificationPageIndex];
            EditorUtility.DisplayProgressBar(
                "VRCQuickImporter",
                $"商品情報を確認中...（ページ {page}）",
                (float)_productVerificationPageIndex / _productVerificationPages.Count);
            _productVerificationPageStartedAtUtc = DateTime.UtcNow;
            _productVerificationProcess = WebView2HostLauncher.StartLibrarySync(
                _productVerificationOutputPath,
                headless: true,
                page: page,
                skipRateLimit: _productVerificationPageIndex > 0);
            if (_productVerificationProcess == null)
            {
                FinishProductVerification();
                ShowProductVerificationGuidance();
            }
        }

        private void PollProductVerification()
        {
            if (!_productVerificationInProgress)
            {
                EditorApplication.update -= PollProductVerification;
                return;
            }

            if (IsProductVerificationOutputReady())
            {
                var document = TryLoadProductVerificationPage();
                if (!WaitForProductVerificationProcessExitBriefly() || document == null)
                {
                    FinishProductVerification();
                    ShowProductVerificationGuidance();
                    return;
                }

                var matchingProducts = (document.Products ?? new List<BoothProduct>())
                    .Where(item => item != null && item.ProductId == _productBeingVerified.ProductId)
                    .ToList();
                var updatedProduct = matchingProducts.FirstOrDefault(item =>
                    FindUpdatedDownloadFile(_fileBeingVerified, item.Files) != null) ?? matchingProducts.FirstOrDefault();
                if (updatedProduct != null)
                {
                    ProcessVerifiedProduct(updatedProduct);
                    return;
                }

                _productVerificationPageIndex++;
                LaunchProductVerificationPage();
                return;
            }

            try
            {
                if (_productVerificationProcess == null || _productVerificationProcess.HasExited)
                {
                    FinishProductVerification();
                    ShowProductVerificationGuidance();
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 商品確認プロセスの監視に失敗しました: " + ex.Message);
                FinishProductVerification();
                ShowProductVerificationGuidance();
                return;
            }

            if ((DateTime.UtcNow - _productVerificationPageStartedAtUtc).TotalSeconds > ProductVerificationTimeoutSeconds)
            {
                try
                {
                    if (_productVerificationProcess != null && !_productVerificationProcess.HasExited)
                    {
                        _productVerificationProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[VRCQuickImporter] 商品確認プロセスのタイムアウト終了に失敗しました: " + ex.Message);
                }

                FinishProductVerification();
                ShowProductVerificationGuidance();
            }
        }

        private BoothLibraryDocument TryLoadProductVerificationPage()
        {
            try
            {
                var json = File.ReadAllText(_productVerificationOutputPath);
                var status = JsonUtility.FromJson<LibraryExtractionStatus>(json);
                if (!string.IsNullOrEmpty(status?.ParserError))
                {
                    Debug.LogWarning("[VRCQuickImporter] 商品確認用ライブラリページの抽出エラー: " + status.ParserError);
                    return null;
                }

                return JsonUtility.FromJson<BoothLibraryDocument>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 商品確認用ライブラリページの読み込みに失敗しました: " + ex.Message);
                return null;
            }
        }

        private static string TryReadLibraryParserError(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonUtility.FromJson<LibraryExtractionStatus>(json)?.ParserError ?? string.Empty;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] BOOTHライブラリ抽出状態の読み込みに失敗しました: " + ex.Message);
                return string.Empty;
            }
        }

        private bool IsProductVerificationOutputReady()
        {
            try
            {
                return File.Exists(_productVerificationOutputPath) &&
                       File.GetLastWriteTimeUtc(_productVerificationOutputPath) >= _productVerificationPageStartedAtUtc.AddSeconds(-1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 商品確認用データの状態確認に失敗しました: " + ex.Message);
                return false;
            }
        }

        private bool WaitForProductVerificationProcessExitBriefly()
        {
            try
            {
                if (_productVerificationProcess == null)
                {
                    return false;
                }

                if (!_productVerificationProcess.HasExited)
                {
                    _productVerificationProcess.WaitForExit(3000);
                }

                return _productVerificationProcess.HasExited;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 商品確認プロセスの終了待機に失敗しました: " + ex.Message);
                return false;
            }
        }

        private void ProcessVerifiedProduct(BoothProduct updatedProduct)
        {
            if (!HasCompleteDownloadData(updatedProduct))
            {
                FinishProductVerification();
                ShowProductVerificationGuidance();
                return;
            }

            var updatedFile = FindUpdatedDownloadFile(_fileBeingVerified, updatedProduct.Files);
            // 表示用カードは複数バリエーションのFilesを統合しているため、
            // 確認対象のファイルが取得ページに残っているかだけを判定する。
            var metadataChanged = HasMetadataChanged(_productBeingVerified, updatedProduct);

            BoothLibraryStore.UpsertProductInPlace(updatedProduct, _fileBeingVerified);
            FinishProductVerification();

            if (updatedFile == null)
            {
                RefreshWindow();
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "ファイルの更新が見つかりました。もう一度DL対象を選択してください。",
                    "OK");
                return;
            }

            if (metadataChanged)
            {
                RefreshWindow();
            }

            BoothImportPipeline.StartImport(updatedProduct, updatedFile);
        }

        private static bool HasCompleteDownloadData(BoothProduct product)
        {
            return product != null &&
                   !string.IsNullOrWhiteSpace(product.ProductId) &&
                   product.Files != null &&
                   product.Files.Count > 0 &&
                   product.Files.All(item => item != null &&
                                             !string.IsNullOrWhiteSpace(item.FileId) &&
                                             !string.IsNullOrWhiteSpace(item.Name) &&
                                             !string.IsNullOrWhiteSpace(item.DownloadUrl));
        }

        private static BoothDownloadFile FindUpdatedDownloadFile(BoothDownloadFile requestedFile, IEnumerable<BoothDownloadFile> updatedFiles)
        {
            if (requestedFile == null)
            {
                return null;
            }

            var files = (updatedFiles ?? Enumerable.Empty<BoothDownloadFile>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.DownloadUrl))
                .ToList();
            var byId = files.FirstOrDefault(item => item.FileId == requestedFile.FileId);
            if (byId != null)
            {
                return byId;
            }

            var requestedName = NormalizeDownloadFileName(requestedFile.Name);
            return files.FirstOrDefault(item => item.Kind == requestedFile.Kind &&
                                                NormalizeDownloadFileName(item.Name) == requestedName);
        }

        private static string NormalizeDownloadFileName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
        }

        private static bool HasMetadataChanged(BoothProduct current, BoothProduct updated)
        {
            return current.Name != updated.Name ||
                   current.ShopName != updated.ShopName ||
                   current.ThumbnailUrl != updated.ThumbnailUrl ||
                   current.ProductUrl != updated.ProductUrl ||
                   current.CategoryLabel != updated.CategoryLabel ||
                   current.BadgeText != updated.BadgeText ||
                   current.PriceText != updated.PriceText ||
                   current.LikeCount != updated.LikeCount;
        }

        private void FinishProductVerification()
        {
            EditorApplication.update -= PollProductVerification;
            EditorUtility.ClearProgressBar();
            _productVerificationInProgress = false;
            _productVerificationProcess = null;
            _productBeingVerified = null;
            _fileBeingVerified = null;
            _productVerificationPages.Clear();
            _productVerificationPageIndex = 0;

            try
            {
                if (!string.IsNullOrEmpty(_productVerificationOutputPath) && File.Exists(_productVerificationOutputPath))
                {
                    File.Delete(_productVerificationOutputPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 商品確認用データの削除に失敗しました: " + ex.Message);
            }

            _productVerificationOutputPath = string.Empty;
        }

        private static void ShowProductVerificationGuidance()
        {
            EditorUtility.DisplayDialog(
                "VRCQuickImporter",
                "商品を確認できませんでした。\n\nページ上部の「BOOTHと同期」ボタンで再試行してください。\nそれでも解決しない場合は、詳細設定＞完全リフレッシュをお試しください。",
                "OK");
        }

        private void AbortProductVerification()
        {
            try
            {
                if (_productVerificationProcess != null && !_productVerificationProcess.HasExited)
                {
                    _productVerificationProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 商品確認プロセスの終了に失敗しました: " + ex.Message);
            }

            FinishProductVerification();
        }

        private static void OpenProductPage(BoothProduct product)
        {
            if (string.IsNullOrEmpty(product.ProductUrl))
            {
                return;
            }

            Application.OpenURL(product.ProductUrl);
        }

        private void OnProductCardDoubleClick(BoothProduct product)
        {
            HideImportPathOverlays();

            var paths = BoothImportHistoryStore.GetEntries(product.ProductId)
                .SelectMany(entry => entry.Paths ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(IsExistingAssetPath)
                .ToList();

            if (paths.Count == 0)
            {
                return;
            }

            if (paths.Count == 1)
            {
                PingAssetPath(paths[0]);
                return;
            }

            var cardRoot = rootVisualElement.Q<VisualElement>("product-card-" + product.ProductId);
            if (cardRoot != null)
            {
                ProductCard.ShowImportPathOverlay(cardRoot, paths, PingAssetPath);
            }
        }

        private static bool IsExistingAssetPath(string path)
        {
            return AssetDatabase.IsValidFolder(path) || File.Exists(Path.GetFullPath(path));
        }

        private static void PingAssetPath(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset == null)
            {
                return;
            }

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void HideImportPathOverlays()
        {
            rootVisualElement.Query<VisualElement>(className: "vrcqi-product-card")
                .ForEach(ProductCard.HideImportPathOverlay);
        }

        private void OnDisable()
        {
            HideImportPathOverlays();
            AbortProductVerification();

            try
            {
                if (_librarySyncProcess != null && !_librarySyncProcess.HasExited)
                {
                    _librarySyncProcess.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 同期プロセスの終了に失敗しました: " + ex.Message);
            }
            finally
            {
                _librarySyncProcess = null;
            }

            EditorApplication.update -= PollLibrarySync;
        }

        private static void ClearWebViewProfile()
        {
            if (WebView2HostLauncher.IsRunning)
            {
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "WebView2 helperがまだ動作中です。プロファイルを削除する前に閉じてください。",
                    "OK");
                return;
            }

            if (!Directory.Exists(VRCQuickImporterPaths.WebViewProfileDirectory))
            {
                Debug.Log("[VRCQuickImporter] ログイン用プロファイルフォルダは存在しません。");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "ログイン用プロファイルの削除",
                    "このUnityプロジェクトに保存されているBOOTHのログイン/セッション情報を削除します。続行しますか？",
                    "削除",
                    "キャンセル"))
            {
                return;
            }

            Directory.Delete(VRCQuickImporterPaths.WebViewProfileDirectory, true);
            Directory.CreateDirectory(VRCQuickImporterPaths.WebViewProfileDirectory);
            Debug.Log("[VRCQuickImporter] ログイン用プロファイルを削除しました。");
        }

        private sealed class ProductGridState
        {
            public List<BoothProduct> Products;
            public readonly BoothLibraryDataState DataState;
            public bool IsNameFiltered;
            public int LastColumnCount = -1;
            public float LastCardWidth = -1f;

            public ProductGridState(List<BoothProduct> products, BoothLibraryDataState dataState, bool isNameFiltered)
            {
                Products = products ?? new List<BoothProduct>();
                DataState = dataState;
                IsNameFiltered = isNameFiltered;
            }
        }

        private readonly struct ProductGridLayout
        {
            public readonly int ColumnCount;
            public readonly float CardWidth;

            public ProductGridLayout(int columnCount, float cardWidth)
            {
                ColumnCount = columnCount;
                CardWidth = cardWidth;
            }
        }
    }
}
