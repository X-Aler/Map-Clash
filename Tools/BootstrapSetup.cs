using System;
using System.Collections.Generic;
using System.IO;
using MapClash;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UIElements;

public static class BootstrapSetup
{
    private const string Root = "Assets/_Project";

    public static string ImportEssentials()
    {
        if (AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf") == null)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TMP_Text).Assembly);
            AssetDatabase.ImportPackage(Path.Combine(package.resolvedPath, "Package Resources/TMP Essential Resources.unitypackage"), false);
        }

        return "TMP Essentials imported with Liberation Sans and its OFL license.";
    }

    public static string CreateAssets()
    {
        Directory.CreateDirectory(Root + "/Configs");
        Directory.CreateDirectory(Root + "/Scenes");
        Directory.CreateDirectory(Root + "/UI/Fonts");
        Directory.CreateDirectory(Root + "/UI/Localization");
        AssetDatabase.Refresh();
        var settings = CreateLocalization();
        var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
        if (font == null)
        {
            throw new InvalidOperationException("Import TMP Essentials before creating Bootstrap assets.");
        }

        var characters = new System.Text.StringBuilder();
        for (var codepoint = 32; codepoint < 127; codepoint++) { characters.Append((char)codepoint); }
        for (var codepoint = 1024; codepoint < 1280; codepoint++) { characters.Append((char)codepoint); }
        characters.Append("—–…×→←•·%+−°");

        var labelFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Root + "/UI/Fonts/MapLabels.asset");
        if (labelFont == null)
        {
            labelFont = TMP_FontAsset.CreateFontAsset(font, 64, 8, GlyphRenderMode.SDFAA, 2048, 2048, TMPro.AtlasPopulationMode.Dynamic, true);
            labelFont.name = "Map Labels";
            labelFont.TryAddCharacters(characters.ToString());
            AssetDatabase.CreateAsset(labelFont, Root + "/UI/Fonts/MapLabels.asset");
            AssetDatabase.AddObjectToAsset(labelFont.material, labelFont);
            foreach (var texture in labelFont.atlasTextures) { AssetDatabase.AddObjectToAsset(texture, labelFont); }
        }

        var uiFont = AssetDatabase.LoadAssetAtPath<UnityEngine.TextCore.Text.FontAsset>(Root + "/UI/Fonts/Interface.asset");
        if (uiFont == null)
        {
            uiFont = UnityEngine.TextCore.Text.FontAsset.CreateFontAsset(font, 64, 8, GlyphRenderMode.SDFAA, 2048, 2048,
                UnityEngine.TextCore.Text.AtlasPopulationMode.Dynamic, true);
            uiFont.name = "Interface";
            uiFont.TryAddCharacters(characters.ToString());
            AssetDatabase.CreateAsset(uiFont, Root + "/UI/Fonts/Interface.asset");
            AssetDatabase.AddObjectToAsset(uiFont.material, uiFont);
            foreach (var texture in uiFont.atlasTextures) { AssetDatabase.AddObjectToAsset(texture, uiFont); }
        }

        var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(Root + "/UI/PanelTextSettings.asset");
        if (textSettings == null)
        {
            textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
            textSettings.defaultFontAsset = uiFont;
            AssetDatabase.CreateAsset(textSettings, Root + "/UI/PanelTextSettings.asset");
        }

        var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(Root + "/UI/PanelSettings.asset");
        if (panel == null)
        {
            panel = ScriptableObject.CreateInstance<PanelSettings>();
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.match = 0.5f;
            panel.textSettings = textSettings;
            AssetDatabase.CreateAsset(panel, Root + "/UI/PanelSettings.asset");
        }

        var balance = AssetDatabase.LoadAssetAtPath<GameBalanceConfig>(Root + "/Configs/GameBalance.asset");
        if (balance == null)
        {
            balance = ScriptableObject.CreateInstance<GameBalanceConfig>();
            AssetDatabase.CreateAsset(balance, Root + "/Configs/GameBalance.asset");
        }

