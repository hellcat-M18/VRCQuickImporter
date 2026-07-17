using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using Microsoft.Win32;
using UnityEditor;
using UnityEngine;
using VRCQuickImporter.Editor.Storage;
using Debug = UnityEngine.Debug;

namespace VRCQuickImporter.Editor.WebView
{
    internal static class WebView2HostLauncher
    {
        private const string LoginUrl = "https://accounts.booth.pm/users/sign_in";
        private const string HostExeName = "VRCQuickImporter.WebView2Host.exe";
        private const string WebView2BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
        private const string WebView2DownloadPageUrl = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
        private const string WebView2RuntimeRegistryKey = @"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const string WebView2RuntimeRegistryKeyUser = @"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        private const string WebView2RuntimeRegistryValue = "pv";

        private static readonly List<Process> Processes = new List<Process>();

        public static bool IsRunning
        {
            get
            {
                CleanupExitedProcesses();
                return Processes.Any(process => !process.HasExited);
            }
        }

        public static void OpenLogin()
        {
            StartHost(new[]
            {
                Arg("--profile", VRCQuickImporterPaths.WebViewProfileDirectory),
                Arg("--logs", VRCQuickImporterPaths.LogsDirectory),
                Arg("--downloads", VRCQuickImporterPaths.DownloadsDirectory),
                Arg("--url", LoginUrl)
            });
        }

        public static Process StartLibrarySync(string outputPath, bool headless = true, int page = 1, bool skipRateLimit = false)
        {
            var url = page <= 1
                ? "https://accounts.booth.pm/library"
                : $"https://accounts.booth.pm/library?page={page}";

            var args = new List<string>
            {
                Arg("--profile", VRCQuickImporterPaths.WebViewProfileDirectory),
                Arg("--logs", VRCQuickImporterPaths.LogsDirectory),
                Arg("--downloads", VRCQuickImporterPaths.DownloadsDirectory),
                Arg("--url", url),
                "--sync-library",
                "--exit-after-sync",
                Arg("--output", outputPath),
                Arg("--page", page.ToString()),
                Arg("--rate-limit-file", VRCQuickImporterPaths.BoothLibraryAccessStampPath),
                Arg("--min-access-interval-ms", "5000")
            };

            if (headless)
            {
                args.Add("--headless");
            }

            if (skipRateLimit)
            {
                args.Add("--skip-rate-limit");
            }

            return StartHost(args);
        }

        public static Process StartDownload(string downloadUrl, string outputPath, bool headless = true)
        {
            var args = new List<string>
            {
                Arg("--profile", VRCQuickImporterPaths.WebViewProfileDirectory),
                Arg("--logs", VRCQuickImporterPaths.LogsDirectory),
                Arg("--downloads", VRCQuickImporterPaths.DownloadsDirectory),
                Arg("--download", downloadUrl),
                Arg("--output", outputPath),
                "--exit-after-sync"
            };

            if (headless)
            {
                args.Add("--headless");
            }

            return StartHost(args);
        }

        private static Process StartHost(IEnumerable<string> arguments)
        {
#if UNITY_EDITOR_WIN
            VRCQuickImporterPaths.EnsureDirectories();

            if (IsRunning)
            {
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "別のBOOTHアクセスが進行中です。完了後に再度お試しください。",
                    "OK");
                return null;
            }

            if (!EnsureWebView2RuntimeAvailable())
            {
                return null;
            }

            var hostExe = GetHostExePath();
            if (!File.Exists(hostExe))
            {
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "WebView2 helper exe が見つかりません。\n\n" + hostExe,
                    "OK");
                return null;
            }

            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = hostExe,
                    Arguments = string.Join(" ", arguments),
                    WorkingDirectory = Path.GetDirectoryName(hostExe) ?? VRCQuickImporterPaths.ProjectRoot,
                    UseShellExecute = false,
                    CreateNoWindow = false
                });

                if (process == null)
                {
                    EditorUtility.DisplayDialog("VRCQuickImporter", "WebView2 helper exe の起動に失敗しました。", "OK");
                    return null;
                }

                Processes.Add(process);
                Debug.Log("[VRCQuickImporter] WebView2 helperを起動しました: " + hostExe);
                return process;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "WebView2 helper exe の起動に失敗しました。\n\n" + ex.Message +
                    "\n\nWebView2 Runtime が必要です。",
                    "OK");
                return null;
            }
#else
            EditorUtility.DisplayDialog("VRCQuickImporter", "WebView2 helper はWindows Editor専用です。", "OK");
            return null;
