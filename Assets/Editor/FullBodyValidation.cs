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
    public static class FullBodyValidation
    {
        static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LocalValidationBridge"));
        [Serializable] sealed class Report { public int checks; public string verification, preview; public RigReport[] rigs; }
        [Serializable] sealed class RigReport {
            public string name; public Region[] regions; public int oppositeArmFrames, movingFrames;
            public float elbowAngleRange, handForwardRange, maxContactError; public string[] modes;
        }
        [Serializable] sealed class Region { public string name; public int vertices; public float maxChange; }
        sealed class Skin { public SkinnedMeshRenderer renderer; public BoneWeight[] weights; public Transform[] bones; public int[] regions; public Vector3[] baseline; }
        static void Check(Report r, bool condition, string message) { r.checks++; if (!condition) throw new InvalidOperationException("Full-body validation: " + message); }
        static float Elbow(JointChain arm) => Vector3.Angle(arm.joints[1].position - arm.joints[0].position, arm.joints[2].position - arm.joints[1].position);
        static bool Under(Transform bone, Transform owner) => owner && (bone == owner || bone.IsChildOf(owner));
        static int RegionOf(Transform bone, BodyRig rig) {
            if (Under(bone, rig.head)) return 1;
            for (int side = 0; side < 2; side++) {
                var arm = rig.arms.Single(a => a.right == (side == 1));
                if (Under(bone, arm.joints[2])) return 2 + side * 3 + 2;
                if (Under(bone, arm.joints[1])) return 2 + side * 3 + 1;
                if (Under(bone, arm.joints[0]) || arm.restJoints.Any(t => t && t != arm.joints[2] && Under(bone, t))) return 2 + side * 3;
            }
            return Under(bone, rig.spine) ? 0 : -1;
        }
        static Transform Anchor(int region, BodyRig rig) => region == 0 ? rig.waist : region == 1 ? rig.head.parent : rig.arms.Single(a => a.right == (region >= 5)).joints[0].parent;
        static Skin[] Skins(BodyRig rig, RigReport result) {
            var skins = new List<Skin>();
            foreach (var renderer in rig.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled)) {
                var weights = renderer.sharedMesh.boneWeights; var bones = renderer.bones; var regions = new int[weights.Length];
                for (int i = 0; i < weights.Length; i++) {
                    var w = weights[i]; int index = w.boneIndex0; float strength = w.weight0;
                    if (w.weight1 > strength) { index = w.boneIndex1; strength = w.weight1; }
                    if (w.weight2 > strength) { index = w.boneIndex2; strength = w.weight2; }
                    if (w.weight3 > strength) { index = w.boneIndex3; strength = w.weight3; }
                    regions[i] = strength > 0 ? RegionOf(bones[index], rig) : -1;
                    if (regions[i] >= 0) result.regions[regions[i]].vertices++;
                }
                skins.Add(new Skin { renderer = renderer, weights = weights, bones = bones, regions = regions });
            }
            return skins.ToArray();
        }
        static Vector3[] Bake(Skin skin, BodyRig rig) {
            var mesh = new Mesh(); try {
                skin.renderer.BakeMesh(mesh); var vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++) if (skin.regions[i] >= 0)
                    vertices[i] = Anchor(skin.regions[i], rig).InverseTransformPoint(skin.renderer.transform.TransformPoint(vertices[i]));
                return vertices;
            } finally { UnityEngine.Object.DestroyImmediate(mesh); }
        }
        static void Capture(BodyRig rig, Texture2D sheet, int column, int row) {
            var utility = new PreviewRenderUtility(); Texture2D picture = null;
            try {
                var model = UnityEngine.Object.Instantiate(rig.model.gameObject); model.transform.SetParent(null); model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); utility.AddSingleGO(model);
                var renderers = model.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray(); var b = renderers[0].bounds; foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                float radius = b.size.magnitude * .5f;
                utility.camera.fieldOfView = 35; utility.camera.nearClipPlane = .01f; utility.camera.farClipPlane = 100;
                utility.camera.transform.position = b.center + new Vector3(.35f, .08f, 1).normalized * radius / Mathf.Sin(17.5f * Mathf.Deg2Rad) * 1.12f;
                utility.camera.transform.LookAt(b.center); utility.camera.backgroundColor = new Color(.24f, .29f, .33f); utility.camera.clearFlags = CameraClearFlags.SolidColor;
                utility.lights[0].intensity = 1.8f; utility.lights[0].transform.rotation = Quaternion.Euler(25, 200, 0);
                utility.lights[1].intensity = 1.1f; utility.lights[1].transform.rotation = Quaternion.Euler(30, 35, 0);
                utility.ambientColor = new Color(.5f, .5f, .5f);
                utility.BeginStaticPreview(new Rect(0, 0, 512, 512)); utility.Render(); picture = utility.EndStaticPreview();
                sheet.SetPixels(column * 512, row * 512, 512, 512, picture.GetPixels());
            } finally { if (picture) UnityEngine.Object.DestroyImmediate(picture); utility.Cleanup(); }
        }
        static void PoseParity(Report report) {
            float expected = 1f - Mathf.Exp(-12f * .02f);
            var motion = new GaitDriver { speed = 3f, vertical = 0 };
            var pose = new BodyPoseDriver(); pose.Reset(); pose.Advance(motion, .02f, true, TraversalMode.Ground, false, false, false);
            Check(report, pose.mode == BodyPoseMode.Walk && Mathf.Abs(pose[BodyPoseMode.Walk] - expected) < .000001f && Mathf.Abs(pose[BodyPoseMode.Idle] - (1 - expected)) < .000001f, "advancePose source walk damping vector");
            motion.speed = 8; pose.Reset(); pose.Advance(motion, .02f, true, TraversalMode.Ground, false, false, false);
            Check(report, pose.mode == BodyPoseMode.Run && Mathf.Abs(pose[BodyPoseMode.Run] - expected) < .000001f, "advancePose source run damping vector");
            motion.vertical = 1; pose.Reset(); pose.Advance(motion, .02f, false, TraversalMode.Ground, false, false, false); Check(report, pose.mode == BodyPoseMode.Jump, "advancePose jump threshold");
            motion.vertical = -.5f; pose.Reset(); pose.Advance(motion, .02f, false, TraversalMode.Ground, false, false, false); Check(report, pose.mode == BodyPoseMode.Fall, "advancePose fall threshold");
            pose.Advance(motion, .02f, true, TraversalMode.Ground, false, false, true); Check(report, pose.mode == BodyPoseMode.Dodge, "advancePose dodge precedence");
            pose.Advance(motion, .02f, true, TraversalMode.Swim, false, false, false); Check(report, pose.mode == BodyPoseMode.Swim, "advancePose swimming precedence");
            pose.Advance(motion, .02f, true, TraversalMode.Climb, false, false, false); Check(report, pose.mode == BodyPoseMode.Climb, "advancePose climbing precedence");
            motion.vertical = -2; pose.Advance(motion, .02f, false, TraversalMode.Flight, false, false, false); Check(report, pose.mode == BodyPoseMode.Glide, "advancePose glide descent threshold");
            motion.vertical = 0; motion.speed = 2; pose.Advance(motion, .02f, false, TraversalMode.Flight, false, false, false); Check(report, pose.mode == BodyPoseMode.Flight, "advancePose normal flight");
        }
        public static string Run() {
            FullBodyAuthoring.RequireIdle(); var live = FullBodyAuthoring.Scene(); var snapshot = FullBodyAuthoring.Snapshot(live);
            var report = new Report(); PoseParity(report); var results = new List<RigReport>(); var preview = EditorSceneManager.NewPreviewScene();
            GameObject floor = null; var sheet = new Texture2D(2048, 1024, TextureFormat.RGB24, false);
            try {
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.hideFlags = HideFlags.HideAndDontSave; floor.layer = 8;
                floor.transform.position = new Vector3(300, -.5f, 300); floor.transform.localScale = new Vector3(100, 1, 100); Physics.SyncTransforms();
                for (int specimen = 0; specimen < 2; specimen++) {
                    var actor = new GameObject("Isolated full-body specimen"); SceneManager.MoveGameObjectToScene(actor, preview); actor.transform.position = new Vector3(300, 0, 300);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(specimen == 0 ? "Assets/Prefabs/Pokemon/066-machop.prefab" : "Assets/Prefabs/Trainer/PokemonTrainer.prefab");
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, preview); model.transform.SetParent(actor.transform, false);
                    var rig = specimen == 0 ? PokemonPrefabAuthoring.BindMachop(model.transform) : FullBodyAuthoring.BindTrainer(model.transform);
                    var animator = actor.AddComponent<ProceduralBodyAnimator>(); animator.Bind(rig);
                    var state = model.GetComponentsInChildren<Transform>(true).Select(t => (t, t.localPosition, t.localRotation, t.localScale)).ToArray();
                    var result = new RigReport { name = specimen == 0 ? "Machop" : "Trainer", regions = new[] { "Torso", "Head", "LeftUpperArm", "LeftForearm", "LeftHand", "RightUpperArm", "RightForearm", "RightHand" }.Select(n => new Region { name = n }).ToArray() };
                    var skins = Skins(rig, result); foreach (var region in result.regions) Check(report, region.vertices > 4, result.name + " region has weighted skin: " + region.name);
                    float clock = 0;
                    for (int i = 0; i < 60; i++) { clock += .02f; animator.Tick(.02f, clock, false); }
                    foreach (var skin in skins) skin.baseline = Bake(skin, rig); Capture(rig, sheet, 0, specimen);
                    var seen = new HashSet<string> { animator.Pose.mode.ToString() };
                    float elbowMin = 180, elbowMax = 0, handMin = float.PositiveInfinity, handMax = float.NegativeInfinity;
                    for (int stage = 0; stage < 6; stage++) {
                        for (int frame = 0; frame < 50; frame++) {
                            if (stage == 0) actor.transform.position += actor.transform.forward * .06f;
                            if (stage == 1) actor.transform.position += actor.transform.forward * .16f;
                            if (stage == 2) { actor.transform.rotation *= Quaternion.Euler(0, 1.2f, 0); actor.transform.position += actor.transform.forward * .06f; }
                            if (stage == 3) { actor.transform.position += actor.transform.forward * .04f; actor.transform.position += Vector3.up * (frame < 25 ? .03f : -.03f); }
                            if (stage == 4) { actor.transform.position = new Vector3(actor.transform.position.x, 0, actor.transform.position.z); }
                            animator.dodging = stage == 5; if (stage == 5) actor.transform.position += actor.transform.forward * .2f;
                            clock += .02f; animator.Tick(.02f, clock, false); seen.Add(animator.Pose.mode.ToString());
                            foreach (var t in model.GetComponentsInChildren<Transform>(true)) Check(report, float.IsFinite(t.localPosition.x) && float.IsFinite(t.localPosition.y) && float.IsFinite(t.localPosition.z) && float.IsFinite(t.localRotation.x) && float.IsFinite(t.localRotation.y) && float.IsFinite(t.localRotation.z) && float.IsFinite(t.localRotation.w), result.name + " finite skeleton");
                            foreach (var skin in skins) {
                                var vertices = Bake(skin, rig);
                                for (int i = 0; i < vertices.Length; i++) if (skin.regions[i] >= 0) {
                                    float change = (vertices[i] - skin.baseline[i]).magnitude;
                                    result.regions[skin.regions[i]].maxChange = Mathf.Max(result.regions[skin.regions[i]].maxChange, change);
                                }
                            }
                            var left = rig.arms.Single(a => !a.right); var right = rig.arms.Single(a => a.right);
                            if (stage <= 2) {
                                var l = rig.model.InverseTransformDirection(left.joints[1].position - left.joints[0].position).normalized;
                                var r = rig.model.InverseTransformDirection(right.joints[1].position - right.joints[0].position).normalized;
                                Check(report, l.y < -.65f && r.y < -.65f, result.name + " relaxed upper arms, not T-pose");
                                if (l.z * r.z < 0) result.oppositeArmFrames++;
                                result.movingFrames++;
                                float elbow = Elbow(left); elbowMin = Mathf.Min(elbowMin, elbow); elbowMax = Mathf.Max(elbowMax, elbow);
                                float hand = rig.model.InverseTransformPoint(left.joints[2].position).z; handMin = Mathf.Min(handMin, hand); handMax = Mathf.Max(handMax, hand);
                            }
                            foreach (var leg in rig.legs) if (leg.planted && animator.GroundAt(leg.foot.position, out var hit)) result.maxContactError = Mathf.Max(result.maxContactError, Mathf.Abs(leg.foot.position.y - hit.point.y - leg.sole));
                        }
                        if (stage <= 2) Capture(rig, sheet, stage + 1, specimen);
                    }
                    result.elbowAngleRange = elbowMax - elbowMin; result.handForwardRange = handMax - handMin; result.modes = seen.OrderBy(n => n).ToArray();
                    foreach (var region in result.regions) Check(report, region.maxChange > .001f, result.name + " isolated region deforms: " + region.name);
                    Check(report, result.oppositeArmFrames > 40, result.name + " opposite arm pendulum");
                    Check(report, result.elbowAngleRange > 5, result.name + " elbow independently flexes"); Check(report, result.handForwardRange > .05f, result.name + " hands swing with arms");
                    Check(report, result.maxContactError < .08f, result.name + " floor contact preserved");
                    foreach (var mode in new[] { "Idle", "Walk", "Run", "Jump", "Fall", "Land", "Dodge" }) Check(report, seen.Contains(mode), result.name + " measured motion selects " + mode);
                    animator.dodging = false; animator.Tick(.02f, clock + .02f, false); float blocked = animator.Driver.phase;
                    for (int i = 2; i < 35; i++) animator.Tick(.02f, clock + i * .02f, false);
                    Check(report, Mathf.Abs(animator.Driver.phase - blocked) < .0001f, result.name + " blocked gait does not advance");
                    animator.Tick(.02f, clock + 1, true);
                    foreach (var item in state) Check(report, item.t.localPosition == item.localPosition && Quaternion.Angle(item.t.localRotation, item.localRotation) < .001f && item.t.localScale == item.localScale, result.name + " exact rest restoration");
                    actor.transform.position += Vector3.right * 100; animator.Tick(.02f, clock + 1.02f, false); Check(report, animator.Driver.distance == 0, result.name + " resume rejects displacement discontinuity");
                    results.Add(result); UnityEngine.Object.DestroyImmediate(actor);
                }
                report.rigs = results.ToArray(); sheet.Apply(); report.preview = Path.Combine(Evidence, "fullbody-machop-trainer-poses.png"); File.WriteAllBytes(report.preview, sheet.EncodeToPNG());
                FullBodyAuthoring.AssertSnapshot(snapshot, FullBodyAuthoring.Snapshot(live));
                report.verification = "Weighted regions measured in torso/shoulder/head parent frames to exclude actor/torso rigid motion; finite source-derived arms, elbow flexion, contraphase, traversal modes, contacts, exact rest and blocked/resume passed; live user scene unchanged.";
                var json = JsonUtility.ToJson(report, true); File.WriteAllText(Path.Combine(Evidence, "fullbody-regions-report.json"), json); return json;
            } finally { if (floor) UnityEngine.Object.DestroyImmediate(floor); UnityEngine.Object.DestroyImmediate(sheet); EditorSceneManager.ClosePreviewScene(preview); Physics.SyncTransforms(); }
        }
    }
}
