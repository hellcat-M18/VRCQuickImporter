using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRCQuickImporter.Editor.Library;
using VRCQuickImporter.Editor.Storage;
using VRCQuickImporter.Editor.Thumbnails;
using VRCQuickImporter.Editor.WebView;
using Debug = UnityEngine.Debug;

namespace VRCQuickImporter.Editor.UI
{
    public sealed class VRCQuickImporterWindow : EditorWindow
    {
        private Process _librarySyncProcess;
        private DateTime _librarySyncStartedAtUtc;
        private string _libraryStatusOverride = string.Empty;
        private bool _librarySyncInProgress;
        private bool _showSyncWindow;

        private const float BackgroundSyncTimeoutSeconds = 120f;
        private const int PreferredCardWidth = 228;
        private const int MinCardWidth = 190;
        private const int MaxCardWidth = 270;
        private const int CardPadding = 8;
        private const int CardSpacing = 10;
        private const int GridPadding = 10;

        [MenuItem("Tools/VRCQuickImporter/開く")]
        public static void Open()
        {
            var window = GetWindow<VRCQuickImporterWindow>();
            window.titleContent = new GUIContent("VRCQuickImporter");
            window.minSize = new Vector2(780, 520);
            window.Show();
        }

        [MenuItem("Tools/VRCQuickImporter/プロトタイプ/BOOTHログイン画面を開く")]
        public static void OpenBoothLoginPrototype()
        {
            WebView2HostLauncher.OpenLogin();
        }

        private void CreateGUI()
        {
            VRCQuickImporterPaths.EnsureDirectories();

            var root = rootVisualElement;
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
            container.Add(BuildLoginSection());
            container.Add(BuildPathsSection());
        }

        private VisualElement BuildHeader()
        {
            var wrap = new VisualElement();

            var title = new Label("VRCQuickImporter");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 20;
            wrap.Add(title);

            var subtitle = new Label("BOOTHライブラリの取得とUnityへの取り込みを支援します（プロトタイプ）");
            subtitle.style.marginTop = 2;
            wrap.Add(subtitle);

            return wrap;
        }

        private VisualElement BuildLibrarySection()
        {
            var section = new VisualElement();
            section.style.marginTop = 10;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;

            var heading = new Label("BOOTHライブラリ");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 15;
            heading.style.flexGrow = 1;
            heading.style.alignSelf = Align.Center;
            headerRow.Add(heading);

            var syncButton = new Button(StartLibrarySync)
            {
                text = _librarySyncInProgress ? "同期中..." : "BOOTHと同期"
            };
            syncButton.SetEnabled(!_librarySyncInProgress);
            syncButton.tooltip = "ログイン済みのWebView2 helperからBOOTHライブラリの商品候補を取得します";
            headerRow.Add(syncButton);

            var visibleToggle = new Toggle("同期ウインドウを表示")
            {
                value = _showSyncWindow
            };
            visibleToggle.style.marginLeft = 8;
            visibleToggle.style.alignSelf = Align.Center;
            visibleToggle.RegisterValueChangedCallback(evt => _showSyncWindow = evt.newValue);
            headerRow.Add(visibleToggle);

            section.Add(headerRow);

            var products = BoothLibraryStore.LoadProductsOrMock(out var storeStatus);
            var statusText = string.IsNullOrEmpty(_libraryStatusOverride)
                ? storeStatus
                : _libraryStatusOverride + "\n" + storeStatus;

            var notice = new HelpBox(statusText, BoothLibraryStore.HasDatabase ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning);
            notice.style.marginTop = 6;
            section.Add(notice);

            section.Add(Spacer(8));

            section.Add(BuildProductGrid(products));

            return section;
        }

        private VisualElement BuildProductGrid(List<BoothProduct> products)
        {
            // UI ToolkitのWrap任せだと折り返し境界で余白が不自然になりやすいため、
            // 列数とカード幅をこちらで決め、行単位で明示的に配置する。
            var grid = new VisualElement();
            grid.style.backgroundColor = GridBackgroundColor;
            grid.style.paddingTop = GridPadding;
            grid.style.paddingBottom = GridPadding;
            grid.style.paddingLeft = GridPadding;
            grid.style.paddingRight = GridPadding;
            grid.style.alignSelf = Align.Stretch;
            grid.style.flexGrow = 1;
            SetBorderRadius(grid, 8);

            grid.userData = new ProductGridState(products);
            grid.RegisterCallback<GeometryChangedEvent>(_ => RebuildProductGridRows(grid));
            grid.schedule.Execute(() => RebuildProductGridRows(grid));
            return grid;
        }

