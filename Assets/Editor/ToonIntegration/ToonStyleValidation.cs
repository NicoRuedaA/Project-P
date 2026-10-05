using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Wildbound.Editor
{
    public static class ToonStyleValidation
    {
        [Serializable] sealed class Report
        {
            public int materials, assertions, firstChanged, repeatChanged;
            public bool outlinesDisabled, nonStylePreserved, uiApplyTested;
            public string graphicsDevice, visualVerification = "Not performed; property, preservation and panel interaction checks only.";
        }
        static void Check(bool value, string message, Report report)
        {
            if (!value) throw new InvalidOperationException(message);
            report.assertions++;
        }
        static string NonStyle(Material material)
        {
            var copy = new Material(material) { name = material.name };
            try
            {
                copy.SetFloat("_Outline_Width", .5f);
                copy.SetColor("_Outline_Color", Color.black);
                copy.shaderKeywords = copy.shaderKeywords.Where(k => k != ToonStyle.DisableOutlineKeyword).OrderBy(k => k).ToArray();
                return EditorJsonUtility.ToJson(copy);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }

        static readonly string[] ShadowFloats = {
            "_BaseColor_Step", "_ShadeColor_Step", "_BaseShade_Feather", "_1st2nd_Shades_Feather",
            "_1st_ShadeColor_Step", "_2nd_ShadeColor_Step", "_1st_ShadeColor_Feather", "_2nd_ShadeColor_Feather"
        };
        static string NonShadow(Material material)
        {
            var copy = new Material(material) { name = material.name };
            try
            {
                copy.SetColor("_1st_ShadeColor", Color.white); copy.SetColor("_2nd_ShadeColor", Color.black);
                foreach (string property in ShadowFloats) copy.SetFloat(property, 0);
                copy.SetOverrideTag(ToonStyle.ShadeBaselineTag, "verification");
                return EditorJsonUtility.ToJson(copy);
            }
            finally { UnityEngine.Object.DestroyImmediate(copy); }
        }
        [Serializable] sealed class ShadowReport
        {
            public int materials, assertions, specialMaterials;
            public bool materialCopiesOnly, defaultAppearancePreserved, oldProfileDefaults, uiBindings, visualVerification;
            public string graphicsDevice;
        }
        static void ShadowCheck(bool value, string message, ShadowReport report)
        {
            if (!value) throw new InvalidOperationException(message);
            report.assertions++;
        }
        public static string RunShadowChecks()
        {
            ToonStyleAuthoring.RequireEditMode();
            var report = new ShadowReport { materials = ToonStyleAuthoring.Materials().Length, materialCopiesOnly = true, graphicsDevice = SystemInfo.graphicsDeviceType.ToString() };
            var style = ScriptableObject.CreateInstance<ToonStyle>();
            var legacy = ScriptableObject.CreateInstance<ToonStyle>();
            try
            {
                JsonUtility.FromJsonOverwrite("{\"outline\":false,\"outlineWidth\":0}", legacy);
                ShadowCheck(!legacy.overrideToonShadows && legacy.firstShadeBrightness == 1 && legacy.secondShadeBrightness == 1 && legacy.firstShadeThreshold == .55f,
                    "Old profile serialization lost safe opt-in defaults.", report); report.oldProfileDefaults = true;
                foreach (var source in ToonStyleAuthoring.Materials())
                {
                    var copy = new Material(source) { name = source.name };
                    try
                    {
                        style.outline = ToonStyle.Current.outline; style.outlineWidth = ToonStyle.Current.outlineWidth; style.outlineColor = ToonStyle.Current.outlineColor;
                        style.overrideToonShadows = false; style.ApplyTo(copy);
                        var first = copy.GetColor("_1st_ShadeColor"); var second = copy.GetColor("_2nd_ShadeColor");
                        var bands = ShadowFloats.Select(copy.GetFloat).ToArray(); var preserved = NonShadow(copy);
                        style.ApplyTo(copy);
                        ShadowCheck(copy.GetColor("_1st_ShadeColor") == first && copy.GetColor("_2nd_ShadeColor") == second && bands.SequenceEqual(ShadowFloats.Select(copy.GetFloat)),
                            "Inactive global shadows changed authored shading: " + source.name, report);
                        style.overrideToonShadows = true; style.firstShadeBrightness = .5f; style.secondShadeBrightness = 1.5f;
                        style.firstShadeThreshold = .65f; style.secondShadeThreshold = .25f; style.firstShadeFeather = .12f; style.secondShadeFeather = .24f;
                        style.ApplyTo(copy);
                        ShadowCheck(copy.GetColor("_1st_ShadeColor") == new Color(first.r * .5f, first.g * .5f, first.b * .5f, first.a) &&
                            copy.GetColor("_2nd_ShadeColor") == new Color(second.r * 1.5f, second.g * 1.5f, second.b * 1.5f, second.a), "Relative shade brightness/alpha failed.", report);
                        ShadowCheck(copy.GetFloat("_BaseColor_Step") == .65f && copy.GetFloat("_1st_ShadeColor_Step") == .65f && copy.GetFloat("_ShadeColor_Step") == .25f &&
                            copy.GetFloat("_2nd_ShadeColor_Step") == .25f && copy.GetFloat("_BaseShade_Feather") == .12f && copy.GetFloat("_1st_ShadeColor_Feather") == .12f &&
                            copy.GetFloat("_1st2nd_Shades_Feather") == .24f && copy.GetFloat("_2nd_ShadeColor_Feather") == .24f, "Technique-specific shading aliases differ.", report);
                        ShadowCheck(NonShadow(copy) == preserved, "Textures/UV/base tint/transparency/outline/unrelated settings changed.", report);
                        ShadowCheck(!style.ApplyTo(copy), "Repeated shade style accumulated or changed.", report);
                        style.firstShadeBrightness = 0; style.secondShadeBrightness = 0; style.ApplyTo(copy);
                        style.firstShadeBrightness = 1; style.secondShadeBrightness = 1; style.ApplyTo(copy);
                        ShadowCheck(copy.GetColor("_1st_ShadeColor") == first && copy.GetColor("_2nd_ShadeColor") == second, "Black-to-original brightness did not restore authored tint.", report);
                        style.overrideToonShadows = false; style.ApplyTo(copy);
                        ShadowCheck(copy.GetColor("_1st_ShadeColor") == first && copy.GetColor("_2nd_ShadeColor") == second && bands.SequenceEqual(ShadowFloats.Select(copy.GetFloat)) &&
                            copy.GetTag(ToonStyle.ShadeBaselineTag, false, "") == "", "Disabling override did not restore authored colors/bands.", report);
                        ShadowCheck(NonShadow(copy) == preserved && !style.ApplyTo(copy), "Restoration altered artwork or was not idempotent.", report);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(copy); }
                }
                report.defaultAppearancePreserved = true;
                style.firstShadeBrightness = float.NaN; style.secondShadeBrightness = float.PositiveInfinity;
                style.firstShadeThreshold = -.5f; style.secondShadeThreshold = 4; style.firstShadeFeather = 0; style.secondShadeFeather = 5; style.Normalize();
                ShadowCheck(style.firstShadeBrightness == 1 && style.secondShadeBrightness == 1 && style.firstShadeThreshold == 0 && style.secondShadeThreshold == 0 &&
                    style.firstShadeFeather == .0001f && style.secondShadeFeather == 1, "Bounds/non-finite/threshold ordering failed.", report);
                var factory = ToonMaterials.Create(new Color(.23f, .41f, .67f, .8f), style: legacy);
                try
                {
                    factory.SetOverrideTag(ToonStyle.ShadeBaselineTag, "inherited-template-state");
                    ToonMaterials.Configure(factory, new Color(.4f, .6f, .8f, .7f), Texture2D.whiteTexture, new Vector2(2, 3), new Vector2(.1f, .2f), style: legacy);
                    ShadowCheck(factory.GetTag(ToonStyle.ShadeBaselineTag, false, "") == "" && factory.GetColor("_1st_ShadeColor") == new Color(.4f * .72f, .6f * .72f, .8f * .72f, .7f),
                        "Factory inherited another material's shade baseline.", report);
                }
                finally { UnityEngine.Object.DestroyImmediate(factory); }
                style.overrideToonShadows = true; style.firstShadeBrightness = .5f; style.secondShadeBrightness = .75f;
                var styledFactory = ToonMaterials.Create(new Color(.4f, .6f, .8f, .7f), style: style);
                try
                {
                    ShadowCheck(styledFactory.GetColor("_1st_ShadeColor") == new Color(.4f * .72f * .5f, .6f * .72f * .5f, .8f * .72f * .5f, .7f), "New factory material is not profile-driven.", report);
                    legacy.ApplyTo(styledFactory);
                    ShadowCheck(styledFactory.GetColor("_1st_ShadeColor") == new Color(.4f * .72f, .6f * .72f, .8f * .72f, .7f), "Cached material copy did not restore authored shades.", report);
                    style.ApplyTo(styledFactory); ToonStyle.ApplyCurrent(styledFactory);
                    ShadowCheck(styledFactory.GetTag(ToonStyle.ShadeBaselineTag, false, "") == "" || ToonStyle.Current.overrideToonShadows,
                        "Current profile refresh did not update cached material copy.", report);
                }
                finally { UnityEngine.Object.DestroyImmediate(styledFactory); }
                foreach (var path in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat")))
                {
                    var source = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!source || !source.shader || source.shader.name != "Wildbound/Pokemon Gallery") continue;
                    var copy = new Material(source); string before = EditorJsonUtility.ToJson(copy);
                    try { ShadowCheck(!style.ApplyTo(copy) && EditorJsonUtility.ToJson(copy) == before, "Special gallery material changed.", report); report.specialMaterials++; }
                    finally { UnityEngine.Object.DestroyImmediate(copy); }
                }
                var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Editor/ToonIntegration/ToonStyleWindow.uxml").CloneTree();
                foreach (string name in new[] { "firstShadeBrightness", "secondShadeBrightness", "firstShadeThreshold", "secondShadeThreshold", "firstShadeFeather", "secondShadeFeather" })
                    ShadowCheck(layout.Q<Slider>(name) != null && layout.Q<Slider>(name).bindingPath == name, "Shadow panel field/binding missing: " + name, report);
                ShadowCheck(layout.Q<Toggle>("overrideToonShadows") != null && layout.Q<Button>("apply") != null, "Shadow panel enable/apply missing.", report);
                report.uiBindings = true;
                string json = JsonUtility.ToJson(report, true); Directory.CreateDirectory("Library/ToonShadowStyle"); File.WriteAllText("Library/ToonShadowStyle/validation-report.json", json); return json;
            }
            finally { UnityEngine.Object.DestroyImmediate(style); UnityEngine.Object.DestroyImmediate(legacy); }
        }

        static ToonStyleWindow shadowWindow;
        [Serializable] sealed class ShadowPanelReport { public bool success, sharedProfileUntouched, materialsUntouched, applyAndRestore; public string status; }
        public static string BeginShadowPanelCheck()
        {
            ToonStyleAuthoring.RequireEditMode();
            if (shadowWindow) throw new InvalidOperationException("A shadow panel check is already pending.");
            string sharedBefore = EditorJsonUtility.ToJson(ToonStyle.Current);
            var sources = ToonStyleAuthoring.Materials(); var assetBefore = sources.Select(EditorJsonUtility.ToJson).ToArray();
            var transient = ScriptableObject.CreateInstance<ToonStyle>();
            EditorJsonUtility.FromJsonOverwrite(sharedBefore, transient); transient.overrideToonShadows = false;
            var copy = new Material(sources[0]); transient.ApplyTo(copy);
            var originalFirst = copy.GetColor("_1st_ShadeColor"); var originalSecond = copy.GetColor("_2nd_ShadeColor");
            var originalBands = ShadowFloats.Select(copy.GetFloat).ToArray();
            shadowWindow = ScriptableObject.CreateInstance<ToonStyleWindow>(); shadowWindow.ShowUtility();
            shadowWindow.CreateGUIForProfile(transient, () => new ToonStyleAuthoring.Report { profile = "Transient verification profile", materials = 1, changed = transient.ApplyTo(copy) ? 1 : 0, outline = transient.outline });
            void Finish(bool success, string status)
            {
                bool unchanged = sharedBefore == EditorJsonUtility.ToJson(ToonStyle.Current) && sources.Select(EditorJsonUtility.ToJson).SequenceEqual(assetBefore);
                var result = new ShadowPanelReport { success = success && unchanged, sharedProfileUntouched = sharedBefore == EditorJsonUtility.ToJson(ToonStyle.Current),
                    materialsUntouched = sources.Select(EditorJsonUtility.ToJson).SequenceEqual(assetBefore), applyAndRestore = success, status = status };
                Directory.CreateDirectory("Library/ToonShadowStyle"); File.WriteAllText("Library/ToonShadowStyle/panel-report.json", JsonUtility.ToJson(result, true));
                UnityEngine.Object.DestroyImmediate(shadowWindow); shadowWindow = null; UnityEngine.Object.DestroyImmediate(copy); UnityEngine.Object.DestroyImmediate(transient);
            }
            EditorApplication.delayCall += () => {
                try {
                    var root = shadowWindow.rootVisualElement;
                    root.Q<Toggle>("overrideToonShadows").value = true;
                    root.Q<Slider>("firstShadeBrightness").value = .4f; root.Q<Slider>("secondShadeBrightness").value = .7f;
                    root.Q<Slider>("firstShadeThreshold").value = .6f; root.Q<Slider>("secondShadeThreshold").value = .2f;
                    root.Q<Slider>("firstShadeFeather").value = .1f; root.Q<Slider>("secondShadeFeather").value = .2f;
                    EditorApplication.delayCall += () => {
                        try {
                            var button = root.Q<Button>("apply"); button.Focus(); using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
                            EditorApplication.delayCall += () => {
                                try {
                                    bool applied = transient.overrideToonShadows && transient.firstShadeBrightness == .4f && transient.secondShadeBrightness == .7f &&
                                        copy.GetFloat("_BaseColor_Step") == .6f && copy.GetFloat("_ShadeColor_Step") == .2f && copy.GetFloat("_BaseShade_Feather") == .1f &&
                                        copy.GetColor("_1st_ShadeColor") == new Color(originalFirst.r * .4f, originalFirst.g * .4f, originalFirst.b * .4f, originalFirst.a);
                                    if (!applied) { Finish(false, "Binding or navigation Apply did not apply the requested shadow fields."); return; }
                                    root.Q<Toggle>("overrideToonShadows").value = false;
                                    EditorApplication.delayCall += () => {
                                        try {
                                            button.Focus(); using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
                                            EditorApplication.delayCall += () => {
                                                bool restored = copy.GetColor("_1st_ShadeColor") == originalFirst && copy.GetColor("_2nd_ShadeColor") == originalSecond && originalBands.SequenceEqual(ShadowFloats.Select(copy.GetFloat));
                                                Finish(restored, root.Q<Label>("status").text);
                                            };
                                        } catch (Exception exception) { Finish(false, exception.Message); }
                                    };
                                } catch (Exception exception) { Finish(false, exception.Message); }
                            };
                        } catch (Exception exception) { Finish(false, exception.Message); }
                    };
                } catch (Exception exception) { Finish(false, exception.Message); }
            };
            return "Shadow panel binding/navigation check scheduled with transient profile/material only.";
        }

        static ToonStyleWindow interactionWindow;
        public static string BeginPanelInteractionCheck()
        {
            ToonStyleAuthoring.RequireEditMode();
            if (interactionWindow) throw new InvalidOperationException("A Toon panel interaction check is already pending.");
            interactionWindow = ScriptableObject.CreateInstance<ToonStyleWindow>();
            interactionWindow.ShowUtility(); interactionWindow.CreateGUI();
            EditorApplication.delayCall += () =>
            {
                var button = interactionWindow.rootVisualElement.Q<Button>("apply"); button.Focus();
                using (var submit = NavigationSubmitEvent.GetPooled()) button.SendEvent(submit);
                EditorApplication.delayCall += () =>
                {
                    try
                    {
                        string text = interactionWindow.rootVisualElement.Q<Label>("status").text;
                        bool success = text.StartsWith("Applied to ", StringComparison.Ordinal);
                        File.WriteAllText("Library/ToonStyle/panel-report.json", "{\"success\":" + (success ? "true" : "false") +
                            ",\"status\":" + JsonUtility.ToJson(new PanelStatus { text = text }) + "}");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(interactionWindow); interactionWindow = null; }
                };
            };
            return "Panel navigation interaction check scheduled across Editor frames.";
        }
        [Serializable] sealed class PanelStatus { public string text; }

        public static string Run()
        {
            ToonStyleAuthoring.RequireEditMode();
            var report = new Report { graphicsDevice = SystemInfo.graphicsDeviceType.ToString() };
            var profile = ToonStyleAuthoring.LoadOrCreate();
            Check(!profile.outline, "Expected the user-approved outline-disabled profile.", report);
            var materials = ToonStyleAuthoring.Materials();
            report.materials = materials.Length;
            var before = materials.Select(NonStyle).ToArray();
            report.firstChanged = JsonUtility.FromJson<ToonStyleAuthoring.Report>(ToonStyleAuthoring.Apply()).changed;
            for (int i = 0; i < materials.Length; i++)
            {
                Check(materials[i].IsKeywordEnabled(ToonStyle.DisableOutlineKeyword), "Outline remains enabled: " + materials[i].name, report);
                Check(NonStyle(materials[i]) == before[i], "A non-outline material setting changed: " + materials[i].name, report);
            }
            report.outlinesDisabled = true; report.nonStylePreserved = true;
            var paths = materials.Select(AssetDatabase.GetAssetPath).ToArray();
            var bytes = paths.Select(File.ReadAllBytes).ToArray();
            report.repeatChanged = JsonUtility.FromJson<ToonStyleAuthoring.Report>(ToonStyleAuthoring.Apply()).changed;
            Check(report.repeatChanged == 0, "Style application was not idempotent.", report);
            for (int i = 0; i < paths.Length; i++)
                Check(bytes[i].SequenceEqual(File.ReadAllBytes(paths[i])), "Repeated apply rewrote an unchanged material.", report);
            var probe = ToonMaterials.Create(new Color(.23f, .47f, .81f, 1), Texture2D.whiteTexture, true);
            var temporaryStyle = ScriptableObject.CreateInstance<ToonStyle>();
            try
            {
                Check(probe.IsKeywordEnabled(ToonStyle.DisableOutlineKeyword) && probe.GetFloat("_Outline_Width") == profile.outlineWidth &&
                    probe.GetColor("_Outline_Color") == profile.outlineColor, "New material does not use the shared profile.", report);
                var preserved = NonStyle(probe);
                temporaryStyle.outline = true; temporaryStyle.outlineWidth = 2.25f; temporaryStyle.outlineColor = Color.cyan;
                temporaryStyle.ApplyTo(probe);
                Check(!probe.IsKeywordEnabled(ToonStyle.DisableOutlineKeyword) && probe.GetFloat("_Outline_Width") == 2.25f &&
                    probe.GetColor("_Outline_Color") == Color.cyan, "Outline enable/profile values failed.", report);
                temporaryStyle.outline = false; temporaryStyle.ApplyTo(probe);
                Check(probe.IsKeywordEnabled(ToonStyle.DisableOutlineKeyword) && probe.GetFloat("_Outline_Width") == 2.25f &&
                    probe.GetColor("_Outline_Color") == Color.cyan, "Disable discarded width/color values.", report);
                temporaryStyle.outline = true; temporaryStyle.ApplyTo(probe);
                Check(!probe.IsKeywordEnabled(ToonStyle.DisableOutlineKeyword) && NonStyle(probe) == preserved,
                    "Toggle restoration changed non-outline settings.", report);
                Check(!temporaryStyle.ApplyTo(probe), "Style-only material application is not idempotent.", report);
                temporaryStyle.outlineWidth = float.NaN; temporaryStyle.ApplyTo(probe);
                Check(probe.GetFloat("_Outline_Width") == .5f, "Invalid width was not normalized safely.", report);
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); UnityEngine.Object.DestroyImmediate(temporaryStyle); }
            var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            Material cached = null;
            try
            {
                var root = new GameObject("Toon style cache verification");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                var color = new Color(.137f, .419f, .731f, 1);
                var first = World.Part("First", PrimitiveType.Sphere, Vector3.zero, Vector3.one, color, root.transform);
                cached = first.GetComponent<Renderer>().sharedMaterial;
                cached.DisableKeyword(ToonStyle.DisableOutlineKeyword);
                var second = World.Part("Second", PrimitiveType.Sphere, Vector3.right, Vector3.one, color, root.transform);
                Check(second.GetComponent<Renderer>().sharedMaterial == cached && cached.IsKeywordEnabled(ToonStyle.DisableOutlineKeyword),
                    "Procedural material cache retained an outdated style.", report);
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
                if (cached) UnityEngine.Object.DestroyImmediate(cached);
            }
            var window = ScriptableObject.CreateInstance<ToonStyleWindow>();
            try
            {
                window.ShowUtility();
                window.CreateGUI();
                var root = window.rootVisualElement;
                Check(root.Q<Toggle>("outline") != null && root.Q<FloatField>("outlineWidth") != null &&
                    root.Q<UnityEditor.UIElements.ColorField>("outlineColor") != null, "Toon panel fields failed import/build.", report);
                var button = root.Q<Button>("apply");
                Check(button != null && button.enabledSelf, "Toon panel Apply action is unavailable.", report);
                Check(root.panel != null, "Panel is not attached to the Editor UI.", report);
            }
            finally { UnityEngine.Object.DestroyImmediate(window); }
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory("Library/ToonStyle"); File.WriteAllText("Library/ToonStyle/validation-report.json", json);
            return json;
        }
    }
}
