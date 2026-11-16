using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Prototype.BaseEconomy;
using Prototype.Combat;
using Prototype.UnityClient;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;

namespace Prototype.Editor
{
    /// <summary>Explicit, repeatable entry points. Nothing regenerates scenes on import.</summary>
    public static class PrototypeProjectBuilder
    {
        public const string HubScenePath = "Assets/Prototype/Scenes/PrototypeHub.unity";
        public const string FontAssetPath = "Assets/Prototype/Art/Fonts/PrototypeChinese.asset";
        private const string FontSourcePath = "Assets/Prototype/Art/Fonts/NotoSansCJKsc-Regular.otf";
        private const string TmpSettingsPath = "Assets/Prototype/Art/Fonts/TMPResources/Resources/TMP Settings.asset";
        private const string PipelinePath = "Assets/Settings/UniversalRP.asset";

        [MenuItem("Prototype/Generate Hub")]
        public static void GenerateHub()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ConfigureProject();
            var font = EnsureChineseFont();
            Directory.CreateDirectory(Path.GetDirectoryName(HubScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0, 0, -10);
            var camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(11, 20, 32, 255);
            var root = new GameObject("Prototype Hub");
            var controller = root.AddComponent<PrototypeHubController>();
            controller.SetFontForScene(font);
            EditorUtility.SetDirty(controller);
            if (!EditorSceneManager.SaveScene(scene, HubScenePath))
                throw new InvalidOperationException("Could not save Hub scene.");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(HubScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("PROTOTYPE_HUB_READY " + HubScenePath + " | font " + FontAssetPath
                + " | UI is created by PrototypeHubController.Start from the shared runtime factory.");
        }

        private static void ConfigureProject()
        {
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelinePath);
            if (pipeline == null) throw new FileNotFoundException("Universal 2D template pipeline is missing: " + PipelinePath);
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorSettings.serializationMode = SerializationMode.ForceText;
            PlayerSettings.companyName = "Local Prototype";
            PlayerSettings.productName = "条件指令与基地经营原型";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultIsNativeResolution = false;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var settingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settingsAsset != null)
            {
                var settings = new SerializedObject(settingsAsset);
                var activeInput = settings.FindProperty("activeInputHandler");
                if (activeInput != null) { activeInput.intValue = 1; settings.ApplyModifiedPropertiesWithoutUndo(); }
            }
        }

        public static TMP_FontAsset EnsureChineseFont()
        {
            var settings = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
            if (settings == null)
                throw new FileNotFoundException("Bundled TMP settings are missing: " + TmpSettingsPath);
            var settingsObject = new SerializedObject(settings);
            settingsObject.FindProperty("m_ClearDynamicDataOnBuild").boolValue = false;
            settingsObject.FindProperty("m_enableEmojiSupport").boolValue = false;
            settingsObject.FindProperty("m_defaultSpriteAsset").objectReferenceValue = null;
            settingsObject.FindProperty("m_fallbackFontAssets").arraySize = 0;
            settingsObject.ApplyModifiedPropertiesWithoutUndo();
            TMP_Settings.LoadDefaultSettings();

            var source = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            if (source == null) throw new FileNotFoundException("Noto source font is missing: " + FontSourcePath);
            var importer = AssetImporter.GetAtPath(FontSourcePath) as TrueTypeFontImporter;
            if (importer != null && !importer.includeFontData)
            {
                importer.includeFontData = true;
                importer.SaveAndReimport();
                source = AssetDatabase.LoadAssetAtPath<Font>(FontSourcePath);
            }
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (font == null)
            {
                if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null)
                    throw new InvalidOperationException("Bundled TMP Distance Field shader did not import.");
                font = TMP_FontAsset.CreateFontAsset(source, 64, 7, GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
                if (font == null) throw new InvalidOperationException("Could not create Noto TMP font asset.");
                font.name = "Prototype Chinese";
                AssetDatabase.CreateAsset(font, FontAssetPath);
            }
            font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            font.isMultiAtlasTexturesEnabled = true;
            // This TMP version resolves bold special characters through its weight table.
            // Reuse the existing Noto SDF as a fallback; no new or counterfeit font file.
            var weights = font.fontWeightTable;
            if (weights[(int)FontWeight.Bold / 100].regularTypeface == null)
            {
                var bold = weights[(int)FontWeight.Bold / 100];
                bold.regularTypeface = font;
                weights[(int)FontWeight.Bold / 100] = bold;
            }

            var characters = CollectPrototypeCharacters();
            font.TryAddCharacters(characters, out string missing, true);
            var atlasIndex = 0;
            foreach (var atlas in font.atlasTextures)
            {
                if (atlas == null) continue;
                atlas.name = "Prototype Chinese Atlas " + atlasIndex++;
                if (!AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
                EditorUtility.SetDirty(atlas);
            }
            font.material.name = "Prototype Chinese Material";
            if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font.material);
            EditorUtility.SetDirty(font);
            settingsObject.Update();
            settingsObject.FindProperty("m_defaultFontAsset").objectReferenceValue = font;
            settingsObject.ApplyModifiedPropertiesWithoutUndo();
            TMP_Settings.defaultFontAsset = font;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("Noto missing prewarm glyphs: " + missing);
            if (!font.HasCharacters("战斗基地法师急救厨房供粮顾客公库钱包账本重置…", out List<char> requiredMissing))
                throw new InvalidOperationException("Required Chinese glyphs are missing: " + new string(requiredMissing.ToArray()));
            bool alternative;
            if (TMP_FontAssetUtilities.GetCharacterFromFontAsset(0x2026, font, false, FontStyles.Normal, FontWeight.Regular, out alternative) == null
                || TMP_FontAssetUtilities.GetCharacterFromFontAsset(0x2026, font, false, FontStyles.Bold, FontWeight.Bold, out alternative) == null)
                throw new InvalidOperationException("Noto ellipsis cannot be resolved for both regular and bold UI text.");
            Debug.Log("PROTOTYPE_FONT_READY " + characters.Length + " prewarm characters, " + atlasIndex + " atlases.");
            return font;
        }

