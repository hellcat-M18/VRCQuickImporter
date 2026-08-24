using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VRCQuickImporter.Editor.Storage;

namespace VRCQuickImporter.Editor.Library
{
    /// <summary>
    /// 購入履歴から解決した ProductId → 注文URL 群を保持する。
    /// UIでは参照のみ、helperが更新する。
    /// </summary>
    internal static class BoothOrderMapStore
    {
        [Serializable]
        internal sealed class OrderMapData
        {
            public string SchemaVersion = "1";
            public string UpdatedAt = string.Empty;
            public List<OrderMapEntry> Entries = new List<OrderMapEntry>();
        }

        [Serializable]
        internal sealed class OrderMapEntry
        {
            public string ProductId = string.Empty;
            public List<string> OrderUrls = new List<string>();
        }

        public static List<string> GetOrderUrls(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return new List<string>();
            }

            var data = TryLoad();
            var entry = data?.Entries?.FirstOrDefault(item => item != null && item.ProductId == productId);
            return entry != null
                ? (entry.OrderUrls ?? new List<string>()).Where(url => !string.IsNullOrWhiteSpace(url)).ToList()
                : new List<string>();
        }

        public static OrderMapData TryLoad()
        {
            try
            {
                var path = VRCQuickImporterPaths.OrderMapPath;
                if (!File.Exists(path))
                {
                    return new OrderMapData();
                }

                var json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<OrderMapData>(json);
                if (data == null)
                {
                    return new OrderMapData();
                }

                data.SchemaVersion = string.IsNullOrEmpty(data.SchemaVersion) ? "1" : data.SchemaVersion;
                data.Entries = data.Entries ?? new List<OrderMapEntry>();
                foreach (var entry in data.Entries)
                {
                    if (entry == null) continue;
                    entry.ProductId = entry.ProductId ?? string.Empty;
                    entry.OrderUrls = entry.OrderUrls ?? new List<string>();
                }

                return data;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] order-map.json の読み込みに失敗しました: " + ex.Message);
                return new OrderMapData();
            }
        }

        public static void Save(OrderMapData data)
        {
            if (data == null)
            {
                return;
            }

            data.SchemaVersion = "1";
            data.UpdatedAt = DateTimeOffset.Now.ToString("o");
            data.Entries = data.Entries ?? new List<OrderMapEntry>();
            foreach (var entry in data.Entries)
            {
                if (entry == null)
                {
                    continue;
                }
                entry.ProductId = entry.ProductId ?? string.Empty;
                entry.OrderUrls = entry.OrderUrls ?? new List<string>();
            }

            VRCQuickImporterPaths.EnsureDirectories();

            var path = VRCQuickImporterPaths.OrderMapPath;
            var tmpPath = path + ".tmp";
            var backupPath = path + ".bak";
            var json = JsonUtility.ToJson(data, true);

            try
            {
                File.WriteAllText(tmpPath, json);
                if (File.Exists(path))
                {
                    File.Replace(tmpPath, path, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmpPath, path);
                }
            }
            catch
            {
                try
                {
                    if (File.Exists(backupPath))
                    {
                        File.Copy(backupPath, path, overwrite: true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[VRCQuickImporter] order-map.json バックアップ復元に失敗しました: " + ex.Message);
                }
                throw;
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }

        public static void SetOrderUrls(string productId, IEnumerable<string> urls)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return;
            }

            var data = TryLoad();
            data.Entries = data.Entries ?? new List<OrderMapEntry>();

            var normalized = (urls ?? Enumerable.Empty<string>())
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var existing = data.Entries.FirstOrDefault(item => item != null && item.ProductId == productId);
            if (existing == null)
            {
                data.Entries.Add(new OrderMapEntry
                {
                    ProductId = productId,
                    OrderUrls = normalized
                });
            }
            else
            {
                existing.OrderUrls = normalized;
            }

            Save(data);
        }
    }
}
