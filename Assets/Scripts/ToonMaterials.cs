using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wildbound
{
    /// <summary>Shared Unity Toon Shader settings for authored and procedural visuals.</summary>
    public static class ToonMaterials
    {
        public const string ShaderName = "Toon/Toon";
        const string TemplatePath = "UnityToonDefault";

        public static Material Create(Color color, Texture texture = null, bool doubleSided = false, ToonStyle style = null)
        {
            // A Resources material retains the shader and its variants in bootstrap-only builds.
            var template = Resources.Load<Material>(TemplatePath);
            var shader = template ? template.shader : Shader.Find(ShaderName);
            if (!shader || !shader.isSupported) throw new InvalidOperationException("Unity Toon Shader is unavailable. Install the project packages before creating visuals.");
            var material = template ? new Material(template) : new Material(shader);
            Configure(material, color, texture, Vector2.one, Vector2.zero, doubleSided, style);
            return material;
        }

        public static void Configure(Material material, Color color, Texture texture,
            Vector2 scale, Vector2 offset, bool doubleSided = false, ToonStyle style = null)
        {
            ToonStyle.ClearShadeBaseline(material);
            material.SetColor("_Color", color);
            material.SetColor("_BaseColor", color);
            material.SetColor("_1st_ShadeColor", Shade(color, .72f));
            material.SetColor("_2nd_ShadeColor", Shade(color, .42f));
            foreach (var property in new[] { "_MainTex", "_BaseMap", "_1st_ShadeMap", "_2nd_ShadeMap" })
            {
                material.SetTexture(property, texture);
                material.SetTextureScale(property, scale);
                material.SetTextureOffset(property, offset);
            }
            material.SetFloat("_Use_BaseAs1st", 1);
            material.SetFloat("_Use_1stAs2nd", 1);
            material.SetFloat("_BaseColor_Step", .55f);
            material.SetFloat("_ShadeColor_Step", .05f);
            material.SetFloat("_BaseShade_Feather", .015f);
            material.SetFloat("_1st2nd_Shades_Feather", .015f);
            material.SetFloat("_Set_SystemShadowsToBase", 1);
            material.SetFloat("_GI_Intensity", .25f);
            material.SetFloat("_Unlit_Intensity", .25f);
            material.SetFloat("_CullMode", doubleSided ? 0 : 2);
            material.SetFloat("_TransparentEnabled", 0);
            material.SetFloat("_ClippingMode", 0);
            material.SetFloat("_IsBaseMapAlphaAsClippingMask", 0);
            material.SetFloat("_ZWriteMode", 1);
            material.SetFloat("_ZOverDrawMode", 0);
            // The package consumes this variant only from its Built-in subshader; its URP pass has no such keyword.
            if (GraphicsSettings.currentRenderPipeline == null) material.EnableKeyword("UTS_RP_BUILTIN");
            else material.DisableKeyword("UTS_RP_BUILTIN");
            material.EnableKeyword("_IS_CLIPPING_OFF");
            material.EnableKeyword("_IS_OUTLINE_CLIPPING_NO");
            material.EnableKeyword("_OUTLINE_NML");
            material.EnableKeyword("_EMISSIVE_SIMPLE");
            if (style) style.ApplyTo(material);
            else ToonStyle.ApplyCurrent(material);
        }

        static Color Shade(Color color, float factor) => new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
    }
}
