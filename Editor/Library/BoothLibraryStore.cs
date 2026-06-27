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

        public static bool InitialFullSyncCompleted
        {
            get
            {
                var document = LoadDatabaseDocument();
                return document?.InitialFullSyncCompleted == true;
            }
        }

        public static HashSet<string> LoadKnownProductIds()
        {
            var document = LoadDatabaseDocument();
            return new HashSet<string>((document?.Products ?? new List<BoothProduct>())
                .Where(p => !string.IsNullOrEmpty(p.ProductId))
                .Select(p => p.ProductId));
        }

        public static BoothLibraryDocument LoadDatabaseDocument()
        {
            if (!File.Exists(VRCQuickImporterPaths.DatabasePath)) return null;
            var document = TryLoadDocument(VRCQuickImporterPaths.DatabasePath);
            if (document != null) NormalizeDocument(document);
            return document;
        }

        public static BoothLibraryDocument LoadPendingPageDocument()
        {
            if (!File.Exists(VRCQuickImporterPaths.PendingPagePath)) return null;
            var document = TryLoadDocument(VRCQuickImporterPaths.PendingPagePath);
            if (document != null) NormalizeDocument(document);
            return document;
        }

        public static void DeletePendingPage()
        {
            try
            {
                if (File.Exists(VRCQuickImporterPaths.PendingPagePath))
                {
                    File.Delete(VRCQuickImporterPaths.PendingPagePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] pending-page.json の削除に失敗しました: " + ex.Message);
            }
        }

        public static BoothLibraryDocument MergeProductsIntoDatabase(IEnumerable<BoothProduct> products, int maxPage)
        {
            var main = LoadDatabaseDocument() ?? new BoothLibraryDocument();
            main.Products = MergeProducts(products, main.Products);
            main.MaxPage = Math.Max(main.MaxPage, maxPage);
            main.ReachedLastPage = false;
            main.SyncedAt = DateTimeOffset.Now.ToString("yyyy/MM/dd HH:mm:ss");
            NormalizeDocument(main);
            SaveDocument(main);
            return main;
        }

        public static void UpdatePageState(int maxPage, bool reachedLast)
        {
            var document = LoadDatabaseDocument();
            if (document == null) return;
            document.MaxPage = Math.Max(document.MaxPage, maxPage);
            document.ReachedLastPage = reachedLast;
            SaveDocument(document);
        }

        public static BoothLibraryDocument ReplaceDatabaseWithProducts(IEnumerable<BoothProduct> products, int maxPage, bool initialFullSync, bool fullRefresh)
        {
            var now = DateTimeOffset.Now.ToString("yyyy/MM/dd HH:mm:ss");
            var document = new BoothLibraryDocument
            {
                SchemaVersion = "1",
                SyncedAt = now,
                SourceUrl = "https://accounts.booth.pm/library",
                Products = MergeProducts(new List<BoothProduct>(), products),
                MaxPage = maxPage,
                ReachedLastPage = true,
                InitialFullSyncCompleted = true,
                InitialFullSyncCompletedAt = initialFullSync ? now : (LoadDatabaseDocument()?.InitialFullSyncCompletedAt ?? now),
                LastFullRefreshAt = fullRefresh ? now : (LoadDatabaseDocument()?.LastFullRefreshAt ?? string.Empty)
            };
            NormalizeDocument(document);
            SaveDocument(document);
            return document;
        }

        private static List<BoothProduct> MergeProducts(IEnumerable<BoothProduct> existing, IEnumerable<BoothProduct> incoming)
        {
            var result = new List<BoothProduct>();
            var indexById = new Dictionary<string, int>();

            void Upsert(BoothProduct product)
            {
                if (product == null || string.IsNullOrEmpty(product.ProductId)) return;
                if (indexById.ContainsKey(product.ProductId))
                {
                    return;
                }

                indexById[product.ProductId] = result.Count;
                result.Add(product);
            }

            foreach (var product in existing ?? Enumerable.Empty<BoothProduct>()) Upsert(product);
            foreach (var product in incoming ?? Enumerable.Empty<BoothProduct>()) Upsert(product);
            return result;
        }

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
            var databasePath = VRCQuickImporterPaths.DatabasePath;
            var tmpPath = databasePath + ".tmp";
            var backupPath = databasePath + ".bak";

            File.WriteAllText(tmpPath, json);

            try
            {
                if (File.Exists(databasePath))
                {
                    File.Copy(databasePath, backupPath, overwrite: true);
                }

                File.Copy(tmpPath, databasePath, overwrite: true);
            }
            catch
            {
                TryRestoreBackup(databasePath, backupPath);
                throw;
            }
            finally
            {
                try { File.Delete(tmpPath); } catch { }
            }
        }

        private static void TryRestoreBackup(string databasePath, string backupPath)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, databasePath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] database.json バックアップからの復元に失敗しました: " + ex.Message);
            }
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
