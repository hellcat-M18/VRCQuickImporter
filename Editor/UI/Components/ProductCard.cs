using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VRCQuickImporter.Editor.Library;
using VRCQuickImporter.Editor.Thumbnails;

namespace VRCQuickImporter.Editor.UI
{
    /// <summary>
    /// BOOTH商品カードの組み立て。
    /// ネスト構造（外シェル＋内コア）で物理感を出し、
    /// サムネイル・バッジ・ファイル情報チップ・メイン/サブアクション・hover を扱う。
    /// </summary>
    internal static class ProductCard
    {
        // ネスト構造の両側パディング合計（shell border + shell padding + core padding）
        private const float NestingInsetBothSides = 2f * (1f + VRCQuickImporterTheme.SpaceXs) + 2f * VRCQuickImporterTheme.SpaceMd;

        /// <summary>
        /// 商品カードを構築する。
        /// </summary>
        /// <param name="onImport">メインアクション。選択中ファイルを渡す。</param>
        /// <param name="onOpenPage">商品ページを開くサブアクション。null なら表示しない。</param>
        public static VisualElement Build(
            BoothProduct product,
            Action<BoothProduct, BoothDownloadFile> onImport,
            Action<BoothProduct> onOpenPage = null)
        {
            var card = VRCQuickImporterTheme.MakeShell(VRCQuickImporterTheme.RadiusCardOuter, VRCQuickImporterTheme.SpaceXs);

            var core = VRCQuickImporterTheme.MakeCore(VRCQuickImporterTheme.RadiusCardInner, VRCQuickImporterTheme.SpaceMd);
            card.Add(core);

            core.Add(BuildThumbnail(product));
            core.Add(BuildBody(product, onImport, onOpenPage));

            // hover フィードバック: シェルのボーダーと内コア背景を強調
            var defaultCore = VRCQuickImporterTheme.CardCore;
            var hoverCore = VRCQuickImporterTheme.CardCoreHover;
            card.RegisterCallback<MouseEnterEvent>(_ =>
            {
                PaintBorder(card, VRCQuickImporterTheme.BorderHover);
                core.style.backgroundColor = hoverCore;
            });
            card.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                PaintBorder(card, VRCQuickImporterTheme.Border);
                core.style.backgroundColor = defaultCore;
            });