#endif
        }

        public static void CloseAll()
        {
            CleanupExitedProcesses();

            foreach (var process in Processes.ToArray())
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.CloseMainWindow();
                        if (!process.WaitForExit(2000))
                        {
                            process.Kill();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[VRCQuickImporter] WebView2 helper終了時に問題が発生しました: " + ex.Message);
                }
            }

            Processes.Clear();
        }

        private static bool EnsureWebView2RuntimeAvailable()
        {
            if (IsWebView2RuntimeInstalled())
            {
                return true;
            }

            var result = EditorUtility.DisplayDialog(
                "VRCQuickImporter",
                "WebView2 Runtimeが見つかりません。\n\n" +
                "Microsoft WebView2 Runtimeをインストールしますか？\n" +
                "インターネット接続が必要です。約2MBをダウンロードします。",
                "インストール",
                "キャンセル");

            if (!result)
            {
                return false;
            }

            var bootstrapperPath = Path.Combine(VRCQuickImporterPaths.CacheDirectory, "MicrosoftEdgeWebview2Setup.exe");

            if (!TryDownloadWebView2Bootstrapper(bootstrapperPath))
            {
                EditorUtility.DisplayDialog(
                    "VRCQuickImporter",
                    "WebView2 Runtime Bootstrapperのダウンロードに失敗しました。\n\n" +
                    "以下から手動でインストールしてください：\n" + WebView2DownloadPageUrl,
                    "OK");
                return false;
            }

            if (RunWebView2Bootstrapper(bootstrapperPath) && IsWebView2RuntimeInstalled())
            {
                return true;
            }

            EditorUtility.DisplayDialog(
                "VRCQuickImporter",
                "WebView2 Runtimeのインストールに失敗しました。\n\n" +
                "以下から手動でインストールしてください：\n" + WebView2DownloadPageUrl,
                "OK");
            return false;
        }

        private static bool IsWebView2RuntimeInstalled()
        {
            try
            {
                using var hklmKey = Registry.LocalMachine.OpenSubKey(WebView2RuntimeRegistryKey);
                if (IsValidWebView2Version(hklmKey))
                {
                    return true;
                }

                using var hkcuKey = Registry.CurrentUser.OpenSubKey(WebView2RuntimeRegistryKeyUser);
                return IsValidWebView2Version(hkcuKey);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] WebView2 Runtime検出中に例外が発生しました: " + ex.Message);
                return false;
            }
        }

        private static bool IsValidWebView2Version(RegistryKey key)
        {
            if (key == null) return false;
            var value = key.GetValue(WebView2RuntimeRegistryValue) as string;
            return !string.IsNullOrWhiteSpace(value) && !string.Equals(value, "0.0.0.0", StringComparison.Ordinal);
        }

        private static bool TryDownloadWebView2Bootstrapper(string destinationPath)
        {
            try
            {
                Debug.Log("[VRCQuickImporter] WebView2 Runtime Bootstrapperをダウンロード中: " + WebView2BootstrapperUrl);
                using var client = new WebClient();
                client.DownloadFile(WebView2BootstrapperUrl, destinationPath);
                Debug.Log("[VRCQuickImporter] WebView2 Runtime Bootstrapperを保存しました: " + destinationPath);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] WebView2 Runtime Bootstrapperのダウンロードに失敗しました: " + ex.Message);
                return false;
            }
        }

        private static bool RunWebView2Bootstrapper(string bootstrapperPath)
        {
            try
            {
                Debug.Log("[VRCQuickImporter] WebView2 Runtimeをインストール中...");
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = bootstrapperPath,
                    Arguments = "/silent /install",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(bootstrapperPath) ?? VRCQuickImporterPaths.CacheDirectory
                });

                if (process == null)
                {
                    Debug.LogWarning("[VRCQuickImporter] WebView2 Runtime Bootstrapperの起動に失敗しました。");
                    return false;
                }

                if (!process.WaitForExit(60000))
                {
                    Debug.LogWarning("[VRCQuickImporter] WebView2 Runtimeのインストールがタイムアウトしました。");
                    try { process.Kill(); } catch { }
                    return false;
                }

                if (process.ExitCode != 0)
                {
                    Debug.LogWarning("[VRCQuickImporter] WebView2 Runtimeのインストールが終了コード " + process.ExitCode + " で失敗しました。");
                    return false;
                }

                Debug.Log("[VRCQuickImporter] WebView2 Runtimeのインストールが完了しました。");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] WebView2 Runtimeのインストール中に例外が発生しました: " + ex.Message);
                return false;
            }
        }

        private static string GetHostExePath()
        {
            var packageRoot = VRCQuickImporterPaths.GetPackageRoot();
            packageRoot = VRCQuickImporterPaths.ToAbsoluteAssetPath(packageRoot);
            return Path.Combine(packageRoot, "Editor", "Helpers~", "WebView2Host", "win-x64", HostExeName);
        }

        private static string Arg(string key, string value)
        {
            return key + " \"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void CleanupExitedProcesses()
        {
            Processes.RemoveAll(process =>
            {
                try
                {
                    return process.HasExited;
                }
                catch
                {
                    return true;
                }
            });
        }
    }
}
