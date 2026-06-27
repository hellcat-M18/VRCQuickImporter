using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRCQuickImporter.Editor.Storage
{
    internal static class VRCQuickImporterPaths
    {
        private const string PackageName = "tool.hellcat.vrc-quick-importer";

        public static string ProjectRoot => Directory.GetParent(Application.dataPath)?.FullName ?? Directory.GetCurrentDirectory();

        public static string DataRoot => Path.Combine(ProjectRoot, "Library", "VRCQuickImporter");
        public static string CacheDirectory => Path.Combine(DataRoot, "cache");
        public static string DownloadsDirectory => Path.Combine(DataRoot, "downloads");
        public static string ExtractedDirectory => Path.Combine(DataRoot, "extracted");
        public static string WebViewProfileDirectory => Path.Combine(DataRoot, "webview-profile");
        public static string LogsDirectory => Path.Combine(DataRoot, "logs");
        public static string DatabasePath => Path.Combine(DataRoot, "database.json");

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(CacheDirectory);
            Directory.CreateDirectory(DownloadsDirectory);
            Directory.CreateDirectory(ExtractedDirectory);
            Directory.CreateDirectory(WebViewProfileDirectory);
            Directory.CreateDirectory(LogsDirectory);
        }

        public static string GetPackageRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VRCQuickImporterPaths).Assembly);
            if (packageInfo != null)
            {
                if (!string.IsNullOrEmpty(packageInfo.resolvedPath))
                {
                    return packageInfo.resolvedPath;
                }

                if (!string.IsNullOrEmpty(packageInfo.assetPath))
                {
                    return packageInfo.assetPath;
                }
            }

            var guids = AssetDatabase.FindAssets("tool.hellcat.vrc-quick-importer.editor");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (path.EndsWith("Editor/tool.hellcat.vrc-quick-importer.editor.asmdef", StringComparison.Ordinal))
                {
                    return path.Substring(0, path.Length - "/Editor/tool.hellcat.vrc-quick-importer.editor.asmdef".Length);
                }
            }

            var assemblyPath = Assembly.GetExecutingAssembly().Location;
            Debug.LogWarning($"[VRCQuickImporter] Could not resolve package root from PackageInfo/AssetDatabase. Assembly: {assemblyPath}");
            return string.Empty;
        }

        public static string ToAbsoluteAssetPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return string.Empty;
            if (Path.IsPathRooted(assetPath)) return assetPath;
            return Path.GetFullPath(Path.Combine(ProjectRoot, assetPath));
        }
    }
}