        private static void RebuildProductGridRows(VisualElement grid)
        {
            if (!(grid.userData is ProductGridState state))
            {
                return;
            }

            var availableWidth = grid.resolvedStyle.width - GridPadding * 2;
            if (availableWidth <= 0)
            {
                return;
            }

            var layout = CalculateProductGridLayout(availableWidth);
            if (state.LastColumnCount == layout.ColumnCount && Mathf.Approximately(state.LastCardWidth, layout.CardWidth))
            {
                return;
            }

            state.LastColumnCount = layout.ColumnCount;
            state.LastCardWidth = layout.CardWidth;
            grid.Clear();

            for (var index = 0; index < state.Products.Count;)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = CardSpacing;
                grid.Add(row);

                for (var column = 0; column < layout.ColumnCount && index < state.Products.Count; column++, index++)
                {
                    var card = BuildProductCard(state.Products[index]);
                    ApplyCardSize(card, layout.CardWidth);
                    card.style.marginRight = column == layout.ColumnCount - 1 ? 0 : CardSpacing;
                    row.Add(card);
                }
            }
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

        private static void ApplyCardSize(VisualElement card, float cardWidth)
        {
            card.style.width = cardWidth;
            var thumbnail = card.Q<VisualElement>("thumbnail");
            if (thumbnail == null)
            {
                return;
            }

            var thumbnailSize = Mathf.Max(1, cardWidth - CardPadding * 2);
            thumbnail.style.width = thumbnailSize;
            thumbnail.style.height = thumbnailSize;
        }

        private static VisualElement BuildProductCard(BoothProduct product)
        {
            var card = new VisualElement();
            card.style.width = PreferredCardWidth;
            card.style.marginRight = CardSpacing;
            card.style.marginBottom = CardSpacing;
            card.style.paddingTop = CardPadding;
            card.style.paddingBottom = CardPadding;
            card.style.paddingLeft = CardPadding;
            card.style.paddingRight = CardPadding;
            card.style.backgroundColor = CardBackgroundColor;
            SetBorderRadius(card, 8);

            card.Add(BuildThumbnail(product));

            var body = new VisualElement();
            body.style.marginTop = 6;

            body.Add(BuildNameLabel(product));

            var shopName = NormalizeOptionalLabel(product.ShopName);
            if (!string.IsNullOrEmpty(shopName) && shopName != NormalizeOptionalLabel(product.Name))
            {
                var shopLabel = new Label(shopName);
                shopLabel.tooltip = shopName;
                shopLabel.style.color = MutedTextColor;
                shopLabel.style.fontSize = 11;
                shopLabel.style.marginTop = 2;
                shopLabel.style.whiteSpace = WhiteSpace.Normal;
                shopLabel.style.maxHeight = 32;
                shopLabel.style.overflow = Overflow.Hidden;
                body.Add(shopLabel);
            }

            var priceLikeRow = BuildPriceLikeRow(product);
            if (priceLikeRow != null)
            {
                body.Add(priceLikeRow);
                body.Add(Spacer(4));
            }
            body.Add(BuildFileRow(product));

            card.Add(body);
            return card;
        }