        private static string CollectPrototypeCharacters()
        {
            var characters = new SortedSet<char>();
            for (var code = 32; code < 127; code++) characters.Add((char)code);
            foreach (var path in Directory.GetFiles("Assets/Prototype", "*", SearchOption.AllDirectories))
            {
                if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    && !path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                AddCharacters(characters, File.ReadAllText(path, Encoding.UTF8));
            }
            var streamingRoot = "Assets/StreamingAssets/Prototype";
            if (Directory.Exists(streamingRoot))
                foreach (var path in Directory.GetFiles(streamingRoot, "*.json", SearchOption.AllDirectories))
                    AddCharacters(characters, File.ReadAllText(path, Encoding.UTF8));
            // TMP's Ellipsis mode requests U+2026 internally even when UI source only names the enum.
            AddCharacters(characters, "战斗基地法师急救厨房供粮顾客公库钱包账本重置 × → ↑ ↓ · ≤ …");
            return new string(characters.ToArray());
        }

        private static void AddCharacters(ISet<char> target, string content)
        {
            foreach (var character in content)
                if (!char.IsControl(character) && !char.IsSurrogate(character)
                    && char.GetUnicodeCategory(character) != UnicodeCategory.Format) target.Add(character);
        }

        [MenuItem("Prototype/Build Windows Development")]
        public static void BuildWindowsDevelopment()
        {
            if (!File.Exists(HubScenePath)) GenerateHub();
            else { ConfigureProject(); EnsureChineseFont(); }
            var configuredPath = Environment.GetEnvironmentVariable("PROTOTYPE_BUILD_PATH");
            var output = string.IsNullOrWhiteSpace(configuredPath)
                ? Path.GetFullPath("Builds/Windows/Prototype.exe") : Path.GetFullPath(configuredPath);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { HubScenePath },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            });
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Windows development build failed: " + report.summary.result);
            var notices = Path.Combine(Path.GetDirectoryName(output), "ThirdPartyNotices", "NotoSansCJK");
            Directory.CreateDirectory(notices);
            File.Copy("Assets/Prototype/Art/Fonts/OFL.txt", Path.Combine(notices, "OFL.txt"), true);
            File.Copy("Assets/Prototype/Art/Fonts/SOURCE.txt", Path.Combine(notices, "SOURCE.txt"), true);
            Debug.Log("PROTOTYPE_BUILD_READY " + output + " | " + report.summary.totalSize + " bytes");
        }

        [MenuItem("Prototype/Export Domain Examples")]
        public static void ExportExamples()
        {
            var configuredPath = Environment.GetEnvironmentVariable("PROTOTYPE_REPORT_PATH");
            var output = string.IsNullOrWhiteSpace(configuredPath) ? Path.GetFullPath("Builds/Examples") : Path.GetFullPath(configuredPath);
            Directory.CreateDirectory(output);
            var combatJson = File.ReadAllText("Assets/StreamingAssets/Prototype/Combat/default.json", Encoding.UTF8);
            var combat = new CombatSession(JsonUtility.FromJson<CombatScenario>(combatJson));
            combat.Step();
            File.WriteAllText(Path.Combine(output, "combat_heal_first.txt"), combat.ExportLogText(), Encoding.UTF8);
            combat.Reset();
            var mage = combat.GetScenario().units.First(unit => unit.id == "mage");
            var healing = Array.FindIndex(mage.instructions, instruction => instruction.actionId == "first_aid");
            var attack = Array.FindIndex(mage.instructions, instruction => instruction.actionId == "basic_attack");
            if (healing >= 0 && attack >= 0)
            {
                combat.MoveInstruction("mage", attack, 0);
                combat.Reset();
                combat.Step();
                File.WriteAllText(Path.Combine(output, "combat_attack_first.txt"), combat.ExportLogText(), Encoding.UTF8);
            }
            var baseJson = File.ReadAllText("Assets/StreamingAssets/Prototype/BaseEconomy/default.json", Encoding.UTF8);
            var scenario = JsonUtility.FromJson<BaseScenario>(baseJson);
            ExportBaseExample(output, "base_near", scenario.WithKitchen(scenario.nearKitchenPosition));
            ExportBaseExample(output, "base_far", scenario.WithKitchen(scenario.farKitchenPosition));
            Debug.Log("PROTOTYPE_EXAMPLES_READY " + output);
        }

        private static void ExportBaseExample(string directory, string name, BaseScenario scenario)
        {
            var session = new BaseSession(scenario);
            session.RecruitCook();
            session.RunUntilFirstMeal(scenario.runUntilFirstMealMaxTicks);
            File.WriteAllText(Path.Combine(directory, name + ".txt"), session.ExportLedgerText(), Encoding.UTF8);
            File.WriteAllText(Path.Combine(directory, name + ".csv"), session.ExportLedgerCsv(), Encoding.UTF8);
        }
    }
}
