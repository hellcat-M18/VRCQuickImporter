using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        public static Process StartLibrarySync(string outputPath, bool headless = true, int page = 1)
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
                    "\n\n.NET 8 Desktop Runtime と WebView2 Runtime が必要です。",
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
