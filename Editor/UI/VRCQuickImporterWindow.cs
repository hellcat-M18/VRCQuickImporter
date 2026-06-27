using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public sealed class VRCQuickImporterWindow : EditorWindow
    {
        private Process _librarySyncProcess;
        private DateTime _librarySyncStartedAtUtc;
        private string _libraryStatusOverride = string.Empty;
        private bool _librarySyncInProgress;
        private bool _showSyncWindow;
        private Vector2 _pendingScrollOffset;
        private bool _needsScrollRestore;
        private int _currentMaxPage;
        private bool _reachedLastPage;
        private int _syncRequestedPage = 1;
        private bool _syncReplace = true;
        private bool _librarySyncWaitingForRateLimit;
        private DateTime _librarySyncLaunchAtUtc;

        internal event System.Action<BoothProduct, BoothDownloadFile> OnImportRequested;

        private const float BackgroundSyncTimeoutSeconds = 120f;
        private const double BoothLibraryAccessIntervalSeconds = 2.0;
        private const string ConfirmedBoothAccessPrefKey = "VRCQuickImporter.confirmedBoothAccess";
        private const int PreferredCardWidth = 228;
        private const int MinCardWidth = 190;
        private const int MaxCardWidth = 270;
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
        }

        private VisualElement BuildHeader()
        {
            var wrap = new VisualElement();

            var title = new Label("VRCQuickImporter");
            BoothFontProvider.Apply(title, FontStyle.Bold);
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

            // 同期中はUI再描画でもインメモリのページ状態を維持し、そうでなければDBから読み直す
            if (!_librarySyncInProgress)
            {
                BoothLibraryStore.TryGetPageState(out _currentMaxPage, out _reachedLastPage);
            }

            var products = BoothLibraryStore.LoadProducts(out var storeStatus, out var dataState);

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
                text = _librarySyncInProgress ? "同期中..." : "BOOTHと同期",
                name = "sync-button"
            };
            syncButton.SetEnabled(!_librarySyncInProgress);
            syncButton.tooltip = "BOOTHライブラリの1ページ目を再取得します（表示内容はリセットされます）。";
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

            section.Add(BuildLibraryStatePanel(products.Count, storeStatus, dataState));

            section.Add(VRCQuickImporterTheme.Spacer(8));

            section.Add(BuildProductGrid(products, dataState));

            section.Add(BuildLoadMoreButton(dataState));

            return section;
        }

        private VisualElement BuildLoadMoreButton(BoothLibraryDataState dataState)
        {
            var wrap = new VisualElement();
            wrap.style.flexDirection = FlexDirection.Column;
            wrap.style.alignItems = Align.Center;
            wrap.style.marginTop = 12;
            wrap.style.marginBottom = 4;

            var nextMaxPage = Mathf.Max(1, _currentMaxPage) + 1;
            var loadMoreButton = new Button(StartLoadMore)
            {
                text = _librarySyncInProgress ? "取得中..." : "もっと読み込む",
                name = "load-more-button"
            };
            loadMoreButton.SetEnabled(!_librarySyncInProgress && dataState == BoothLibraryDataState.Ready && !_reachedLastPage);
            loadMoreButton.tooltip = _reachedLastPage
                ? "これ以降ページはありません。"
                : dataState != BoothLibraryDataState.Ready
                    ? "まずBOOTHライブラリを同期してください。"
                    : "BOOTHライブラリの「次のページ（" + nextMaxPage + "ページ目）」を読み込みます。";
            wrap.Add(loadMoreButton);

            var caption = new Label(BuildLoadMoreCaption(dataState, nextMaxPage)) { name = "load-more-caption" };
            BoothFontProvider.Apply(caption, FontStyle.Normal);
            caption.style.marginTop = VRCQuickImporterTheme.SpaceXs;
            caption.style.fontSize = VRCQuickImporterTheme.FontCaption;
            caption.style.color = VRCQuickImporterTheme.TextFaint;
            caption.style.unityTextAlign = TextAnchor.MiddleCenter;
            wrap.Add(caption);

            return wrap;
        }

        private string BuildLoadMoreCaption(BoothLibraryDataState dataState, int nextMaxPage)
        {
            if (_librarySyncInProgress) return "取得完了までお待ちください。";
            if (dataState == BoothLibraryDataState.MissingDatabase) return "まず「BOOTHと同期」で1ページ目を取得してください。";
            if (dataState == BoothLibraryDataState.Empty) return "取得済みデータに商品がないため、追加読み込みはできません。";
            if (dataState == BoothLibraryDataState.Error) return "データ読み込みエラーを解消してから再試行してください。";
            if (_reachedLastPage) return "これ以降のページはありません。";
            return "次はページ" + nextMaxPage + "を取得します。";
        }

        private VisualElement BuildLibraryStatePanel(int productCount, string storeStatus, BoothLibraryDataState dataState)
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
            accent.style.height = 54;
            accent.style.flexShrink = 0;
            accent.style.backgroundColor = GetLibraryStateAccent(dataState);
            VRCQuickImporterTheme.SetBorderRadius(accent, 4);
            core.Add(accent);

            var textColumn = new VisualElement();
            textColumn.style.flexGrow = 1;
            textColumn.style.marginLeft = VRCQuickImporterTheme.SpaceLg;
            core.Add(textColumn);

            var eyebrow = new Label(GetLibraryStateEyebrow(dataState));
            BoothFontProvider.Apply(eyebrow, FontStyle.Bold);
            eyebrow.style.fontSize = 9;
            eyebrow.style.color = GetLibraryStateAccent(dataState);
            textColumn.Add(eyebrow);

            var title = new Label(GetLibraryStateTitle(dataState));
            BoothFontProvider.Apply(title, FontStyle.Bold);
            title.style.fontSize = VRCQuickImporterTheme.FontSection;
            title.style.color = VRCQuickImporterTheme.TextPrimary;
            title.style.marginTop = 1;
            textColumn.Add(title);

            var body = new Label(BuildLibraryStatusText(productCount, storeStatus));
            BoothFontProvider.Apply(body, FontStyle.Normal);
            body.style.fontSize = VRCQuickImporterTheme.FontBody;
            body.style.color = VRCQuickImporterTheme.TextMuted;
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.marginTop = VRCQuickImporterTheme.SpaceXs;
            textColumn.Add(body);

            var meta = new VisualElement();
            meta.style.flexDirection = FlexDirection.Row;
            meta.style.flexWrap = Wrap.Wrap;
            meta.style.justifyContent = Justify.FlexEnd;
            meta.style.marginLeft = VRCQuickImporterTheme.SpaceLg;
            meta.style.flexShrink = 0;
            meta.Add(MakeStateChip(productCount + "件"));
            if (_currentMaxPage > 0)
            {
                meta.Add(MakeStateChip("ページ" + _currentMaxPage));
            }
            if (_reachedLastPage)
            {
                meta.Add(MakeStateChip("全件取得済み"));
            }
            core.Add(meta);

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

        private string GetLibraryStateEyebrow(BoothLibraryDataState dataState)
        {
            if (_librarySyncInProgress) return _librarySyncWaitingForRateLimit ? "WAITING" : "SYNCING";
            if (dataState == BoothLibraryDataState.MissingDatabase) return "GET STARTED";
            if (dataState == BoothLibraryDataState.Empty) return "EMPTY";
            if (dataState == BoothLibraryDataState.Error) return "ERROR";
            return _reachedLastPage ? "COMPLETE" : "READY";
        }

        private string GetLibraryStateTitle(BoothLibraryDataState dataState)
        {
            if (_librarySyncInProgress) return _librarySyncWaitingForRateLimit ? "BOOTHアクセス間隔を調整中" : "BOOTHライブラリを取得中";
            if (dataState == BoothLibraryDataState.MissingDatabase) return "まだ同期していません";
            if (dataState == BoothLibraryDataState.Empty) return "同期済みですが商品が見つかりません";
            if (dataState == BoothLibraryDataState.Error) return "ライブラリデータの確認が必要です";
            return _reachedLastPage ? "全件取得済み" : "同期済みライブラリ";
        }

        private bool IsLibraryProblemState(BoothLibraryDataState dataState)
        {
            if (dataState == BoothLibraryDataState.MissingDatabase || dataState == BoothLibraryDataState.Empty || dataState == BoothLibraryDataState.Error)
            {
                return true;
            }

            if (string.IsNullOrEmpty(_libraryStatusOverride)) return false;
            return _libraryStatusOverride.Contains("失敗")
                || _libraryStatusOverride.Contains("タイムアウト")
                || _libraryStatusOverride.Contains("終了しました")
                || _libraryStatusOverride.Contains("確認できません");
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
            return "右上の「BOOTHと同期」から1ページ目を取得します。BOOTHアクセスは明示操作ごとに行われ、自動巡回はしません。";
        }

        private string BuildLibraryStatusText(int productCount, string storeStatus)
        {
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(_libraryStatusOverride))
            {
                lines.Add(_libraryStatusOverride);
            }

            if (BoothLibraryStore.HasDatabase)
            {
                var pagePart = _reachedLastPage
                    ? "（全件取得済み）"
                    : (_currentMaxPage > 0 ? "（ページ" + _currentMaxPage + "まで）" : "");
                lines.Add("表示中: " + productCount + "件" + pagePart);
            }

            lines.Add(storeStatus);
            return string.Join("\n", lines);
        }

        private VisualElement BuildProductGrid(List<BoothProduct> products, BoothLibraryDataState dataState)
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

            if (products == null || products.Count == 0)
            {
                grid.Add(BuildGridStateBlock(dataState));
                return grid;
            }

            grid.userData = new ProductGridState(products);
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
            grid.Clear();

            for (var index = 0; index < state.Products.Count;)
            {
                var row = new VisualElement { name = "product-row" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = CardSpacing;
                grid.Add(row);

                for (var column = 0; column < layout.ColumnCount && index < state.Products.Count; column++, index++)
                {
                    var card = ProductCard.Build(state.Products[index], (p, f) => OnImportRequested?.Invoke(p, f), OpenProductPage);
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

            foldout.Add(BuildLoginSection());
            foldout.Add(BuildPathsSection());

            return foldout;
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
            if (!ConfirmBoothAccess())
            {
                return;
            }

            BeginLibrarySync(page: 1, replace: true);
        }

        private void StartLoadMore()
        {
            if (!ConfirmBoothAccess())
            {
                return;
            }

            BeginLibrarySync(page: Mathf.Max(1, _currentMaxPage) + 1, replace: false);
        }

        private void BeginLibrarySync(int page, bool replace)
        {
            if (_librarySyncInProgress)
            {
                return;
            }

            VRCQuickImporterPaths.EnsureDirectories();

            try
            {
                if (File.Exists(VRCQuickImporterPaths.PendingPagePath))
                {
                    File.Delete(VRCQuickImporterPaths.PendingPagePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] pending-page.json のクリアに失敗しました: " + ex.Message);
            }

            _syncRequestedPage = page;
            _syncReplace = replace;
            _librarySyncInProgress = true;
            _librarySyncProcess = null;
            _librarySyncStartedAtUtc = DateTime.UtcNow;

            var wait = GetRemainingBoothLibraryAccessWait();
            if (wait > TimeSpan.Zero)
            {
                _librarySyncWaitingForRateLimit = true;
                _librarySyncLaunchAtUtc = DateTime.UtcNow.Add(wait);
                _libraryStatusOverride = $"BOOTHへの次のアクセスまで {wait.TotalSeconds:F1} 秒待機中...";
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

            // 「もっと読み込む」の場合は商品グリッドを維持し、ボタン状態だけ更新する
            if (!replace)
            {
                var syncBtn = rootVisualElement.Q<Button>("sync-button");
                if (syncBtn != null)
                {
                    syncBtn.text = "同期中...";
                    syncBtn.SetEnabled(false);
                }
                var loadMoreBtn = rootVisualElement.Q<Button>("load-more-button");
                if (loadMoreBtn != null)
                {
                    loadMoreBtn.text = "取得中...";
                    loadMoreBtn.SetEnabled(false);
                }
            }
            else
            {
                // ページ1再取得は全再構築
                RefreshWindow();
            }
        }

        private bool LaunchLibrarySyncProcess()
        {
            _librarySyncWaitingForRateLimit = false;
            _librarySyncLaunchAtUtc = DateTime.MinValue;
            _librarySyncStartedAtUtc = DateTime.UtcNow;
            _libraryStatusOverride = (_showSyncWindow ? "WebView2 helperで" : "バックグラウンドで") + "BOOTHライブラリ " + _syncRequestedPage + "ページ目を取得中...";

            _librarySyncProcess = WebView2HostLauncher.StartLibrarySync(VRCQuickImporterPaths.PendingPagePath, headless: !_showSyncWindow, page: _syncRequestedPage);
            if (_librarySyncProcess == null)
            {
                _librarySyncInProgress = false;
                _libraryStatusOverride = "WebView2 helperの起動に失敗しました。";
                RefreshWindow();
                return false;
            }

            return true;
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
                    _libraryStatusOverride = $"BOOTHへの次のアクセスまで {remaining.TotalSeconds:F1} 秒待機中...";
                    return;
                }

                if (!LaunchLibrarySyncProcess())
                {
                    EditorApplication.update -= PollLibrarySync;
                    return;
                }

                if (_syncReplace)
                {
                    RefreshWindow();
                }
            }

            var elapsed = (float)(DateTime.UtcNow - _librarySyncStartedAtUtc).TotalSeconds;
            var pendingReady = File.Exists(VRCQuickImporterPaths.PendingPagePath) &&
                               File.GetLastWriteTimeUtc(VRCQuickImporterPaths.PendingPagePath) >= _librarySyncStartedAtUtc.AddSeconds(-1);

            if (pendingReady)
            {
                var merge = BoothLibraryStore.MergePendingPage(_syncRequestedPage, _syncReplace);
                _currentMaxPage = merge.MaxPage;
                _reachedLastPage = merge.ReachedLastPage;

                string message;
                if (_syncReplace)
                {
                    message = "BOOTHライブラリの同期が完了しました（" + merge.TotalProducts + "件）。";
                }
                else
                {
                    message = "もっと読み込むが完了しました（" + merge.TotalProducts + "件 / ページ" + _syncRequestedPage + "まで）。";
                    if (!merge.PageHadProducts)
                    {
                        message += " これ以降ページはありません。";
                    }
                }

                FinishLibrarySync(message);
                return;
            }

            if (_librarySyncProcess == null)
            {
                FinishLibrarySync("BOOTHライブラリの取得状態を確認できませんでした。", keepOverride: true);
                return;
            }

            try
            {
                if (_librarySyncProcess.HasExited)
                {
                    FinishLibrarySync("WebView2 helperが終了しました。データが作成されていない場合は、BOOTHにログイン済みか確認してもう一度お試しください。", keepOverride: true);
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

                FinishLibrarySync("BOOTHライブラリの取得がタイムアウトしました。ログイン状態を確認するため、「同期ウインドウを表示」をオンにして再実行してください。", keepOverride: true);
            }
        }

        private void FinishLibrarySync(string message, bool keepOverride = true)
        {
            EditorApplication.update -= PollLibrarySync;
            _librarySyncInProgress = false;
            _librarySyncWaitingForRateLimit = false;
            _librarySyncLaunchAtUtc = DateTime.MinValue;
            _librarySyncProcess = null;
            _libraryStatusOverride = keepOverride ? message : string.Empty;
            Debug.Log("[VRCQuickImporter] " + message);

            if (!_syncReplace)
            {
                // もっと読み込む: 既存グリッドにappend、ボタンとステータスだけ更新
                AppendNewProducts();
            }
            else
            {
                RefreshWindow();
            }
        }

        private void AppendNewProducts()
        {
            // データベースから最新の商品リストを取得
            var products = BoothLibraryStore.LoadProducts(out var storeStatus, out var dataState);

            // ボタン状態を更新
            var syncBtn = rootVisualElement.Q<Button>("sync-button");
            if (syncBtn != null)
            {
                syncBtn.text = "BOOTHと同期";
                syncBtn.SetEnabled(true);
            }

            var loadMoreBtn = rootVisualElement.Q<Button>("load-more-button");
            if (loadMoreBtn != null)
            {
                loadMoreBtn.text = "もっと読み込む";
                loadMoreBtn.SetEnabled(dataState == BoothLibraryDataState.Ready && !_reachedLastPage);
                loadMoreBtn.tooltip = _reachedLastPage
                    ? "これ以降ページはありません。"
                    : dataState != BoothLibraryDataState.Ready
                        ? "まずBOOTHライブラリを同期してください。"
                        : "BOOTHライブラリの「次のページ（" + (Mathf.Max(1, _currentMaxPage) + 1) + "ページ目）」を読み込みます。";
            }

            var loadMoreCaption = rootVisualElement.Q<Label>("load-more-caption");
            if (loadMoreCaption != null)
            {
                loadMoreCaption.text = BuildLoadMoreCaption(dataState, Mathf.Max(1, _currentMaxPage) + 1);
            }

            var statePanel = rootVisualElement.Q<VisualElement>("library-state-panel");
            if (statePanel?.parent != null)
            {
                var parent = statePanel.parent;
                var indexInParent = parent.IndexOf(statePanel);
                parent.Remove(statePanel);
                parent.Insert(indexInParent, BuildLibraryStatePanel(products.Count, storeStatus, dataState));
            }

            // グリッドに新商品をappend
            var grid = rootVisualElement.Q<VisualElement>("product-grid");
            if (grid == null || !(grid.userData is ProductGridState state))
            {
                RefreshWindow();
                return;
            }

            var oldCount = state.Products.Count;
            if (products.Count <= oldCount)
            {
                // 新商品がない場合は何もしない
                return;
            }

            var newProducts = products.GetRange(oldCount, products.Count - oldCount);
            state.Products.Clear();
            state.Products.AddRange(products);

            var availableWidth = grid.resolvedStyle.width - GridPadding * 2;
            if (availableWidth <= 0)
            {
                // レイアウト未確定なら次フレームで再構築
                state.LastColumnCount = 0;
                RebuildProductGridRows(grid);
                return;
            }

            var layout = CalculateProductGridLayout(availableWidth);

            // 最後の行を見つけて残りスロットを埋める
            var rows = grid.Children().Where(c => c.name == "product-row").ToList();
            var lastRow = rows.Count > 0 ? rows[rows.Count - 1] : null;
            var cardsInLastRow = lastRow?.childCount ?? 0;

            var index = 0;

            // 最後の行の残りを埋める
            if (lastRow != null && cardsInLastRow < layout.ColumnCount)
            {
                for (var col = cardsInLastRow; col < layout.ColumnCount && index < newProducts.Count; col++, index++)
                {
                    var card = ProductCard.Build(newProducts[index], (p, f) => OnImportRequested?.Invoke(p, f), OpenProductPage);
                    ProductCard.ApplyCardWidth(card, layout.CardWidth);
                    card.style.marginRight = col == layout.ColumnCount - 1 ? 0 : CardSpacing;
                    lastRow.Add(card);
                }
            }

            // 残りを新しい行で追加
            while (index < newProducts.Count)
            {
                var row = new VisualElement { name = "product-row" };
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = CardSpacing;
                grid.Add(row);

                for (var col = 0; col < layout.ColumnCount && index < newProducts.Count; col++, index++)
                {
                    var card = ProductCard.Build(newProducts[index], (p, f) => OnImportRequested?.Invoke(p, f), OpenProductPage);
                    ProductCard.ApplyCardWidth(card, layout.CardWidth);
                    card.style.marginRight = col == layout.ColumnCount - 1 ? 0 : CardSpacing;
                    row.Add(card);
                }
            }

            // サムネイルは ProductCard.Build 内で取得される
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

            BoothImportPipeline.StartImport(product, file);
        }

        private static void OpenProductPage(BoothProduct product)
        {
            if (string.IsNullOrEmpty(product.ProductUrl))
            {
                return;
            }

            Application.OpenURL(product.ProductUrl);
        }

        private void OnDisable()
        {
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
