using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression; // ZipArchive only (no FileSystem needed)
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRCQuickImporter.Editor.Library;
using VRCQuickImporter.Editor.Storage;
using VRCQuickImporter.Editor.WebView;

namespace VRCQuickImporter.Editor.Import
{
    /// <summary>
    /// BOOTHファイルのダウンロード→解凍→インポートを統括するパイプライン。
    /// </summary>
    [InitializeOnLoad]
    internal static class BoothImportPipeline
    {
        private const int DownloadTimeoutSeconds = 180;
        private const string ImportingProductIdSessionKey = "VRCQuickImporter.importingProductId";
        private const string PendingImportCountSessionKey = "VRCQuickImporter.pendingImportPackageCount";

        private static DateTime _activeDownloadStartedAt;
        private static bool _importPackageHandlersRegistered;

        static BoothImportPipeline()
        {
            EnsureImportPackageHandlersRegistered();
        }

        /// <summary>
        /// 指定した商品の指定ファイルをダウンロードしてインポートする。
        /// </summary>
        public static void StartImport(BoothProduct product, BoothDownloadFile file)
        {
            if (string.IsNullOrEmpty(file.DownloadUrl))
            {
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "このファイルのダウンロードURLが取得できていません。\n再度ライブラリを同期してください。",
                    "OK");
                return;
            }

            var fileName = file.Name;
            if (string.IsNullOrEmpty(fileName))
            {
                fileName = $"booth_{file.FileId}.bin";
            }

            var downloadPath = Path.Combine(VRCQuickImporterPaths.DownloadsDirectory, fileName);
            downloadPath = MakeUniquePath(downloadPath);

            // 既存のダウンロードプロセスがないか確認
            if (WebView2HostLauncher.IsRunning)
            {
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "別のBOOTHアクセスが進行中です。完了後に再度お試しください。",
                    "OK");
                return;
            }

            var process = WebView2HostLauncher.StartDownload(file.DownloadUrl, downloadPath, headless: true);
            if (process == null)
            {
                return;
            }

            Debug.Log($"[VRCQuickImporter] ダウンロード開始: {file.DownloadUrl} → {downloadPath}");

            // プロセス完了をポーリング（進捗表示付き）
            var startedAt = DateTime.UtcNow;
            _activeDownloadStartedAt = startedAt;
            var progressPath = Path.Combine(VRCQuickImporterPaths.LogsDirectory, "download-progress.json");
            var lastProgressText = string.Empty;
            EditorApplication.update -= PollDownload;
            EditorApplication.update += PollDownload;

