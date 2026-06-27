using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VRCQuickImporter.Editor.Storage;

namespace VRCQuickImporter.Editor.Library
{
    internal enum BoothLibraryDataState
    {
        Ready,
        MissingDatabase,
        Empty,
        Error
    }

    internal static class BoothLibraryStore
    {
        public static List<BoothProduct> LoadProducts(out string statusText, out BoothLibraryDataState state)
        {
            if (!File.Exists(VRCQuickImporterPaths.DatabasePath))
            {
                state = BoothLibraryDataState.MissingDatabase;
                statusText = "まだ同期されていません。右上の「BOOTHと同期」から購入履歴を取得してください。";
                return new List<BoothProduct>();
            }

            try
            {
                var json = File.ReadAllText(VRCQuickImporterPaths.DatabasePath);
                var document = JsonUtility.FromJson<BoothLibraryDocument>(json);
                if (document?.Products == null || document.Products.Count == 0)
                {
                    state = BoothLibraryDataState.Empty;
                    statusText = "同期済みデータに商品がありません。BOOTHログイン状態や購入履歴を確認してください。";
                    return new List<BoothProduct>();
                }

                NormalizeDocument(document);

                state = BoothLibraryDataState.Ready;
                statusText = string.IsNullOrEmpty(document.SyncedAt)
                    ? "同期済みデータを表示しています。"
                    : "同期済みデータを表示しています。最終同期: " + document.SyncedAt;
                return document.Products;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] database.json の読み込みに失敗しました: " + ex.Message);
                state = BoothLibraryDataState.Error;
                statusText = "database.json の読み込みに失敗しました。詳細設定からデータフォルダを確認してください。";
                return new List<BoothProduct>();
            }
        }

