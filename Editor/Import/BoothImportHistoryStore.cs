using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using VRCQuickImporter.Editor.Storage;

namespace VRCQuickImporter.Editor.Import
{
    [Serializable]
    internal sealed class ImportHistoryEntry
    {
        public string ProductId = string.Empty;
        public List<string> Paths = new List<string>();

        /// <summary>ISO 8601形式のインポート日時。</summary>
        public string ImportedAt = string.Empty;

        public DateTimeOffset ImportedAtValue
        {
            get
            {
                return DateTimeOffset.TryParse(ImportedAt, out var value)
                    ? value
                    : DateTimeOffset.MinValue;
            }
            set => ImportedAt = value.ToString("o");
        }
    }

    [Serializable]
    internal sealed class ImportHistoryData
    {
        public string SchemaVersion = "1";
        public List<ImportHistoryEntry> Entries = new List<ImportHistoryEntry>();
    }

    /// <summary>
    /// VRCQuickImporter経由でインポートしたアセットパスを商品ID単位で保持する。
    /// </summary>
    internal static class BoothImportHistoryStore
    {
        public static void RecordImport(string productId, List<string> paths)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return;
            }

            var normalizedPaths = (paths ?? new List<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeAssetPath)
                .Where(path => !string.IsNullOrEmpty(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedPaths.Count == 0)
            {
                return;
            }

            try
            {
                var data = LoadData();
                var entry = data.Entries.FirstOrDefault(item => string.Equals(item.ProductId, productId, StringComparison.Ordinal));
                if (entry == null)
                {
                    entry = new ImportHistoryEntry
                    {
                        ProductId = productId
                    };
                    data.Entries.Add(entry);
                }

                entry.Paths = normalizedPaths
                    .Concat(entry.Paths ?? Enumerable.Empty<string>())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(NormalizeAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                entry.ImportedAtValue = DateTimeOffset.Now;

                data.Entries = data.Entries
                    .OrderByDescending(item => item.ImportedAtValue)
                    .ToList();
                SaveData(data);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] import-history.json の保存に失敗しました: " + ex.Message);
            }
        }

        public static List<ImportHistoryEntry> GetEntries(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId))
            {
                return new List<ImportHistoryEntry>();
            }

            try
            {
                return LoadData().Entries
                    .Where(entry => string.Equals(entry.ProductId, productId, StringComparison.Ordinal))
                    .OrderByDescending(entry => entry.ImportedAtValue)
                    .Select(CloneEntry)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] import-history.json の読み込みに失敗しました: " + ex.Message);
                return new List<ImportHistoryEntry>();
            }
        }

        private static ImportHistoryData LoadData()
        {
            var path = VRCQuickImporterPaths.ImportHistoryPath;
            if (!File.Exists(path))
            {
                return new ImportHistoryData();
            }

            var data = JsonUtility.FromJson<ImportHistoryData>(File.ReadAllText(path)) ?? new ImportHistoryData();
            data.SchemaVersion = string.IsNullOrEmpty(data.SchemaVersion) ? "1" : data.SchemaVersion;
            data.Entries = data.Entries ?? new List<ImportHistoryEntry>();
            foreach (var entry in data.Entries)
            {
                entry.ProductId = entry.ProductId ?? string.Empty;
                entry.Paths = entry.Paths ?? new List<string>();
            }
            return data;
        }

        private static void SaveData(ImportHistoryData data)
        {
            VRCQuickImporterPaths.EnsureDirectories();

            var historyPath = VRCQuickImporterPaths.ImportHistoryPath;
            var tmpPath = historyPath + ".tmp";
            var backupPath = historyPath + ".bak";
            var json = JsonUtility.ToJson(data, true);

            File.WriteAllText(tmpPath, json);
            try
            {
                if (File.Exists(historyPath))
                {
                    File.Replace(tmpPath, historyPath, backupPath, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(tmpPath, historyPath);
                }
            }
            catch
            {
                TryRestoreBackup(historyPath, backupPath);
                throw;
            }
            finally
            {
                try { File.Delete(tmpPath); } catch { }
            }
        }

        private static void TryRestoreBackup(string historyPath, string backupPath)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, historyPath, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] import-history.json バックアップからの復元に失敗しました: " + ex.Message);
            }
        }

        private static ImportHistoryEntry CloneEntry(ImportHistoryEntry entry)
        {
            return new ImportHistoryEntry
            {
                ProductId = entry.ProductId,
                Paths = new List<string>(entry.Paths ?? new List<string>()),
                ImportedAt = entry.ImportedAt
            };
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim().Replace('\\', '/');
        }
    }
}
