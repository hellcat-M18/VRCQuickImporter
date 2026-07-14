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
    internal static class BoothImportPipeline
    {
        private const int DownloadTimeoutSeconds = 180;

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
                                    if (doc.percent >= 0)
                                    {
                                        progressText = $"ダウンロード中... {doc.percent}%";
                                        pct = doc.percent / 100f;
                                    }
                                    else if (doc.status == "started")
                                    {
                                        progressText = "ダウンロード準備中...";
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
            var safeProductName = SanitizeForFolderName(product.Name);
            var safeFileName = Path.GetFileNameWithoutExtension(file.Name);
            if (string.IsNullOrEmpty(safeFileName))
            {
                safeFileName = $"booth_{file.FileId}";
            }

            if (ext == ".unitypackage")
            {
                // unitypackage を直接インポート
                ImportUnityPackage(filePath);
            }
            else if (ext == ".zip")
            {
                // zipを解凍して中身を判定
                var extractDir = Path.Combine(VRCQuickImporterPaths.ExtractedDirectory, safeFileName);
                extractDir = MakeUniquePath(extractDir);
                Directory.CreateDirectory(extractDir);

                ExtractZip(filePath, extractDir);

                var unityPackages = Directory.GetFiles(extractDir, "*.unitypackage", SearchOption.AllDirectories);

                if (unityPackages.Length == 1)
                {
                    // unitypackage 1つ → そのままインポート
                    ImportUnityPackage(unityPackages[0]);
                }
                else if (unityPackages.Length > 1)
                {
                    // 複数のunitypackage → ユーザーに選択させる
                    SelectAndImportUnityPackages(unityPackages, product.Name);
                }
                else
                {
                    // unitypackageなし → Assets配下に展開
                    DeployToAssets(extractDir, safeFileName);
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
        private static void ImportUnityPackage(string path)
        {
            Debug.Log("[VRCQuickImporter] unitypackage をインポート: " + path);
            AssetDatabase.ImportPackage(path, interactive: true);

            var window = EditorWindow.GetWindow<VRCQuickImporter.Editor.UI.VRCQuickImporterWindow>();
            window.ShowNotification(new GUIContent("✅ unitypackageのインポートが完了しました"));
        }

        /// <summary>
        /// 複数のunitypackageがある場合、ユーザーに選択させる。
        /// </summary>
        private static void SelectAndImportUnityPackages(string[] packages, string productName)
        {
            var displayNames = packages.Select(p => Path.GetFileName(p)).ToArray();
            var message = $"「{productName}」に複数のunitypackageが含まれています。\n\n" +
                           "インポートするものを選択してください（複数選択可）:";
            var options = displayNames;

            // EditorUtility.DisplayDialogでは複数選択できないので、
            // 1つずつダイアログで確認する
            var toImport = new List<string>();
            foreach (var pkg in packages)
            {
                var name = Path.GetFileName(pkg);
                var ok = EditorUtility.DisplayDialog(
                    "VRCQuickImporter - unitypackage選択",
                    $"「{productName}」に複数のunitypackageが含まれています。\n\n" +
                    $"{name}\n\nこのファイルをインポートしますか？\n" +
                    $"({toImport.Count + 1}/{packages.Length}個目)",
                    "インポート",
                    "スキップ");

                if (ok)
                {
                    toImport.Add(pkg);
                }
            }

            if (toImport.Count == 0)
            {
                Debug.Log("[VRCQuickImporter] インポートがスキップされました。");
                return;
            }

            foreach (var pkg in toImport)
            {
                ImportUnityPackage(pkg);
            }

            var window = EditorWindow.GetWindow<VRCQuickImporter.Editor.UI.VRCQuickImporterWindow>();
            window.ShowNotification(new GUIContent($"✅ unitypackage {toImport.Count}個のインポートが完了しました"));
        }

        /// <summary>
        /// zipの中身を Assets/BOOTH/{folderName}/ に展開する。
        /// </summary>
        private static void DeployToAssets(string extractDir, string folderName)
        {
            var assetsRoot = Path.GetFullPath("Assets");
            var targetDir = Path.Combine(assetsRoot, "BOOTH", folderName);
            targetDir = MakeUniquePath(targetDir);

            // フォルダをコピー
            CopyDirectory(extractDir, targetDir);

            AssetDatabase.Refresh();
            Debug.Log("[VRCQuickImporter] Assets配下に展開しました: " + targetDir);

            EditorUtility.DisplayDialog(
                "VRCQuickImporter",
                $"「{folderName}」を Assets/BOOTH/{folderName}/ に展開しました。\n" +
                $"AssetDatabaseを更新しました。",
                "OK");

            // Projectウィンドウで開く
            var relativePath = "Assets/BOOTH/" + folderName;
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(relativePath);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }

            var window = EditorWindow.GetWindow<VRCQuickImporter.Editor.UI.VRCQuickImporterWindow>();
            window.ShowNotification(new GUIContent($"✅ 「{folderName}」を {relativePath} に展開しました"));
        }

        private static string SanitizeForFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "BOOTH_Item";
            var invalid = Path.GetInvalidFileNameChars();
            var result = name;
            foreach (var c in invalid)
            {
                result = result.Replace(c, '_');
            }
            // 長すぎる名前を切り詰める
            if (result.Length > 60) result = result.Substring(0, 60);
            return result.Trim();
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
            public string outputPath = string.Empty;
            public string updatedAt = string.Empty;
        }
    }
}