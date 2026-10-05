using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Wildbound.Editor
{
    public static class ToonStyleAuthoring
    {
        public const string ProfilePath = "Assets/Resources/ToonStyle.asset";
        [Serializable] public sealed class Report { public string profile; public int materials, changed; public bool outline; }
        public static bool CanEdit => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        public static ToonStyle LoadOrCreate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ToonStyle>(ProfilePath);
            if (profile) return profile;
            RequireEditMode();
            if (File.Exists(ProfilePath)) throw new InvalidOperationException("An incompatible asset already occupies " + ProfilePath);
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            profile = ScriptableObject.CreateInstance<ToonStyle>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
            return profile;
        }

        public static Material[] Materials() => AssetDatabase.FindAssets("t:Material", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat", StringComparison.Ordinal))
            .Select(AssetDatabase.LoadAssetAtPath<Material>)
            .Where(m => m && m.shader && m.shader.name == ToonMaterials.ShaderName).ToArray();

        public static string Apply()
        {
            RequireEditMode();
            var profile = LoadOrCreate();
            var materials = Materials();
            var report = new Report { profile = ProfilePath, materials = materials.Length, outline = profile.outline };
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Apply global Toon style");
            Undo.RecordObjects(materials, "Apply global Toon style");
            foreach (var material in materials)
            {
                if (!profile.ApplyTo(material)) continue;
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                report.changed++;
            }
            AssetDatabase.SaveAssetIfDirty(profile);
            Undo.CollapseUndoOperations(group);
            string json = JsonUtility.ToJson(report, true);
            Directory.CreateDirectory("Library/ToonStyle"); File.WriteAllText("Library/ToonStyle/apply-report.json", json);
            return json;
        }

        public static void RequireEditMode()
        {
            if (!CanEdit) throw new InvalidOperationException("Toon style changes require an idle Editor in Edit mode. No play state or scenes were changed.");
        }
    }
}
