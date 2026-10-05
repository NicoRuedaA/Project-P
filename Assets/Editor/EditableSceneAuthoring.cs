using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Pokemon3D.Locomotion;

namespace Pokemon3D.Editor
{
    public static class EditableSceneAuthoring
    {
        public const string ScenePath = "Assets/Scenes/Valley.unity";
        const string MaterialsFolder = "Assets/Materials/Valley";
        [Serializable]
        public sealed class SceneReport
        {
            public string path, name, action, verification;
            public bool dirty, active;
            public int roots, objects, renderers, missingMaterials, transientMaterials, games, authoredWorlds, trainers, cameras, creatures, proceduralRigs, legs;
        }
        [Serializable] sealed class Inspection { public SceneReport[] scenes; }
        static void RequireEditMode()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Scene authoring requires the idle editor in Edit mode.");
        }
        public static string Inspect()
        {
            var reports = new List<SceneReport>();
            for (int i = 0; i < SceneManager.sceneCount; i++) reports.Add(Describe(SceneManager.GetSceneAt(i), "inspect"));
            return JsonUtility.ToJson(new Inspection { scenes = reports.ToArray() }, true);
        }
        static SceneReport Describe(Scene scene, string action)
        {
            var report = new SceneReport { path = scene.path, name = scene.name, action = action, dirty = scene.isDirty, active = scene == SceneManager.GetActiveScene() };
            if (!scene.isLoaded) return report;
            var roots = scene.GetRootGameObjects(); report.roots = roots.Length;
            foreach (var root in roots)
            {
                report.objects += root.GetComponentsInChildren<Transform>(true).Length;
                report.games += root.GetComponentsInChildren<Game>(true).Length;
                report.authoredWorlds += root.GetComponentsInChildren<AuthoredWorld>(true).Length;
                report.trainers += root.GetComponentsInChildren<PlayerMotor>(true).Length;
                report.cameras += root.GetComponentsInChildren<Camera>(true).Length;
                report.creatures += root.GetComponentsInChildren<Creature>(true).Length;
                foreach (var animator in root.GetComponentsInChildren<ProceduralBodyAnimator>(true))
                    if (animator.SavedRig != null && animator.SavedRig.model && animator.SavedRig.waist) { report.proceduralRigs++; report.legs += animator.SavedRig.legs.Length; }
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    report.renderers++;
                    foreach (var material in renderer.sharedMaterials)
                    {
                        if (!material) report.missingMaterials++;
                        else if (!AssetDatabase.Contains(material)) report.transientMaterials++;
                    }
                }
            }
            return report;
        }
        static Game BootstrapOnly(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            var roots = scene.GetRootGameObjects(); if (roots.Length != 1) return null;
            var root = roots[0]; var game = root.GetComponent<Game>();
            if (!game || root.name != "Wildbound / bootstrap" || root.transform.childCount != 0 || root.GetComponents<Component>().Length != 2) return null;
            if (game.player || game.cam || game.companion || game.creatures.Count != 0) return null;
            return game;
        }
        static AuthoredWorld Marker(Scene scene)
        {
            AuthoredWorld marker = null;
            foreach (var root in scene.GetRootGameObjects()) foreach (var item in root.GetComponentsInChildren<AuthoredWorld>(true))
            { if (marker) throw new InvalidOperationException("The target scene has multiple authored worlds; no changes were made."); marker = item; }
            return marker;
        }
        public static string Build()
        {
            RequireEditMode();
            Scene original = SceneManager.GetActiveScene(), scene = SceneManager.GetSceneByPath(ScenePath);
            bool existing = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (!scene.IsValid() && existing) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            if (scene.IsValid())
            {
                var marker = Marker(scene);
                if (marker)
                {
                    SceneManager.SetActiveScene(scene); Frame(scene);
                    var reused = Describe(scene, "reused_without_rebuilding");
                    reused.verification = "Existing authored objects and unsaved edits preserved; no scene or materials overwritten.";
                    return JsonUtility.ToJson(reused, true);
                }
                if (!BootstrapOnly(scene)) throw new InvalidOperationException("Valley.unity already contains non-bootstrap content. It was not overwritten. Choose a separate authored scene or explicitly approve modifying that scene.");
            }
            Game game = scene.IsValid() ? BootstrapOnly(scene) : null;
            if (!scene.IsValid())
            {
                // A proven original generated stub can be populated without discarding its scene.
                game = string.IsNullOrEmpty(original.path) ? BootstrapOnly(original) : null;
                if (game) scene = original;
                else scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }
            SceneManager.SetActiveScene(scene);
            var before = new HashSet<GameObject>(scene.GetRootGameObjects());
            if (!game) game = new GameObject("Wildbound / bootstrap").AddComponent<Game>();
            var random = UnityEngine.Random.state;
            try { World.Build(game); } finally { UnityEngine.Random.state = random; }
            game.gameObject.AddComponent<AuthoredWorld>();
            GroundActors(game);
            Quaternion cameraRotation = Quaternion.Euler(24f, 30f, 0f);
            game.cam.transform.SetPositionAndRotation(game.player.transform.position + Vector3.up * 1.7f + cameraRotation * (Vector3.back * 6.5f), cameraRotation);
            PersistMaterials(scene);
            EnsureFolder("Assets/Scenes");
            foreach (var root in scene.GetRootGameObjects()) if (!before.Contains(root)) Undo.RegisterCreatedObjectUndo(root, "Create playable valley");
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("The populated scene could not be saved; its visible unsaved objects were preserved.");
            var settings = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (!settings.Exists(s => s.path == ScenePath)) { settings.Add(new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = settings.ToArray(); }
            AssetDatabase.SaveAssets();
            string proof = VerifySaved();
            SceneManager.SetActiveScene(scene); Frame(scene);
            var result = Describe(scene, "built_and_saved"); result.verification = proof;
            return JsonUtility.ToJson(result, true);
        }
        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/'); string parent = parts[0];
            for (int i = 1; i < parts.Length; i++) { string next = parent + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, parts[i]); parent = next; }
        }
        static void PersistMaterials(Scene scene)
        {
            EnsureFolder(MaterialsFolder); var saved = new Dictionary<Material, Material>(); int index = 0;
            foreach (var root in scene.GetRootGameObjects()) foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i]; if (!material || AssetDatabase.Contains(material)) continue;
                    Material persistent;
                    if (!saved.TryGetValue(material, out persistent))
                    {
                        persistent = new Material(material) { name = "Valley material " + index++ };
                        string path = AssetDatabase.GenerateUniqueAssetPath(MaterialsFolder + "/" + persistent.name + ".mat");
                        AssetDatabase.CreateAsset(persistent, path); saved.Add(material, persistent);
                    }
                    materials[i] = persistent;
                }
                renderer.sharedMaterials = materials;
            }
        }
        static void GroundActors(Game game)
        {
            Physics.SyncTransforms(); var actors = new List<Transform> { game.player.transform };
            foreach (var creature in game.creatures) actors.Add(creature.transform);
            foreach (var actor in actors)
            {
                RaycastHit hit;
                if (!Physics.Raycast(actor.position + Vector3.up * 4f, Vector3.down, out hit, 20f, 1 << 8, QueryTriggerInteraction.Ignore)) continue;
                var controller = actor.GetComponent<CharacterController>(); bool enabled = controller.enabled; controller.enabled = false;
                actor.position = new Vector3(actor.position.x, hit.point.y + .01f, actor.position.z); controller.enabled = enabled;
            }
            foreach (var creature in game.creatures) creature.BindRuntime();
            Physics.SyncTransforms();
        }
        static void Frame(Scene scene)
        {
            var view = SceneView.lastActiveSceneView;
            if (!view) view = EditorWindow.GetWindow<SceneView>();
            view.sceneLighting = true; view.rotation = Quaternion.Euler(35f, 35f, 0f);
            view.Frame(new Bounds(new Vector3(0f, 3f, -5f), new Vector3(70f, 25f, 65f)), false);
            Selection.activeGameObject = Marker(scene).gameObject; SceneView.RepaintAll();
        }
        public static string VerifySaved()
        {
            RequireEditMode();
            Scene preview = EditorSceneManager.OpenPreviewScene(ScenePath);
            try
            {
                var marker = Marker(preview);
                if (!marker) throw new InvalidOperationException("Saved scene has no authored world marker.");
                var game = marker.GetComponent<Game>(); var before = Describe(preview, "verify");
                if (before.trainers != 1 || before.cameras != 1 || before.creatures != 9 || before.proceduralRigs != 9 || before.legs != 18 || before.missingMaterials != 0 || before.transientMaterials != 0)
                    throw new InvalidOperationException("Saved scene references, actor counts, rigs, or persistent materials failed readback.");
                var positions = new Dictionary<Transform, Vector3>(); var materials = new Dictionary<Renderer, Material>();
                foreach (var root in preview.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) positions[t] = t.localPosition;
                foreach (var root in preview.GetRootGameObjects()) foreach (var r in root.GetComponentsInChildren<Renderer>(true)) materials[r] = r.sharedMaterial;
                Vector3 authoredPlayer = game.player.transform.position + new Vector3(.5f, .25f, -.5f);
                game.player.transform.position = authoredPlayer;
                positions[game.player.transform] = game.player.transform.localPosition;
                if (!game.InitializeWorld() || !game.InitializeWorld()) throw new InvalidOperationException("Authored startup did not reuse the saved world.");
                game.Load(false);
                var after = Describe(preview, "verify");
                if (after.objects != before.objects || after.creatures != before.creatures || after.games != 1) throw new InvalidOperationException("Startup duplicated authored objects.");
                foreach (var item in positions) if ((item.Key.localPosition - item.Value).sqrMagnitude > 1e-8f) throw new InvalidOperationException("Startup reset an authored transform.");
                foreach (var item in materials) if (item.Key.sharedMaterial != item.Value) throw new InvalidOperationException("Startup replaced an authored material.");
                return "Native saved-scene preview reload passed: 1 trainer, 1 camera, 9 creatures, 9 saved rigs/18 legs, persistent materials; repeated startup did not duplicate objects or reset authored positions/materials.";
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