        public static List<BoothProduct> LoadProductsOrMock(out string statusText)
        {
            if (!File.Exists(VRCQuickImporterPaths.DatabasePath))
            {
                statusText = "database.json がまだ無いため、サンプル商品を表示しています。";
                return BoothLibraryMockData.CreateSampleProducts();
            }

            try
            {
                var json = File.ReadAllText(VRCQuickImporterPaths.DatabasePath);
                var document = JsonUtility.FromJson<BoothLibraryDocument>(json);
                if (document?.Products == null || document.Products.Count == 0)
                {
                    statusText = "同期済みデータに商品が無いため、サンプル商品を表示しています。";
                    return BoothLibraryMockData.CreateSampleProducts();
                }

                NormalizeDocument(document);

                statusText = string.IsNullOrEmpty(document.SyncedAt)
                    ? "同期済みデータを表示しています。"
                    : "同期済みデータを表示しています。最終同期: " + document.SyncedAt;
                return document.Products;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] database.json の読み込みに失敗しました: " + ex.Message);
                statusText = "database.json の読み込みに失敗したため、サンプル商品を表示しています。";
                return BoothLibraryMockData.CreateSampleProducts();
            }
        }

        public static bool HasDatabase => File.Exists(VRCQuickImporterPaths.DatabasePath);

        /// <summary>
        /// database.json の MaxPage / ReachedLastPage を UI 表示用に取得します。
        /// </summary>
        public static void TryGetPageState(out int maxPage, out bool reachedLast)
        {
            maxPage = 0;
            reachedLast = false;
            if (!File.Exists(VRCQuickImporterPaths.DatabasePath)) return;
            try
            {
                var document = JsonUtility.FromJson<BoothLibraryDocument>(File.ReadAllText(VRCQuickImporterPaths.DatabasePath));
                if (document == null) return;
                maxPage = document.MaxPage;
                reachedLast = document.ReachedLastPage;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] ページ状態の読み込みに失敗しました: " + ex.Message);
            }
        }

        /// <summary>
        /// pending-page.json を本 database.json に取り込みます。
        /// replace=true の場合はページ1件取得（全件置き換え）。
        /// </summary>
        public static PageMergeResult MergePendingPage(int requestedPage, bool replace)
        {
            var result = new PageMergeResult();

            if (!File.Exists(VRCQuickImporterPaths.PendingPagePath))
            {
                return result;
            }

            var pending = TryLoadDocument(VRCQuickImporterPaths.PendingPagePath);
            if (pending == null)
            {
                // pending-page.json の読み込みに失敗（ロック競合など）
                // 既存データを上書きしないよう、マージを中止する
                Debug.LogWarning("[VRCQuickImporter] pending-page.json の読み込みに失敗したため、マージをスキップします。");
                result.PageHadProducts = false;
                return result;
            }
            var pageProducts = pending.Products ?? new List<BoothProduct>();
            result.PageHadProducts = pageProducts.Count > 0;

            BoothLibraryDocument main;
            if (replace || !File.Exists(VRCQuickImporterPaths.DatabasePath))
            {
                main = new BoothLibraryDocument
                {
                    SchemaVersion = "1",
                    SyncedAt = pending?.SyncedAt ?? string.Empty,
                    SourceUrl = pending?.SourceUrl ?? string.Empty,
                    Products = new List<BoothProduct>(pageProducts),
                    MaxPage = pageProducts.Count > 0 ? Math.Max(1, requestedPage) : 0,
                    ReachedLastPage = pageProducts.Count == 0
                };
            }
            else
            {
                main = TryLoadDocument(VRCQuickImporterPaths.DatabasePath) ?? new BoothLibraryDocument();
                var seen = new HashSet<string>(main.Products.Select(p => p.ProductId));
                foreach (var product in pageProducts)
                {
                    if (seen.Add(product.ProductId))
                    {
                        main.Products.Add(product);
                    }
                }

                main.SyncedAt = pending?.SyncedAt ?? main.SyncedAt;
                main.MaxPage = pageProducts.Count > 0 ? Math.Max(main.MaxPage, requestedPage) : main.MaxPage;
                main.ReachedLastPage = pageProducts.Count == 0 || main.ReachedLastPage;
            }

            NormalizeDocument(main);
            SaveDocument(main);

            try
            {
                File.Delete(VRCQuickImporterPaths.PendingPagePath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] pending-page.json の削除に失敗しました: " + ex.Message);
            }

            result.TotalProducts = main.Products.Count;
            result.MaxPage = main.MaxPage;
            result.ReachedLastPage = main.ReachedLastPage;
            return result;
        }

        private static BoothLibraryDocument TryLoadDocument(string path)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using (var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var reader = new StreamReader(stream))
                    {
                        return JsonUtility.FromJson<BoothLibraryDocument>(reader.ReadToEnd());
                    }
                }
                catch (Exception ex)
                {
                    if (attempt < 4)
                    {
                        System.Threading.Thread.Sleep(200);
                        continue;
                    }
                    Debug.LogWarning("[VRCQuickImporter] ドキュメントの読み込みに失敗しました: " + ex.Message);
                    return null;
                }
            }
            return null;
        }

        private static void SaveDocument(BoothLibraryDocument document)
        {
            var json = JsonUtility.ToJson(document, true);
            var tmpPath = VRCQuickImporterPaths.DatabasePath + ".tmp";
            File.WriteAllText(tmpPath, json);
            File.Copy(tmpPath, VRCQuickImporterPaths.DatabasePath, overwrite: true);
            try { File.Delete(tmpPath); } catch { }
        }

        public struct PageMergeResult
        {
            public int TotalProducts;
            public int MaxPage;
            public bool ReachedLastPage;
            public bool PageHadProducts;
        }

        private static void NormalizeDocument(BoothLibraryDocument document)
        {
            foreach (var product in document.Products ?? (document.Products = new List<BoothProduct>()))
            {
                product.Name = Normalize(product.Name);
                product.ShopName = Normalize(product.ShopName);
                product.CategoryLabel = Normalize(product.CategoryLabel);
                product.BadgeText = Normalize(product.BadgeText);
                product.PriceText = Normalize(product.PriceText);

                if (product.ShopName == product.Name)
                {
                    product.ShopName = string.Empty;
                }

                if (product.CategoryLabel.Length > 16 || product.CategoryLabel == product.Name || product.CategoryLabel == product.ShopName || product.CategoryLabel.Contains("."))
                {
                    product.CategoryLabel = string.Empty;
                }

                if (product.BadgeText.Length > 12)
                {
                    product.BadgeText = string.Empty;
                }

                if (!(product.PriceText.Contains("¥") || product.PriceText.Contains("￥") || product.PriceText.Contains("無料")))
                {
                    product.PriceText = string.Empty;
                }

                if (product.Files == null)
                {
                    product.Files = new List<BoothDownloadFile>();
                }

                foreach (var file in product.Files)
                {
                    file.Name = Normalize(file.Name);
                    file.SizeText = Normalize(file.SizeText);
                }
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
