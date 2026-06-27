using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRCQuickImporter.Editor.Library;
using VRCQuickImporter.Editor.Storage;
using VRCQuickImporter.Editor.WebView;

namespace VRCQuickImporter.Editor.UI
{
    public sealed class VRCQuickImporterWindow : EditorWindow
    {
        private const string MockNotice =
            "この一覧はモックデータ（サンプル）です。BOOTHライブラリとの実際の同期は、今後「ログイン済みのWebView2 helper」から取得する予定です。";

        private const int CardWidth = 228;
        private const int CardPadding = 8;
        private const int CardSpacing = 10;
        private const int ThumbSize = CardWidth - CardPadding * 2;

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

            var syncButton = new Button { text = "BOOTHと同期（未実装）" };
            syncButton.SetEnabled(false);
            syncButton.tooltip = "ログイン済みのWebView2 helperからライブラリを取得します（今後実装予定）";
            headerRow.Add(syncButton);

            section.Add(headerRow);

            var notice = new HelpBox(MockNotice, HelpBoxMessageType.Info);
            notice.style.marginTop = 6;
            section.Add(notice);

            section.Add(Spacer(8));

            section.Add(BuildProductGrid());

            return section;
        }

        private VisualElement BuildProductGrid()
        {
            // CSS Grid相当は使わず、Row + Wrap でBOOTHスキリスト風に折り返す。
            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row;
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.backgroundColor = GridBackgroundColor;
            grid.style.paddingTop = 10;
            grid.style.paddingBottom = 10;
            grid.style.paddingLeft = 10;
            grid.style.paddingRight = 10;
            SetBorderRadius(grid, 8);

            foreach (var product in BoothLibraryMockData.CreateSampleProducts())
            {
                var card = BuildProductCard(product);
                grid.Add(card);
            }

            return grid;
        }

        private static VisualElement BuildProductCard(BoothProduct product)
        {
            var card = new VisualElement();
            card.style.width = CardWidth;
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

            if (!string.IsNullOrEmpty(product.ShopName))
            {
                var shopLabel = new Label(product.ShopName);
                shopLabel.style.color = MutedTextColor;
                shopLabel.style.fontSize = 11;
                shopLabel.style.marginTop = 2;
                body.Add(shopLabel);
            }

            body.Add(BuildPriceLikeRow(product));
            body.Add(Spacer(4));
            body.Add(BuildFileRow(product));

            card.Add(body);
            return card;
        }

        private static VisualElement BuildThumbnail(BoothProduct product)
        {
            var thumbWrap = new VisualElement();
            thumbWrap.style.width = ThumbSize;
            thumbWrap.style.height = ThumbSize;
            thumbWrap.style.backgroundColor = ThumbnailBackgroundColor;
            SetBorderRadius(thumbWrap, 6);

            var placeholder = new Label("サムネ");
            placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
            placeholder.style.color = ThumbnailTextColor;
            placeholder.style.fontSize = 12;
            thumbWrap.Add(placeholder);

            // カテゴリ/バッジをサムネ上にオーバーレイ（BOOTHスキリスト風）
            var badgeRow = new VisualElement();
            badgeRow.style.position = Position.Absolute;
            badgeRow.style.left = 6;
            badgeRow.style.right = 6;
            badgeRow.style.top = 6;
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.justifyContent = Justify.SpaceBetween;

            if (!string.IsNullOrEmpty(product.CategoryLabel))
            {
                badgeRow.Add(MakePill(product.CategoryLabel, CategoryPillBgColor, CategoryPillTextColor, bold: false));
            }

            if (!string.IsNullOrEmpty(product.BadgeText))
            {
                badgeRow.Add(MakePill(product.BadgeText, VrchatBadgeBgColor, Color.white, bold: true));
            }

            thumbWrap.Add(badgeRow);
            return thumbWrap;
        }

        private static VisualElement MakePill(string text, Color bg, Color fg, bool bold)
        {
            var pill = new Label(text);
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
            var nameLabel = new Label(string.IsNullOrEmpty(product.Name) ? "(商品名なし)" : product.Name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.fontSize = 13;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            return nameLabel;
        }

        private static VisualElement BuildPriceLikeRow(BoothProduct product)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = 4;

            var priceLabel = new Label(string.IsNullOrEmpty(product.PriceText) ? "—" : product.PriceText);
            priceLabel.style.color = PriceColor;
            priceLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            priceLabel.style.fontSize = 13;
            priceLabel.style.flexGrow = 1;
            row.Add(priceLabel);

            var likeLabel = new Label("♥ " + product.LikeCount.ToString("N0"));
            likeLabel.style.color = LikeColor;
            likeLabel.style.fontSize = 11;
            likeLabel.style.alignSelf = Align.FlexEnd;
            row.Add(likeLabel);

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
                choices.Add(file.DisplayName);
            }

            var popup = new PopupField<string>(choices, 0);
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

            var heading = new Label("BOOTHログイン（WebView2 helper）");
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.fontSize = 15;
            section.Add(heading);

            var openLoginButton = new Button(WebView2HostLauncher.OpenLogin) { text = "BOOTHログイン画面を開く" };
            section.Add(openLoginButton);

            var closeButton = new Button(WebView2HostLauncher.CloseAll) { text = "WebView2 helperをすべて閉じる" };
            section.Add(closeButton);

            var help = new HelpBox(
                "WebView2はBOOTHログイン専用です。BOOTHのライブラリ画面をUnity内にそのまま表示する方針ではありません。" +
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
    }
}
