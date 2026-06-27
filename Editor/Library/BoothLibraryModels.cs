using System;
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
    /// database.json のトップレベル。
    /// JsonUtility互換のため public field で保持する。
    /// </summary>
    [Serializable]
    internal sealed class BoothLibraryDocument
    {
        public string SchemaVersion = "1";
        public string SyncedAt = string.Empty;
        public string SourceUrl = string.Empty;
        public List<BoothProduct> Products = new List<BoothProduct>();

        /// <summary>ローカルJSONキャッシュが取得済みの最大ページ番号。</summary>
        public int MaxPage = 0;

        /// <summary>最終ページまで取得済みの場合 true。</summary>
        public bool ReachedLastPage = false;

        /// <summary>初回フルキャッシュ作成が完了している場合 true。</summary>
        public bool InitialFullSyncCompleted = false;

        /// <summary>初回フルキャッシュ作成の完了日時。</summary>
        public string InitialFullSyncCompletedAt = string.Empty;

        /// <summary>完全リフレッシュの完了日時。</summary>
        public string LastFullRefreshAt = string.Empty;
    }

    /// <summary>
    /// BOOTH商品に紐づくダウンロードファイルのモデル。
    /// </summary>
    [Serializable]
    internal sealed class BoothDownloadFile
    {
        public string FileId = string.Empty;
        public string Name = string.Empty;
        public string SizeText = string.Empty;
        public BoothDownloadFileKind Kind = BoothDownloadFileKind.Unknown;

        /// <summary>将来の実DL用。現時点では未使用。</summary>
        public string DownloadUrl = string.Empty;

        public string DisplayName => string.IsNullOrEmpty(SizeText)
            ? Name
            : Name + " (" + SizeText + ")";
    }

    /// <summary>
    /// BOOTH商品モデル。
    /// </summary>
    [Serializable]
    internal sealed class BoothProduct
    {
        public string ProductId = string.Empty;
        public string Name = string.Empty;
        public string ShopName = string.Empty;

        /// <summary>将来のサムネイル表示用。現時点ではURL保持のみで、Unity側ではネットワーク取得しない。</summary>
        public string ThumbnailUrl = string.Empty;

        public string ProductUrl = string.Empty;
        public List<BoothDownloadFile> Files = new List<BoothDownloadFile>();

        /// <summary>BOOTHのカテゴリ表示名（例: 「3D衣装」「3Dキャラクター」）。</summary>
        public string CategoryLabel = string.Empty;

        /// <summary>プラットフォーム/対象バッジ（例: 「VRCHAT」）。</summary>
        public string BadgeText = "VRCHAT";

        /// <summary>価格表示文字列（例: 「¥3,300」「無料」）。</summary>
        public string PriceText = string.Empty;

        /// <summary>いいね数。</summary>
        public int LikeCount;
    }
}
