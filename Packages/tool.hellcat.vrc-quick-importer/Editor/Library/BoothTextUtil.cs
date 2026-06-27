using System.Text;

namespace VRCQuickImporter.Editor.Library
{
    /// <summary>
    /// BOOTH由来文字列のUI表示向け整形ユーティリティ。
    /// サニタイズ（絵文字除去）・ラベル正規化・トランケートを担う。
    /// Window/Card の双方から参照されるためモデル層に置く。
    /// </summary>
    internal static class BoothTextUtil
    {
        public static string NormalizeOptionalLabel(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public static string NormalizeCategoryLabel(string value, BoothProduct product)
        {
            value = NormalizeOptionalLabel(value);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > 16) return string.Empty;
            if (value == NormalizeOptionalLabel(product.Name)) return string.Empty;
            if (value == NormalizeOptionalLabel(product.ShopName)) return string.Empty;
            if (value.Contains(".")) return string.Empty;
            return value;
        }

        public static string NormalizeBadgeText(string value)
        {
            value = NormalizeOptionalLabel(value);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > 12) return string.Empty;
            return value;
        }

        public static string NormalizePriceText(string value)
        {
            value = NormalizeOptionalLabel(value);
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Length > 24) return string.Empty;
            if (value.Contains("¥") || value.Contains("￥") || value.Contains("無料")) return value;
            return string.Empty;
        }

        public static string TruncateForCard(string value, int maxLength)
        {
            value = SanitizeDisplayText(NormalizeOptionalLabel(value));
            if (value.Length <= maxLength) return value;
            if (maxLength <= 1) return "…";
            return value.Substring(0, maxLength - 1) + "…";
        }

        /// <summary>
        /// UI Toolkitで描画できない絵文字（カラー絵文字・制御文字）を取り除き、豆腐（□）を防ぐ。
        /// BMP内の一般的な記号（★♪など）は保持する。
        /// </summary>
        public static string SanitizeDisplayText(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var ch = value[i];
                var code = (int)ch;

                // バリエーションセレクタ / ゼロ幅文字（単独では描画されない、絵文字表示制御用）
                if (code == 0xFE0E || code == 0xFE0F || code == 0xFEFF) continue;
                if (code >= 0x200B && code <= 0x200D) continue;

                // サロゲートペア（第1面外の絵文字等）
                if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    var codePoint = char.ConvertToUtf32(ch, value[i + 1]);
                    if (IsUnsupportedEmojiCodePoint(codePoint))
                    {
                        i++; // 下位サロゲートも読み飛ばす
                        continue;
                    }

                    builder.Append(ch);
                    builder.Append(value[i + 1]);
                    i++;
                    continue;
                }

                builder.Append(ch);
            }

            return builder.ToString();
        }

        private static bool IsUnsupportedEmojiCodePoint(int codePoint)
        {
            return (codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF) // 国旗
                || (codePoint >= 0x1F300 && codePoint <= 0x1FAFF); // 絵文字・ピクトグラム
        }
    }
}
