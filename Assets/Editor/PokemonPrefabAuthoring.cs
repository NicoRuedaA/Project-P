using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Pokemon3D.Locomotion;
using Pokemon3D.Pokemon;

namespace Pokemon3D.Editor
{
    public static class PokemonPrefabAuthoring
    {
        const string Folder = "Assets/Prefabs/Pokemon";
        const string Representative = Folder + "/066-machop.prefab";
        static string EvidenceFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LocalValidationBridge"));
        [Serializable] sealed class Profile { public int dex; public string archetype; public Skeleton skeleton; public Metrics metrics; }
        [Serializable] sealed class Skeleton { public string waist, head; public Leg[] legs; public Arm[] arms; public string[] spine; }
        [Serializable] sealed class Leg { public string side, upper, lower, effector; }
        [Serializable] sealed class Arm { public string side, upper, lower, effector; public string[] chain; public float spread; }
        [Serializable] sealed class Metrics { public float stride; }
        [Serializable] sealed class Report {
            public string action, scene, verification, regression, contactDiagnostic, posePreview;
            public int prefabs, created, reused, replacements, representativeDex = 66, checks, changedBoneFrames;
            public float maxSkinVertexChange, maxFootClearanceError, measuredLegLength, leftSoleHeight, rightSoleHeight;
            public bool saved, originalGameplayPreserved, galleryPreserved;
        }
        sealed class Pose { public Transform t; public Vector3 p, s; public Quaternion q; }
        sealed class Actor {
            public Creature c; public Transform model; public BodyRig rig; public BodyMotionProfile profile;
            public Vector3 position, scale; public Quaternion rotation;
            public int species; public float health; public bool ally; public CreatureRecord record;
        }
        static void RequireIdle() {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Prefab authoring requires the idle editor in Edit mode.");
        }
        static Scene TargetScene() {
            var scene = SceneManager.GetSceneByPath(EditableSceneAuthoring.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Saved Valley must be open; no scene will be replaced.");
            return scene;
        }
        static PokemonGalleryEntry[] Entries(Scene scene) {
            var galleries = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PokemonGallery>(true)).ToArray();
            if (galleries.Length != 1) throw new InvalidOperationException("Expected one existing first-generation gallery.");
            var entries = galleries[0].GetComponentsInChildren<PokemonGalleryEntry>(true).OrderBy(e => e.dex).ToArray();
            if (entries.Length != 151 || entries.Where((e, i) => e.dex != i + 1).Any()) throw new InvalidOperationException("Gallery is not the complete first-generation set.");
            return entries;
        }
        static string PrefabPath(PokemonGalleryEntry e) {
            var profile = JsonUtility.FromJson<Slug>(e.sourceProfile.text);
            if (profile == null || string.IsNullOrEmpty(profile.slug) || profile.slug.Any(c => !(c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-')))
                throw new InvalidOperationException("Unsafe source slug for dex " + e.dex);
            return Folder + "/" + e.dex.ToString("000") + "-" + profile.slug + ".prefab";
        }
        [Serializable] sealed class Slug { public string slug; }
        static Bounds BoundsOf(Transform model) {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("A model has no skin renderers.");
            var bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds); return bounds;
        }
        static List<Pose> PoseOf(Transform root) => root.GetComponentsInChildren<Transform>(true).Select(t => new Pose { t = t, p = t.localPosition, q = t.localRotation, s = t.localScale }).ToList();
        static void Restore(List<Pose> pose) { foreach (var p in pose) { p.t.localPosition = p.p; p.t.localRotation = p.q; p.t.localScale = p.s; } }
        static void AssertPose(List<Pose> pose) {
            foreach (var p in pose) if (!p.t || (p.t.localPosition - p.p).sqrMagnitude > 1e-8f || Quaternion.Angle(p.t.localRotation, p.q) > .01f || (p.t.localScale - p.s).sqrMagnitude > 1e-8f)
                throw new InvalidOperationException("An authoring/rest transform was reset or drifted: " + (p.t ? p.t.name : "missing"));
        }
        static void ValidatePrefab(GameObject prefab, PokemonGalleryEntry source) {
            var e = prefab ? prefab.GetComponent<PokemonGalleryEntry>() : null;
            if (!e || e.dex != source.dex || e.sourceModel != source.sourceModel || e.sourceProfile != source.sourceProfile || !e.visual)
                throw new InvalidOperationException("Existing prefab conflicts with source metadata for dex " + source.dex + "; it was not overwritten.");
            if (prefab.GetComponentsInChildren<Creature>(true).Length != 0 || prefab.GetComponentsInChildren<ProceduralBodyAnimator>(true).Length != 0)
                throw new InvalidOperationException("Static model prefab has unexpected gameplay/procedural components.");
            foreach (var renderer in e.visual.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if (!renderer.sharedMesh || renderer.bones.Length == 0 || renderer.bones.Any(b => !b) || renderer.bones.Length != renderer.sharedMesh.bindposes.Length)
                    throw new InvalidOperationException("Invalid prefab skin for dex " + source.dex);
                foreach (var m in renderer.sharedMaterials) if (!m || !AssetDatabase.Contains(m) || !m.shader || !m.shader.isSupported || ShaderUtil.ShaderHasError(m.shader))
                    throw new InvalidOperationException("Invalid prefab material for dex " + source.dex);
            }
            foreach (var t in prefab.GetComponentsInChildren<Transform>(true)) if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                throw new InvalidOperationException("Prefab contains missing scripts.");
        }
        public static string Export() {
            RequireIdle(); var scene = TargetScene(); if (scene.isDirty) throw new InvalidOperationException("Save unrelated Valley edits before exporting prefabs; no changes were made.");
            var entries = Entries(scene); var pose = PoseOf(scene.GetRootGameObjects().First(r => r.GetComponent<PokemonGallery>()).transform);
            foreach (var e in entries) {
                string path = PrefabPath(e); var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (File.Exists(path) && !existing) throw new InvalidOperationException("Unreadable prefab conflict at " + path);
                if (existing) ValidatePrefab(existing, e);
            }
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefabs", "Pokemon");
            var report = new Report { action = "model_prefabs_saved", scene = scene.path };
            var preview = EditorSceneManager.NewPreviewScene();
            try {
                foreach (var e in entries) {
                    string path = PrefabPath(e); var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (existing) { report.reused++; report.prefabs++; continue; }
                    var copy = UnityEngine.Object.Instantiate(e.gameObject); SceneManager.MoveGameObjectToScene(copy, preview);
                    try {
                        copy.name = e.dex.ToString("000") + " " + e.speciesName; copy.transform.SetParent(null);
                        copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); copy.transform.localScale = Vector3.one;
                        var ce = copy.GetComponent<PokemonGalleryEntry>(); var bounds = BoundsOf(ce.visual);
                        ce.visual.position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                        bool success; var prefab = PrefabUtility.SaveAsPrefabAsset(copy, path, out success);
                        if (!success || !prefab) throw new InvalidOperationException("Native prefab save failed for dex " + e.dex);
                        ValidatePrefab(prefab, e); report.created++; report.prefabs++;
                    } finally { UnityEngine.Object.DestroyImmediate(copy); }
                }
            } finally { EditorSceneManager.ClosePreviewScene(preview); }
            AssetDatabase.SaveAssets(); AssertPose(pose);
            if (scene.isDirty) throw new InvalidOperationException("Prefab export unexpectedly dirtied Valley.");
            report.galleryPreserved = true; report.saved = true;
            report.verification = "151 native reusable static-model prefabs, centered local origins and bounds-grounded skins; source material/profile/bone references preserved; no gameplay/procedural components.";
            return Publish(report, "pokemon151-prefabs-report.json");
        }
        static Transform Unique(Transform root, string name) {
            var found = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            if (found.Length != 1) throw new InvalidOperationException("Representative skeleton joint must be unique: " + name);
            return found[0];
        }
        public static BodyRig BindMachop(Transform model) {
            var metadata = model.GetComponent<PokemonGalleryEntry>();
            if (!metadata || metadata.dex != 66 || !metadata.sourceProfile) throw new InvalidOperationException("Only verified Machop is supported by this bounded imported-rig adapter.");
            var source = JsonUtility.FromJson<Profile>(metadata.sourceProfile.text);
            if (source.dex != 66 || source.archetype != "biped" || source.skeleton.legs.Length != 2 || !(source.metrics.stride > 0)) throw new InvalidOperationException("Machop source topology/profile is incompatible.");
            var rig = new BodyRig { model = model, waist = Unique(model, source.skeleton.waist), head = Unique(model, source.skeleton.head),
                spine = Unique(model, source.skeleton.spine[0]), profile = BodyMotionProfile.For(BodyArchetype.Biped) };
            rig.profile.displayHeight = metadata.displayHeight; rig.profile.stride = source.metrics.stride;
            float floor = BoundsOf(metadata.visual).min.y;
            rig.legs = source.skeleton.legs.Select(l => {
                var upper = Unique(model, l.upper); var lower = Unique(model, l.lower); var foot = Unique(model, l.effector);
                if (!lower.IsChildOf(upper) || !foot.IsChildOf(lower) || !upper.IsChildOf(rig.waist)) throw new InvalidOperationException("Machop leg chain ancestry is incompatible.");
                float sole = (foot.position.y - floor) / Mathf.Abs(model.lossyScale.y);
                if (sole < 0 || sole > metadata.displayHeight * .3f) throw new InvalidOperationException("Machop ankle-to-ground clearance is incompatible.");
                return new LimbRig { upper = upper, lower = lower, foot = foot, right = l.side == "R", soleHeight = Mathf.Max(.001f, sole) };
            }).ToArray();
            rig.arms = source.skeleton.arms.Select(a => new JointChain { right = a.side == "R", spread = a.spread, restJoints = new[] { Unique(model, a.chain[0]), Unique(model, a.effector) }, joints = new[] { Unique(model, a.upper), Unique(model, a.lower), Unique(model, a.effector) } }).ToArray();
            // The checked Machop profile names Tail1/Tail2. JsonUtility does not support jagged arrays.
            rig.tails = new[] { new JointChain { joints = new[] { Unique(model, "Tail1"), Unique(model, "Tail2") } } };
            rig.Measure();
            // Imported bind legs are nearly straight. Reserve real knee flexion and bound
            // planted stride by measured reach; do not edit bone rests or skin bind poses.
            rig.stanceLowering = Mathf.Max(0f, rig.legs.Max(l => l.upper.position.y - floor - l.sole - l.length * .78f)) / Mathf.Abs(model.lossyScale.y);
            rig.profile.stride = Mathf.Min(source.metrics.stride, rig.legs.Min(l => l.length) * 1.5f);
            return rig;
        }
        static void Check(Report report, bool value, string message) { report.checks++; if (!value) throw new InvalidOperationException("Imported Machop validation: " + message); }
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        static Vector3[] Bake(SkinnedMeshRenderer skin) { var mesh = new Mesh(); try { skin.BakeMesh(mesh); return mesh.vertices; } finally { UnityEngine.Object.DestroyImmediate(mesh); } }
        static Report TestRepresentative(GameObject prefab) {
            var report = new Report { action = "machop_procedural_validated" }; var preview = EditorSceneManager.NewPreviewScene();
            GameObject root = null, floor = null; Texture2D poseSheet = null;
            try {
                root = new GameObject("Isolated imported-skin locomotion validation"); SceneManager.MoveGameObjectToScene(root, preview); root.transform.position = new Vector3(300, 0, 300);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview); model.transform.SetParent(root.transform, false);
                var animator = root.AddComponent<ProceduralBodyAnimator>(); var rig = BindMachop(model.transform); var pose = PoseOf(model.transform);
                foreach (var a in model.GetComponentsInChildren<Animator>(true)) Check(report, !a.enabled, "ordinary Animator disabled");
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.hideFlags = HideFlags.HideAndDontSave; floor.layer = 8;
                floor.transform.position = root.transform.position + Vector3.down * .5f; floor.transform.localScale = new Vector3(40, 1, 40);
                // Only this hidden temporary floor enters the default physics world. Tested actors
                // remain isolated in preview at a distant location, away from live gameplay.
                Physics.SyncTransforms(); animator.Bind(rig); animator.Tick(.02f, 0, false);
                var skin = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).First();
                report.measuredLegLength = rig.legs[0].length; report.leftSoleHeight = rig.legs[0].soleHeight; report.rightSoleHeight = rig.legs[1].soleHeight;
                animator.ResetState();
                poseSheet = new Texture2D(1024, 512, TextureFormat.RGB24, false); CapturePose(model.transform, poseSheet, 0);
                var restVertices = Bake(skin); var boneRotations = rig.legs.SelectMany(l => new[] { l.upper, l.lower, l.foot }).Select(t => t.localRotation).ToArray();
                animator.Tick(.02f, 0, false); float phase = animator.Driver.phase;
                for (int frame = 1; frame <= 120; frame++) {
                    root.transform.position += Vector3.forward * .04f; animator.Tick(.02f, frame * .02f, false);
                    Check(report, animator.Driver.distance > 0 && animator.Driver.speed > 1.9f && !animator.Driver.teleported, "real displacement drives gait");
                    foreach (var t in model.GetComponentsInChildren<Transform>(true)) Check(report, Finite(t.localPosition) && Finite(t.localScale) && Finite(new Vector3(t.localRotation.x, t.localRotation.y, t.localRotation.z)) && !float.IsNaN(t.localRotation.w), "finite skin skeleton");
                    var bones = rig.legs.SelectMany(l => new[] { l.upper, l.lower, l.foot }).ToArray();
                    if (bones.Where((b, i) => Quaternion.Angle(b.localRotation, boneRotations[i]) > 1).Any()) report.changedBoneFrames++;
                    if (frame == 30) CapturePose(model.transform, poseSheet, 512);
                    var vertices = Bake(skin); for (int v = 0; v < vertices.Length; v++) report.maxSkinVertexChange = Mathf.Max(report.maxSkinVertexChange, (vertices[v] - restVertices[v]).magnitude);
                    foreach (var leg in rig.legs) if (leg.planted) {
                        RaycastHit hit; if (animator.GroundAt(leg.foot.position, out hit)) {
                            float error = Mathf.Abs(leg.foot.position.y - hit.point.y - leg.sole);
                            if (error > report.maxFootClearanceError) { report.maxFootClearanceError = error; report.contactDiagnostic = "frame=" + frame + " hip=" + leg.upper.position + " foot=" + leg.foot.position + " plant=" + leg.plant + " length=" + leg.length + " sole=" + leg.sole; }
                        }
                    }
                }
                Check(report, animator.Driver.phase != phase && report.changedBoneFrames > 100, "phase and leg bone rotations change");
                Check(report, report.maxSkinVertexChange > .005f, "baked skin vertices deform, not just visual root");
                Check(report, report.maxFootClearanceError < .18f, "reasonable ankle-ground contact: " + JsonUtility.ToJson(report));
                float blockedPhase = animator.Driver.phase; for (int i = 121; i < 141; i++) animator.Tick(.02f, i * .02f, false);
                Check(report, Mathf.Abs(Mathf.DeltaAngle(blockedPhase * Mathf.Rad2Deg, animator.Driver.phase * Mathf.Rad2Deg)) < .01f, "blocked displacement does not advance gait");
                animator.Tick(.02f, 3, true); AssertPose(pose); Check(report, animator.Suppressed, "suppression restores imported rest pose");
                var restored = Bake(skin); Check(report, restored.Length == restVertices.Length && restored.Where((v, i) => (v - restVertices[i]).magnitude > .0001f).Count() == 0, "skin returns to original bind presentation");
                root.transform.position += Vector3.right * 100; animator.Tick(.02f, 3.02f, false); Check(report, !animator.Driver.teleported && animator.Driver.distance == 0, "resume/teleport does not animate discontinuity");
                poseSheet.Apply(); Directory.CreateDirectory(EvidenceFolder); report.posePreview = Path.Combine(EvidenceFolder, "pokemon-machop-rest-stride.png"); File.WriteAllBytes(report.posePreview, poseSheet.EncodeToPNG());
                report.measuredLegLength = rig.legs[0].length; report.leftSoleHeight = rig.legs[0].soleHeight; report.rightSoleHeight = rig.legs[1].soleHeight;
                report.verification = "Native isolated imported Machop: measured two-bone IK, actual bone/skin deformation from real displacement, blocked phase, finite pose, calibrated contact, exact rest restoration and resume rejection passed.";
                return report;
            } finally { if (poseSheet) UnityEngine.Object.DestroyImmediate(poseSheet); if (root) UnityEngine.Object.DestroyImmediate(root); if (floor) UnityEngine.Object.DestroyImmediate(floor); EditorSceneManager.ClosePreviewScene(preview); Physics.SyncTransforms(); }
        }
        static void CapturePose(Transform model, Texture2D sheet, int x) {
            var utility = new PreviewRenderUtility(); Texture2D picture = null;
            try {
                var copy = UnityEngine.Object.Instantiate(model.gameObject); copy.transform.SetParent(null);
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); utility.AddSingleGO(copy);
                var bounds = BoundsOf(copy.transform); float radius = bounds.size.magnitude * .5f;
                utility.camera.fieldOfView = 35; utility.camera.nearClipPlane = .01f; utility.camera.farClipPlane = 100;
                utility.camera.transform.position = bounds.center + new Vector3(.8f, .2f, 1).normalized * (radius / Mathf.Sin(17.5f * Mathf.Deg2Rad) * 1.2f);
                utility.camera.transform.LookAt(bounds.center); utility.camera.backgroundColor = new Color(.19f, .23f, .27f);
                utility.camera.clearFlags = CameraClearFlags.SolidColor; utility.lights[0].intensity = 1.2f;
                utility.lights[0].transform.rotation = Quaternion.Euler(30, 35, 0);
                utility.BeginStaticPreview(new Rect(0, 0, 512, 512)); utility.Render(); picture = utility.EndStaticPreview();
                sheet.SetPixels(x, 0, 512, 512, picture.GetPixels());
            } finally { if (picture) UnityEngine.Object.DestroyImmediate(picture); utility.Cleanup(); }
        }
        [MenuItem("Wildbound/Restore Original Creature Visuals")]
        public static void RestoreOriginalMenu() {
            RequireIdle(); var scene = TargetScene();
            if (scene.isDirty) throw new InvalidOperationException("Save unrelated Valley edits before restoring original visuals.");
            var creatures = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Creature>(true)).ToArray();
            CheckReplacements(creatures);
            foreach (var c in creatures) RestoreOriginal(c);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Restored visual scene save failed.");
            Debug.Log("Original creature visuals restored; gameplay fields and gallery preserved.");
        }
        static void RestoreOriginal(Creature c) {
            var marker = c.GetComponent<PokemonCreatureVisualOverride>(); var animator = c.GetComponent<ProceduralBodyAnimator>();
            var replacement = marker.replacementModel.gameObject; animator.ResetState();
            c.model = marker.originalModel; c.model.gameObject.SetActive(true); marker.originalRig.profile = marker.originalProfile;
            c.BindRuntime(); animator.Bind(marker.originalRig, c);
            UnityEngine.Object.DestroyImmediate(replacement); UnityEngine.Object.DestroyImmediate(marker);
        }
        public static string Replace() {
            RequireIdle(); var scene = TargetScene(); if (scene.isDirty) throw new InvalidOperationException("Save unrelated Valley edits before replacing creature visuals; nothing was changed.");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Representative); if (!prefab) throw new InvalidOperationException("Export the model prefabs first.");
            var report = TestRepresentative(prefab);
            var creatures = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Creature>(true)).ToArray();
            if (creatures.Length != 9) throw new InvalidOperationException("Expected the original nine authored creatures.");
            if (creatures.All(c => c.GetComponent<PokemonCreatureVisualOverride>())) {
                CheckReplacements(creatures); report.action = "existing_visual_replacements_preserved"; report.replacements = 9; report.verification += " " + VerifySaved(); return Publish(report, "pokemon151-moving-report.json");
            }
            if (creatures.Any(c => c.GetComponent<PokemonCreatureVisualOverride>())) throw new InvalidOperationException("Partial visual replacement exists; no actor was overwritten.");
            var galleryPose = PoseOf(scene.GetRootGameObjects().First(r => r.GetComponent<PokemonGallery>()).transform);
            var actors = creatures.Select(c => new Actor { c = c, model = c.model, rig = c.GetComponent<ProceduralBodyAnimator>().SavedRig, profile = c.GetComponent<ProceduralBodyAnimator>().profile,
                position = c.transform.position, rotation = c.transform.rotation, scale = c.transform.localScale, species = c.species, health = c.health, ally = c.ally, record = c.record }).ToArray();
            var added = new List<PokemonCreatureVisualOverride>();
            try {
                foreach (var actor in actors) {
                    var c = actor.c; var animator = c.GetComponent<ProceduralBodyAnimator>(); animator.ResetState();
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); model.transform.SetParent(c.transform, false);
                    model.name = "Procedural imported model / 066 Machop";
                    var rig = BindMachop(model.transform); var marker = c.gameObject.AddComponent<PokemonCreatureVisualOverride>(); added.Add(marker);
                    marker.originalModel = actor.model; marker.originalRig = actor.rig; marker.originalProfile = actor.profile;
                    marker.modelPrefab = prefab; marker.replacementModel = model.transform;
                    actor.model.gameObject.SetActive(false); c.model = model.transform; c.BindRuntime(); animator.Bind(rig, c);
                }
                foreach (var a in actors) if (a.c.species != a.species || a.c.health != a.health || a.c.ally != a.ally || a.c.record != a.record || a.c.transform.position != a.position || a.c.transform.rotation != a.rotation || a.c.transform.localScale != a.scale)
                    throw new InvalidOperationException("A gameplay actor field/root changed during visual replacement.");
                AssertPose(galleryPose); CheckReplacements(creatures); report.replacements = 9;
                report.originalGameplayPreserved = report.galleryPreserved = true;
                EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Replacement scene save failed.");
                report.saved = true; report.action = "moving_visuals_replaced_and_saved"; report.scene = scene.path;
                report.verification += " " + VerifySaved();
                var view = SceneView.lastActiveSceneView; if (view) view.Frame(new Bounds(creatures[0].transform.position + Vector3.up, Vector3.one * 10), false);
                Selection.activeGameObject = creatures[0].gameObject; SceneView.RepaintAll();
                return Publish(report, "pokemon151-moving-report.json");
            } catch {
                foreach (var marker in added) {
                    var c = marker.GetComponent<Creature>(); c.GetComponent<ProceduralBodyAnimator>().ResetState();
                    if (marker.replacementModel) UnityEngine.Object.DestroyImmediate(marker.replacementModel.gameObject);
                    c.model = marker.originalModel; c.model.gameObject.SetActive(true); c.BindRuntime(); c.GetComponent<ProceduralBodyAnimator>().Bind(marker.originalRig, c); UnityEngine.Object.DestroyImmediate(marker);
                }
                throw;
            }
        }
        static void CheckReplacements(Creature[] creatures) {
            foreach (var c in creatures) {
                var marker = c.GetComponent<PokemonCreatureVisualOverride>(); var animator = c.GetComponent<ProceduralBodyAnimator>();
                if (!marker || marker.visualDex != 66 || !marker.originalModel || marker.originalModel.gameObject.activeSelf || marker.originalRig == null || c.model != marker.replacementModel || animator.SavedRig.model != c.model || animator.SavedRig.legs.Length != 2)
                    throw new InvalidOperationException("Imported visual/rollback/serialized rig binding is incomplete.");
            }
        }
        static string VerifySaved() {
            var preview = EditorSceneManager.OpenPreviewScene(EditableSceneAuthoring.ScenePath);
            try {
                var creatures = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Creature>(true)).ToArray(); CheckReplacements(creatures);
                var poses = creatures.Select(c => PoseOf(c.model)).ToArray(); var roots = creatures.Select(c => c.transform.position).ToArray();
                var game = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Game>(true)).Single();
                if (!game.InitializeWorld() || !game.InitializeWorld()) throw new InvalidOperationException("Saved authored startup did not reuse its world.");
                game.Load(false); CheckReplacements(creatures);
                for (int i = 0; i < creatures.Length; i++) { AssertPose(poses[i]); if (creatures[i].transform.position != roots[i]) throw new InvalidOperationException("Saved startup reset a creature root."); }
                foreach (var c in creatures) RestoreOriginal(c);
                if (!game.InitializeWorld()) throw new InvalidOperationException("Preview rollback lost authored startup.");
                foreach (var c in creatures) if (c.GetComponent<PokemonCreatureVisualOverride>() || !c.model.gameObject.activeSelf || c.GetComponent<ProceduralBodyAnimator>().SavedRig.model != c.model)
                    throw new InvalidOperationException("Preview rollback did not restore the original model and procedural rig.");
                return "Saved native preview reload and repeated authored startup preserved all nine imported visual/rig/rollback references and actor positions; original visual rollback also passed in preview.";
            } finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        public static string Inspect() {
            RequireIdle(); var scene = TargetScene(); var entries = Entries(scene); int count = 0;
            foreach (var e in entries) { var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(e)); ValidatePrefab(prefab, e); count++; }
            var creatures = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Creature>(true)).ToArray(); CheckReplacements(creatures);
            return Publish(new Report { action = "prefabs_and_moving_visuals_inspected", scene = scene.path, prefabs = count, replacements = creatures.Length, saved = !scene.isDirty,
                verification = "151 reusable static prefabs; nine Machop visual substitutes with serialized two-bone procedural rigs and disabled original visuals retained for rollback." }, "pokemon151-prefab-inspection.json");
        }
        static string Publish(Report report, string filename) { Directory.CreateDirectory(EvidenceFolder); var json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(EvidenceFolder, filename), json); return json; }
    }
}
