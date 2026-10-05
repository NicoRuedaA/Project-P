using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pokemon3D.Editor
{
    // Explicit one-time folder consolidation; no scene or asset regeneration.
    public static class AssetFolderOrganization
    {
        const string Evidence = "Library/AssetOrganization";
        static readonly string[][] Moves = {
            new[] { "Assets/Prefab", "Assets/Prefabs" },
            new[] { "Assets/Pokemon/Prefabs", "Assets/Prefabs/Pokemon" },
            new[] { "Assets/Pokemon/Trainer/PokemonTrainer.prefab", "Assets/Prefabs/Trainer/PokemonTrainer.prefab" },
            new[] { "Assets/Pokemon/Materials", "Assets/Materials/Pokemon" },
            new[] { "Assets/Pokemon/Trainer/Materials", "Assets/Materials/Trainer" },
            new[] { "Assets/Pokemon/Models", "Assets/Models/Pokemon" },
            new[] { "Assets/Pokemon/Trainer", "Assets/Models/Trainer" },
            new[] { "Assets/Pokemon", "Assets/Data/Pokemon" }
        };
        [Serializable] sealed class Entry { public string path, guid, identities, dependencies, sourceMaterials, remaps, materialState; }
        [Serializable] sealed class Snapshot { public Entry[] entries; public string sceneState; public int missingReferences; }
        [Serializable] sealed class Report { public bool success; public int operations, assets, models, materials, prefabs, textures, missingReferences; public string graphicsDevice, sceneState; }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static string Identity(UnityEngine.Object obj)
        {
            if (!obj) return "null";
            return AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long id) ? guid + ":" + id : obj.GetType().Name + ":" + obj.name;
        }
        static string SceneState() => string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(i => {
            var s = SceneManager.GetSceneAt(i);
            return s.path + ":" + s.handle + ":" + s.isDirty + ":" + s.isLoaded + ":" + s.rootCount;
        })) + "\nactive:" + SceneManager.GetActiveScene().handle + ";playing:" + EditorApplication.isPlaying;
        static int Missing(GameObject root)
        {
            int count = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                foreach (var component in t.GetComponents<Component>().Where(c => c))
                {
                    using (var serialized = new SerializedObject(component))
                    {
                        var property = serialized.GetIterator();
                        while (property.Next(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference && !property.objectReferenceValue && property.objectReferenceEntityIdValue.IsValid()) count++;
                    }
                }
            }
            return count;
        }
        static Snapshot Capture()
        {
            var entries = new List<Entry>(); int missing = 0;
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal))
            {
                var objects = AssetDatabase.LoadAllAssetsAtPath(path);
                var importer = AssetImporter.GetAtPath(path);
                string source = "";
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    source = string.Join("\n", model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Select(m =>
                        m ? m.name + ":" + string.Join(",", m.GetTexturePropertyNames().OrderBy(n => n).Select(n => n + "=" + Identity(m.GetTexture(n)))) : "null").OrderBy(v => v));
                    missing += Missing(model);
                }
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) missing += Missing(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    var preview = EditorSceneManager.OpenPreviewScene(path);
                    try { foreach (var root in preview.GetRootGameObjects()) missing += Missing(root); }
                    finally { EditorSceneManager.ClosePreviewScene(preview); }
                }
                entries.Add(new Entry {
                    path = path, guid = AssetDatabase.AssetPathToGUID(path),
                    identities = string.Join("\n", objects.Select(o => o.GetType().FullName + ":" + o.name + ":" + Identity(o)).OrderBy(v => v)),
                    dependencies = string.Join("\n", AssetDatabase.GetDependencies(path, true).Select(AssetDatabase.AssetPathToGUID).OrderBy(v => v)),
                    sourceMaterials = source,
                    materialState = path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ? EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<Material>(path)) : "",
                    remaps = importer ? string.Join("\n", importer.GetExternalObjectMap().Select(kv => kv.Key.type.FullName + ":" + kv.Key.name + "=" + Identity(kv.Value)).OrderBy(v => v)) : ""
                });
            }
            return new Snapshot { entries = entries.ToArray(), sceneState = SceneState(), missingReferences = missing };
        }
        static string Destination(string path)
        {
            foreach (var move in Moves)
                if (path == move[0] || path.StartsWith(move[0] + "/", StringComparison.Ordinal)) return move[1] + path.Substring(move[0].Length);
            return path;
        }
        static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(path)) return;
            Require(!File.Exists(path), "Folder collision: " + path);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Cannot create folder: " + path);
        }
        [MenuItem("Wildbound/Assets/Consolidate Asset Folders")]
        public static void Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "Organization requires the idle editor in Edit mode.");
            Directory.CreateDirectory(Evidence);
            var before = Capture();
            File.WriteAllText(Evidence + "/unity-before.json", JsonUtility.ToJson(before, true));
            var planned = before.entries.Select(e => Destination(e.path)).ToArray();
            Require(planned.Distinct(StringComparer.OrdinalIgnoreCase).Count() == planned.Length, "The full move plan has a filename collision; no folders were moved.");
            var pending = Moves.Where(m => AssetDatabase.AssetPathToGUID(m[0]) != "").ToArray();
            foreach (var move in pending)
            {
                Require(AssetDatabase.AssetPathToGUID(move[1]) == "" && !File.Exists(move[1]) && !Directory.Exists(move[1]), "Destination already exists: " + move[1]);
                Require(AssetDatabase.ValidateMoveAsset(move[0], move[1]) == "" || !Directory.Exists(Path.GetDirectoryName(move[1])), "Invalid move: " + move[0]);
            }
            File.WriteAllText(Evidence + "/move-plan.txt", string.Join("\n", pending.Select(m => m[0] + " -> " + m[1])));
            var completed = new List<string[]>();
            try
            {
                foreach (var move in pending)
                {
                    EnsureFolder(Path.GetDirectoryName(move[1]).Replace('\\', '/'));
                    string error = AssetDatabase.MoveAsset(move[0], move[1]);
                    Require(string.IsNullOrEmpty(error), "Move failed: " + error);
                    completed.Add(move);
                }
            }
            catch
            {
                foreach (var move in completed.AsEnumerable().Reverse())
                {
                    string error = AssetDatabase.MoveAsset(move[1], move[0]);
                    if (!string.IsNullOrEmpty(error)) Debug.LogError("Rollback move failed: " + error);
                }
                throw;
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var after = Capture(); var byPath = after.entries.ToDictionary(e => e.path);
            foreach (var entry in before.entries)
            {
                string destination = Destination(entry.path);
                Require(byPath.TryGetValue(destination, out var current), "Missing asset: " + destination);
                Require(entry.guid == current.guid && entry.identities == current.identities, "GUID/local identity changed: " + destination);
                Require(entry.dependencies == current.dependencies, "Dependencies changed: " + destination);
                Require(entry.materialState == current.materialState, "Material settings changed: " + destination);
                Require(entry.sourceMaterials == current.sourceMaterials && entry.remaps == current.remaps, "Source texture discovery or material remaps changed: " + destination);
            }
            Require(before.sceneState == after.sceneState, "Open scene or Play state changed.");
            Require(after.missingReferences == before.missingReferences && after.missingReferences == 0, "Missing script/object references detected.");
            Require(Resources.Load<ToonStyle>("ToonStyle") && Resources.Load<Material>("UnityToonDefault"), "Toon Resources are missing.");
            var mats = after.entries.Where(e => e.path.EndsWith(".mat")).Select(e => AssetDatabase.LoadAssetAtPath<Material>(e.path)).ToArray();
            var probe = ToonMaterials.Create(Color.white);
            try { Require(probe.IsKeywordEnabled(ToonStyle.DisableOutlineKeyword) == !Resources.Load<ToonStyle>("ToonStyle").outline, "Factory no longer uses the shared style."); }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            Require(AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>("Assets/Editor/ToonIntegration/ToonStyleWindow.uxml"), "Toon panel UXML is missing.");
            File.WriteAllText(Evidence + "/unity-after.json", JsonUtility.ToJson(after, true));
            var report = new Report { success = true, operations = completed.Count, assets = before.entries.Length, models = after.entries.Count(e => e.path.EndsWith(".fbx")), materials = mats.Length,
                prefabs = after.entries.Count(e => e.path.EndsWith(".prefab")), textures = after.entries.Count(e => e.path.EndsWith(".png")), missingReferences = after.missingReferences,
                graphicsDevice = SystemInfo.graphicsDeviceType.ToString(), sceneState = after.sceneState };
            string json = JsonUtility.ToJson(report, true); File.WriteAllText(Evidence + "/validation-report.json", json); Debug.Log(json);
        }
    }
}
