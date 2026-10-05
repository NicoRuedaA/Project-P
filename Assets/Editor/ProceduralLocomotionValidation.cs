using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Pokemon3D.Locomotion;

namespace Pokemon3D.Editor
{
    // Explicit menu/CLI entry point: no tests run automatically on reload or play.
    public static class ProceduralLocomotionValidation
    {
        static int checks;
        static void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException("Locomotion validation: " + message); }
        static void Near(float value, float expected, string message, float tolerance = .00002f) { Check(Mathf.Abs(value - expected) <= tolerance, message + " (" + value + " vs " + expected + ")"); }
        [MenuItem("Wildbound/Validate Procedural Locomotion")]
        public static void RunMenu() { Debug.Log(Run()); }
        // Call with editor script evaluation or a fresh editor -executeMethod RunMenu.
        public static string Run()
        {
            checks = 0; MathParity(); Displacement(); StanceTransitions(); SupportScaling();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Temporary locomotion validation floor"; floor.hideFlags = HideFlags.HideAndDontSave; floor.layer = 8;
            floor.transform.position = new Vector3(10000f, -.5f, 10000f); floor.transform.localScale = new Vector3(40f, 1f, 40f);
            try {
                Physics.SyncTransforms();
                foreach (BodyArchetype type in Enum.GetValues(typeof(BodyArchetype))) Family(type);
                CompositeAnatomy(); Lifecycle();
            }
            finally { UnityEngine.Object.DestroyImmediate(floor); Physics.SyncTransforms(); }
            return "Procedural locomotion: " + checks + " checks passed across 12 body families (scalar parity, IK, displacement, stance, support scaling, restore, lifecycle and composition).";
        }
        static void SupportScaling()
        {
            // Read the private contact helper rather than adding test-only runtime API.
            var bottom = typeof(ProceduralBodyAnimator).GetMethod("Bottom", BindingFlags.Instance | BindingFlags.NonPublic);
            Check(bottom != null, "contact support helper exists");
            foreach (float rootScale in new[] { 1f, 3f })
            {
                var root = new GameObject("Temporary support-scale validation"); root.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    root.transform.localScale = Vector3.one * rootScale;
                    var model = new GameObject("Visual").transform; model.SetParent(root.transform, false); model.localScale = Vector3.one * 2f;
                    var waist = new GameObject("Waist").transform; waist.SetParent(model, false); waist.localPosition = Vector3.up;
                    var skin = GameObject.CreatePrimitive(PrimitiveType.Sphere); skin.transform.SetParent(waist, false);
                    skin.transform.localPosition = Vector3.down * .125f;
                    var renderer = skin.GetComponent<Renderer>();
                    var rig = new BodyRig { model = model, waist = waist, profile = BodyMotionProfile.For(BodyArchetype.Floater) };
                    var animator = root.AddComponent<ProceduralBodyAnimator>(); animator.Bind(rig);
                    float support = (float)bottom.Invoke(animator, new object[] { waist });
                    float measured = waist.position.y - renderer.bounds.min.y;
                    Near(support, measured, "non-unit bind support is world-linear, root scale " + rootScale);
                    Near(support, .625f * 2f * rootScale, "bind support applies scale once, root scale " + rootScale);
                    model.localScale = Vector3.one * (2f * 1.07f);
                    float castSupport = (float)bottom.Invoke(animator, new object[] { waist });
                    Near(castSupport, support * 1.07f, "cast multiplier scales support linearly");
                    Near(castSupport, waist.position.y - renderer.bounds.min.y, "cast support matches renderer bounds");
                    root.transform.localScale *= 1.5f;
                    float grown = (float)bottom.Invoke(animator, new object[] { waist });
                    Near(grown, castSupport * 1.5f, "runtime root scaling scales support linearly");
                    Near(grown, waist.position.y - renderer.bounds.min.y, "runtime root support matches renderer bounds");
                }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
        }
        static void MathParity()
        {
            // Reference vectors evaluated from source gait.js/multileg.js on 2026-10-02.
            Near(LocomotionMath.RelativeSpeed(5f, 1.2f), 2.06091403f, "relativeSpeed source parity");
            Vector3 weights = LocomotionMath.GaitWeights(.65f); Near(weights.x, .5f, "walk blend"); Near(weights.y, .5f, "trot blend"); Near(weights.z, 0f, "gallop blend");
            Near(LocomotionMath.DutyFactor(LocomotionMath.GaitWeights(2f), 2f), .3f, "gallop duty source parity");
            var foot = LocomotionMath.Step(0f, .5f); Near(foot.cycle, .75f, "foot cycle"); Check(!foot.stance, "swing at phase zero"); Near(foot.lift, 1f, "swing apex");
            foot = LocomotionMath.Step(Mathf.PI, .65f, true); Near(foot.sweep, .23076923f, "linear planted sweep"); Check(foot.stance, "walk stance");
            Vector3 offsets = LocomotionMath.Footfalls(true, true); Near(offsets.x, -4.71238898f, "front-right walk"); Near(offsets.y, -6.28318531f, "front-right trot"); Near(offsets.z, -3.76991118f, "front-right gallop");
            offsets = LocomotionMath.WaveFootfalls(true, 2, 3); Near(offsets.x, -7.33038286f, "arthropod wave"); Near(offsets.y, -9.42477796f, "arthropod tripod");
            var hop = LocomotionMath.HopArc(.375f, true); Near(hop.lift, 1f, "bounce apex"); Near(hop.squash, -.07f, "bounce stretch"); Near(hop.pitch, 0f, "bounce apex tilt");
            hop = LocomotionMath.HopArc(.85f, false); Near(hop.squash, .14f, "short-hop landing squash"); Near(hop.lift, 0f, "short-hop landing floor");
            Near(LocomotionMath.Contraction(LocomotionMath.Tau * .3f), 1f, "pulse contraction");
            Near(LocomotionMath.Contraction(0f), 0f, "pulse relaxed");
            var spring = new LocomotionMath.Spring(); spring.Advance(.12f, 10f, .18f, 0f, .1f, .35f);
            Check(spring.value > 0f && spring.value < .35f, "bounded ooze spring");
        }
        static void Displacement()
        {
            var gait = new GaitDriver(); gait.Sample(Vector3.zero, 0f, .02f, 0f, true, true, 1.2f, null);
            gait.Sample(new Vector3(0f, 0f, .06f), .02f, .02f, 0f, true, true, 1.2f, null);
            Near(gait.speed, 3f, "actual displacement speed"); Near(gait.phase, .31415927f, "actual displacement phase");
            float phase = gait.phase;
            gait.Sample(new Vector3(0f, 0f, .06f), .04f, .02f, 0f, true, true, 1.2f, null);
            Near(gait.speed, 0f, "blocked controller is stationary"); Near(gait.phase, phase, "blocked motion cannot advance steps");
            gait.Sample(new Vector3(0f, 0f, .06f), .04f, .01f, 0f, true, true, 1.2f, null); Near(gait.phase, phase, "duplicate timestamp cannot advance");
            gait.Sample(new Vector3(0f, 0f, 10f), .06f, .02f, 0f, true, true, 1.2f, null); Check(gait.teleported, "teleport rejected"); Near(gait.travel, .06f, "teleport not travelled");
            gait.Sample(new Vector3(0f, 0f, 10.1f), .08f, .02f, 0f, false, true, 1.2f, null); Near(gait.weight, 0f, "suppression clears gait"); Near(gait.speed, 0f, "suppression clears speed");
        }
        static void StanceTransitions()
        {
            var gait = new GaitDriver { speed = 3f };
            var patterns = new[] { LocomotionMath.Footfalls(false, false), LocomotionMath.Footfalls(true, false), LocomotionMath.Footfalls(false, true), LocomotionMath.Footfalls(true, true) };
            gait.Settle(1.2f, patterns, float.PositiveInfinity); gait.phase = Mathf.PI;
            float ownOffset = gait.offsets[0], duty = gait.duties[0]; gait.speed = 12f; gait.Settle(1.2f, patterns, .04f);
            Near(gait.offsets[0], ownOffset, "gait change retains planted footfall"); Check(gait.duties[0] <= duty, "planted stance can only shorten during speed-up");
            for (int i = 0; i < 4; i++) Check(gait.duties[i] >= 0f && gait.duties[i] <= 1f, "finite transition duty");
        }
        struct Pose
        {
            public Transform t; public Vector3 p, s; public Quaternion q;
            public Pose(Transform joint) { t = joint; p = joint.localPosition; q = joint.localRotation; s = joint.localScale; }
        }
        static GameObject Fixture(BodyArchetype type, out ProceduralBodyAnimator animator, out BodyRig rig)
        {
            var root = new GameObject("Temporary " + type + " validation"); root.hideFlags = HideFlags.HideAndDontSave; root.transform.position = new Vector3(10000f, 0f, 10000f);
            var model = new GameObject("Visual").transform; model.SetParent(root.transform, false);
            rig = PrimitiveBodyRig.Build(model, 0, BodyMotionProfile.For(type)); animator = root.AddComponent<ProceduralBodyAnimator>(); animator.Bind(rig);
            return root;
        }
        static void Family(BodyArchetype type)
        {
            ProceduralBodyAnimator animator; BodyRig rig; var root = Fixture(type, out animator, out rig);
            try
            {
                var poses = new List<Pose>(); foreach (var t in rig.model.GetComponentsInChildren<Transform>()) if (t != rig.model) poses.Add(new Pose(t));
                if (type == BodyArchetype.Aquatic) animator.traversalMode = TraversalMode.Swim;
                if (type == BodyArchetype.Winged) animator.traversalMode = TraversalMode.Flight;
                animator.Tick(.02f, 0f, false);
                for (int i = 1; i <= 80; i++) { root.transform.position += Vector3.forward * .025f; animator.Tick(.02f, i * .02f, false); }
                bool animated = false;
                foreach (var pose in poses)
                {
                    Check(Finite(pose.t.localPosition) && Finite(pose.t.localScale) && Finite(pose.t.localRotation), type + " finite pose " + pose.t.name);
                    animated |= (pose.t.localPosition - pose.p).magnitude > .001f || Quaternion.Angle(pose.t.localRotation, pose.q) > .1f || (pose.t.localScale - pose.s).magnitude > .001f;
                }
                Check(animated, type + " has actual anatomy motion"); Check(animator.Driver.travel > 1.9f, type + " displacement-driven travel");
                if (type == BodyArchetype.Quadruped) Check(rig.legs.Length == 4 && rig.FourLegged, "quadruped anatomy");
                if (type == BodyArchetype.Arthropod) Check(rig.legs.Length == 6 && rig.profile.multileg, "arthropod anatomy");
                if (type == BodyArchetype.Winged) Check(rig.legs.Length == 2 && rig.wings.Length == 2, "winged walking anatomy composed");
                if (type == BodyArchetype.Biped)
                {
                    // A foot inside reachable stance keeps its world plant through body motion.
                    var leg = rig.legs[0]; leg.planted = false;
                    animator.Driver.phase = Mathf.PI; animator.Tick(.02f, 1.62f, false);
                    Vector3 anchor = leg.plant;
                    root.transform.position += Vector3.forward * .002f; animator.Tick(.02f, 1.64f, false);
                    Check(leg.planted && Vector3.Distance(anchor, leg.plant) < .0001f, "planted world anchor retained");
                    Check(Vector3.Distance(leg.foot.position, leg.plant + Vector3.up * (leg.foot.position.y - leg.plant.y)) < .03f, "IK foot remains on planted horizontal target");
                }
                rig.model.localScale = Vector3.one * 1.07f;
                animator.RestorePose();
                foreach (var pose in poses) { Near(Vector3.Distance(pose.t.localPosition, pose.p), 0f, type + " restored position"); Near(Quaternion.Angle(pose.t.localRotation, pose.q), 0f, type + " restored rotation", .05f); Near(Vector3.Distance(pose.t.localScale, pose.s), 0f, type + " restored scale"); }
                Near(rig.model.localScale.x, 1.07f, "cast-scale layer preserved");
                animator.Tick(.02f, 2f, true); Near(animator.Driver.speed, 0f, type + " lifecycle stop speed"); Check(animator.Suppressed, type + " suppressed");
                root.transform.position += Vector3.forward * 8f; animator.Tick(.02f, 2.02f, false); Near(animator.Driver.travel, 0f, type + " resumed without artificial stride");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void CompositeAnatomy()
        {
            ProceduralBodyAnimator animator; BodyRig rig; var root = Fixture(BodyArchetype.Winged, out animator, out rig);
            try {
                animator.Tick(.02f, 0f, false); root.transform.position += Vector3.forward * .03f; animator.Tick(.02f, .02f, false);
                Check(animator.Driver.weight > 0f && rig.wings.Length > 0 && rig.legs.Length == 2, "wings do not disable terrestrial legs");
                animator.traversalMode = TraversalMode.Glide; for (int i = 0; i < 60; i++) animator.Tick(.02f, .04f + i * .02f, false);
                Check(animator.WingPhase > 0f, "glide wing rhythm available");
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static void Lifecycle()
        {
            ProceduralBodyAnimator animator; BodyRig rig; var root = Fixture(BodyArchetype.Biped, out animator, out rig);
            try {
                var owner = root.AddComponent<Creature>(); owner.Init(0, false); animator.Bind(rig, owner);
                owner.dead = true; Check(animator.IsMotionSuppressed, "dead owner suppresses"); owner.dead = false;
                owner.capturing = true; Check(animator.IsMotionSuppressed, "capturing owner suppresses"); owner.capturing = false;
                owner.stunUntil = Time.time + 10f; Check(animator.IsMotionSuppressed, "stunned owner suppresses"); owner.stunUntil = 0f;
                animator.enabled = false; Check(animator.IsMotionSuppressed, "disabled animator suppresses");
                var game = Game.Instance;
                if (game) { bool paused = game.paused; try { game.paused = true; animator.enabled = true; Check(animator.IsMotionSuppressed, "paused game suppresses"); } finally { game.paused = paused; } }
            } finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        static bool Finite(Vector3 v) { return !(float.IsNaN(v.x) || float.IsInfinity(v.x) || float.IsNaN(v.y) || float.IsInfinity(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.z)); }
        static bool Finite(Quaternion q) { return !(float.IsNaN(q.x) || float.IsInfinity(q.x) || float.IsNaN(q.y) || float.IsInfinity(q.y) || float.IsNaN(q.z) || float.IsInfinity(q.z) || float.IsNaN(q.w) || float.IsInfinity(q.w)); }
    }
}
