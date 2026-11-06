using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UIImporterFree.Editor
{
public class UIImporter
{
    // Add a menu item to the Assets right-click menu
    [MenuItem("Assets/UI Importer Free/Create Prefab")]
    public static void CreatePrefabFromAsset()
    {
        // 1. Get the selected asset
        var selectedObject = Selection.activeObject;

        if (selectedObject == null || !(selectedObject is TextAsset))
        {
            EditorUtility.DisplayDialog(
                "UI Importer Free",
                "Please select a JSON file.",
                "OK");
            return;
        }

        // 2. Get the file path
        // AssetDatabase paths are always project-relative (for example, Assets/UI.json).
        // Keep the path in this format so path separator differences between macOS and Windows
        // cannot affect the Assets folder check.
        string assetPath = AssetDatabase.GetAssetPath(selectedObject).Replace("\\", "/");

        if (!assetPath.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog(
                "UI Importer Free",
                "Please select a JSON file.",
                "OK");
            return;
        }

        DoImport(assetPath);
    }

    // Actual import process (shared)
    private static void DoImport(string jsonAssetPath)
    {
        // Only project assets can be used as Prefab destinations.
        if (!jsonAssetPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("The JSON file is not inside the Assets folder. Prefab cannot be created.");
            return;
        }

        // Read the JSON
        TextAsset jsonAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonAssetPath);
        if (jsonAsset == null)
        {
            Debug.LogError("Failed to load the selected JSON file.");
            return;
        }
        string json = jsonAsset.text;

        // (The JSON exported from Figma starts with an array [ ], so wrap it for Unity parsing)
        FigmaNode[] nodes = JsonHelper.GetJsonArray<FigmaNode>(json);

        if (nodes == null || nodes.Length == 0)
        {
            Debug.LogError("Failed to parse JSON data, or the data is empty.");
            return;
        }

        // Create the Prefab in the same directory as the JSON file
        string assetDir = Path.GetDirectoryName(jsonAssetPath).Replace("\\", "/");

        // Look up the settings asset
        ImportSettings settings = null;
        string[] settingsGuids = AssetDatabase.FindAssets("t:ImportSettings");
        if (settingsGuids.Length > 0)
        {
            string settingsPath = AssetDatabase.GUIDToAssetPath(settingsGuids[0]);
            settings = AssetDatabase.LoadAssetAtPath<ImportSettings>(settingsPath);
        }

        // Create a temporary Canvas so RectTransform values serialize correctly
        // Outside of a Canvas hierarchy, the relationship between anchoredPosition and localPosition is not computed properly
        GameObject tempCanvasGO = new GameObject("__TempCanvas_UIImport__");
        Canvas tempCanvas = tempCanvasGO.AddComponent<Canvas>();
        tempCanvas.renderMode = RenderMode.ScreenSpaceOverlay;

        try
        {
            // Create a root GameObject for each node and turn it into a Prefab
            foreach (var node in nodes)
            {
                GameObject root = new GameObject(node.name);
                RectTransform rootRect = root.AddComponent<RectTransform>();
                rootRect.sizeDelta = new Vector2(node.width, node.height);

                // Apply components to the root node itself
                ApplyNodeComponents(root, rootRect, node, settings);

                // Place under the Canvas so RectTransform values are driven correctly
                root.transform.SetParent(tempCanvasGO.transform, false);

                // Flatten the children (do not reproduce the hierarchy)
                if (node.children != null && node.children.Length > 0)
                {
                    FlattenChildren(node.children, root, settings);
                }

                string prefabPath = Path.Combine(assetDir, node.name + ".prefab").Replace("\\", "/");
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                GameObject.DestroyImmediate(root);
            }
        }
        finally
        {
            // Ensure the temporary Canvas is destroyed even if an exception occurs
            GameObject.DestroyImmediate(tempCanvasGO);
        }

        Debug.Log("Figma UI Prefab generation complete.");
    }

    // Recursively walk children and place them all flat directly under the root
    private static void FlattenChildren(FigmaNode[] children, GameObject root, ImportSettings settings)
    {
        foreach (var child in children)
        {
            GameObject go = new GameObject(child.name);
            RectTransform rect = go.AddComponent<RectTransform>();

            // Basic RectTransform settings (position, size)
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(child.x, -child.y);
            rect.sizeDelta = new Vector2(child.width, child.height);

            // Apply rotation
            if (child.rotation != 0)
            {
                rect.localEulerAngles = new Vector3(0, 0, child.rotation);
            }

            // Apply components
            ApplyNodeComponents(go, rect, child, settings);

            // Place directly under root (do not reproduce hierarchy)
            go.transform.SetParent(root.transform, false);

            // Recursively flatten grandchildren as well
            if (child.children != null && child.children.Length > 0)
            {
                FlattenChildren(child.children, root, settings);
            }
        }
    }

    // Apply components (Image, Text, etc.) to a node
    private static void ApplyNodeComponents(GameObject go, RectTransform rect, FigmaNode node, ImportSettings settings)
    {
        // Apply CanvasGroup (group opacity)
        bool hasCanvasGroup = false;
        if (node.opacity < 1f && node.children != null && node.children.Length > 0)
        {
            CanvasGroup canvasGroup = go.AddComponent<CanvasGroup>();
            canvasGroup.alpha = node.opacity;
            hasCanvasGroup = true;
        }

        // Add an Image component when an image file name is specified
        if (!string.IsNullOrEmpty(node.imageFileName))
        {
            Image img = go.AddComponent<Image>();

            // Opacity (when CanvasGroup is used it controls alpha, so keep this at 1)
            Color c = img.color;
            c.a = hasCanvasGroup ? 1f : node.opacity;
            img.color = c;

            // Strip the extension (e.g. .png) to get the Sprite name
            string spriteName = Path.GetFileNameWithoutExtension(node.imageFileName);

            // Find the matching Sprite in the project and attach it
            string[] guids = AssetDatabase.FindAssets(spriteName + " t:Sprite");
            if (guids.Length > 0)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                img.sprite = sprite;
            }
            else
            {
                Debug.LogWarning($"Sprite not found: {spriteName}");
            }
        }

        // Add an Image component for a background color (only when not an image or text)
        if (node.fillColorR >= 0 && string.IsNullOrEmpty(node.imageFileName)
            && node.type != "TEXT")
        {
            Image bgImg = go.AddComponent<Image>();
            bgImg.color = new Color(node.fillColorR, node.fillColorG, node.fillColorB,
                hasCanvasGroup ? node.fillColorA : node.fillColorA * node.opacity);
        }

        // For text nodes, add a TextMeshProUGUI component
        if (node.type == "TEXT" && !string.IsNullOrEmpty(node.text))
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = node.text;
            tmp.fontSize = node.textFontSize;
            tmp.color = new Color(node.textColorR, node.textColorG, node.textColorB, node.textColorA);

            // Multiply alpha by the node's overall opacity (CanvasGroup handles it when present)
            if (node.opacity < 1f && !hasCanvasGroup)
            {
                Color c = tmp.color;
                c.a *= node.opacity;
                tmp.color = c;
            }

            // Text alignment
            tmp.alignment = MapTextAlignment(node.textAlignHorizontal, node.textAlignVertical);

            // Line height (lineSpacing)
            if (node.textLineHeight > 0)
            {
                float defaultLineHeight = node.textFontSize * 1.2f;
                tmp.lineSpacing = ((node.textLineHeight / defaultLineHeight) - 1f) * 100f;
            }

            // Character spacing
            if (node.textLetterSpacing != 0)
            {
                tmp.characterSpacing = node.textLetterSpacing;
            }

            // Font asset resolution (4-step fallback)
            TMP_FontAsset resolvedFont = null;

            // (1) Look up a mapping in the settings' fontMappings
            if (resolvedFont == null && settings != null && settings.fontMappings != null
                && !string.IsNullOrEmpty(node.textFontFamily))
            {
                foreach (var mapping in settings.fontMappings)
                {
                    if (mapping.figmaFontName == node.textFontFamily && mapping.fontAsset != null)
                    {
                        resolvedFont = mapping.fontAsset;
                        break;
                    }
                }
            }

            // (2) Search assets by the Figma font name
            if (resolvedFont == null && !string.IsNullOrEmpty(node.textFontFamily))
            {
                string[] fontGuids = AssetDatabase.FindAssets(node.textFontFamily + " t:TMP_FontAsset");
                if (fontGuids.Length > 0)
                {
                    string fontPath = AssetDatabase.GUIDToAssetPath(fontGuids[0]);
                    resolvedFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                }
            }

            // (3) Search for any TMP FontAsset in the project
            if (resolvedFont == null)
            {
                string[] allFontGuids = AssetDatabase.FindAssets("t:TMP_FontAsset");
                if (allFontGuids.Length > 0)
                {
                    string fontPath = AssetDatabase.GUIDToAssetPath(allFontGuids[0]);
                    resolvedFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                    Debug.LogWarning($"TMP FontAsset not found: {node.textFontFamily} (falling back to project font \"{resolvedFont.name}\")");
                }
            }

            // Apply the font
            if (resolvedFont != null)
            {
                tmp.font = resolvedFont;
            }
            else
            {
                Debug.LogWarning($"No TMP FontAsset exists in the project: {node.textFontFamily} (falling back to TMP default font)");
            }

            // Text overflow / auto-resize settings
            if (node.textAutoResize == "TRUNCATE")
            {
                tmp.overflowMode = TextOverflowModes.Ellipsis;
            }
            else if (node.textAutoResize == "NONE")
            {
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.textWrappingMode = TextWrappingModes.Normal;
            }
            else
            {
                tmp.overflowMode = TextOverflowModes.Overflow;
                tmp.textWrappingMode = TextWrappingModes.Normal;

                var fitter = go.AddComponent<ContentSizeFitter>();
                if (node.textAutoResize == "WIDTH_AND_HEIGHT")
                {
                    fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
                else if (node.textAutoResize == "HEIGHT")
                {
                    fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
            }

            // Bold / Italic style settings
            if (!string.IsNullOrEmpty(node.textFontStyle))
            {
                string style = node.textFontStyle.ToLower();
                FontStyles fontStyles = FontStyles.Normal;
                if (style.Contains("bold"))
                    fontStyles |= FontStyles.Bold;
                if (style.Contains("italic"))
                    fontStyles |= FontStyles.Italic;
                tmp.fontStyle = fontStyles;
            }
        }
    }

    // Convert Figma's textAlignHorizontal / textAlignVertical to TMP TextAlignmentOptions
    private static TextAlignmentOptions MapTextAlignment(string horizontal, string vertical)
    {
        if (string.IsNullOrEmpty(horizontal)) horizontal = "LEFT";
        if (string.IsNullOrEmpty(vertical)) vertical = "TOP";

        switch (vertical)
        {
            case "CENTER":
                switch (horizontal)
                {
                    case "CENTER": return TextAlignmentOptions.Center;
                    case "RIGHT": return TextAlignmentOptions.MidlineRight;
                    case "JUSTIFIED": return TextAlignmentOptions.MidlineJustified;
                    default: return TextAlignmentOptions.MidlineLeft;
                }
            case "BOTTOM":
                switch (horizontal)
                {
                    case "CENTER": return TextAlignmentOptions.Bottom;
                    case "RIGHT": return TextAlignmentOptions.BottomRight;
                    case "JUSTIFIED": return TextAlignmentOptions.BottomJustified;
                    default: return TextAlignmentOptions.BottomLeft;
                }
            default: // "TOP"
                switch (horizontal)
                {
                    case "CENTER": return TextAlignmentOptions.Top;
                    case "RIGHT": return TextAlignmentOptions.TopRight;
                    case "JUSTIFIED": return TextAlignmentOptions.TopJustified;
                    default: return TextAlignmentOptions.TopLeft;
                }
        }
    }

    // --- Data classes for JSON parsing ---
    [System.Serializable]
    internal class FigmaNode
    {
        public string name;
        public string type;
        public float x;
        public float y;
        public float width;
        public float height;
        public float opacity;
        public string imageFileName;
        // Text properties (TextMeshPro)
        public string text;
        public float textFontSize;
        public string textFontFamily;
        public string textFontStyle;
        public float textColorR;
        public float textColorG;
        public float textColorB;
        public float textColorA = 1f;
        public string textAlignHorizontal;
        public string textAlignVertical;
        public float textLineHeight = -1f;
        public float textLetterSpacing;
        public string textAutoResize;
        // Fill color (background)
        public float fillColorR = -1f;
        public float fillColorG = -1f;
        public float fillColorB = -1f;
        public float fillColorA = -1f;
        // Rotation (degrees, counterclockwise positive)
        public float rotation;
        public FigmaNode[] children;
    }

    // Helper class to parse a root-level [ ] array with Unity's JsonUtility
    internal static class JsonHelper
    {
        public static T[] GetJsonArray<T>(string json)
        {
            string newJson = "{ \"array\": " + json + "}";
            Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
            return wrapper.array;
        }

        [System.Serializable]
        private class Wrapper<T>
        {
            public T[] array;
        }
    }
}
} // namespace UIImporterFree.Editor
