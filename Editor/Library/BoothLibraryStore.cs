using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRCQuickImporter.Editor.Storage;

namespace VRCQuickImporter.Editor.Library
{
    internal static class BoothLibraryStore
    {
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

        private static void NormalizeDocument(BoothLibraryDocument document)
        {
            foreach (var product in document.Products)
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
