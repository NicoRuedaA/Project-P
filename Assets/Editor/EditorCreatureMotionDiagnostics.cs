using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Pokemon3D.Locomotion;

namespace Pokemon3D.Editor
{
    // Fixed read-only scene inspection: no binding, ticking, authoring or Play-mode changes.
    public static class EditorCreatureMotionDiagnostics
    {
        static string PathOf(Transform t) => t ? (t.parent ? PathOf(t.parent) + "/" : "") + t.name : "<null>";
        static bool Inside(Transform t, Transform model) => t && model && (t == model || t.IsChildOf(model));
        static Dictionary<EntityId, string> Snapshot(Scene scene) {
            var entries = new Dictionary<EntityId, string>();
            foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                entries[t.gameObject.GetEntityId()] = t.gameObject.name + "|" + t.gameObject.activeSelf + "|" + t.gameObject.layer;
                foreach (var c in t.GetComponents<Component>()) if (c) entries[c.GetEntityId()] = EditorJsonUtility.ToJson(c);
            }
            return entries;
        }
        static int WeightedVertices(Transform bone, Transform model) {
            if (!bone || !model) return 0; int count = 0;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                if (!skin.enabled || !skin.gameObject.activeInHierarchy || !skin.sharedMesh) continue;
                var bones = skin.bones;
                foreach (var w in skin.sharedMesh.boneWeights) {
                    bool Own(int i, float weight) => weight > .01f && i >= 0 && i < bones.Length && Inside(bones[i], bone);
                    if (Own(w.boneIndex0,w.weight0) || Own(w.boneIndex1,w.weight1) || Own(w.boneIndex2,w.weight2) || Own(w.boneIndex3,w.weight3)) count++;
                }
            }
            return count;
        }
        public static string Inspect() {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Wait until editor compilation/import completes.");
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/Valley.unity");
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Loaded Valley required; no scene will be reopened.");
            var before = Snapshot(scene); bool dirty = scene.isDirty; var text = new StringBuilder();
            text.AppendLine("READ ONLY; playing=" + EditorApplication.isPlaying + "; dirty=" + dirty + "; selected=" + PathOf(Selection.activeTransform));
            var roots = scene.GetRootGameObjects(); var actors = roots.SelectMany(r => r.GetComponentsInChildren<Creature>(true)).ToArray();
            if (actors.Length > 100) throw new InvalidOperationException("Diagnostic limit:100 creatures.");
            var games = roots.SelectMany(r => r.GetComponentsInChildren<Game>(true)).ToArray();
            text.AppendLine("Game count=" + games.Length + "; Game.Instance=" + (Game.Instance ? PathOf(Game.Instance.transform) : "<null>") + "; timeScale=" + Time.timeScale);
            foreach (var game in games) text.AppendLine("Game=" + PathOf(game.transform) + "; enabled=" + game.enabled + "; paused=" + game.paused + "; authored=" + (game.GetComponent<AuthoredWorld>() != null));
            foreach (var root in roots) foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.GetComponents<Component>().Any(c => !c)) text.AppendLine("MISSING SCRIPT: " + PathOf(t));
            foreach (var c in actors) {
                text.AppendLine("ACTOR=" + PathOf(c.transform) + "; enabled=" + c.enabled + "; active=" + c.gameObject.activeInHierarchy + "; species=" + c.species + "; ally=" + c.ally + "; dead=" + c.dead + "; capturing=" + c.capturing + "; health=" + c.health + "; stunUntil=" + c.stunUntil + "; now=" + Time.time + "; position=" + c.transform.position);
                text.AppendLine("Creature.Model=" + PathOf(c.model) + "; active=" + (c.model && c.model.gameObject.activeInHierarchy));
                var cc = c.GetComponent<CharacterController>(); text.AppendLine("CharacterController=" + (cc ? "enabled=" + cc.enabled + "; grounded=" + cc.isGrounded + "; velocity=" + cc.velocity + "; height=" + cc.height + "; radius=" + cc.radius : "<missing>"));
                var a = c.GetComponent<ProceduralBodyAnimator>();
                if (!a) { text.AppendLine("ROOT PROCEDURAL ANIMATOR MISSING"); continue; }
                text.AppendLine("Animator enabled=" + a.enabled + "; RuntimeRig=" + (a.Rig != null) + "; suppressed=" + a.IsMotionSuppressed + "; driver speed=" + a.Driver.speed + "; distance=" + a.Driver.distance + "; phase=" + a.Driver.phase + "; traversal=" + a.traversalMode + "; terrainMask=" + a.terrainMask.value);
                text.AppendLine("Top-level Profile=" + JsonUtility.ToJson(a.profile));
                var rig = a.SavedRig;
                if (rig == null) { text.AppendLine("SAVED RIG MISSING"); continue; }
                text.AppendLine("SavedRig.Model=" + PathOf(rig.model) + "; same Creature.Model=" + (rig.model == c.model) + "; active=" + (rig.model && rig.model.gameObject.activeInHierarchy) + "; waist=" + PathOf(rig.waist) + "; spine=" + PathOf(rig.spine) + "; head=" + PathOf(rig.head));
                text.AppendLine("SavedRig.Profile=" + JsonUtility.ToJson(rig.profile) + "; legs=" + rig.legs.Length + "; arms=" + rig.arms.Length + "; FourLegged=" + rig.FourLegged);
                for (int i=0;i<rig.legs.Length;i++) {
                    var l=rig.legs[i]; float length=l.upper && l.lower && l.foot ? Vector3.Distance(l.upper.position,l.lower.position)+Vector3.Distance(l.lower.position,l.foot.position):0;
                    text.AppendLine("Leg["+i+"] right="+l.right+"; front="+l.front+"; set="+l.set+"; upper="+PathOf(l.upper)+"; lower="+PathOf(l.lower)+"; foot="+PathOf(l.foot)+"; measuredWorldLength="+length+"; allInsideModel="+(Inside(l.upper,rig.model)&&Inside(l.lower,rig.model)&&Inside(l.foot,rig.model))+"; weightedVisibleVertices upper/lower/foot="+WeightedVertices(l.upper,rig.model)+"/"+WeightedVertices(l.lower,rig.model)+"/"+WeightedVertices(l.foot,rig.model));
                }
                foreach (var animator in c.GetComponentsInChildren<Animator>(true)) text.AppendLine("Clip Animator="+PathOf(animator.transform)+"; enabled="+animator.enabled+"; active="+animator.gameObject.activeInHierarchy+"; controller="+(animator.runtimeAnimatorController ? animator.runtimeAnimatorController.name:"<null>"));
                if(c.model) foreach(var skin in c.model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) text.AppendLine("Skin="+PathOf(skin.transform)+"; enabled="+skin.enabled+"; active="+skin.gameObject.activeInHierarchy+"; mesh="+(skin.sharedMesh ? skin.sharedMesh.name:"<null>")+"; bones="+skin.bones.Length);
            }
            var after = Snapshot(scene);
            if (dirty != scene.isDirty || before.Count != after.Count || before.Any(p => !after.TryGetValue(p.Key,out var value) || value != p.Value)) throw new InvalidOperationException("Unexpected scene change during read-only inspection.");
            text.AppendLine("SNAPSHOT UNCHANGED entries=" + before.Count + "; dirty=" + scene.isDirty + "; no Bind/Tick/save/reopen performed.");
            return text.ToString();
        }
    }
}
