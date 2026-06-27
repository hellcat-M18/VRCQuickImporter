using System.Collections.Generic;

namespace VRCQuickImporter.Editor.Library
{
    /// <summary>
    /// BOOTHのダウンロードファイル種別。UI表示のヒント用途。
    /// </summary>
    internal enum BoothDownloadFileKind
    {
        Unknown,
        UnityPackage,
        Zip,
        Image,
        Other
    }

    /// <summary>
    /// BOOTH商品に紐づくダウンロードファイルのモデル。
    /// 現段階ではモックUI用。実取得機能の実装後に実データを流し込む想定。
    /// </summary>
    internal sealed class BoothDownloadFile
    {
        public string FileId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string SizeText { get; set; } = string.Empty;
        public BoothDownloadFileKind Kind { get; set; } = BoothDownloadFileKind.Unknown;

        /// <summary>将来の実DL用。現時点では未使用。</summary>
        public string DownloadUrl { get; set; } = string.Empty;

        public string DisplayName => string.IsNullOrEmpty(SizeText)
            ? Name
            : Name + " (" + SizeText + ")";
    }

    /// <summary>
    /// BOOTH商品モデル。現段階ではモックUI用。
    /// </summary>
    internal sealed class BoothProduct
    {
        public string ProductId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ShopName { get; set; } = string.Empty;

        /// <summary>将来のサムネイル表示用。現時点では未使用（ネットワーク取得はしない）。</summary>
        public string ThumbnailUrl { get; set; } = string.Empty;

        public string ProductUrl { get; set; } = string.Empty;
        public List<BoothDownloadFile> Files { get; set; } = new List<BoothDownloadFile>();

        /// <summary>BOOTHのカテゴリ表示名（例: 「3D衣装」「3Dキャラクター」）。モックUI用。</summary>
        public string CategoryLabel { get; set; } = string.Empty;

        /// <summary>プラットフォーム/対象バッジ（例: 「VRCHAT」）。モックUI用。</summary>
        public string BadgeText { get; set; } = "VRCHAT";

        /// <summary>価格表示文字列（例: 「¥3,300」「無料」）。モックUI用。</summary>
        public string PriceText { get; set; } = string.Empty;

        /// <summary>いいね数。モックUI用。</summary>
        public int LikeCount { get; set; }
    }
}
