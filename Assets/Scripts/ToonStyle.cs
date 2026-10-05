using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pokemon3D
{
    public sealed class ToonStyle : ScriptableObject
    {
        public bool outline;
        [Range(0, 10)] public float outlineWidth = .5f;
        public Color outlineColor = new Color(.12f, .15f, .17f, 1);
        public bool overrideToonShadows;
        [Range(0, 2)] public float firstShadeBrightness = 1;
        [Range(0, 2)] public float secondShadeBrightness = 1;
        [Range(0, 1)] public float firstShadeThreshold = .55f;
        [Range(0, 1)] public float secondShadeThreshold = .05f;
        [Range(.0001f, 1)] public float firstShadeFeather = .015f;
        [Range(.0001f, 1)] public float secondShadeFeather = .015f;
        public bool overrideShadowReception;
        public bool receiveProjectedShadows = true;
        public const string ShadeBaselineTag = "WildboundToonShadeBaseline";
        static readonly string[] BandProperties = {
            "_BaseColor_Step", "_ShadeColor_Step", "_BaseShade_Feather", "_1st2nd_Shades_Feather",
            "_1st_ShadeColor_Step", "_2nd_ShadeColor_Step", "_1st_ShadeColor_Feather", "_2nd_ShadeColor_Feather"
        };
        [Serializable] sealed class ShadeBaseline
        {
            public Color first, second;
            public float[] bands;
            public bool hasShadowReception;
            public float shadowReception;
        }
        public const string ResourcePath = "ToonStyle";
        public const string DisableOutlineKeyword = "_DISABLE_OUTLINE";
        const string OutlinePass = "SRPDefaultUnlit";

        public static ToonStyle Current => Resources.Load<ToonStyle>(ResourcePath);

        public static bool ApplyCurrent(Material material)
        {
            var profile = Current;
            if (profile) return profile.ApplyTo(material);
            return Apply(material, false, .5f, new Color(.12f, .15f, .17f, 1));
        }

        public bool ApplyTo(Material material)
        {
            if (!IsToon(material)) return false;
            bool outlineChanged = Apply(material, outline, outlineWidth, outlineColor);
            return ApplyShadows(material) || outlineChanged;
        }

        public bool ApplyShadowSettings(Material material) => IsToon(material) && ApplyShadows(material);

        static bool IsToon(Material material) => material && material.shader && material.shader.name == ToonMaterials.ShaderName;
        static float Safe(float value, float fallback, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);
        public void Normalize()
        {
            outlineWidth = Safe(outlineWidth, .5f, 0, 10);
            firstShadeBrightness = Safe(firstShadeBrightness, 1, 0, 2);
            secondShadeBrightness = Safe(secondShadeBrightness, 1, 0, 2);
            firstShadeThreshold = Safe(firstShadeThreshold, .55f, 0, 1);
            secondShadeThreshold = Mathf.Min(firstShadeThreshold, Safe(secondShadeThreshold, .05f, 0, 1));
            firstShadeFeather = Safe(firstShadeFeather, .015f, .0001f, 1);
            secondShadeFeather = Safe(secondShadeFeather, .015f, .0001f, 1);
        }
        void OnValidate() => Normalize();

        public static void ClearShadeBaseline(Material material)
        {
            if (material.GetTag(ShadeBaselineTag, false, "") != "") material.SetOverrideTag(ShadeBaselineTag, "");
        }
        static Color Brightness(Color color, float multiplier) => new Color(color.r * multiplier, color.g * multiplier, color.b * multiplier, color.a);
        static bool SetColor(Material material, string property, Color value)
        {
            if (material.GetColor(property) == value) return false;
            material.SetColor(property, value); return true;
        }
        static bool SetFloat(Material material, string property, float value)
        {
            if (material.GetFloat(property) == value) return false;
            material.SetFloat(property, value); return true;
        }
        bool ApplyShadows(Material material)
        {
            if (!material.HasProperty("_1st_ShadeColor") || !material.HasProperty("_2nd_ShadeColor")) return false;
            foreach (string property in BandProperties) if (!material.HasProperty(property)) return false;
            string saved = material.GetTag(ShadeBaselineTag, false, "");
            if (!overrideToonShadows && saved == "") return false;
            ShadeBaseline baseline = null;
            if (saved != "")
            {
                try { baseline = JsonUtility.FromJson<ShadeBaseline>(saved); }
                catch (ArgumentException) { return false; }
                if (baseline == null || baseline.bands == null || baseline.bands.Length != BandProperties.Length) return false;
            }
            bool changed = false;
            if (baseline == null)
            {
                baseline = new ShadeBaseline { first = material.GetColor("_1st_ShadeColor"), second = material.GetColor("_2nd_ShadeColor"), bands = new float[BandProperties.Length] };
                for (int i = 0; i < BandProperties.Length; i++) baseline.bands[i] = material.GetFloat(BandProperties[i]);
                material.SetOverrideTag(ShadeBaselineTag, JsonUtility.ToJson(baseline)); changed = true;
            }
            if (!overrideToonShadows)
            {
                changed |= SetColor(material, "_1st_ShadeColor", baseline.first);
                changed |= SetColor(material, "_2nd_ShadeColor", baseline.second);
                for (int i = 0; i < BandProperties.Length; i++) changed |= SetFloat(material, BandProperties[i], baseline.bands[i]);
                if (baseline.hasShadowReception && material.HasProperty("_Set_SystemShadowsToBase"))
                    changed |= SetFloat(material, "_Set_SystemShadowsToBase", baseline.shadowReception);
                ClearShadeBaseline(material); return true;
            }
            if (material.HasProperty("_Set_SystemShadowsToBase"))
            {
                if (overrideShadowReception)
                {
                    if (!baseline.hasShadowReception)
                    {
                        baseline.shadowReception = material.GetFloat("_Set_SystemShadowsToBase");
                        baseline.hasShadowReception = true;
                        material.SetOverrideTag(ShadeBaselineTag, JsonUtility.ToJson(baseline)); changed = true;
                    }
                    changed |= SetFloat(material, "_Set_SystemShadowsToBase", receiveProjectedShadows ? 1 : 0);
                }
                else if (baseline.hasShadowReception)
                {
                    changed |= SetFloat(material, "_Set_SystemShadowsToBase", baseline.shadowReception);
                    baseline.hasShadowReception = false;
                    material.SetOverrideTag(ShadeBaselineTag, JsonUtility.ToJson(baseline)); changed = true;
                }
            }
            changed |= SetColor(material, "_1st_ShadeColor", Brightness(baseline.first, Safe(firstShadeBrightness, 1, 0, 2)));
            changed |= SetColor(material, "_2nd_ShadeColor", Brightness(baseline.second, Safe(secondShadeBrightness, 1, 0, 2)));
            float first = Safe(firstShadeThreshold, .55f, 0, 1), second = Mathf.Min(first, Safe(secondShadeThreshold, .05f, 0, 1));
            float feather1 = Safe(firstShadeFeather, .015f, .0001f, 1), feather2 = Safe(secondShadeFeather, .015f, .0001f, 1);
            var bands = new[] { first, second, feather1, feather2, first, second, feather1, feather2 };
            for (int i = 0; i < BandProperties.Length; i++) changed |= SetFloat(material, BandProperties[i], bands[i]);
            return changed;
        }

        static bool Apply(Material material, bool enabled, float width, Color color)
        {
            if (!material || !material.shader || material.shader.name != ToonMaterials.ShaderName) return false;
            width = float.IsNaN(width) || float.IsInfinity(width) ? .5f : Mathf.Clamp(width, 0, 10);
            bool builtIn = GraphicsSettings.currentRenderPipeline == null;
            bool changed = material.GetFloat("_Outline_Width") != width || material.GetColor("_Outline_Color") != color ||
                (builtIn ? material.IsKeywordEnabled(DisableOutlineKeyword) == enabled : material.GetShaderPassEnabled(OutlinePass) != enabled);
            if (!changed) return false;
            material.SetFloat("_Outline_Width", width);
            material.SetColor("_Outline_Color", color);
            if (builtIn)
            {
                if (enabled) material.DisableKeyword(DisableOutlineKeyword);
                else material.EnableKeyword(DisableOutlineKeyword);
            }
            else material.SetShaderPassEnabled(OutlinePass, enabled);
            return true;
        }
    }
}
