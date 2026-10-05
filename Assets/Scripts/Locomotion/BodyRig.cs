using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pokemon3D.Locomotion
{
    public enum BodyArchetype { Biped, Quadruped, Winged, Serpentine, Aquatic, Floater, Roller, Group, Hopper, Burrower, Amorphous, Arthropod }
    public enum CrawlStyle { None, Slither, Hop, Roll }
    public enum SwimStyle { Paddle, Undulate, Pulse, Drift }
    public enum TraversalMode { Ground, Swim, Flight, Glide, Climb }

    [Serializable]
    public sealed class BodyMotionProfile
    {
        public BodyArchetype archetype;
        [Min(.01f)] public float displayHeight = 1.8f, stride = 1.2f;
        public CrawlStyle crawl;
        public SwimStyle swim;
        public bool hover, multileg, bounce, ooze, burrow;
        // Reserved source-profile metadata; sideways locomotion is not implemented by this adapter.
        public bool sideways;
        public bool verticalWave;
        [Min(0f)] public float hoverLift = .4f, wingArea = .4f, rollerRadius = .5f;
        [Range(0f, 1f)] public float armSwing = 1f;
        // Archetype presets are parameters, not mutually exclusive module switches.
        public static BodyMotionProfile For(BodyArchetype type)
        {
            var p = new BodyMotionProfile { archetype = type };
            switch (type)
            {
                case BodyArchetype.Serpentine: p.crawl = CrawlStyle.Slither; p.swim = SwimStyle.Undulate; break;
                case BodyArchetype.Aquatic: p.crawl = CrawlStyle.Hop; p.swim = SwimStyle.Undulate; break;
                case BodyArchetype.Floater: p.hover = true; p.swim = SwimStyle.Drift; break;
                case BodyArchetype.Roller: p.crawl = CrawlStyle.Roll; break;
                case BodyArchetype.Group: p.crawl = CrawlStyle.Hop; break;
                case BodyArchetype.Hopper: p.bounce = true; break;
                case BodyArchetype.Burrower: p.burrow = true; break;
                case BodyArchetype.Amorphous: p.ooze = true; break;
                case BodyArchetype.Arthropod: p.multileg = true; break;
            }
            return p;
        }
    }
    [Serializable]
    public sealed class LimbRig
    {
        public Transform upper, lower, foot;
        public bool right, front;
        public int set;
        // Optional ankle-to-ground clearance in model units, calibrated for imported skin.
        [Min(0f)] public float soleHeight;
        [NonSerialized] public float length, sole, offset;
        [NonSerialized] public Vector3 footfalls, restFoot, pole;
        [NonSerialized] public Quaternion restSole;
        [NonSerialized] public Vector3 plant;
        [NonSerialized] public bool planted;
    }
    [Serializable]
    public sealed class JointChain
    {
        public Transform[] joints = new Transform[0];
        public bool right, leaf;
        public int set;
        [Range(.2f, 1.2f)] public float spread = .3f;
        public Transform[] restJoints = new Transform[0];
        // Wing geometry in model space; populated for generated primitives or supplied for imported rigs.
        public Vector3 span = Vector3.right, normal = Vector3.up;
    }
    [Serializable]
    public sealed class BurrowColumn
    {
        public Transform mound, head;
    }
    [Serializable]
    public sealed class BodyRig
    {
        public Transform model, waist, spine, head;
        public Transform[] neck = new Transform[0];
        public LimbRig[] legs = new LimbRig[0];
        public JointChain[] arms = new JointChain[0], tails = new JointChain[0], wings = new JointChain[0], appendages = new JointChain[0], paddles = new JointChain[0];
        // A connected head-to-tail chain. The first joint is the forward end.
        public Transform[] body = new Transform[0];
        public Transform[] members = new Transform[0], mass = new Transform[0];
        public BurrowColumn[] columns = new BurrowColumn[0];
        // Optional imported-rig ground stance compression; zero preserves primitive behavior.
        [Min(0f)] public float stanceLowering;
        [NonSerialized] public float bodyLength;
        [NonSerialized] public Vector3[] settledFeet;
        public bool FourLegged { get { return legs.Length == 4 && !profile.multileg; } }
        public BodyMotionProfile profile = BodyMotionProfile.For(BodyArchetype.Biped);
        public void Measure()
        {
            if (!model || !waist) throw new ArgumentException("A body rig needs a model and waist.");
            if (!(profile.stride > 0f) || !(profile.displayHeight > 0f)) throw new ArgumentException("Rig stride and height must be positive.");
            int sets = 1;
            foreach (var leg in legs) sets = Mathf.Max(sets, leg.set + 1);
            settledFeet = new Vector3[legs.Length];
            for (int i = 0; i < legs.Length; i++)
            {
                var leg = legs[i];
                if (!leg.upper || !leg.lower || !leg.foot) throw new ArgumentException("A stepping leg needs upper, lower and foot joints.");
                leg.length = Vector3.Distance(leg.upper.position, leg.lower.position) + Vector3.Distance(leg.lower.position, leg.foot.position);
                if (leg.length < .0001f) throw new ArgumentException("A stepping leg has zero length.");
                leg.sole = leg.soleHeight > 0f ? leg.soleHeight * Mathf.Abs(model.lossyScale.y) : leg.length * .12f;
                leg.restFoot = model.InverseTransformPoint(leg.foot.position);
                leg.restSole = Quaternion.Inverse(model.rotation) * leg.foot.rotation;
                leg.pole = model.InverseTransformDirection(leg.lower.position - (leg.upper.position + leg.foot.position) * .5f).normalized;
                if (leg.pole.sqrMagnitude < .0001f) leg.pole = Vector3.forward;
                leg.offset = ((leg.right ? 1 : 0) + leg.set) % 2 * Mathf.PI;
                leg.footfalls = profile.multileg ? LocomotionMath.WaveFootfalls(leg.right, leg.set, sets) : LocomotionMath.Footfalls(leg.right, leg.front);
                settledFeet[i] = leg.foot.position;
                leg.planted = false;
            }
            bodyLength = 0f;
            for (int i = 1; i < body.Length; i++) bodyLength += Vector3.Distance(body[i - 1].position, body[i].position);
        }
    }

    // Primitive pivot rigs demonstrate the imported math without pretending to be source FBX skeletons.
    public static class PrimitiveBodyRig
    {
        static Transform Pivot(string name, Transform parent, Vector3 position)
        {
            var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = position; return t;
        }
        static void Skin(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
        {
            var o = World.Part(name, type, position, scale, color, parent);
            var collider = o.GetComponent<Collider>();
            // Procedural skins are never locomotion/collision authorities.
            if (collider) { collider.enabled = false; if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider); }
        }
        static void Link(Transform joint, Vector3 end, float width, Color color)
        {
            var o = World.Part("Segment skin", PrimitiveType.Capsule, end * .5f, new Vector3(width, end.magnitude * .5f, width), color, joint);
            o.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end.normalized);
            var collider = o.GetComponent<Collider>(); if (collider) { collider.enabled = false; if (Application.isPlaying) UnityEngine.Object.Destroy(collider); else UnityEngine.Object.DestroyImmediate(collider); }
        }
        static JointChain Chain(string name, Transform parent, Vector3 origin, Vector3 step, int count, Color color, float width)
        {
            var joints = new Transform[count]; Transform holder = parent;
            for (int i = 0; i < count; i++)
            {
                joints[i] = Pivot(name + " " + i, holder, i == 0 ? origin : step);
                if (i < count - 1) Link(joints[i], step, width, color);
                holder = joints[i];
            }
            return new JointChain { joints = joints };
        }
        public static BodyRig Build(Transform model, int species, BodyMotionProfile profile)
        {
            var r = new BodyRig { model = model, profile = profile };
            Color color = Species.Colors[species];
            r.waist = Pivot("Waist", model, new Vector3(0f, .65f, 0f));
            r.spine = Pivot("Spine", r.waist, Vector3.zero);
            Skin("Body", PrimitiveType.Sphere, r.spine, Vector3.zero, new Vector3(1.05f, .95f, 1.3f), color);
            r.head = Pivot("Head pivot", r.spine, new Vector3(0f, .47f, .4f));
            Skin("Head", PrimitiveType.Sphere, r.head, Vector3.zero, new Vector3(.78f, .7f, .7f), color);
            for (int side = -1; side <= 1; side += 2)
            {
                Skin("Eye", PrimitiveType.Sphere, r.head, new Vector3(side * .23f, .1f, .29f), new Vector3(.12f, .15f, .07f), new Color(.08f, .15f, .17f));
                if (species == 0) Skin("Leaf ear", PrimitiveType.Cube, r.head, new Vector3(side * .36f, .45f, -.07f), new Vector3(.18f, .56f, .2f), new Color(.7f, .84f, .34f));
                if (species == 1) Skin("Fox ear", PrimitiveType.Capsule, r.head, new Vector3(side * .27f, .41f, -.1f), new Vector3(.2f, .3f, .22f), new Color(1f, .76f, .37f));
                if (species == 2) Skin("Horn", PrimitiveType.Capsule, r.head, new Vector3(side * .27f, .44f, .04f), new Vector3(.13f, .33f, .13f), new Color(.92f, .87f, .68f));
            }
            bool walks = profile.crawl == CrawlStyle.None && !profile.hover && !profile.bounce && !profile.ooze && !profile.burrow;
            int sets = profile.multileg ? 3 : (profile.archetype == BodyArchetype.Quadruped ? 2 : 1);
            var legs = new List<LimbRig>();
            if (walks) for (int set = 0; set < sets; set++) for (int side = -1; side <= 1; side += 2)
            {
                float z = sets == 1 ? .12f : Mathf.Lerp(-.38f, .38f, (float)set / (sets - 1));
                float x = profile.multileg ? side * .46f : side * .32f;
                Transform upper = Pivot("Leg " + set + (side < 0 ? " L" : " R"), r.waist, new Vector3(x, -.04f, z));
                Vector3 lowerAt = new Vector3(profile.multileg ? side * .16f : 0f, -.23f, .15f);
                Link(upper, lowerAt, .14f, color * .85f);
                Transform lower = Pivot("Knee", upper, lowerAt);
                Vector3 footAt = new Vector3(profile.multileg ? side * .12f : 0f, -.29f, -.09f);
                Link(lower, footAt, .12f, color * .8f);
                Transform foot = Pivot("Foot pivot", lower, footAt);
                Skin("Foot", PrimitiveType.Sphere, foot, new Vector3(0f, 0f, .06f), new Vector3(.25f, .17f, .37f), color * .8f);
                legs.Add(new LimbRig { upper = upper, lower = lower, foot = foot, right = side > 0, front = set > 0, set = set });
            }
            r.legs = legs.ToArray();
            Color tailColor = species == 1 ? new Color(1f, .78f, .33f) : color;
            r.tails = new[] { Chain("Tail", r.waist, new Vector3(0f, .2f, -.6f), new Vector3(0f, 0f, -.22f), 4, tailColor, .22f) };
            if (profile.crawl == CrawlStyle.Slither || profile.swim == SwimStyle.Undulate)
            {
                var chain = Chain("Body wave", r.waist, new Vector3(0f, -.18f, -.38f), new Vector3(0f, 0f, -.36f), 7, color, .34f);
                var body = new List<Transform> { r.head, r.waist }; body.AddRange(chain.joints); r.body = body.ToArray(); r.tails = new JointChain[0];
                if (profile.crawl == CrawlStyle.Slither) r.waist.localPosition = Vector3.up * .28f;
            }
            if (profile.archetype == BodyArchetype.Winged)
            {
                var wings = new List<JointChain>();
                for (int side = -1; side <= 1; side += 2)
                {
                    var wing = Chain("Wing", r.spine, new Vector3(side * .4f, .2f, -.15f), new Vector3(side * .5f, .08f, -.1f), 3, color, .18f);
                    wing.right = side > 0; wing.span = new Vector3(side, .2f, -.25f).normalized;
                    for (int i = 0; i < wing.joints.Length - 1; i++) Skin("Wing membrane", PrimitiveType.Cube, wing.joints[i], new Vector3(side * .24f, 0f, -.15f), new Vector3(.55f, .04f, .62f), color * .9f);
                    wings.Add(wing);
                }
                r.wings = wings.ToArray();
            }
            if (profile.archetype == BodyArchetype.Aquatic)
            {
                var paddles = new List<JointChain>();
                for (int side = -1; side <= 1; side += 2) { var p = Chain("Fin", r.waist, new Vector3(side * .4f, 0f, .2f), new Vector3(side * .28f, -.08f, -.2f), 3, color, .14f); p.right = side > 0; paddles.Add(p); }
                r.paddles = paddles.ToArray();
            }
            if (profile.hover || profile.bounce || profile.ooze)
            {
                var appendages = new List<JointChain>();
                for (int side = -1; side <= 1; side += 2) { var p = Chain("Free chain", r.spine, new Vector3(side * .5f, .1f, 0f), new Vector3(side * .18f, -.2f, -.06f), 4, color, .12f); p.right = side > 0; p.leaf = profile.bounce; appendages.Add(p); }
                r.appendages = appendages.ToArray();
            }
            if (profile.archetype == BodyArchetype.Group)
            {
                var members = new List<Transform>();
                for (int i = 0; i < 3; i++) { var member = Pivot("Member " + i, model, new Vector3((i - 1) * .62f, .25f, -.25f - .3f * i)); Skin("Member skin", PrimitiveType.Sphere, member, Vector3.zero, Vector3.one * .5f, color); members.Add(member); }
                r.members = members.ToArray();
            }
            if (profile.burrow)
            {
                r.waist.localPosition = Vector3.up * .3f;
                var mound = Pivot("Mound", model, Vector3.zero); Skin("Mound skin", PrimitiveType.Sphere, mound, Vector3.up * .08f, new Vector3(1.25f, .22f, 1.2f), color * .55f);
                r.columns = new[] { new BurrowColumn { mound = mound, head = r.waist } };
            }
            if (profile.crawl == CrawlStyle.Roll) { r.waist.localPosition = Vector3.up * profile.rollerRadius; r.spine.localScale = new Vector3(1f, 1f, .78f); }
            if (profile.ooze)
            {
                var mass = new List<Transform> { r.waist, r.spine, r.head };
                for (int i = 0; i < 6; i++) { float angle = i * LocomotionMath.Tau / 6f; var lobe = Pivot("Ooze rim " + i, r.waist, new Vector3(Mathf.Sin(angle) * .5f, -.45f, Mathf.Cos(angle) * .5f)); Skin("Ooze skin", PrimitiveType.Sphere, lobe, Vector3.zero, new Vector3(.5f, .24f, .5f), color); mass.Add(lobe); }
                r.mass = mass.ToArray();
            }
            r.Measure();
            if (r.legs.Length > 0)
            {
                float reach = float.PositiveInfinity;
                foreach (var leg in r.legs)
                {
                    float drop = leg.upper.position.y - model.position.y - leg.sole;
                    float lateral = Mathf.Abs(leg.upper.position.x - leg.foot.position.x);
                    reach = Mathf.Min(reach, Mathf.Sqrt(Mathf.Max(.0025f, leg.length * leg.length - drop * drop - lateral * lateral)));
                }
                // Generated anatomy needs a measured stride, not the source's model-specific profile stride.
                profile.stride = Mathf.Min(profile.stride, reach * 3f);
            }
            return r;
        }
    }
}