        private static VisualElement BuildThumbnail(BoothProduct product)
        {
            var thumbWrap = new VisualElement();
            thumbWrap.name = "thumbnail";
            thumbWrap.style.width = PreferredCardWidth - CardPadding * 2;
            thumbWrap.style.height = PreferredCardWidth - CardPadding * 2;
            thumbWrap.style.backgroundColor = ThumbnailBackgroundColor;
            thumbWrap.style.position = Position.Relative;
            thumbWrap.style.overflow = Overflow.Hidden;
            SetBorderRadius(thumbWrap, 6);

            var placeholder = new Label("サムネ");
            placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
            placeholder.style.color = ThumbnailTextColor;
            placeholder.style.fontSize = 12;
            placeholder.style.position = Position.Absolute;
            placeholder.style.left = 0;
            placeholder.style.top = 0;
            placeholder.style.right = 0;
            placeholder.style.bottom = 0;
            thumbWrap.Add(placeholder);

            var image = new Image();
            image.scaleMode = ScaleMode.ScaleAndCrop;
            image.style.position = Position.Absolute;
            image.style.left = 0;
            image.style.top = 0;
            image.style.right = 0;
            image.style.bottom = 0;
            image.style.opacity = 0;
            thumbWrap.Add(image);

            BoothThumbnailCache.GetTexture(product.ThumbnailUrl, texture =>
            {
                if (texture == null || thumbWrap == null)
                {
                    return;
                }

                image.image = texture;
                image.userData = texture;
                image.style.opacity = 1;
                placeholder.style.display = DisplayStyle.None;
            });

            // カテゴリ/バッジをサムネ上にオーバーレイ（BOOTHスキリスト風）
            var badgeRow = new VisualElement();
            badgeRow.style.position = Position.Absolute;
            badgeRow.style.left = 6;
            badgeRow.style.right = 6;
            badgeRow.style.top = 6;
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.justifyContent = Justify.SpaceBetween;

            var categoryLabel = NormalizeCategoryLabel(product.CategoryLabel, product);
            if (!string.IsNullOrEmpty(categoryLabel))
            {
                badgeRow.Add(MakePill(categoryLabel, CategoryPillBgColor, CategoryPillTextColor, bold: false));
            }

            var badgeText = NormalizeBadgeText(product.BadgeText);
            if (!string.IsNullOrEmpty(badgeText))
            {
                badgeRow.Add(MakePill(badgeText, VrchatBadgeBgColor, Color.white, bold: true));
            }

            thumbWrap.Add(badgeRow);
            return thumbWrap;
        }

        private static VisualElement MakePill(string text, Color bg, Color fg, bool bold)
        {
            var pill = new Label(text);
            pill.tooltip = text;
            pill.style.backgroundColor = bg;
            pill.style.color = fg;
            pill.style.fontSize = 10;
            pill.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            pill.style.paddingLeft = 6;
            pill.style.paddingRight = 6;
            pill.style.paddingTop = 2;
            pill.style.paddingBottom = 2;
            SetBorderRadius(pill, 4);
            return pill;
        }

        private static VisualElement BuildNameLabel(BoothProduct product)
        {
            var name = string.IsNullOrEmpty(product.Name) ? "(商品名なし)" : product.Name;
            var nameLabel = new Label(name);
            nameLabel.tooltip = name;
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.fontSize = 13;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.style.maxHeight = 52;
            nameLabel.style.overflow = Overflow.Hidden;
            return nameLabel;
        }