            void PollDownload()
            {
                try
                {
                    if (!process.HasExited)
                    {
                        var elapsed = (DateTime.UtcNow - startedAt).TotalSeconds;
                        if (elapsed > DownloadTimeoutSeconds)
                        {
                            EditorApplication.update -= PollDownload;
                            try { process.Kill(); } catch { }
                            EditorUtility.ClearProgressBar();
                            EditorUtility.DisplayDialog(
                                "VRCQuickImporter",
                                "ダウンロードがタイムアウトしました。",
                                "OK");
                            return;
                        }

                        // 進捗ファイルを読んで表示
                        var progressText = "ダウンロード中...";
                        var pct = -1f;
                        try
                        {
                            if (File.Exists(progressPath))
                            {
                                var json = File.ReadAllText(progressPath);
                                var doc = JsonUtility.FromJson<DownloadProgressInfo>(json);
                                if (doc != null && !string.IsNullOrEmpty(doc.status))
                                {
                                    if (doc.status == "started")
                                    {
                                        progressText = "ダウンロード準備中...";
                                    }
                                    else if (doc.percent >= 0)
                                    {
                                        progressText = $"ダウンロード中... {doc.percent}%";
                                        pct = doc.percent / 100f;
                                    }
                                    else if (doc.status == "downloading")
                                    {
                                        var mbReceived = doc.bytesReceived / 1048576.0;
                                        var mbTotal = doc.bytesTotal > 0 ? doc.bytesTotal / 1048576.0 : 0;
                                        progressText = mbTotal > 0
                                            ? $"ダウンロード中... {mbReceived:F1} / {mbTotal:F1} MB"
                                            : $"ダウンロード中... {mbReceived:F1} MB";
                                    }
                                }
                            }
                        }
                        catch { }

                        if (progressText != lastProgressText || pct >= 0)
                        {
                            lastProgressText = progressText;
                            if (pct >= 0)
                                EditorUtility.DisplayProgressBar("VRCQuickImporter", progressText, pct);
                            else
                                EditorUtility.DisplayProgressBar("VRCQuickImporter", progressText, -1f);
                        }

                        return;
                    }
                }
                catch
                {
                    EditorApplication.update -= PollDownload;
                    EditorUtility.ClearProgressBar();
                    return;
                }

                EditorApplication.update -= PollDownload;
                EditorUtility.ClearProgressBar();

                if (!File.Exists(downloadPath))
                {
                    EditorUtility.DisplayDialog(
                        "VRCQuickImporter",
                        "ダウンロードに失敗しました。BOOTHにログイン済みか確認してください。",
                        "OK");
                    return;
                }

                var fi = new FileInfo(downloadPath);
                Debug.Log($"[VRCQuickImporter] ダウンロード完了: {downloadPath} ({fi.Length} bytes)");

                try
                {
                    ProcessDownloadedFile(downloadPath, product, file);
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                    EditorUtility.DisplayDialog(
                        "VRCQuickImporter",
                        "インポート処理中にエラーが発生しました:\n\n" + ex.Message,
                        "OK");
                }
            }
        }

        /// <summary>
        /// ダウンロードしたファイルを種別に応じて処理する。
        /// </summary>
        private static void ProcessDownloadedFile(string filePath, BoothProduct product, BoothDownloadFile file)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var safeFileName = Path.GetFileNameWithoutExtension(file.Name);
            if (string.IsNullOrEmpty(safeFileName))
            {
                safeFileName = $"booth_{file.FileId}";
            }

            if (ext == ".unitypackage")
            {
                // unitypackage を直接インポート
                ImportUnityPackage(filePath, product.ProductId);
            }
            else if (ext == ".zip")
            {
                // zipを解凍して中身を判定
                var extractDir = Path.Combine(VRCQuickImporterPaths.ExtractedDirectory, safeFileName);
                extractDir = MakeUniquePath(extractDir);
                Directory.CreateDirectory(extractDir);

                ExtractZip(filePath, extractDir);

                var unityPackages = Directory.GetFiles(extractDir, "*.unitypackage", SearchOption.AllDirectories);

                if (unityPackages.Length > 0)
                {
                    var totalElapsed = (DateTime.UtcNow - _activeDownloadStartedAt).TotalSeconds;
                    if (totalElapsed >= 10)
                    {
                        var sizeMB = new FileInfo(filePath).Length / 1048576.0;
                        BoothNotificationHelper.ShowNotification(
                            "VRCQuickImporter",
                            $"zipの展開が完了しました ({sizeMB:F1} MB, {totalElapsed:F0}秒)");
                    }
                }

                if (unityPackages.Length == 1)
                {
                    // unitypackage 1つ → そのままインポート
                    ImportUnityPackage(unityPackages[0], product.ProductId);
                }
                else if (unityPackages.Length > 1)
                {
                    // 複数のunitypackage → ユーザーに選択させる
                    SelectAndImportUnityPackages(unityPackages, product.Name, product.ProductId);
                }
                else
                {
                    // unitypackageなし → Assets配下に展開
                    DeployToAssets(extractDir, safeFileName, product.ProductId);
                }
            }
            else
            {
                // その他のファイル：ダウンロード先に置いたままユーザーに通知
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    $"ダウンロードが完了しましたが、このファイル形式（{ext}）は自動インポートの対象外です。\n\n" +
                    $"ファイル: {filePath}\n\n" +
                    $"手動でAssetsフォルダに配置してください。",
                    "OK");
            }
        }

        /// <summary>
        /// unitypackage をインポートする（Unity標準ダイアログあり）。
        /// </summary>
        private static void ImportUnityPackage(string path, string productId)
        {
            Debug.Log("[VRCQuickImporter] unitypackage をインポート: " + path);

            EnsureImportPackageHandlersRegistered();
            if (!string.IsNullOrEmpty(productId))
            {
                BeginPackageImport(productId);
            }
            AssetDatabase.ImportPackage(path, interactive: true);
        }

        private static void EnsureImportPackageHandlersRegistered()
        {
            if (_importPackageHandlersRegistered)
            {
                return;
            }

            AssetDatabase.importPackageCompleted += OnImportPackageCompleted;
            AssetDatabase.onImportPackageItemsCompleted += OnImportPackageItemsCompleted;
            AssetDatabase.importPackageCancelled += OnImportPackageCancelled;
            AssetDatabase.importPackageFailed += OnImportPackageFailed;
            _importPackageHandlersRegistered = true;
        }

        private static void OnImportPackageCompleted(string packageName)
        {
            if (string.IsNullOrEmpty(GetImportingProductId()))
            {
                return;
            }

            BoothNotificationHelper.ShowNotification(
                "VRCQuickImporter",
                $"{Path.GetFileName(packageName)}のインポートが完了しました");
            // onImportPackageItemsCompleted が同じ完了通知内で後から呼ばれる場合にも
            // SessionStateのコンテキストを維持できるよう、次フレームで片付ける。
            EditorApplication.delayCall += CompletePackageImport;
        }

        private static void OnImportPackageItemsCompleted(string[] importedAssetPaths)
        {
            var productId = GetImportingProductId();
            if (string.IsNullOrEmpty(productId))
            {
                return;
            }

            var paths = CollectImportRootPaths(importedAssetPaths);
            if (paths.Count > 0)
            {
                BoothImportHistoryStore.RecordImport(productId, paths);
            }
        }

        private static void OnImportPackageCancelled(string packageName)
        {
            if (!string.IsNullOrEmpty(GetImportingProductId()))
            {
                CompletePackageImport();
            }
        }

        private static void OnImportPackageFailed(string packageName, string errorMessage)
        {
            if (!string.IsNullOrEmpty(GetImportingProductId()))
            {
                CompletePackageImport();
            }
        }

        private static void BeginPackageImport(string productId)
        {
            SessionState.SetString(ImportingProductIdSessionKey, productId ?? string.Empty);
            SessionState.SetInt(PendingImportCountSessionKey, SessionState.GetInt(PendingImportCountSessionKey, 0) + 1);
        }

        private static string GetImportingProductId()
        {
            return SessionState.GetString(ImportingProductIdSessionKey, string.Empty);
        }

        private static void CompletePackageImport()
        {
            var pendingCount = Math.Max(0, SessionState.GetInt(PendingImportCountSessionKey, 0) - 1);
            SessionState.SetInt(PendingImportCountSessionKey, pendingCount);
            if (pendingCount == 0)
            {
                SessionState.SetString(ImportingProductIdSessionKey, string.Empty);
            }
        }

        private static List<string> CollectImportRootPaths(IEnumerable<string> importedAssetPaths)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var assetPath in importedAssetPaths ?? Enumerable.Empty<string>())
            {
                var normalizedPath = (assetPath ?? string.Empty).Replace('\\', '/');
                if (!normalizedPath.StartsWith("Assets/", StringComparison.Ordinal) || normalizedPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var segments = normalizedPath.Split('/');
                var candidate = segments.Length <= 2
                    ? normalizedPath
                    : "Assets/" + segments[1];
                if (AssetDatabase.IsValidFolder(candidate) || AssetDatabase.LoadMainAssetAtPath(candidate) != null)
                {
                    results.Add(candidate);
                }
            }

            return results.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 複数のunitypackageがある場合、インポート方法をユーザーに選択させる。
        /// </summary>
        private static void SelectAndImportUnityPackages(string[] packages, string productName, string productId)
        {
            var choice = EditorUtility.DisplayDialogComplex(
                "VRCQuickImporter - unitypackage選択",
                $"「{productName}」に{packages.Length}個のunitypackageが含まれています。\n\n" +
                "すべてインポートするか、ファイルを選択してインポートできます。",
                "すべてインポート",
                "キャンセル",
                "選択してインポート");

            if (choice == 0)
            {
                foreach (var pkg in packages)
                {
                    ImportUnityPackage(pkg, productId);
                }

            }
            else if (choice == 2)
            {
                SelectWindow.Open(packages, productName, productId);
            }
        }

        private sealed class SelectWindow : EditorWindow
        {
            private string[] _packages = Array.Empty<string>();
            private bool[] _selected = Array.Empty<bool>();
            private string _productName = string.Empty;
            private string _productId = string.Empty;
            private Vector2 _scrollPosition;

            public static void Open(string[] packages, string productName, string productId)
            {
                var window = GetWindow<SelectWindow>(true, "インポートするファイルを選択", true);
                window._packages = packages ?? Array.Empty<string>();
                window._selected = Enumerable.Repeat(true, window._packages.Length).ToArray();
                window._productName = productName ?? string.Empty;
                window._productId = productId ?? string.Empty;

                var height = Mathf.Min(400, 140 + window._packages.Length * 24);
                window.minSize = new Vector2(400, 160);
                window.position = new Rect(window.position.x, window.position.y, 400, height);
            }

            private void OnGUI()
            {
                EditorGUILayout.LabelField("インポートするファイルを選択", EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"「{_productName}」", EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(6);

                _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
                for (var index = 0; index < _packages.Length; index++)
                {
                    var fileName = Path.GetFileName(_packages[index]);
                    _selected[index] = EditorGUILayout.ToggleLeft(fileName, _selected[index]);
                }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                var selectedCount = _selected.Count(selected => selected);
                EditorGUI.BeginDisabledGroup(selectedCount == 0);
                if (GUILayout.Button("インポート", GUILayout.Width(110)))
                {
                    var toImport = _packages.Where((_, index) => _selected[index]).ToArray();
                    Close();

                    foreach (var pkg in toImport)
                    {
                        ImportUnityPackage(pkg, _productId);
                    }

                }
                EditorGUI.EndDisabledGroup();

                if (GUILayout.Button("キャンセル", GUILayout.Width(90)))
                {
                    Close();
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// zipの中身を Assets/BOOTH/{folderName}/ に展開する。
        /// </summary>
        private static void DeployToAssets(string extractDir, string folderName, string productId)
        {
            var assetsRoot = Path.GetFullPath("Assets");
            var targetDir = Path.Combine(assetsRoot, "BOOTH", folderName);
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }

            // フォルダをコピー
            CopyDirectory(extractDir, targetDir);

            AssetDatabase.Refresh();
            Debug.Log("[VRCQuickImporter] Assets配下に展開しました: " + targetDir);

            var deployedFolderName = Path.GetFileName(targetDir);
            var relativePath = "Assets/BOOTH/" + deployedFolderName;
            BoothImportHistoryStore.RecordImport(productId, new List<string> { relativePath });
            BoothNotificationHelper.ShowNotification(
                "VRCQuickImporter",
                $"「{deployedFolderName}」を {relativePath} に展開しました");

            // Projectウィンドウで開く
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            EditorUtility.DisplayDialog(
                "VRCQuickImporter",
                $"「{deployedFolderName}」を {relativePath}/ に展開しました。\n" +
                $"AssetDatabaseを更新しました。",
                "OK");

        }

        private static string MakeUniquePath(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path)) return path;

            var parent = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            for (var i = 1; i < 10000; i++)
            {
                var candidate = Path.Combine(parent, $"{name} ({i}){ext}");
                if (!Directory.Exists(candidate) && !File.Exists(candidate)) return candidate;
            }

            return path;
        }

        private static void ExtractZip(string zipPath, string destDir)
        {
            using (var stream = File.OpenRead(zipPath))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                    var destPath = Path.Combine(destDir, relativePath);

                    // ディレクトリエントリ
                    if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                    {
                        Directory.CreateDirectory(destPath);
                        continue;
                    }

                    var dir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    using (var entryStream = entry.Open())
                    using (var fileStream = File.Create(destPath))
                    {
                        entryStream.CopyTo(fileStream);
                    }
                }
            }
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        [Serializable]
        private class DownloadProgressInfo
        {
            public string status = string.Empty;
            public int percent = -1;
            public long bytesReceived;
            public long bytesTotal;
        }
    }
}