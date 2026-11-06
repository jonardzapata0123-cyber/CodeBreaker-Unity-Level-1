# UI Importer Free (compatible with Figma)

A Unity Editor extension that converts UI layout JSON exported from Figma into Unity UI Prefabs. Pair it with the companion Figma export plugin to bridge your designs and Unity's uGUI system.

This is the **Free** edition with a limited feature set. For full functionality, please consider upgrading to the **Pro** edition.

## Requirements

- Unity 6 or later
- TextMeshPro
- Unity UI (uGUI)

## Setup

1. Import the package into your Unity project.

2. **(Optional)** Create a settings asset:
   - Menu: **Assets > UI Importer Free > Create Settings**
   - This creates an `ImportSettings` ScriptableObject (one per project).

3. **(Optional)** Configure font mappings in the settings asset:
   - Add entries mapping Figma font names (e.g. `"Inter"`) to TMP_FontAsset references in your project.

## Usage

### 1. Export JSON from Figma

Use the companion Figma export plugin to export your UI layout as a JSON file.

### 2. Set up the authorization code

The Figma export plugin requires an authorization code to authenticate.

- Menu: **Assets > UI Importer Free > Create Authorization Code**
- Enter the displayed code into the Figma plugin's authorization code field.

### 3. Import into Unity

1. Place the exported JSON file and any referenced image assets (sprites) into your Unity project's `Assets` folder.
2. Select the JSON file in the Project window.
3. Right-click and choose **UI Importer Free > Create Prefab**.
4. Prefab(s) are generated in the same directory as the JSON file (one prefab per root node).

## Supported Features (Free)

| Source Feature | Unity Component |
|---|---|
| Image / Export | `Image` with Sprite reference |
| Fill Color | `Image` with solid color |
| Text | `TextMeshProUGUI` (font, size, color, alignment, line height, letter spacing, bold/italic, overflow mode) |
| Opacity (with children) | `CanvasGroup` |
| RectTransform (basic) | Position, size, rotation |

## Free Edition Limitations

The following features are available only in the **Pro** edition:

| Feature | Description |
|---|---|
| 9-Slice | `Image.Type.Sliced` with sprite border |
| Auto Layout (LayoutGroup) | `HorizontalLayoutGroup` / `VerticalLayoutGroup` |
| Mask / ScrollRect | `Mask`, `RectMask2D`, `ScrollRect` |
| Hierarchy Reproduction | Parent-child structure matching source layers |
| Constraints | `RectTransform` anchoring (Min/Center/Max/Stretch/Scale) |
| Button Auto-Detection | `Button` component auto-added by name prefix |

In the Free edition, all child elements are placed flat under the root object (no nested hierarchy).

## Font Resolution

Fonts are resolved in the following order:

1. **Settings font mappings** - User-defined Figma font name to TMP_FontAsset mapping.
2. **Asset name search** - Searches the project for a TMP_FontAsset matching the Figma font family name.
3. **Any TMP_FontAsset** - Uses the first available TMP font in the project (with a warning).
4. **TMP default font** - Falls back to TextMeshPro's default font (with a warning).

## Official Website

For more information, tutorials, and updates, visit the official website:

https://onederappli.jp/figma-to-unity/index.html

---

*"Figma" is a trademark of Figma, Inc. This asset is an independent third-party tool and is not affiliated with, endorsed by, or sponsored by Figma, Inc.*