        private static VisualElement BuildPriceLikeRow(BoothProduct product)
        {
            var hasPrice = !string.IsNullOrEmpty(NormalizePriceText(product.PriceText));
            var hasLike = product.LikeCount > 0;
            if (!hasPrice && !hasLike)
            {
                return null;
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4;

            var priceLabel = new Label(hasPrice ? NormalizePriceText(product.PriceText) : string.Empty);
            priceLabel.style.color = PriceColor;
            priceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            priceLabel.style.fontSize = 13;
            priceLabel.style.flexGrow = 1;
            row.Add(priceLabel);

            if (hasLike)
            {
                var likeLabel = new Label("♥ " + product.LikeCount.ToString("N0"));
                likeLabel.style.color = LikeColor;
                likeLabel.style.fontSize = 11;
                likeLabel.style.alignSelf = Align.FlexEnd;
                row.Add(likeLabel);
            }

            return row;
        }

        private static VisualElement BuildFileRow(BoothProduct product)
        {
            var row = new VisualElement();

            var files = product.Files ?? new List<BoothDownloadFile>();

            if (files.Count == 0)
            {
                var none = new Label("ダウンロード可能なファイルがありません");
                none.style.color = MutedTextColor;
                none.style.fontSize = 11;
                none.style.whiteSpace = WhiteSpace.Normal;
                row.Add(none);
                return row;
            }

            var choices = new List<string>(files.Count);
            foreach (var file in files)
            {
                choices.Add(TruncateForCard(file.DisplayName, 34));
            }

            var popup = new PopupField<string>(choices, 0);
            popup.tooltip = files.Count > 0 ? files[0].DisplayName : string.Empty;
            popup.style.marginBottom = 4;
            row.Add(popup);

            var importButton = new Button { text = "インポート（未実装）" };
            importButton.SetEnabled(false);
            importButton.tooltip = "選択したファイルをUnityへインポートします（今後実装予定）";
            row.Add(importButton);

            return row;
        }

        private VisualElement BuildLoginSection()
        {
            var section = new VisualElement();
            section.style.marginTop = 18;

            var heading = new Label("BOOTHログイン / 同期（WebView2 helper）");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 15;
            section.Add(heading);

            var openLoginButton = new Button(WebView2HostLauncher.OpenLogin) { text = "BOOTHログイン画面を開く" };
            section.Add(openLoginButton);

            var closeButton = new Button(WebView2HostLauncher.CloseAll) { text = "WebView2 helperをすべて閉じる" };
            section.Add(closeButton);

            var help = new HelpBox(
                "WebView2 helperはBOOTHログインと同期処理にのみ使います。BOOTHのライブラリ画面をUnity内にそのまま表示する方針ではありません。" +
                "ログイン情報はプロジェクト内の専用プロファイルに保存されます。",
                HelpBoxMessageType.Info);
            help.style.marginTop = 6;
            section.Add(help);

            return section;
        }

        private VisualElement BuildPathsSection()
        {
            var section = new VisualElement();
            section.style.marginTop = 18;

            var heading = new Label("データ / 設定");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 15;
            section.Add(heading);

            section.Add(Spacer(4));
            section.Add(MakePathRow("データ保存先", VRCQuickImporterPaths.DataRoot));
            section.Add(MakePathRow("ログイン用プロファイル", VRCQuickImporterPaths.WebViewProfileDirectory));
            section.Add(MakePathRow("ログ", VRCQuickImporterPaths.LogsDirectory));

            section.Add(Spacer(8));

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
            labelElement.style.unityFontStyleAndWeight = FontStyle.Bold;
            row.Add(labelElement);

            var pathElement = new TextField { value = path, isReadOnly = true };
            row.Add(pathElement);

            return row;
        }

        private static VisualElement Spacer(int height)
        {
            var spacer = new VisualElement();
            spacer.style.height = height;
            return spacer;
        }

        private static void SetBorderRadius(VisualElement el, float radius)
        {
            el.style.borderTopLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderBottomRightRadius = radius;
        }

        private void StartLibrarySync()
        {
            if (_librarySyncInProgress)
            {
                return;
            }

            VRCQuickImporterPaths.EnsureDirectories();
            _librarySyncStartedAtUtc = DateTime.UtcNow;
            _libraryStatusOverride = _showSyncWindow
                ? "WebView2 helperでBOOTHライブラリ同期を開始しました。ログイン画面が出た場合はログイン後、helper内の「同期」を押してください。"
                : "BOOTHライブラリをバックグラウンドで同期中...";
            _librarySyncProcess = WebView2HostLauncher.StartLibrarySync(VRCQuickImporterPaths.DatabasePath, headless: !_showSyncWindow);
            if (_librarySyncProcess == null)
            {
                _libraryStatusOverride = "WebView2 helperの起動に失敗しました。";
                RefreshWindow();
                return;
            }

            _librarySyncInProgress = true;
            EditorApplication.update -= PollLibrarySync;
            EditorApplication.update += PollLibrarySync;
            RefreshWindow();
        }

        private void PollLibrarySync()
        {
            var elapsed = (float)(DateTime.UtcNow - _librarySyncStartedAtUtc).TotalSeconds;
            var databaseUpdated = File.Exists(VRCQuickImporterPaths.DatabasePath) &&
                                  File.GetLastWriteTimeUtc(VRCQuickImporterPaths.DatabasePath) >= _librarySyncStartedAtUtc.AddSeconds(-1);

            if (databaseUpdated)
            {
                FinishLibrarySync("BOOTHライブラリ同期が完了しました。");
                return;
            }

            if (_librarySyncProcess == null)
            {
                FinishLibrarySync("BOOTHライブラリ同期の状態を確認できませんでした。", keepOverride: true);
                return;
            }

            try
            {
                if (_librarySyncProcess.HasExited)
                {
                    FinishLibrarySync("WebView2 helperが終了しました。同期データが作成されていない場合は、ログイン後にもう一度同期してください。", keepOverride: true);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] 同期プロセス監視に失敗しました: " + ex.Message);
                FinishLibrarySync("同期プロセス監視に失敗しました。", keepOverride: true);
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

                FinishLibrarySync("BOOTHライブラリ同期がタイムアウトしました。ログイン状態を確認するため、「同期ウインドウを表示」をオンにして再実行してください。", keepOverride: true);
            }
        }

        private void FinishLibrarySync(string message, bool keepOverride = false)
        {
            EditorApplication.update -= PollLibrarySync;
            _librarySyncInProgress = false;
            _librarySyncProcess = null;
            _libraryStatusOverride = keepOverride ? message : string.Empty;
            Debug.Log("[VRCQuickImporter] " + message);
            RefreshWindow();
        }

        private void RefreshWindow()
        {
            rootVisualElement.Clear();
            CreateGUI();
            Repaint();
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollLibrarySync;
        }

        private static string NormalizeOptionalLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeCategoryLabel(string value, BoothProduct product)
        {
            value = NormalizeOptionalLabel(value);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > 16) return string.Empty;
            if (value == NormalizeOptionalLabel(product.Name)) return string.Empty;
            if (value == NormalizeOptionalLabel(product.ShopName)) return string.Empty;
            if (value.Contains(".")) return string.Empty;
            return value;
        }

