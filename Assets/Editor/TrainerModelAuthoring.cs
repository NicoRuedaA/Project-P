using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wildbound.Pokemon;

namespace Wildbound.Editor
{
    // Fixed local male trainer assets only. No caller paths, scripts, network or Play control.
    public static class TrainerModelAuthoring
    {
        const string Folder = "Assets/Models/Trainer";
        const string MaterialsFolder = "Assets/Materials/Trainer";
        const string ModelPath = Folder + "/ptrainer.fbx";
        const string PrefabPath = "Assets/Prefabs/Trainer/PokemonTrainer.prefab";
        static string EvidenceFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LocalValidationBridge"));
        static readonly Dictionary<string, string> Maps = new Dictionary<string, string> {
            { "EyeL", "eye_ptrainer_w_col.png" }, { "EyeR", "eye_ptrainer_w_col.png" },
            { "alp_ptrainer_001", "alp_ptrainer_001_col.png" }, { "def_mball_001", "def_mball_001_col.png" },
            { "def_ptrainer_001", "def_ptrainer_001_col.png" }, { "skin_ptrainer_001", "skin_ptrainer_001_col.png" }
        };
        [Serializable] sealed class Report {
            public string action, scene, prefab, preview, verification, appearance;
            public int renderers, activeRenderers, skins, bones, vertices, materials, missingMaterials, missingTextures, missingScripts;
            public float uprightError, facingError;
            public float height;
            public bool saved, controllerPreserved, startupPreserved;
        }
        static void Idle() {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Trainer authoring requires idle Edit mode.");
        }
        static Scene Target() {
            var s = SceneManager.GetSceneByPath(EditableSceneAuthoring.ScenePath);
            if (!s.IsValid() || !s.isLoaded) throw new InvalidOperationException("Saved Valley must already be open.");
            return s;
        }
        static PlayerMotor Player(Scene s) => s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerMotor>(true)).Single();
        static Transform Visual(PlayerMotor p) => new SerializedObject(p).FindProperty("visual").objectReferenceValue as Transform;
        static void SetVisual(PlayerMotor p, Transform visual) {
            var data = new SerializedObject(p); data.FindProperty("visual").objectReferenceValue = visual;
            data.ApplyModifiedPropertiesWithoutUndo(); p.BindRuntime();
        }
        static Bounds BoundsOf(GameObject model) {
            var rs = model.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeSelf).ToArray(); if (rs.Length == 0) throw new InvalidOperationException("Trainer has no visible mesh.");
            var b = rs[0].bounds; foreach (var r in rs.Skip(1)) b.Encapsulate(r.bounds); return b;
        }
        static Material MaterialFor(string slot) {
            if (!Maps.TryGetValue(slot, out var filename)) throw new InvalidOperationException("Unrecognized trainer material slot: " + slot);
            string path = MaterialsFolder + "/" + slot + ".mat";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/images/" + filename);
            if (!texture) throw new InvalidOperationException("Missing source trainer color map: " + filename);
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) {
                if (existing.mainTexture != texture || !existing.shader || !existing.shader.isSupported)
                    throw new InvalidOperationException("Existing trainer material conflicts; not overwritten: " + slot);
                return existing;
            }
            if (File.Exists(path)) throw new InvalidOperationException("Unreadable trainer material conflict.");
            
            // The five referenced PNG alpha channels were measured >=251/255; they are opaque color maps.
            var m = ToonMaterials.Create(Color.white, texture); m.name = slot;
            AssetDatabase.CreateAsset(m, path); return m;
        }
        static Transform Bone(Transform model, string name) {
            var matches = model.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Trainer bone must be unique: " + name); return matches[0];
        }
        static Vector3 Up(Transform model) => Bone(model, "Head").position - (Bone(model, "FootL").position + Bone(model, "FootR").position) * .5f;
        static Vector3 Forward(Transform model) => Vector3.ProjectOnPlane(Bone(model, "ToeL").position + Bone(model, "ToeR").position - Bone(model, "FootL").position - Bone(model, "FootR").position, Vector3.up);
        static float UprightError(Transform model) => Vector3.Angle(Up(model), Vector3.up);
        static void Normalize(GameObject root) {
            var model = root.transform.Find("ptrainer skin"); if (!model) throw new InvalidOperationException("Generated trainer skin wrapper missing.");
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) {
                string n = r.gameObject.name;
                bool alternate = n.IndexOf("blink", StringComparison.OrdinalIgnoreCase) >= 0 || (n.Contains("_Mouth_") && !n.Contains("_faceN_Mouth_")) || n.Contains("pokeballL");
                r.enabled = !alternate;
            }
            model.rotation = Quaternion.FromToRotation(Up(model), Vector3.up) * model.rotation;
            var forward = Forward(model); if (forward.sqrMagnitude < 1e-8f) throw new InvalidOperationException("Trainer foot forward axis is degenerate.");
            model.rotation = Quaternion.FromToRotation(forward.normalized, Vector3.forward) * model.rotation;
            var b = BoundsOf(root); if (b.size.y <= .001f) throw new InvalidOperationException("Trainer geometry has zero height.");
            model.localScale *= 1.8f / b.size.y; b = BoundsOf(root); model.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            if (UprightError(model) > .1f || Vector3.Angle(Forward(model), Vector3.forward) > .1f) throw new InvalidOperationException("Trainer visual orientation calibration failed.");
        }
        static Report Validate(GameObject model) {
            var result = new Report { prefab = PrefabPath, height = BoundsOf(model).size.y };
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) result.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) {
                result.renderers++; if (r.enabled && r.gameObject.activeSelf) result.activeRenderers++;
                foreach (var m in r.sharedMaterials) {
                    if (!m || !AssetDatabase.Contains(m) || !m.shader || !m.shader.isSupported || ShaderUtil.ShaderHasError(m.shader)) result.missingMaterials++;
                    else if (!m.mainTexture || !AssetDatabase.Contains(m.mainTexture)) result.missingTextures++;
                }
                if (r is SkinnedMeshRenderer skin) {
                    result.skins++; if (!skin.sharedMesh || skin.bones.Length == 0 || skin.bones.Any(b => !b) || skin.bones.Length != skin.sharedMesh.bindposes.Length)
                        throw new InvalidOperationException("Invalid trainer skin/bind poses.");
                    result.bones += skin.bones.Length; result.vertices += skin.sharedMesh.vertexCount;
                    var mesh = new Mesh(); try { skin.BakeMesh(mesh); foreach (var v in mesh.vertices) if (float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z)) throw new InvalidOperationException("Non-finite trainer skin."); } finally { UnityEngine.Object.DestroyImmediate(mesh); }
                }
            }
            result.materials = model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().Count();
            if (result.skins == 0 || result.missingMaterials != 0 || result.missingTextures != 0 || result.missingScripts != 0 || model.GetComponentsInChildren<Animator>(true).Any(a => a.enabled))
                throw new InvalidOperationException("Trainer validation failed: missing skin/material/map/script or active ordinary Animator.");
            var skinRoot = model.transform.Find("ptrainer skin"); if (skinRoot) { result.uprightError = UprightError(skinRoot); result.facingError = Vector3.Angle(Forward(skinRoot), Vector3.forward); }
            return result;
        }
        static GameObject Prefab() {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing) {
                var skin = existing.transform.Find("ptrainer skin");
                if (!skin) throw new InvalidOperationException("Trainer prefab does not match the generated visual structure; not overwritten.");
                if (UprightError(skin) > 5) {
                    // One-time correction admits only the exact initially generated artifact, never arbitrary user edits.
                    string hash; using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(PrefabPath))).Replace("-", "").ToLowerInvariant();
                    if (hash != "16a3ca5bb7ae8b61b4ace50717a7d839306afac602f36ac4db649c5dfdb988bc")
                        throw new InvalidOperationException("Trainer prefab has edits; automatic initial orientation correction refused.");
                    var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                    try { Normalize(contents); PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath, out bool success); if (!success) throw new InvalidOperationException("Trainer orientation save failed."); }
                    finally { PrefabUtility.UnloadPrefabContents(contents); }
                    existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                }
                Validate(existing); return existing;
            }
            if (File.Exists(PrefabPath)) throw new InvalidOperationException("Unreadable trainer prefab conflict; not overwritten.");
            var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (!importer) throw new InvalidOperationException("Trainer FBX has not been imported.");
            bool changed = importer.animationType != ModelImporterAnimationType.Generic || importer.optimizeGameObjects || importer.importAnimation || !importer.isReadable;
            importer.animationType = ModelImporterAnimationType.Generic; importer.optimizeGameObjects = false; importer.importAnimation = false; importer.isReadable = true;
            if (changed) importer.SaveAndReimport();
            if (!AssetDatabase.IsValidFolder(MaterialsFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Trainer");
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Trainer")) AssetDatabase.CreateFolder("Assets/Prefabs", "Trainer");
            foreach (var slot in Maps.Keys) MaterialFor(slot); AssetDatabase.SaveAssets();
            var preview = EditorSceneManager.NewPreviewScene(); GameObject root = null;
            try {
                root = new GameObject("Pokemon Trainer / imported male model"); SceneManager.MoveGameObjectToScene(root, preview);
                var model = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), root.transform);
                model.name = "ptrainer skin"; model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                foreach (var a in model.GetComponentsInChildren<Animator>(true)) a.enabled = false;
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(m => MaterialFor(m ? m.name : "")).ToArray();
                Normalize(root);
                Validate(root); var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool success);
                if (!success || !saved) throw new InvalidOperationException("Trainer prefab save failed."); return saved;
            } finally { if (root) UnityEngine.Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
        }
        static string Render(GameObject model) {
            var utility = new PreviewRenderUtility(); Texture2D picture = null;
            try {
                var copy = UnityEngine.Object.Instantiate(model); copy.transform.SetParent(null); copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); utility.AddSingleGO(copy);
                var b = BoundsOf(copy); float radius = b.size.magnitude * .5f;
                utility.camera.fieldOfView = 35; utility.camera.nearClipPlane = .01f; utility.camera.farClipPlane = 100;
                utility.camera.transform.position = b.center + new Vector3(.35f, .12f, 1).normalized * (radius / Mathf.Sin(17.5f * Mathf.Deg2Rad) * 1.2f);
                utility.camera.transform.LookAt(b.center); utility.camera.backgroundColor = new Color(.19f, .23f, .27f); utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.lights[0].intensity = 1.2f; utility.lights[0].transform.rotation = Quaternion.Euler(30, 35, 0);
                utility.BeginStaticPreview(new Rect(0, 0, 768, 768)); utility.Render(); picture = utility.EndStaticPreview();
                Directory.CreateDirectory(EvidenceFolder); string path = Path.Combine(EvidenceFolder, "pokemon-trainer-preview.png"); File.WriteAllBytes(path, picture.EncodeToPNG()); return path;
            } finally { if (picture) UnityEngine.Object.DestroyImmediate(picture); utility.Cleanup(); }
        }
        static void Check(PlayerMotor p) {
            var marker = p.GetComponent<TrainerVisualOverride>();
            if (!marker || !marker.originalVisual || marker.originalVisual.gameObject.activeSelf || !marker.replacementVisual || Visual(p) != marker.replacementVisual || !marker.modelPrefab)
                throw new InvalidOperationException("Trainer visual replacement/rollback references are incomplete.");
            Validate(marker.replacementVisual.gameObject);
        }
        static void Restore(PlayerMotor p) {
            Check(p); var marker = p.GetComponent<TrainerVisualOverride>(); var replacement = marker.replacementVisual.gameObject;
            if (marker.proceduralAnimator) { marker.proceduralAnimator.ResetState(); UnityEngine.Object.DestroyImmediate(marker.proceduralAnimator); }
            SetVisual(p, marker.originalVisual); marker.originalVisual.gameObject.SetActive(true); UnityEngine.Object.DestroyImmediate(replacement); UnityEngine.Object.DestroyImmediate(marker);
        }
        static void VerifySaved(Report report) {
            var s = EditorSceneManager.OpenPreviewScene(EditableSceneAuthoring.ScenePath);
            try {
                var p = Player(s); Check(p); var visual = Visual(p); var pos = p.transform.position; var rot = p.transform.rotation;
                var cc = p.GetComponent<CharacterController>(); var height = cc.height; var radius = cc.radius;
                var game = s.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Game>(true)).Single(); var camera = game.cam;
                if (!game.InitializeWorld() || !game.InitializeWorld() || game.player != p || game.cam != camera || Visual(p) != visual || p.transform.position != pos || p.transform.rotation != rot || cc.height != height || cc.radius != radius)
                    throw new InvalidOperationException("Saved authored startup changed trainer/controller references or root.");
                var animator = p.GetComponent<Wildbound.Locomotion.ProceduralBodyAnimator>();
                var marker = p.GetComponent<TrainerVisualOverride>();
                if (!animator || marker.proceduralAnimator != animator || animator.Rig == null || animator.Rig.model != visual || animator.Rig.legs.Length != 2 || animator.Rig.arms.Length != 2)
                    throw new InvalidOperationException("Saved authored startup lost the full-body trainer binding.");
                report.controllerPreserved = true;
                Check(p); Restore(p); if (!game.InitializeWorld() || !Visual(p) || !Visual(p).gameObject.activeSelf || p.GetComponent<TrainerVisualOverride>()) throw new InvalidOperationException("Trainer preview rollback failed.");
                report.startupPreserved = true;
            } finally { EditorSceneManager.ClosePreviewScene(s); }
        }
        public static string Replace() {
            Idle(); var scene = Target(); if (scene.isDirty) throw new InvalidOperationException("Save unrelated Valley edits before trainer visual replacement.");
            var p = Player(scene); var prefab = Prefab(); var report = Validate(prefab); report.preview = Render(prefab);
            report.appearance = "Source opaque color maps; no source-specific Smash shader recreation or skeletal trainer walking clips. The imported trainer remains in its source rest pose.";
            if (p.GetComponent<TrainerVisualOverride>()) { Check(p); VerifySaved(report); report.action = "existing_trainer_visual_preserved"; report.saved = true; report.scene = scene.path; return Publish(report); }
            var original = Visual(p); if (!original) throw new InvalidOperationException("Existing player visual reference is missing.");
            var cc = p.GetComponent<CharacterController>(); var oldPosition = p.transform.position; var oldRotation = p.transform.rotation; var oldScale = p.transform.localScale;
            var ccState = EditorJsonUtility.ToJson(cc); var health = p.health; var stamina = p.stamina; var invulnerable = p.invulnerable;
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Game>(true)).Single(); var camera = game.cam;
            var replacement = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); replacement.transform.SetParent(p.transform, false);
            var marker = p.gameObject.AddComponent<TrainerVisualOverride>(); marker.originalVisual = original; marker.replacementVisual = replacement.transform; marker.modelPrefab = prefab;
            try {
                original.gameObject.SetActive(false); SetVisual(p, replacement.transform); Check(p);
                if (p.transform.position != oldPosition || p.transform.rotation != oldRotation || p.transform.localScale != oldScale || EditorJsonUtility.ToJson(cc) != ccState || p.health != health || p.stamina != stamina || p.invulnerable != invulnerable || game.player != p || game.cam != camera)
                    throw new InvalidOperationException("Trainer visual replacement changed an actor/controller/gameplay field.");
                report.controllerPreserved = true; EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Trainer scene save failed.");
                report.saved = true; VerifySaved(report); report.action = "trainer_visual_replaced_and_saved"; report.scene = scene.path;
                var view = SceneView.lastActiveSceneView; if (view) view.Frame(new Bounds(p.transform.position + Vector3.up, Vector3.one * 5), false);
                Selection.activeGameObject = p.gameObject; SceneView.RepaintAll(); return Publish(report);
            } catch { Restore(p); throw; }
        }
        public static string Inspect() {
            Idle(); var s = Target(); var p = Player(s); Check(p); var r = Validate(Visual(p).gameObject);
            r.action = "trainer_visual_inspected"; r.scene = s.path; r.saved = !s.isDirty; VerifySaved(r); return Publish(r);
        }
        [MenuItem("Wildbound/Restore Original Trainer Visual")]
        public static void RestoreMenu() {
            Idle(); var s = Target(); if (s.isDirty) throw new InvalidOperationException("Save unrelated Valley edits before visual rollback.");
            Restore(Player(s)); EditorSceneManager.MarkSceneDirty(s); if (!EditorSceneManager.SaveScene(s)) throw new InvalidOperationException("Trainer rollback scene save failed.");
        }
        static string Publish(Report r) {
            r.verification = "Native finite skin/bind poses, persistent supported materials/color maps, disabled Animator, saved reload/repeated authored initialization and isolated rollback verified; no Play-mode or save-file write.";
            Directory.CreateDirectory(EvidenceFolder); string json = JsonUtility.ToJson(r, true); File.WriteAllText(Path.Combine(EvidenceFolder, "pokemon-trainer-report.json"), json); return json;
        }
    }
}
