using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using VRCQuickImporter.Editor.Storage;

namespace VRCQuickImporter.Editor.Thumbnails
{
    /// <summary>
    /// BOOTHサムネイル画像の取得・ディスクキャッシュを管理します。
    /// </summary>
    internal static class BoothThumbnailCache
    {
        private static readonly Dictionary<string, Texture2D> Cache = new();
        private static readonly Dictionary<string, List<Action<Texture2D>>> PendingCallbacks = new();
        private static readonly List<Request> ActiveRequests = new();
        private static bool _updateRegistered;

        public static void GetTexture(string url, Action<Texture2D> onLoaded)
        {
            if (string.IsNullOrWhiteSpace(url) || onLoaded == null)
            {
                onLoaded?.Invoke(null);
                return;
            }

            if (Cache.TryGetValue(url, out var cached))
            {
                onLoaded(cached);
                return;
            }

            VRCQuickImporterPaths.EnsureDirectories();
            var cachePath = GetCachePath(url);
            if (File.Exists(cachePath))
            {
                var loaded = LoadTextureFromFile(cachePath);
                if (loaded != null)
                {
                    Cache[url] = loaded;
                    onLoaded(loaded);
                    return;
                }

                try
                {
                    File.Delete(cachePath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[VRCQuickImporter] 破損サムネイルキャッシュの削除に失敗しました: " + ex.Message);
                }
            }

            if (!PendingCallbacks.TryGetValue(url, out var callbacks))
            {
                callbacks = new List<Action<Texture2D>>();
                PendingCallbacks[url] = callbacks;
            }

            callbacks.Add(onLoaded);

            if (ActiveRequests.Exists(r => r.Url == url))
            {
                return;
            }

            StartRequest(url, cachePath);
        }

        private static void StartRequest(string url, string cachePath)
        {
            var request = UnityWebRequestTexture.GetTexture(url);
            request.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36");
            request.SetRequestHeader("Accept", "image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8");
            request.SendWebRequest();

            ActiveRequests.Add(new Request
            {
                Url = url,
                WebRequest = request,
                CachePath = cachePath
            });

            EnsureUpdateLoop();
        }

        private static void EnsureUpdateLoop()
        {
            if (_updateRegistered)
            {
                return;
            }

            _updateRegistered = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private static void OnEditorUpdate()
        {
            for (var i = ActiveRequests.Count - 1; i >= 0; i--)
            {
                var request = ActiveRequests[i];
                if (!request.WebRequest.isDone)
                {
                    continue;
                }

                Texture2D texture = null;
                if (request.WebRequest.result == UnityWebRequest.Result.Success)
                {
                    texture = DownloadHandlerTexture.GetContent(request.WebRequest);
                    if (texture != null)
                    {
                        texture.hideFlags = HideFlags.HideAndDontSave;
                        Cache[request.Url] = texture;
                        SaveDownloadedTexture(request.WebRequest, request.CachePath);
                    }
                }
                else
                {
                    Debug.LogWarning($"[VRCQuickImporter] サムネイル取得失敗 ({request.WebRequest.result}): {request.Url}");
                }

                request.WebRequest.Dispose();
                ActiveRequests.RemoveAt(i);

                if (PendingCallbacks.TryGetValue(request.Url, out var callbacks))
                {
                    PendingCallbacks.Remove(request.Url);
                    foreach (var callback in callbacks)
                    {
                        callback(texture);
                    }
                }
            }

            if (ActiveRequests.Count == 0)
            {
                EditorApplication.update -= OnEditorUpdate;
                _updateRegistered = false;
            }
        }

        private static void SaveDownloadedTexture(UnityWebRequest request, string cachePath)
        {
            try
            {
                if (request.downloadHandler is DownloadHandlerTexture handler && handler.data != null && handler.data.Length > 0)
                {
                    File.WriteAllBytes(cachePath, handler.data);
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] サムネイルキャッシュ保存に失敗しました: " + ex.Message);
            }
        }

        private static Texture2D LoadTextureFromFile(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                texture.hideFlags = HideFlags.HideAndDontSave;
                if (texture.LoadImage(bytes))
                {
                    return texture;
                }

                UnityEngine.Object.DestroyImmediate(texture);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[VRCQuickImporter] サムネイルキャッシュ読み込みに失敗しました: " + ex.Message);
            }

            return null;
        }

        private static string GetCachePath(string url)
        {
            var hash = ComputeHash(url);
            var extension = GetImageExtension(url);
            return Path.Combine(VRCQuickImporterPaths.ThumbnailsDirectory, hash + extension);
        }

        private static string ComputeHash(string value)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        private static string GetImageExtension(string url)
        {
            try
            {
                var path = new Uri(url).AbsolutePath;
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (extension == ".jpg" || extension == ".jpeg" || extension == ".png" || extension == ".webp" || extension == ".gif")
                {
                    return extension;
                }
            }
            catch
            {
                // ignored
            }

            return ".png";
        }

        private sealed class Request
        {
            public string Url;
            public UnityWebRequest WebRequest;
            public string CachePath;
        }
    }
}