        private static string NormalizeBadgeText(string value)
        {
            value = NormalizeOptionalLabel(value);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > 12) return string.Empty;
            return value;
        }

        private static string NormalizePriceText(string value)
        {
            value = NormalizeOptionalLabel(value);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > 24) return string.Empty;
            if (value.Contains("¥") || value.Contains("￥") || value.Contains("無料")) return value;
            return string.Empty;
        }

        private static string TruncateForCard(string value, int maxLength)
        {
            value = NormalizeOptionalLabel(value);
            if (value.Length <= maxLength) return value;
            if (maxLength <= 1) return "…";
            return value.Substring(0, maxLength - 1) + "…";
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

        private static bool IsProSkin => EditorGUIUtility.isProSkin;

        private static Color GridBackgroundColor => IsProSkin
            ? new Color(0.16f, 0.16f, 0.16f, 1f)
            : new Color(0.94f, 0.94f, 0.94f, 1f);

        private static Color CardBackgroundColor => IsProSkin
            ? new Color(0.23f, 0.23f, 0.23f, 1f)
            : new Color(1f, 1f, 1f, 1f);

        private static Color ThumbnailBackgroundColor => IsProSkin
            ? new Color(0.13f, 0.13f, 0.13f, 1f)
            : new Color(0.82f, 0.82f, 0.82f, 1f);

        private static Color ThumbnailTextColor => IsProSkin
            ? new Color(0.55f, 0.55f, 0.55f, 1f)
            : new Color(0.45f, 0.45f, 0.45f, 1f);

        private static Color MutedTextColor => IsProSkin
            ? new Color(0.62f, 0.62f, 0.62f, 1f)
            : new Color(0.35f, 0.35f, 0.35f, 1f);

        private static Color PriceColor => new Color(0.83f, 0.07f, 0.24f, 1f);

        private static Color LikeColor => new Color(0.83f, 0.12f, 0.30f, 1f);

        private static Color CategoryPillBgColor => new Color(1f, 1f, 1f, 0.85f);

        private static Color CategoryPillTextColor => new Color(0.2f, 0.2f, 0.2f, 1f);

        private static Color VrchatBadgeBgColor => new Color(0.11f, 0.62f, 0.54f, 1f);

        private sealed class ProductGridState
        {
            public readonly List<BoothProduct> Products;
            public int LastColumnCount = -1;
            public float LastCardWidth = -1f;

            public ProductGridState(List<BoothProduct> products)
            {
                Products = products ?? new List<BoothProduct>();
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
