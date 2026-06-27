using UnityEditor;
using UnityEngine;

namespace VRCQuickImporter.Editor.UI
{
    /// <summary>
    /// Noto Sans JP（SIL Open Font License）をUI Toolkit EditorWindowに提供します。
    /// フォント読み込みに失敗した場合はUnity既定フォントへフォールバックします。
    /// </summary>
    internal static class BoothFontProvider
    {
        private const string PackageRootMarker = "tool.hellcat.vrc-quick-importer.editor";
        private const string FontAssetPath = "Editor/Fonts/{0}.ttf";

        private static Font _regular;
        private static Font _bold;
        private static bool _regularLoaded;
        private static bool _boldLoaded;

        public static Font Regular
        {
            get
            {
                if (!_regularLoaded)
                {
                    _regular = LoadFont("NotoSansJP-Regular");
                    _regularLoaded = true;
                }

                return _regular;
            }
        }

        public static Font Bold
        {
            get
            {
                if (!_boldLoaded)
                {
                    _bold = LoadFont("NotoSansJP-Bold");
                    _boldLoaded = true;
                }

                return _bold;
            }
        }

        /// <summary>
        /// 指定したウエイトのフォントを返す。読み込み失敗時は既定フォントを返す。
        /// </summary>
        public static Font Resolve(FontStyle style)
        {
            var font = style == FontStyle.Bold ? Bold : Regular;
            return font != null ? font : GetDefaultFont();
        }

        private static Font GetDefaultFont()
        {
            return EditorGUIUtility.Load("Fonts/Inter/Inter-Regular.ttf") as Font
                ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private static Font LoadFont(string nameWithoutExtension)
        {
            var guids = AssetDatabase.FindAssets(nameWithoutExtension);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(nameWithoutExtension + ".ttf", System.StringComparison.Ordinal))
                {
                    var font = AssetDatabase.LoadAssetAtPath<Font>(path);
                    if (font != null)
                    {
                        return font;
                    }
                }
            }

            // asmdef名を手がかりにパスを組み立てるフォールバック
            var asmdefGuids = AssetDatabase.FindAssets(PackageRootMarker);
            foreach (var guid in asmdefGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("Editor/" + PackageRootMarker + ".asmdef", System.StringComparison.Ordinal))
                {
                    var packageRoot = path.Substring(0, path.Length - ("/Editor/" + PackageRootMarker + ".asmdef").Length);
                    var fontPath = packageRoot + "/" + string.Format(FontAssetPath, nameWithoutExtension);
                    return AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                }
            }

            Debug.LogWarning("[VRCQuickImporter] フォントの読み込みに失敗しました: " + nameWithoutExtension);
            return null;
        }
    }
}