            return card;
        }

        /// <summary>Window 側のカラム計算から呼ばれる。カード幅に合わせてサムネを正方形化する。</summary>
        public static void ApplyCardWidth(VisualElement card, float cardWidth)
        {
            card.style.width = cardWidth;
            var thumbnail = card.Q<VisualElement>("thumbnail");
            if (thumbnail == null) return;

            var size = Mathf.Max(1f, cardWidth - NestingInsetBothSides);
            thumbnail.style.width = size;
            thumbnail.style.height = size;
        }

        private static void PaintBorder(VisualElement el, Color color)
        {
            el.style.borderTopColor = color;
            el.style.borderRightColor = color;
            el.style.borderBottomColor = color;
            el.style.borderLeftColor = color;
        }

        private static VisualElement BuildThumbnail(BoothProduct product)
        {
            var thumbWrap = new VisualElement();
            thumbWrap.name = "thumbnail";
            thumbWrap.style.backgroundColor = VRCQuickImporterTheme.ThumbnailSurface;
            thumbWrap.style.position = Position.Relative;
            thumbWrap.style.overflow = Overflow.Hidden;
            VRCQuickImporterTheme.SetBorderRadius(thumbWrap, VRCQuickImporterTheme.RadiusImage);
            thumbWrap.style.marginBottom = VRCQuickImporterTheme.SpaceSm;

            var placeholder = new Label("サムネ");
            placeholder.style.unityTextAlign = TextAnchor.MiddleCenter;
            placeholder.style.color = VRCQuickImporterTheme.ThumbnailText;
            placeholder.style.fontSize = VRCQuickImporterTheme.FontShop;
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
                if (texture == null || thumbWrap == null) return;
                image.image = texture;
                image.userData = texture;
                image.style.opacity = 1;
                placeholder.style.display = DisplayStyle.None;
            });

            // オーバーレイバッジ（BOOTHスキリスト風）。サムネ右上/左上に配置。
            var badgeRow = new VisualElement();
            badgeRow.style.position = Position.Absolute;
            badgeRow.style.left = VRCQuickImporterTheme.SpaceXs;
            badgeRow.style.right = VRCQuickImporterTheme.SpaceXs;
            badgeRow.style.top = VRCQuickImporterTheme.SpaceXs;
            badgeRow.style.flexDirection = FlexDirection.Row;
            badgeRow.style.justifyContent = Justify.SpaceBetween;

            var categoryLabel = BoothTextUtil.NormalizeCategoryLabel(product.CategoryLabel, product);
            if (!string.IsNullOrEmpty(categoryLabel))
            {
                badgeRow.Add(VRCQuickImporterTheme.MakePill(
                    categoryLabel, VRCQuickImporterTheme.CategoryPillBg, VRCQuickImporterTheme.CategoryPillText, bold: false));
            }

            var badgeText = BoothTextUtil.NormalizeBadgeText(product.BadgeText);
            if (!string.IsNullOrEmpty(badgeText))
            {
                badgeRow.Add(VRCQuickImporterTheme.MakePill(
                    badgeText, VRCQuickImporterTheme.VrchatBadgeBg, VRCQuickImporterTheme.VrchatBadgeText, bold: true));
            }

            thumbWrap.Add(badgeRow);
            return thumbWrap;
        }

        private static VisualElement BuildBody(
            BoothProduct product,
            Action<BoothProduct, BoothDownloadFile> onImport,
            Action<BoothProduct> onOpenPage)
        {
            var body = new VisualElement();

            body.Add(BuildNameLabel(product));

            var shopSlot = new VisualElement();
            shopSlot.style.minHeight = VRCQuickImporterTheme.CardShopHeight;
            shopSlot.style.maxHeight = VRCQuickImporterTheme.CardShopHeight;
            shopSlot.style.marginTop = 2;
            shopSlot.style.overflow = Overflow.Hidden;

            var shopName = BoothTextUtil.SanitizeDisplayText(BoothTextUtil.NormalizeOptionalLabel(product.ShopName));
            var productName = BoothTextUtil.SanitizeDisplayText(BoothTextUtil.NormalizeOptionalLabel(product.Name));
            if (!string.IsNullOrEmpty(shopName) && shopName != productName)
            {
                var shopLabel = new Label(shopName);
                shopLabel.tooltip = shopName;
                BoothFontProvider.Apply(shopLabel, FontStyle.Normal);
                shopLabel.style.color = VRCQuickImporterTheme.TextMuted;
                shopLabel.style.fontSize = VRCQuickImporterTheme.FontShop;
                shopLabel.style.whiteSpace = WhiteSpace.Normal;
                shopLabel.style.overflow = Overflow.Hidden;
                shopSlot.Add(shopLabel);
            }
            body.Add(shopSlot);

            var metaRow = BuildMetaRow(product);
            if (metaRow != null)
            {
                metaRow.style.marginTop = VRCQuickImporterTheme.SpaceSm;
                body.Add(metaRow);
            }

            var chipsRow = BuildFileChipsRow(product);
            if (chipsRow != null)
            {
                chipsRow.style.marginTop = VRCQuickImporterTheme.SpaceSm;
                body.Add(chipsRow);
            }

            body.Add(BuildFileRow(product, onImport, onOpenPage));
            return body;
        }

        private static Label BuildNameLabel(BoothProduct product)
        {
            var rawName = BoothTextUtil.SanitizeDisplayText(string.IsNullOrEmpty(product.Name) ? "(商品名なし)" : product.Name);
            var nameLabel = new Label(rawName);
            nameLabel.tooltip = rawName;
            BoothFontProvider.Apply(nameLabel, FontStyle.Bold);
            nameLabel.style.fontSize = VRCQuickImporterTheme.FontCardName;
            nameLabel.style.color = VRCQuickImporterTheme.TextPrimary;
            nameLabel.style.whiteSpace = WhiteSpace.Normal;
            nameLabel.style.minHeight = VRCQuickImporterTheme.CardNameHeight;
            nameLabel.style.maxHeight = VRCQuickImporterTheme.CardNameHeight;
            nameLabel.style.overflow = Overflow.Hidden;
            return nameLabel;
        }

        private static VisualElement BuildMetaRow(BoothProduct product)
        {
            var hasPrice = !string.IsNullOrEmpty(BoothTextUtil.NormalizePriceText(product.PriceText));
            var hasLike = product.LikeCount > 0;
            if (!hasPrice && !hasLike) return null;

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;

            if (hasPrice)
            {
                var priceLabel = new Label(BoothTextUtil.NormalizePriceText(product.PriceText));
                priceLabel.style.color = VRCQuickImporterTheme.Price;
                BoothFontProvider.Apply(priceLabel, FontStyle.Bold);
                priceLabel.style.fontSize = VRCQuickImporterTheme.FontCardName;
                priceLabel.style.flexGrow = 1;
                row.Add(priceLabel);
            }
            else
            {
                var spacer = new VisualElement();
                spacer.style.flexGrow = 1;
                row.Add(spacer);
            }

            if (hasLike)
            {
                var likeLabel = new Label("♥ " + product.LikeCount.ToString("N0"));
                likeLabel.style.color = VRCQuickImporterTheme.Like;
                likeLabel.style.fontSize = VRCQuickImporterTheme.FontShop;
                likeLabel.style.alignSelf = Align.FlexEnd;
                row.Add(likeLabel);
            }

            return row;
        }

        /// <summary>ファイル数と主要種別（Unityパッケージ/ZIP/画像）のチップ行。</summary>
        private static VisualElement BuildFileChipsRow(BoothProduct product)
        {
            var files = product.Files ?? new List<BoothDownloadFile>();
            if (files.Count == 0) return null;

            bool hasUnityPackage = false;
            bool hasZip = false;
            bool hasImage = false;
            foreach (var file in files)
            {
                switch (file.Kind)
                {
                    case BoothDownloadFileKind.UnityPackage: hasUnityPackage = true; break;
                    case BoothDownloadFileKind.Zip: hasZip = true; break;
                    case BoothDownloadFileKind.Image: hasImage = true; break;
                }
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;

            row.Add(MakeChip(files.Count + "ファイル", VRCQuickImporterTheme.ChipBg, VRCQuickImporterTheme.TextMuted));
            if (hasUnityPackage)
            {
                row.Add(MakeChip("Unityパッケージ", VRCQuickImporterTheme.ChipBgAccent,
                    VRCQuickImporterTheme.IsProSkin ? VRCQuickImporterTheme.AccentFg : VRCQuickImporterTheme.Accent, bold: true));
            }
            if (hasZip) row.Add(MakeChip("ZIP", VRCQuickImporterTheme.ChipBg, VRCQuickImporterTheme.TextMuted));
            if (hasImage) row.Add(MakeChip("画像", VRCQuickImporterTheme.ChipBg, VRCQuickImporterTheme.TextMuted));

            return row;
        }

        private static Label MakeChip(string text, Color bg, Color fg, bool bold = false)
        {
            var chip = new Label(text);
            BoothFontProvider.Apply(chip, bold ? FontStyle.Bold : FontStyle.Normal);
            chip.style.backgroundColor = bg;
            chip.style.color = fg;
            chip.style.fontSize = VRCQuickImporterTheme.FontCaption;
            chip.style.paddingLeft = VRCQuickImporterTheme.SpaceXs;
            chip.style.paddingRight = VRCQuickImporterTheme.SpaceXs;
            chip.style.paddingTop = 2;
            chip.style.paddingBottom = 2;
            chip.style.marginRight = VRCQuickImporterTheme.SpaceXs;
            chip.style.marginTop = 2;
            VRCQuickImporterTheme.SetBorderRadius(chip, VRCQuickImporterTheme.RadiusChip);
            return chip;
        }

        private static VisualElement BuildFileRow(
            BoothProduct product,
            Action<BoothProduct, BoothDownloadFile> onImport,
            Action<BoothProduct> onOpenPage)
        {
            var row = new VisualElement();
            row.style.marginTop = VRCQuickImporterTheme.SpaceSm;

            var files = product.Files ?? new List<BoothDownloadFile>();

            if (files.Count == 0)
            {
                var none = new Label("ダウンロード可能なファイルがありません");
                none.style.color = VRCQuickImporterTheme.TextMuted;
                none.style.fontSize = VRCQuickImporterTheme.FontShop;
                none.style.whiteSpace = WhiteSpace.Normal;
                row.Add(none);
                return row;
            }

            var choices = new List<string>(files.Count);
            foreach (var file in files)
            {
                choices.Add(BoothTextUtil.TruncateForCard(file.DisplayName, 34));
            }

            var popup = new PopupField<string>(choices, 0);
            popup.tooltip = files.Count > 0 ? BoothTextUtil.SanitizeDisplayText(files[0].DisplayName) : string.Empty;
            popup.style.marginBottom = VRCQuickImporterTheme.SpaceMd;
            row.Add(popup);

            var importButton = new Button(() => onImport?.Invoke(product, files[popup.index]))
            {
                text = "ダウンロード＆インポート"
            };
            StylePrimaryButton(importButton);
            var hasUrl = files.Count > 0 && !string.IsNullOrEmpty(files[0].DownloadUrl);
            importButton.SetEnabled(hasUrl);
            importButton.tooltip = hasUrl
                ? "選択したファイルをダウンロードしてUnityへインポートします"
                : "このファイルのダウンロードURLが未取得です。再度ライブラリを同期してください。";
            importButton.style.width = new Length(100, LengthUnit.Percent);
            row.Add(importButton);

            if (onOpenPage != null && !string.IsNullOrEmpty(product.ProductUrl))
            {
                var openPageButton = new Button(() => onOpenPage.Invoke(product))
                {
                    text = "商品ページを開く"
                };
                StyleSubButton(openPageButton);
                openPageButton.tooltip = "BOOTHの商品ページをブラウザで開きます（Ctrl+クリックでバックグラウンドで開きます）";
                openPageButton.style.marginTop = VRCQuickImporterTheme.SpaceMd;
                openPageButton.style.width = new Length(100, LengthUnit.Percent);
                row.Add(openPageButton);
            }

            return row;
        }

        private static void StylePrimaryButton(Button button)
        {
            button.style.backgroundColor = VRCQuickImporterTheme.Accent;
            button.style.color = VRCQuickImporterTheme.AccentFg;
            BoothFontProvider.Apply(button, FontStyle.Bold);
            button.style.fontSize = VRCQuickImporterTheme.FontBody;
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            VRCQuickImporterTheme.SetBorderRadius(button, VRCQuickImporterTheme.RadiusImage);
            button.style.paddingTop = VRCQuickImporterTheme.SpaceXs + 1;
            button.style.paddingBottom = VRCQuickImporterTheme.SpaceXs + 1;

            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                if (button.enabledSelf) button.style.backgroundColor = VRCQuickImporterTheme.AccentHover;
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.backgroundColor = VRCQuickImporterTheme.Accent;
            });
        }

        private static void StyleSubButton(Button button)
        {
            button.style.backgroundColor = VRCQuickImporterTheme.ChipBg;
            button.style.color = VRCQuickImporterTheme.TextMuted;
            BoothFontProvider.Apply(button, FontStyle.Normal);
            button.style.fontSize = VRCQuickImporterTheme.FontCaption;
            button.style.borderTopWidth = 0;
            button.style.borderRightWidth = 0;
            button.style.borderBottomWidth = 0;
            button.style.borderLeftWidth = 0;
            VRCQuickImporterTheme.SetBorderRadius(button, VRCQuickImporterTheme.RadiusImage);
            button.style.paddingTop = VRCQuickImporterTheme.SpaceXs;
            button.style.paddingBottom = VRCQuickImporterTheme.SpaceXs;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;

            button.RegisterCallback<MouseEnterEvent>(_ =>
            {
                button.style.color = VRCQuickImporterTheme.TextPrimary;
            });
            button.RegisterCallback<MouseLeaveEvent>(_ =>
            {
                button.style.color = VRCQuickImporterTheme.TextMuted;
            });
        }
    }
}
