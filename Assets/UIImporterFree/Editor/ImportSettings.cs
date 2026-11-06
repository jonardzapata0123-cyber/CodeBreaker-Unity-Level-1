using UnityEngine;
using TMPro;
using UnityEditor;

namespace UIImporterFree.Editor
{
public class ImportSettings : ScriptableObject
{
    [System.Serializable]
    public class FontMapping
    {
        [Tooltip("Font name in Figma")]
        public string figmaFontName;
        [Tooltip("Corresponding TMP Font Asset")]
        public TMP_FontAsset fontAsset;
    }

    [Header("Font Mappings")]
    [Tooltip("Mapping table from Figma font names to TMP Font Assets")]
    public FontMapping[] fontMappings;

    [MenuItem("Assets/UI Importer Free/Create Settings")]
    public static void CreateSettingsAsset()
    {
        string[] existingGuids = AssetDatabase.FindAssets("t:ImportSettings");
        if (existingGuids.Length > 0)
        {
            string existingPath = AssetDatabase.GUIDToAssetPath(existingGuids[0]);
            var existingAsset = AssetDatabase.LoadAssetAtPath<ImportSettings>(existingPath);

            EditorUtility.DisplayDialog(
                "UI Importer Free",
                "ImportSettings already exists.\nThe existing settings asset has been selected.",
                "OK");

            Selection.activeObject = existingAsset;
            EditorGUIUtility.PingObject(existingAsset);
            return;
        }

        var asset = ScriptableObject.CreateInstance<ImportSettings>();

        // Use the selected path only when invoked from the Project window
        string targetPath;
        var focusedWindow = EditorWindow.focusedWindow;
        if (focusedWindow != null && focusedWindow.GetType().Name == "ProjectBrowser")
        {
            targetPath = GetSelectedPathOrFallback();
        }
        else
        {
            targetPath = "Assets";
        }
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            System.IO.Path.Combine(targetPath, "ImportSettings.asset"));

        AssetDatabase.CreateAsset(asset, assetPath);
        AssetDatabase.SaveAssets();
        ProjectWindowUtil.ShowCreatedAsset(asset);
    }

    private static string GetSelectedPathOrFallback()
    {
        string path = "Assets";
        Object selected = Selection.activeObject;
        if (selected == null) return path;

        string selectedPath = AssetDatabase.GetAssetPath(selected);
        if (string.IsNullOrEmpty(selectedPath)) return path;

        if (System.IO.Directory.Exists(selectedPath)) return selectedPath;

        string directory = System.IO.Path.GetDirectoryName(selectedPath);
        return string.IsNullOrEmpty(directory) ? path : directory;
    }
}
} // namespace UIImporterFree.Editor
