using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace VRCQuickImporter.Editor.UI
{
    /// <summary>
    /// VRCQuickImporter のデザイントークン（色・スペーシング・角丸・タイポ）と
    /// 共通スタイルヘルパー。Pro/Personal 両テーマに対応し、
    /// ネストカード（外シェル＋内コア）構造による「物理感のある」見た目を提供する。
    /// </summary>
    internal static class VRCQuickImporterTheme
    {
        // ---- Spacing ----
        public const int SpaceXs = 4;
        public const int SpaceSm = 6;
        public const int SpaceMd = 8;
        public const int SpaceLg = 12;
        public const int SpaceXl = 16;

        // ---- Radius ----
        public const float RadiusCardOuter = 12f;
        public const float RadiusCardInner = 9f;
        public const float RadiusImage = 8f;
        public const float RadiusPill = 100f;
        public const float RadiusChip = 4f;

        // ---- Card fixed heights (align all cards regardless of text length) ----
        public const float CardNameHeight = 38f;
        public const float CardShopHeight = 18f;

        // ---- Typography sizes ----
        public const int FontSection = 15;
        public const int FontCardName = 13;
        public const int FontBody = 12;
        public const int FontShop = 11;
        public const int FontCaption = 10;

        public static bool IsProSkin => EditorGUIUtility.isProSkin;

        // ---- Surfaces / cards ----
        public static Color GridSurface => IsProSkin
            ? new Color(0.145f, 0.145f, 0.145f, 1f)
            : new Color(0.93f, 0.93f, 0.93f, 1f);

        public static Color CardShell => IsProSkin
            ? new Color(0.165f, 0.165f, 0.165f, 1f)
            : new Color(0.965f, 0.965f, 0.965f, 1f);

        public static Color CardCore => IsProSkin
            ? new Color(0.215f, 0.215f, 0.215f, 1f)
            : new Color(1f, 1f, 1f, 1f);

        public static Color CardCoreHover => IsProSkin
            ? new Color(0.250f, 0.250f, 0.250f, 1f)
            : new Color(0.992f, 0.992f, 0.992f, 1f);

        // ---- Borders ----
        public static Color Border => IsProSkin
            ? new Color(1f, 1f, 1f, 0.06f)
            : new Color(0f, 0f, 0f, 0.06f);

        public static Color BorderHover => IsProSkin
            ? new Color(1f, 1f, 1f, 0.18f)
            : new Color(0f, 0f, 0f, 0.16f);

        // ---- Thumbnail ----
        public static Color ThumbnailSurface => IsProSkin
            ? new Color(0.12f, 0.12f, 0.12f, 1f)
            : new Color(0.82f, 0.82f, 0.82f, 1f);

        public static Color ThumbnailText => IsProSkin
            ? new Color(0.50f, 0.50f, 0.50f, 1f)
            : new Color(0.45f, 0.45f, 0.45f, 1f);

        // ---- Text ----
        public static Color TextPrimary => IsProSkin
            ? new Color(0.92f, 0.92f, 0.92f, 1f)
            : new Color(0.10f, 0.10f, 0.10f, 1f);

        public static Color TextMuted => IsProSkin
            ? new Color(0.62f, 0.62f, 0.62f, 1f)
            : new Color(0.38f, 0.38f, 0.38f, 1f);

        // ---- Accent (BOOTH / VRChat 緑) ----
        public static Color Accent => new Color(0.11f, 0.62f, 0.54f, 1f);
        public static Color AccentHover => new Color(0.15f, 0.71f, 0.62f, 1f);
        public static Color AccentFg => Color.white;

        // ---- Semantic ----
        public static Color Price => IsProSkin
            ? new Color(0.95f, 0.30f, 0.42f, 1f)
            : new Color(0.83f, 0.07f, 0.24f, 1f);

        public static Color Like => IsProSkin
            ? new Color(0.96f, 0.32f, 0.42f, 1f)
            : new Color(0.83f, 0.12f, 0.30f, 1f);

        // ---- Chips / pills ----
        public static Color ChipBg => IsProSkin
            ? new Color(1f, 1f, 1f, 0.10f)
            : new Color(0f, 0f, 0f, 0.06f);

        public static Color ChipBgAccent => new Color(0.11f, 0.62f, 0.54f, IsProSkin ? 0.85f : 0.14f);

        public static Color CategoryPillBg => new Color(1f, 1f, 1f, 0.85f);
        public static Color CategoryPillText => new Color(0.2f, 0.2f, 0.2f, 1f);
        public static Color VrchatBadgeBg => Accent;
        public static Color VrchatBadgeText => Color.white;

        // ---- Helpers ----

        public static void SetBorderRadius(VisualElement el, float radius)
        {
            el.style.borderTopLeftRadius = radius;
            el.style.borderTopRightRadius = radius;
            el.style.borderBottomLeftRadius = radius;
            el.style.borderBottomRightRadius = radius;
        }

        public static void SetBorder(VisualElement el, Color color, float width = 1f)
        {
            el.style.borderTopWidth = width;
            el.style.borderRightWidth = width;
            el.style.borderBottomWidth = width;
            el.style.borderLeftWidth = width;
            el.style.borderTopColor = color;
            el.style.borderRightColor = color;
            el.style.borderBottomColor = color;
            el.style.borderLeftColor = color;
        }

        public static VisualElement Spacer(int height)
        {
            var spacer = new VisualElement();
            spacer.style.height = height;
            return spacer;
        }

        /// <summary>外側シェル（薄背景＋薄ボーダー＋パディング）。内コアを載せるトレイとして使う。</summary>
        public static VisualElement MakeShell(float radius = RadiusCardOuter, int padding = SpaceXs)
        {
            var shell = new VisualElement();
            shell.style.backgroundColor = CardShell;
            SetBorder(shell, Border);
            SetBorderRadius(shell, radius);
            shell.style.paddingTop = padding;
            shell.style.paddingBottom = padding;
            shell.style.paddingLeft = padding;
            shell.style.paddingRight = padding;
            return shell;
        }

        /// <summary>内側コア。シェル内に置く実際の内容コンテナ。同心円状の角丸。</summary>
        public static VisualElement MakeCore(float radius = RadiusCardInner, int padding = SpaceMd)
        {
            var core = new VisualElement();
            core.style.backgroundColor = CardCore;
            SetBorderRadius(core, radius);
            core.style.paddingTop = padding;
            core.style.paddingBottom = padding;
            core.style.paddingLeft = padding;
            core.style.paddingRight = padding;
            return core;
        }

        public static Label MakePill(string text, Color bg, Color fg, bool bold, float fontSize = FontCaption)
        {
            var pill = new Label(text);
            pill.tooltip = text;
            BoothFontProvider.Apply(pill, bold ? FontStyle.Bold : FontStyle.Normal);
            pill.style.backgroundColor = bg;
            pill.style.color = fg;
            pill.style.fontSize = fontSize;
            pill.style.paddingLeft = SpaceXs;
            pill.style.paddingRight = SpaceXs;
            pill.style.paddingTop = 2;
            pill.style.paddingBottom = 2;
            SetBorderRadius(pill, RadiusPill);
            return pill;
        }
    }
}
