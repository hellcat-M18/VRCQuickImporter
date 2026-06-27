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

            // プロセス完了をポーリング
            var startedAt = DateTime.UtcNow;
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
                            EditorUtility.DisplayDialog(
                                "VRCQuickImporter",
                                "ダウンロードがタイムアウトしました。",
                                "OK");
                        }
                        return;
                    }
                }
                catch
                {
                    EditorApplication.update -= PollDownload;
                    return;
                }

                EditorApplication.update -= PollDownload;

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
    }
}