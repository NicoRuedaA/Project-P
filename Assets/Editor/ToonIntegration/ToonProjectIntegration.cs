using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Pokemon3D.Editor
{
    public static class ToonProjectIntegration
    {
        static readonly string Evidence = Path.GetFullPath("Library/ToonIntegration");
        [Serializable] sealed class Report
        {
            public string package, pipeline, shader, screenshot, verification;
            public bool graphicsVerified;
            public int converted, alreadyToon, normalizedToon, preservedSpecial, preservedOther, assertions, savedSceneRenderers;
            public List<string> convertedPaths = new List<string>();
            public List<string> normalizedPaths = new List<string>();
            public List<string> preservedPaths = new List<string>();
        }
        sealed class Snapshot
        {
            public Material material;
            public Texture texture;
            public Color color;
            public Vector2 scale, offset;
            public int queue;
            public bool gallery;
        }

        [MenuItem("Wildbound/Rendering/Apply Unity Toon Shader")]
        public static void Apply()
        {
            string pipeline = RequireSupportedPipeline();
            var shader = Shader.Find(ToonMaterials.ShaderName);
            Check(shader && shader.isSupported, "Unity Toon Shader unavailable.");
            var report = NewReport(pipeline);
            var candidates = new List<Snapshot>();
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat", StringComparison.Ordinal)) continue; // Never modify embedded source FBX materials.
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!material || !material.shader) throw new InvalidOperationException("Unreadable material: " + path);
                var name = material.shader.name;
                if (name == ToonMaterials.ShaderName)
                {
                    report.alreadyToon++;
                    bool shouldUseBuiltInKeyword = GraphicsSettings.currentRenderPipeline == null;
                    if (material.IsKeywordEnabled("UTS_RP_BUILTIN") != shouldUseBuiltInKeyword)
                    {
                        BackupMaterial(path);
                        Undo.RecordObject(material, "Normalize Unity Toon Shader pipeline variant");
                        if (shouldUseBuiltInKeyword) material.EnableKeyword("UTS_RP_BUILTIN");
                        else material.DisableKeyword("UTS_RP_BUILTIN");
                        EditorUtility.SetDirty(material);
                        report.normalizedToon++;
                        report.normalizedPaths.Add(path);
                    }
                    continue;
                }
                bool gallery = name == "Wildbound/Pokemon Gallery";
                if (gallery && ((material.HasProperty("_UseTile") && material.GetFloat("_UseTile") > .5f) ||
                    (material.HasProperty("_Flame") && material.GetFloat("_Flame") > .5f)))
                {
                    report.preservedSpecial++; report.preservedPaths.Add(path); continue;
                }
                // Transparency, custom FX, UI and source-specific shaders need their own explicit mapping.
                if ((name != "Wildbound/Toon" && !gallery && name != "Standard") ||
                    (name == "Standard" && material.GetFloat("_Mode") != 0) || material.renderQueue >= 2450)
                {
                    report.preservedOther++; report.preservedPaths.Add(path); continue;
                }
                candidates.Add(new Snapshot { material = material, color = material.color,
                    texture = material.HasProperty("_MainTex") ? material.mainTexture : null,
                    scale = material.HasProperty("_MainTex") ? material.mainTextureScale : Vector2.one,
                    offset = material.HasProperty("_MainTex") ? material.mainTextureOffset : Vector2.zero,
                    queue = material.renderQueue, gallery = gallery });
            }
            Directory.CreateDirectory(Evidence);
            foreach (var state in candidates)
            {
                var path = AssetDatabase.GetAssetPath(state.material);
                BackupMaterial(path);
                Undo.RecordObject(state.material, "Apply Unity Toon Shader");
                state.material.shader = shader;
                state.material.shaderKeywords = Array.Empty<string>();
                ToonMaterials.Configure(state.material, state.color, state.texture, state.scale, state.offset, state.gallery);
                state.material.SetFloat("_AutoRenderQueue", 0);
                state.material.renderQueue = state.queue;
                EditorUtility.SetDirty(state.material);
                Check(state.material.color == state.color && state.material.mainTexture == state.texture &&
                    state.material.mainTextureScale == state.scale && state.material.mainTextureOffset == state.offset,
                    "Material color/map/UV preservation failed: " + path);
                report.convertedPaths.Add(path); report.converted++;
            }
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            const string templatePath = "Assets/Resources/UnityToonDefault.mat";
            if (!AssetDatabase.LoadAssetAtPath<Material>(templatePath))
            {
                var template = new Material(shader) { name = "Unity Toon Default" };
                ToonMaterials.Configure(template, Color.white, null, Vector2.one, Vector2.zero);
                AssetDatabase.CreateAsset(template, templatePath);
            }
            AssetDatabase.SaveAssets();
            RunAssertions(report);
            report.screenshot = VerifySavedScene(report);
            report.verification = report.graphicsVerified ? "Material mapping, runtime factory, saved-scene references and GPU rendering checked." :
                "Material mapping, runtime factory and saved-scene references checked. GPU shader compilation and visual rendering unavailable on the Null graphics device.";
            Check(!ShaderUtil.ShaderHasError(shader), "Unity Toon Shader has compilation errors.");
            File.WriteAllText(Path.Combine(Evidence, "report.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[ToonIntegration] " + JsonUtility.ToJson(report));
        }

        public static void ApplyBatch()
        {
            try { Apply(); EditorApplication.Exit(0); }
            catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
        }
        static Report NewReport(string pipeline) => new Report { package = ToonPackageInstaller.Package, pipeline = pipeline, shader = ToonMaterials.ShaderName };
        static void BackupMaterial(string path)
        {
            var backup = Path.Combine(Evidence, "OriginalMaterials", path);
            Directory.CreateDirectory(Path.GetDirectoryName(backup));
            if (!File.Exists(backup)) File.Copy(path, backup); // Preserve the original rollback bytes across repeated runs.
        }
        static string RequireSupportedPipeline()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Apply Unity Toon Shader in Edit mode.");
            var defaultPipeline = GraphicsSettings.defaultRenderPipeline;
            var qualityPipeline = QualitySettings.renderPipeline;
            Check(!defaultPipeline || defaultPipeline is UniversalRenderPipelineAsset,
                "Apply Unity Toon Shader supports Built-in RP and URP only; the Graphics pipeline asset is unsupported.");
            Check(!qualityPipeline || qualityPipeline is UniversalRenderPipelineAsset,
                "Apply Unity Toon Shader supports Built-in RP and URP only; the active Quality pipeline asset is unsupported.");
            var currentPipeline = GraphicsSettings.currentRenderPipeline;
            Check(currentPipeline == null || currentPipeline is UniversalRenderPipelineAsset,
                "Apply Unity Toon Shader supports Built-in RP and URP only; the active render pipeline is unsupported.");
            return currentPipeline == null ? "Built-in" : "Universal Render Pipeline";
        }
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static void RunAssertions(Report report)
        {
            var color = new Color(.2f, .6f, .8f, 1);
            var material = ToonMaterials.Create(color, Texture2D.whiteTexture, true);
            try
            {
                Check(material.shader.name == ToonMaterials.ShaderName, "Runtime factory used a different shader."); report.assertions++;
                Check(material.color == color && material.GetColor("_BaseColor") == color, "Runtime base tint mismatch."); report.assertions++;
                Check(material.mainTexture == Texture2D.whiteTexture && material.GetFloat("_Use_BaseAs1st") == 1 &&
                    material.GetFloat("_Use_1stAs2nd") == 1, "Shade regions lost the source texture."); report.assertions++;
                Check(material.GetColor("_2nd_ShadeColor").b > 0 && material.GetFloat("_CullMode") == 0,
                    "Dark shade or gallery double-sided configuration invalid."); report.assertions++;
                    Check(material.IsKeywordEnabled("UTS_RP_BUILTIN") == (GraphicsSettings.currentRenderPipeline == null) &&
                    material.IsKeywordEnabled("_IS_CLIPPING_OFF") &&
                    material.GetFloat("_IsBaseMapAlphaAsClippingMask") == 0, "Opaque packed-alpha preservation invalid."); report.assertions++;
                Check(Resources.Load<Material>("UnityToonDefault") != null, "Build-retained runtime template missing."); report.assertions++;
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Toon factory verification"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                var part = World.Part("Probe", PrimitiveType.Sphere, Vector3.zero, Vector3.one, color, root.transform);
                Check(part.GetComponent<Renderer>().sharedMaterial.shader.name == ToonMaterials.ShaderName,
                    "Procedural World.Part did not use Unity Toon Shader."); report.assertions++;
                UnityEngine.Object.DestroyImmediate(part.GetComponent<Renderer>().sharedMaterial);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static string VerifySavedScene(Report report)
        {
            var scene = EditorSceneManager.OpenPreviewScene(EditableSceneAuthoring.ScenePath);
            try
            {
                var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
                foreach (var renderer in renderers) foreach (var material in renderer.sharedMaterials)
                {
                    Check(material && material.shader && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader),
                        "Saved Valley contains an invalid material on " + renderer.name);
                    if (material.shader.name == ToonMaterials.ShaderName)
                        Check(material.IsKeywordEnabled("UTS_RP_BUILTIN") == (GraphicsSettings.currentRenderPipeline == null),
                            "Saved material pipeline keyword does not match the active render pipeline.");
                }
                report.savedSceneRenderers = renderers.Length; report.assertions++;
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
                Check(camera, "Saved Valley camera missing.");
                if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return "Unavailable: batch Editor has no graphics device.";
                report.graphicsVerified = true;
                var target = RenderTexture.GetTemporary(1280, 720, 24);
                var previous = RenderTexture.active; var oldTarget = camera.targetTexture;
                var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                try
                {
                    camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                    var path = Path.Combine(Evidence, "valley-unity-toon.png"); File.WriteAllBytes(path, image.EncodeToPNG());
                    return path;
                }
                finally
                {
                    camera.targetTexture = oldTarget; RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(target); UnityEngine.Object.DestroyImmediate(image);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