        AssetDatabase.SaveAssets();
        var oldScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        var cameraObject = new GameObject("Map camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.orthographic = true;
        camera.clearFlags = CameraClearFlags.SolidColor;
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0, 0, -10);
        cameraObject.AddComponent<AudioListener>();
        var mapObject = new GameObject("World map");
        var mapView = mapObject.AddComponent<WorldMapView>();
        Assign(mapView, "_camera", camera);
        Assign(mapView, "_mapShader", Shader.Find("MapClash/WorldMap"));
        Assign(mapView, "_labelFont", labelFont);
        var cameraConfig = AssetDatabase.LoadAssetAtPath<MapCameraConfig>(Root + "/Configs/MapCamera.asset");
        if (cameraConfig == null)
        {
            cameraConfig = ScriptableObject.CreateInstance<MapCameraConfig>();
            AssetDatabase.CreateAsset(cameraConfig, Root + "/Configs/MapCamera.asset");
        }

        Assign(mapView, "_cameraConfig", cameraConfig);
        var root = new GameObject("Map Clash");
        var document = root.AddComponent<UIDocument>();
        document.panelSettings = panel;
        document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Root + "/UI/Resources/MapClashUi.uxml");
        var ui = root.AddComponent<GameUi>();
        var app = root.AddComponent<GameApplication>();
        Assign(app, "_mapView", mapView);
        Assign(app, "_ui", ui);
        Assign(app, "_document", document);
        var scope = root.AddComponent<ApplicationScope>();
        Assign(scope, "_terrainData", AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "/Maps/Resources/WorldTerrain.bytes"));
        Assign(scope, "_balance", balance);
        Assign(scope, "_application", app);
        EditorSceneManager.SaveScene(scene, Root + "/Scenes/Bootstrap.unity");

        var buildScenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(Root + "/Scenes/Bootstrap.unity", true) };
        foreach (var entry in EditorBuildSettings.scenes)
        {
            if (entry.path != Root + "/Scenes/Bootstrap.unity") { buildScenes.Add(entry); }
        }

        EditorBuildSettings.scenes = buildScenes.ToArray();
        if (oldScene.IsValid()) { SceneManager.SetActiveScene(oldScene); }
        EditorSceneManager.CloseScene(scene, true);
        AssetDatabase.SaveAssets();
        return "Created wired Bootstrap scene, balance, RU/EN tables, panel and Cyrillic fonts. Open Bootstrap to play; original scene remains open.";
    }

    private static LocalizationSettings CreateLocalization()
    {
        var settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings == null)
        {
            settings = ScriptableObject.CreateInstance<LocalizationSettings>();
            AssetDatabase.CreateAsset(settings, Root + "/UI/Localization/LocalizationSettings.asset");
            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
        }

        foreach (var code in new[] { "en", "ru" })
        {
            if (LocalizationEditorSettings.GetLocale(code) == null)
            {
                var locale = Locale.CreateLocale(code);
                AssetDatabase.CreateAsset(locale, Root + "/UI/Localization/" + code + ".asset");
                LocalizationEditorSettings.AddLocale(locale);
            }
        }

        var collection = LocalizationEditorSettings.GetStringTableCollection("Game") ??
            LocalizationEditorSettings.CreateStringTableCollection("Game", Root + "/UI/Localization");
        var lines = File.ReadAllLines("Tools/Localization/Game.tsv", System.Text.Encoding.UTF8);
        for (var row = 1; row < lines.Length; row++)
        {
            if (string.IsNullOrWhiteSpace(lines[row])) { continue; }
            var fields = lines[row].Split('\t');
            if (fields.Length != 3) { throw new InvalidDataException("Invalid translation row " + row); }
            for (var column = 1; column <= 2; column++)
            {
                var table = (StringTable)collection.GetTable(column == 1 ? "en" : "ru");
                var entry = table.AddEntry(fields[0], fields[column].Replace("\\n", "\n"));
                entry.IsSmart = false;
                EditorUtility.SetDirty(table);
            }
        }

        collection.SetPreloadTableFlag(true);
        EditorUtility.SetDirty(collection.SharedData);
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        return settings;
    }

    private static void Assign(UnityEngine.Object target, string property, UnityEngine.Object value)
    {
        if (value == null) { throw new InvalidOperationException(target.name + ": missing " + property); }
        var serialized = new SerializedObject(target);
        serialized.FindProperty(property).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
