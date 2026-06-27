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
    }
}
