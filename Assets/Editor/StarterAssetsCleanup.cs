using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Pokemon3D.Editor
{
    // Temporary fixed-scope cleanup harness. Reports and byte backups are outside Assets.
    public static class StarterAssetsCleanup
    {
        const string Root = "Assets/Starter Assets";
        const string Player = "Assets/PlayerArmature.prefab";
        const string Evidence = "Library/StarterAssetsCleanup";
        [Serializable] public sealed class Asset { public string path, guid, ids, deps, importer; public string[] reasons; }
        [Serializable] public sealed class Prefab { public string structure, refs, controller, avatar, clips, actions; public int missing, renderers, unsupportedMaterials; public bool avatarValid, characterController, thirdPersonController, inputs, playerInput, cameraTarget; }
        [Serializable] public sealed class State { public string scenes, activeScene, pipeline; public bool playing; }
        [Serializable] public sealed class Plan { public bool success; public string[] delete, keep; public Asset[] assets; public Prefab prefab; public State state; public string[] setupNotes; public int beforeCount; }
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static bool Starter(string path) => path.StartsWith(Root + "/", StringComparison.Ordinal);
        static string ID(UnityEngine.Object obj) => obj && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long local) ? guid + ":" + local : "";
        static string Hierarchy(Transform t, Transform root) => t == root ? "." : Hierarchy(t.parent, root) + "/" + t.name;
        static State CurrentState() => new State {
            scenes = string.Join("\n", Enumerable.Range(0, SceneManager.sceneCount).Select(i => { var s = SceneManager.GetSceneAt(i); return s.path + ":" + s.handle + ":" + s.isDirty + ":" + s.rootCount; })),
            activeScene = SceneManager.GetActiveScene().handle.ToString(), playing = EditorApplication.isPlaying,
            pipeline = ID(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline) + ";current=" + ID(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline) + ";quality=" + ID(QualitySettings.renderPipeline)
        };
        static Prefab PlayerState()
        {
            var obj = PrefabUtility.LoadPrefabContents(Player);
            try {
                var transforms = obj.GetComponentsInChildren<Transform>(true);
                var components = transforms.SelectMany(t => t.GetComponents<Component>()).ToArray();
                var refs = new List<string>(); int missing = 0;
                foreach (var t in transforms) missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                foreach (var component in components.Where(c => c)) {
                    using (var so = new SerializedObject(component)) {
                        var property = so.GetIterator();
                        while (property.Next(true)) if (property.propertyType == SerializedPropertyType.ObjectReference) {
                            var value = property.objectReferenceValue;
                            if (!value && property.objectReferenceEntityIdValue.IsValid()) missing++;
                            string identity = ID(value);
                            if (identity == "" && value is GameObject go) identity = Hierarchy(go.transform, obj.transform);
                            if (identity == "" && value is Component c) identity = Hierarchy(c.transform, obj.transform) + ":" + c.GetType().FullName;
                            if (value) refs.Add(Hierarchy(component.transform, obj.transform) + ":" + component.GetType().FullName + ":" + property.propertyPath + "=" + identity);
                        }
                    }
                }
                var animator = obj.GetComponent<Animator>();
                var controller = components.FirstOrDefault(c => c && c.GetType().FullName == "StarterAssets.ThirdPersonController");
                var input = components.FirstOrDefault(c => c && c.GetType().FullName == "UnityEngine.InputSystem.PlayerInput");
                var renderers = obj.GetComponentsInChildren<Renderer>(true);
                return new Prefab {
                    structure = string.Join("\n", transforms.Select(t => Hierarchy(t, obj.transform) + ":" + string.Join(",", t.GetComponents<Component>().Select(c => c ? c.GetType().FullName : "missing")))),
                    refs = string.Join("\n", refs.OrderBy(v => v)), missing = missing, renderers = renderers.Length,
                    unsupportedMaterials = renderers.SelectMany(r => r.sharedMaterials).Count(m => !m || !m.shader || !m.shader.isSupported || ShaderUtil.ShaderHasError(m.shader)),
                    controller = animator ? ID(animator.runtimeAnimatorController) : "", avatar = animator ? ID(animator.avatar) : "",
                    avatarValid = animator && animator.avatar && animator.avatar.isValid,
                    clips = animator && animator.runtimeAnimatorController ? string.Join("\n", animator.runtimeAnimatorController.animationClips.Select(ID).Distinct().OrderBy(v => v)) : "",
                    characterController = obj.GetComponent<CharacterController>(), thirdPersonController = controller, inputs = components.Any(c => c && c.GetType().FullName == "StarterAssets.StarterAssetsInputs"),
                    playerInput = input, actions = input ? ID(input.GetType().GetProperty("actions").GetValue(input) as UnityEngine.Object) : "",
                    cameraTarget = controller && controller.GetType().GetField("CinemachineCameraTarget").GetValue(controller) is GameObject
                };
            } finally { PrefabUtility.UnloadPrefabContents(obj); }
        }
        static Asset Snapshot(string path, IEnumerable<string> reasons) {
            var importer = AssetImporter.GetAtPath(path);
            return new Asset { path = path, guid = AssetDatabase.AssetPathToGUID(path),
                ids = string.Join("\n", AssetDatabase.LoadAllAssetsAtPath(path).Select(o => o.GetType().FullName + ":" + o.name + ":" + ID(o)).OrderBy(v => v)),
                deps = string.Join("\n", AssetDatabase.GetDependencies(path, true).Select(AssetDatabase.AssetPathToGUID).OrderBy(v => v)),
                importer = importer ? EditorJsonUtility.ToJson(importer) : "", reasons = reasons.ToArray() };
        }
        static Dictionary<string, HashSet<string>> Closure() {
            var keep = new Dictionary<string, HashSet<string>>();
            void Keep(string path, string reason) {
                if (!Starter(path) || AssetDatabase.IsValidFolder(path)) return;
                foreach (var dep in AssetDatabase.GetDependencies(path, true).Where(p => Starter(p) && !AssetDatabase.IsValidFolder(p))) {
                    if (!keep.TryGetValue(dep, out var reasons)) keep.Add(dep, reasons = new HashSet<string>());
                    reasons.Add(reason);
                }
            }
            foreach (var dep in AssetDatabase.GetDependencies(Player, true)) Keep(dep, "PlayerArmature serialized dependency");
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && !Starter(p) && !AssetDatabase.IsValidFolder(p)))
                foreach (var dep in AssetDatabase.GetDependencies(path, true)) Keep(dep, "Other project asset: " + path);
            foreach (var file in Directory.GetFiles("ProjectSettings"))
                foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\b(?:guid:\s*|GUID:)([a-fA-F0-9]{32})")) Keep(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value), "Project setting: " + file.Replace('\\', '/'));
            foreach (var pipeline in new UnityEngine.Object[] { UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline, UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline, QualitySettings.renderPipeline })
                if (pipeline) Keep(AssetDatabase.GetAssetPath(pipeline), "Currently configured render pipeline");
            var liveRoots = Enumerable.Range(0, SceneManager.sceneCount).SelectMany(i => SceneManager.GetSceneAt(i).GetRootGameObjects()).Cast<UnityEngine.Object>().ToArray();
            foreach (var value in EditorUtility.CollectDependencies(liveRoots)) Keep(AssetDatabase.GetAssetPath(value), "Loaded/unsaved scene object reference");
            foreach (string path in new[] { Root + "/Runtime/ThirdPersonController/Prefabs/MainCamera.prefab", Root + "/Runtime/ThirdPersonController/Prefabs/PlayerFollowCamera.prefab", Root + "/Runtime/InputSystem/StarterAssets.inputsettings.asset", Root + "/Runtime/Unity.StarterAssets.asmdef" }) Keep(path, "Third-person camera/control/assembly support");
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(Starter).Where(p => !AssetDatabase.IsValidFolder(p))) {
                string name = Path.GetFileName(path).ToLowerInvariant();
                if (pResource(path) || name.Contains("license") || name.Contains("notice") || name.Contains("copyright")) Keep(path, "Dynamic resource or legal notice");
            }
            var scripts = AssetDatabase.GetAllAssetPaths().Where(p => p.StartsWith("Assets/") && p.EndsWith(".cs")).ToDictionary(p => p, File.ReadAllText);
            var declarations = scripts.Select(kv => new { kv.Key, Names = Regex.Matches(kv.Value, @"\b(?:class|struct|interface|enum)\s+(\w+)").Cast<Match>().Select(m => m.Groups[1].Value).ToArray() }).ToArray();
            bool changed;
            do {
                int count = keep.Count;
                string[] consumers = scripts.Where(kv => !Starter(kv.Key) || keep.ContainsKey(kv.Key)).Select(kv => kv.Value).ToArray();
                foreach (var script in declarations.Where(s => Starter(s.Key)))
                    if (script.Names.Any(name => consumers.Any(text => Regex.IsMatch(text, @"\b" + Regex.Escape(name) + @"\b")))) Keep(script.Key, "C# symbol/partial type dependency");
                foreach (var script in keep.Keys.Where(p => p.EndsWith(".cs")).ToArray()) {
                    string parent = Path.GetDirectoryName(script).Replace('\\', '/');
                    while (parent.StartsWith(Root)) {
                        foreach (var asm in Directory.GetFiles(parent, "*.asmdef").Concat(Directory.GetFiles(parent, "*.asmref"))) Keep(asm.Replace('\\', '/'), "Retained script assembly boundary");
                        parent = Path.GetDirectoryName(parent).Replace('\\', '/');
                    }
                }
                changed = count != keep.Count;
            } while (changed);
            return keep;
        }
        static bool pResource(string p) => p.Contains("/Resources/") || p.Contains("/StreamingAssets/");
        static void Idle() { Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "Cleanup requires the idle Editor in Edit mode."); }
        public static string Inspect() {
            Idle(); var state = CurrentState(); var keep = Closure(); var prefab = PlayerState();
            Require(JsonUtility.ToJson(state) == JsonUtility.ToJson(CurrentState()), "Inspection changed open scene/Play/pipeline state.");
            var paths = AssetDatabase.GetAllAssetPaths().Where(p => Starter(p) && !AssetDatabase.IsValidFolder(p)).OrderBy(p => p).ToArray();
            var notes = new List<string>();
            if (prefab.missing != 0) notes.Add("Pre-existing missing prefab references: " + prefab.missing);
            if (!prefab.avatarValid) notes.Add("Pre-existing invalid/missing avatar.");
            if (!prefab.playerInput || prefab.actions == "") notes.Add("Pre-existing missing PlayerInput/actions.");
            if (prefab.unsupportedMaterials != 0) notes.Add("Pre-existing unsupported/error prefab materials: " + prefab.unsupportedMaterials);
            notes.Add("ThirdPersonController requires a scene MainCamera and assigned follow camera target; camera support prefabs retained, no user scene wiring changed.");
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline) {
                int builtIn = AssetDatabase.FindAssets("t:Material").Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).Count(m => m && m.shader && m.shader.name == "Toon/Toon" && m.IsKeywordEnabled("UTS_RP_BUILTIN"));
                if (builtIn > 0) notes.Add("Pre-existing Toon materials still use Built-in pipeline keyword under active SRP: " + builtIn + ". Not changed by cleanup.");
            }
            var plan = new Plan { success = true, keep = keep.Keys.OrderBy(p => p).ToArray(), delete = paths.Where(p => !keep.ContainsKey(p)).ToArray(), assets = paths.Select(p => Snapshot(p, keep.TryGetValue(p, out var r) ? r : new HashSet<string>())).ToArray(), prefab = prefab, state = state, setupNotes = notes.ToArray(), beforeCount = paths.Length };
            File.WriteAllText(Evidence + "/plan.json", JsonUtility.ToJson(plan, true));
            return "Inspected " + plan.beforeCount + " assets; keep " + plan.keep.Length + ", delete " + plan.delete.Length + ". No assets deleted.";
        }
        public static string Apply() {
            Idle(); var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Evidence + "/plan.json"));
            Require(JsonUtility.ToJson(plan.state) == JsonUtility.ToJson(CurrentState()), "Scene/Play/pipeline state changed since inspection; nothing deleted.");
            var keep = Closure();
            foreach (var path in plan.delete) {
                Require(Starter(path) && !keep.ContainsKey(path), "Deletion is outside scope or newly referenced: " + path);
                Require(File.Exists(Evidence + "/Before/" + path) && File.Exists(Evidence + "/Before/" + path + ".meta"), "Backup missing: " + path);
                var previous = plan.assets.First(a => a.path == path); Require(previous.guid == AssetDatabase.AssetPathToGUID(path), "Asset changed since plan: " + path);
                Require(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Evidence + "/Before/" + path)) && File.ReadAllBytes(path + ".meta").SequenceEqual(File.ReadAllBytes(Evidence + "/Before/" + path + ".meta")), "Deletion candidate changed since backup: " + path);
            }
            foreach (var path in plan.delete) Require(AssetDatabase.DeleteAsset(path), "Could not delete: " + path);
            foreach (var folder in AssetDatabase.GetAllAssetPaths().Where(p => Starter(p) && AssetDatabase.IsValidFolder(p)).OrderByDescending(p => p.Length))
                if (Directory.GetFileSystemEntries(folder).Length == 0) Require(AssetDatabase.DeleteAsset(folder), "Could not remove empty folder: " + folder);
            File.WriteAllText(Evidence + "/deleted.json", JsonUtility.ToJson(plan, true));
            return "Deleted " + plan.delete.Length + " unused Starter Assets assets; awaiting compilation and post-cleanup verification.";
        }
        public static string Verify() {
            Idle(); var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Evidence + "/plan.json"));
            foreach (var path in plan.delete) Require(AssetDatabase.AssetPathToGUID(path) == "", "Deleted asset remains: " + path);
            foreach (var path in plan.keep) {
                var before = plan.assets.First(a => a.path == path); var after = Snapshot(path, before.reasons);
                Require(JsonUtility.ToJson(before) == JsonUtility.ToJson(after), "Retained asset identity/dependencies/importer changed: " + path);
            }
            var prefab = PlayerState(); Require(JsonUtility.ToJson(prefab) == JsonUtility.ToJson(plan.prefab), "PlayerArmature contents changed.");
            Require(JsonUtility.ToJson(plan.state) == JsonUtility.ToJson(CurrentState()), "Open scene/Play/pipeline state changed.");
            var keep = Closure(); Require(plan.delete.All(p => !keep.ContainsKey(p)), "A deleted asset became necessary.");
            File.WriteAllText(Evidence + "/verification.json", JsonUtility.ToJson(new Plan { success = true, keep = plan.keep, delete = plan.delete, prefab = prefab, state = CurrentState(), setupNotes = plan.setupNotes, beforeCount = plan.beforeCount }, true));
            return "Verified PlayerArmature, retained GUID/local IDs/importers/dependencies, scene/Play/pipeline state and compiled Editor after script deletion.";
        }
        public static string RemoveHarness() {
            Idle(); File.WriteAllBytes("Assets/Editor/LocalValidationBridge.cs", File.ReadAllBytes(Evidence + "/Before/Assets/Editor/LocalValidationBridge.cs"));
            Require(AssetDatabase.DeleteAsset("Assets/Editor/StarterAssetsCleanup.cs"), "Cannot remove temporary harness.");
            EditorApplication.delayCall += () => { AssetDatabase.Refresh(); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); };
            return "Temporary cleanup harness removed; original bridge restored byte-for-byte. Final compilation scheduled.";
        }
    }
}
