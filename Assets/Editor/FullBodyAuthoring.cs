using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wildbound.Locomotion;

namespace Wildbound.Editor
{
    public static class FullBodyAuthoring
    {
        static string Evidence => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LocalValidationBridge"));
        public static void RequireIdle() {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Full-body authoring requires idle Edit mode; Play will not be changed.");
        }
        public static Scene Scene() {
            var scene = SceneManager.GetSceneByPath(EditableSceneAuthoring.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded || scene != SceneManager.GetActiveScene()) throw new InvalidOperationException("The fixed Valley must be open and active; no scene will be reopened.");
            return scene;
        }
        public static Dictionary<int, string> Snapshot(Scene scene) {
            var result = new Dictionary<int, string>();
            using (var hash = SHA256.Create()) foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                result[t.gameObject.GetInstanceID()] = t.gameObject.name + "|" + t.gameObject.activeSelf + "|" + t.gameObject.layer + "|" + t.gameObject.tag;
                foreach (var c in t.GetComponents<Component>()) {
                    if (!c) throw new InvalidOperationException("A current object has a missing component; snapshot refused.");
                    result[c.GetInstanceID()] = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(c))));
                }
            }
            return result;
        }
        public static void AssertSnapshot(Dictionary<int, string> before, Dictionary<int, string> after) {
            if (before.Count != after.Count || before.Any(pair => !after.TryGetValue(pair.Key, out var value) || value != pair.Value))
                throw new InvalidOperationException("A current scene object/component/transform changed unexpectedly.");
        }
        public static string SaveCurrent() {
            RequireIdle(); var scene = Scene(); var before = Snapshot(scene);
            Directory.CreateDirectory(Evidence);
            if (File.Exists(Path.Combine(Evidence, "fullbody-user-baseline.unity"))) throw new InvalidOperationException("The consented baseline backup already exists; it will not be overwritten.");
            var snapshotJson = JsonUtility.ToJson(new SnapshotReport { objectsAndComponents = before.Count, entries = before.Select(p => p.Key + ":" + p.Value).ToArray() }, true);
            File.WriteAllText(Path.Combine(Evidence, "fullbody-user-baseline-snapshot.json"), snapshotJson);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Current Valley save failed; no scene was discarded.");
            AssertSnapshot(before, Snapshot(scene));
            File.Copy(EditableSceneAuthoring.ScenePath, Path.Combine(Evidence, "fullbody-user-baseline.unity"));
            File.Copy(EditableSceneAuthoring.ScenePath + ".meta", Path.Combine(Evidence, "fullbody-user-baseline.unity.meta"));
            return "Current Valley and every pending object/component/transform preserved and saved; recoverable fixed baseline backup in Temp/LocalValidationBridge/fullbody-user-baseline.unity. Snapshot entries: " + before.Count;
        }
        [Serializable] sealed class SnapshotReport { public int objectsAndComponents; public string[] entries; }
        static Transform Bone(Transform model, string name) {
            var matches = model.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Expected one weighted trainer bone: " + name); return matches[0];
        }
        public static BodyRig BindTrainer(Transform model) {
            var rig = new BodyRig { model = model, waist = Bone(model, "Hip"), spine = Bone(model, "Bust"), head = Bone(model, "Head"),
                neck = new[] { Bone(model, "Neck") }, profile = BodyMotionProfile.For(BodyArchetype.Biped) };
            rig.profile.displayHeight = 1.8f;
            var renderers = model.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            float floor = renderers.Min(r => r.bounds.min.y), scale = Mathf.Abs(model.lossyScale.y);
            rig.legs = new[] { "L", "R" }.Select(side => {
                var foot = Bone(model, "Foot" + side);
                return new LimbRig { upper = Bone(model, "Leg" + side), lower = Bone(model, "Knee" + side), foot = foot,
                    right = side == "R", soleHeight = Mathf.Max(.001f, (foot.position.y - floor) / scale) };
            }).ToArray();
            rig.arms = new[] { "L", "R" }.Select(side => new JointChain { right = side == "R", spread = .3f,
                joints = new[] { Bone(model, "Shoulder" + side), Bone(model, "Arm" + side), Bone(model, "Hand" + side) },
                restJoints = new[] { Bone(model, "Clavicle" + side), Bone(model, "Hand" + side) } }).ToArray();
            foreach (var leg in rig.legs) if (!leg.lower.IsChildOf(leg.upper) || !leg.foot.IsChildOf(leg.lower) || !leg.upper.IsChildOf(rig.waist))
                throw new InvalidOperationException("Trainer leg hierarchy is incompatible.");
            rig.Measure(); rig.profile.stride = rig.legs.Min(l => l.length) * 1.5f;
            rig.stanceLowering = Mathf.Max(0, rig.legs.Max(l => l.upper.position.y - floor - l.sole - l.length * .78f)) / scale;
            return rig;
        }
        public static string Upgrade() {
            RequireIdle(); var scene = Scene(); if (scene.isDirty) throw new InvalidOperationException("New pending scene edits exist; save them explicitly before applying full-body binding.");
            if (!File.Exists(Path.Combine(Evidence, "fullbody-user-baseline.unity"))) throw new InvalidOperationException("Consented user baseline backup is missing.");
            var creatures = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Creature>(true)).ToArray();
            var player = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerMotor>(true)).Single();
            var trainerMarker = player.GetComponent<Wildbound.Pokemon.TrainerVisualOverride>();
            if (creatures.Length != 9 || !trainerMarker || !trainerMarker.replacementVisual) throw new InvalidOperationException("Expected nine imported actors and the imported trainer.");
            // Validate both imported topologies before changing any live component.
            var creatureRigs = creatures.Select(c => PokemonPrefabAuthoring.BindMachop(c.model)).ToArray();
            var trainerRig = BindTrainer(trainerMarker.replacementVisual);
            var existingAnimator = player.GetComponent<ProceduralBodyAnimator>();
            if (existingAnimator && existingAnimator != trainerMarker.proceduralAnimator) throw new InvalidOperationException("An unrelated player animator exists; not overwritten.");
            var actorPositions = creatures.Select(c => c.transform.position).ToArray();
            var excluded = new HashSet<int>(creatures.Select(c => c.GetComponent<ProceduralBodyAnimator>().GetInstanceID())) { trainerMarker.GetInstanceID() };
            var before = Snapshot(scene).Where(p => !excluded.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            for (int i = 0; i < creatures.Length; i++) creatures[i].GetComponent<ProceduralBodyAnimator>().Bind(creatureRigs[i], creatures[i]);
            var animator = player.GetComponent<ProceduralBodyAnimator>(); if (!animator) animator = player.gameObject.AddComponent<ProceduralBodyAnimator>();
            else if (animator != trainerMarker.proceduralAnimator) throw new InvalidOperationException("An unrelated player animator exists; not overwritten.");
            trainerMarker.proceduralAnimator = animator; animator.Bind(trainerRig); excluded.Add(animator.GetInstanceID());
            AssertSnapshot(before, Snapshot(scene).Where(p => !excluded.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value));
            for (int i = 0; i < creatures.Length; i++) if (creatures[i].transform.position != actorPositions[i]) throw new InvalidOperationException("Actor root changed.");
            EditorSceneManager.MarkSceneDirty(scene); if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Full-body scene save failed.");
            return "Saved full-body Machop arm metadata and shared procedural trainer binding; all other current objects/components/transforms, including pending user content, unchanged.";
        }
        // RED fixture: cannot pass by moving legs or rotating the entire actor.
        public static string ValidateArmRegression() {
            RequireIdle(); var preview = EditorSceneManager.OpenPreviewScene(EditableSceneAuthoring.ScenePath);
            try {
                var creature = preview.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Creature>(true)).First();
                var animator = creature.GetComponent<ProceduralBodyAnimator>(); animator.BindSavedRig();
                animator.Tick(.02f, 0, false);
                for (int i = 1; i <= 60; i++) { creature.transform.position += Vector3.forward * .06f; animator.Tick(.02f, i * .02f, false); }
                foreach (var arm in animator.Rig.arms) {
                    var direction = animator.Rig.model.InverseTransformDirection(arm.joints[1].position - arm.joints[0].position).normalized;
                    if (Vector3.Dot(direction, Vector3.down) < .7f) throw new InvalidOperationException("Full-body regression: moving upper arms do not hang below the shoulder; source geometry-derived relaxed pose is missing.");
                }
                return "Geometry-derived relaxed upper-arm regression passed.";
            } finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
    }
}
