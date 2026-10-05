using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wildbound.Locomotion
{
    // Presentation only. CharacterController and Creature retain root/movement/combat authority.
    [DefaultExecutionOrder(100)]
    public sealed class ProceduralBodyAnimator : MonoBehaviour
    {
        public BodyMotionProfile profile = BodyMotionProfile.For(BodyArchetype.Biped);
        public TraversalMode traversalMode;
        public bool boosting, takingOff, dodging;
        public BodyPoseDriver Pose { get; private set; } = new BodyPoseDriver();
        public float BodyLean { get; private set; }
        PlayerMotor player;
        readonly List<ArmRest> armRests = new List<ArmRest>();
        sealed class ArmRest { public JointChain chain; public Transform upper, lower, hand; public Quaternion upperRelaxed, lowerRelaxed; public Quaternion[] jointRests; }
        public LayerMask terrainMask = 1 << 8;
        [SerializeField] BodyRig savedRig = new BodyRig();
        public BodyRig SavedRig { get { return savedRig; } }
        public BodyRig Rig { get; private set; }
        public GaitDriver Driver { get; private set; } = new GaitDriver();
        public int GroundedFeet { get; private set; }
        public bool Suppressed { get; private set; }
        public float WavePhase { get { return wavePhase; } }
        public float HopCycle { get { return hopCycle; } }
        public float PulsePhase { get { return pulsePhase; } }
        public float WingPhase { get { return wingPhase; } }
        Creature creature;
        CharacterController controller;
        readonly List<SavedJoint> saved = new List<SavedJoint>();
        readonly Dictionary<Transform, SavedJoint> rest = new Dictionary<Transform, SavedJoint>();
        readonly Dictionary<Transform, LocomotionMath.Spring> chainSprings = new Dictionary<Transform, LocomotionMath.Spring>();
        readonly Dictionary<Transform, float> bottoms = new Dictionary<Transform, float>();
        Vector3[] gaitFootfalls;
        float walk, idle = 1f, swim, flying, glide, climb, wavePhase, waveWeight, stroke, bend, course;
        bool hasCourse;
        float beatPhase, pulsePhase, pulseWeight, hoverWeight, wingPhase, wingRate, wingReach, wingLift;
        float hopCycle, hopWeight, oozeCycle, oozeWeight, burrowCycle, duckTime, feltSpeed, previousSpeed, previousHeight;
        bool burrowMoving, hasHeight;
        LocomotionMath.Spring hopPitch, hopRoll, slosh;
        LocomotionMath.Spring[] ducks = new LocomotionMath.Spring[0];
        Quaternion spin = Quaternion.identity;
        Vector3 spinRate;
        float[] bodyAlong = new float[0];
        float[] memberLag = new float[0], memberOffsets = new float[0], memberSpeeds = new float[0];
        float groundRise, groundPitch, groundRoll;

        sealed class SavedJoint
        {
            public Transform joint;
            public Vector3 position, scale;
            public Quaternion rotation;
            public SavedJoint(Transform t) { joint = t; position = t.localPosition; rotation = t.localRotation; scale = t.localScale; }
            public void Restore() { if (joint) { joint.localPosition = position; joint.localRotation = rotation; joint.localScale = scale; } }
        }
        // Editor Undo invalidates transient measurements without restoring or changing authored bones.
        public void InvalidateAuthoringBinding() {
            Rig = null; saved.Clear(); rest.Clear(); armRests.Clear(); bottoms.Clear();
            Driver.Reset(); Pose.Reset(); Suppressed = false;
        }
        void Awake() { if (savedRig != null && savedRig.model && savedRig.waist) BindSavedRig(); }
        public void BindSavedRig()
        {
            if (savedRig == null) throw new InvalidOperationException("No saved procedural body rig is bound.");
            // Saved transforms are the editable authoring baseline, not a previous cached presentation pose.
            RestorePose();
            savedRig.profile = profile; Bind(savedRig, GetComponent<Creature>());
        }
        [ContextMenu("Bind Configured Body Rig")]
        void BindConfiguredBodyRig() { BeginAuthoringEdit(); BindSavedRig(); MarkAuthoringDirty(); }
        [ContextMenu("Calibrate Biped Ground Stance")]
        void CalibrateBipedGroundStance() {
            if (Application.isPlaying) throw new InvalidOperationException("Calibrate the resting rig in Edit mode, not an animated pose.");
            if (profile.archetype != BodyArchetype.Biped || savedRig.legs.Length != 2) throw new InvalidOperationException("This calibration supports two-leg biped anatomy only.");
            BeginAuthoringEdit(); BindSavedRig();
            float floor = float.PositiveInfinity, scale = Mathf.Max(.00001f, Mathf.Abs(Rig.model.lossyScale.y));
            foreach (var renderer in Rig.model.GetComponentsInChildren<Renderer>(true)) if (renderer.enabled && renderer.gameObject.activeSelf) floor = Mathf.Min(floor, renderer.bounds.min.y);
            if (float.IsPositiveInfinity(floor)) throw new InvalidOperationException("The configured model has no enabled skin renderer.");
            foreach (var leg in Rig.legs) leg.soleHeight = Mathf.Max(.001f, (leg.foot.position.y - floor) / scale);
            Rig.Measure(); Rig.stanceLowering = Mathf.Max(0, Mathf.Max(Rig.legs[0].upper.position.y - floor - Rig.legs[0].sole - Rig.legs[0].length * .78f, Rig.legs[1].upper.position.y - floor - Rig.legs[1].sole - Rig.legs[1].length * .78f)) / scale;
            profile.stride = Mathf.Min(profile.stride, Mathf.Min(Rig.legs[0].length, Rig.legs[1].length) * 1.5f);
            BindSavedRig(); MarkAuthoringDirty();
        }
        void BeginAuthoringEdit() {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.RegisterCompleteObjectUndo(this, "Configure procedural body rig");
#endif
        }
        void MarkAuthoringDirty() {
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        [ContextMenu("Apply Selected Archetype Defaults")]
        void ApplyArchetypeDefaults() {
            BeginAuthoringEdit(); var selected = BodyMotionProfile.For(profile.archetype);
            selected.displayHeight = profile.displayHeight; selected.stride = profile.stride; profile = selected; MarkAuthoringDirty();
        }
        public void Bind(BodyRig rig, Creature owner = null)
        {
            RestorePose(); Rig = rig; savedRig = rig; profile = rig.profile; creature = owner; player = GetComponent<PlayerMotor>(); controller = GetComponent<CharacterController>();
            rig.Measure(); saved.Clear(); rest.Clear(); bottoms.Clear();
            foreach (var t in rig.model.GetComponentsInChildren<Transform>(true))
            {
                if (t == rig.model) continue; // The cast-scale layer belongs to Creature, not this animator.
                var item = new SavedJoint(t); saved.Add(item); rest.Add(t, item);
                float bottom = t.position.y;
                foreach (var renderer in t.GetComponentsInChildren<Renderer>()) bottom = Mathf.Min(bottom, renderer.bounds.min.y);
                // Renderer bounds are already in world units. Normalize once at bind so
                // non-unit model/root scale and later cast scaling are applied only once.
                float bindScale = Mathf.Max(.00001f, Mathf.Abs(rig.model.lossyScale.y));
                bottoms[t] = Mathf.Max(0f, t.position.y - bottom) / bindScale;
            }
            BuildArmRests();
            gaitFootfalls = rig.FourLegged || profile.multileg ? new Vector3[rig.legs.Length] : null;
            if (gaitFootfalls != null) for (int i = 0; i < gaitFootfalls.Length; i++) gaitFootfalls[i] = rig.legs[i].footfalls;
            bodyAlong = new float[rig.body.Length];
            for (int i = 1; i < bodyAlong.Length; i++) bodyAlong[i] = bodyAlong[i - 1] + Vector3.Distance(rig.body[i - 1].position, rig.body[i].position);
            memberLag = new float[rig.members.Length];
            memberOffsets = new float[memberLag.Length]; memberSpeeds = new float[memberLag.Length];
            var order = new List<int>();
            for (int i = 0; i < memberLag.Length; i++) { memberLag[i] = .05f + .07f * Hash(rig.members[i].name, 0); order.Add(i); }
            order.Sort((a, b) => rig.model.InverseTransformPoint(rig.members[b].position).z.CompareTo(rig.model.InverseTransformPoint(rig.members[a].position).z));
            for (int rank = 0; rank < order.Count; rank++) { int i = order[rank]; memberOffsets[i] = (order.Count - rank - .5f + .25f * (Hash(rig.members[i].name, 1) - .5f)) / order.Count; }
            ducks = new LocomotionMath.Spring[rig.columns.Length];
            ResetState();
        }
        public void RestorePose() { for (int i = saved.Count - 1; i >= 0; i--) saved[i].Restore(); }
        public void ResetState()
        {
            RestorePose(); Driver.Reset(); Pose.Reset(); BodyLean = 0f;
            foreach (var leg in Rig != null ? Rig.legs : new LimbRig[0]) leg.planted = false;
            walk = swim = flying = glide = climb = 0f; idle = 1f;
            wavePhase = waveWeight = stroke = bend = course = beatPhase = pulsePhase = pulseWeight = hoverWeight = 0f;
            wingPhase = wingRate = wingReach = wingLift = hopCycle = hopWeight = oozeCycle = oozeWeight = burrowCycle = duckTime = feltSpeed = previousSpeed = previousHeight = 0f;
            hasCourse = hasHeight = burrowMoving = false;
            hopPitch = hopRoll = slosh = new LocomotionMath.Spring();
            chainSprings.Clear(); Array.Clear(ducks, 0, ducks.Length); Array.Clear(memberSpeeds, 0, memberSpeeds.Length); spin = Quaternion.identity; spinRate = Vector3.zero;
            groundRise = groundPitch = groundRoll = 0f; GroundedFeet = 0;
        }
        void LateUpdate()
        {
            if (Rig == null) return;
            Tick(Time.deltaTime, Time.time, IsMotionSuppressed);
        }
        public bool IsMotionSuppressed { get { return !enabled || (creature && (creature.dead || creature.capturing || Time.time < creature.stunUntil)) || (player && player.health <= 0) || (Game.Instance && Game.Instance.paused); } }
        void OnDisable() { ResetState(); Suppressed = true; }
        // Explicit deterministic entry point for editor validation and custom controllers.
        public void Tick(float dt, float timestamp, bool suppressed)
        {
            if (Rig == null) return;
            if (suppressed || !(dt > 0f))
            {
                ResetState(); Suppressed = true; return;
            }
            bool resumed = Suppressed; Suppressed = false;
            if (resumed) ResetState();
            RestorePose();
            float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            bool groundMode = traversalMode == TraversalMode.Ground && !profile.hover;
            bool grounded = Grounded();
            Driver.Sample(transform.position, timestamp, dt, yaw, true, groundMode && grounded, profile.stride, gaitFootfalls);
            if (Driver.teleported)
            {
                foreach (var leg in Rig.legs) leg.planted = false;
                spin = Quaternion.identity; spinRate = Vector3.zero;
                hasCourse = false;
                hopWeight = waveWeight = oozeWeight = 0f;
            }
            Pose.Advance(Driver, dt, grounded, traversalMode, profile.hover, boosting, dodging);
            walk = Pose[BodyPoseMode.Walk] + Pose[BodyPoseMode.Run]; idle = Pose[BodyPoseMode.Idle];
            swim = Pose[BodyPoseMode.Swim]; flying = Pose[BodyPoseMode.Flight] + Pose[BodyPoseMode.Glide];
            glide = Pose[BodyPoseMode.Glide]; climb = Pose[BodyPoseMode.Climb];
            // Same composition order as body/index.js: stance, torso, hover/wave,
            // contact/roll/ooze/hop/group/pulse/burrow, limbs, head/tail/wings/chains.
            if (Rig.legs.Length > 0) Stance(dt, grounded && groundMode);
            Torso();
            if (profile.hover) Hover(dt);
            if (profile.crawl == CrawlStyle.Slither && Rig.body.Length >= 3) Wave(dt);
            if (profile.swim == SwimStyle.Undulate && profile.crawl != CrawlStyle.Slither && Rig.body.Length > 2) TailBeat(dt);
            if (Rig.legs.Length == 0 && !profile.hover) Contact(dt);
            if (profile.crawl == CrawlStyle.Roll) Roll(dt);
            if (profile.ooze) Ooze(dt, grounded);
            if (profile.bounce || profile.crawl == CrawlStyle.Hop && Rig.members.Length == 0) Hop(dt, grounded);
            if (Rig.members.Length > 0) Group(dt, grounded);
            if (profile.swim == SwimStyle.Pulse) Pulse(dt);
            if (profile.burrow) Burrow(dt, grounded);
            Paddles();
            Legs(grounded && groundMode && Pose.mode != BodyPoseMode.Dodge);
            Arms(); Head(); Tails(); Wings(dt); Appendages(dt);
            previousSpeed = Driver.speed;
        }
        static void Blend(ref float value, float target, float rate, float dt) { value += (target - value) * LocomotionMath.Damp(rate, dt); }
        public bool GroundAt(Vector3 at, out RaycastHit hit)
        {
            float reach = Mathf.Max(2f, profile.displayHeight * 2f);
            return Physics.Raycast(at + Vector3.up * reach, Vector3.down, out hit, reach * 2f, terrainMask, QueryTriggerInteraction.Ignore);
        }
        bool Grounded()
        {
            if (controller && controller.enabled && controller.isGrounded) return true;
            RaycastHit hit;
            return GroundAt(transform.position, out hit) && Mathf.Abs(transform.position.y - hit.point.y) < .18f;
        }
        void Rotate(Transform joint, float pitch = 0f, float yaw = 0f, float roll = 0f)
        {
            if (!joint) return;
            // Axes from the model's world frame, independent of imported local joint conventions.
            joint.rotation = Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Rig.model.forward) * Quaternion.AngleAxis(yaw * Mathf.Rad2Deg, Rig.model.up)
                           * Quaternion.AngleAxis(pitch * Mathf.Rad2Deg, Rig.model.right) * joint.rotation;
        }
        void Shift(Transform joint, Vector3 world) { if (joint) joint.position += world; }
        Vector3 Heading { get { return Rig.model.TransformDirection(Driver.localDirection).normalized; } }
        float SpeedShare { get { return Mathf.Min(1.4f, Driver.speed / 7f); } }
        float Bottom(Transform joint) { float b; return bottoms.TryGetValue(joint, out b) ? b * Mathf.Abs(Rig.model.lossyScale.y) : 0f; }
        void Volume(Transform joint, float height)
        {
            height = Mathf.Max(.2f, height);
            joint.localScale = Vector3.Scale(joint.localScale, new Vector3(1f / Mathf.Sqrt(height), height, 1f / Mathf.Sqrt(height)));
        }
        void Stance(float dt, bool grounded)
        {
            if (!grounded) { groundRise = groundPitch = groundRoll = 0f; return; }
            float sum = 0f, forward = 0f, lateral = 0f, ahead2 = 0f, across2 = 0f;
            int contacts = 0;
            foreach (var leg in Rig.legs)
            {
                Vector3 home = Rig.model.TransformPoint(leg.restFoot); RaycastHit hit;
                if (!GroundAt(home, out hit)) continue;
                Vector3 d = Rig.model.InverseTransformDirection(home - Rig.waist.position);
                float error = hit.point.y + leg.sole - home.y;
                sum += error; forward += d.z * error; lateral += d.x * error;
                ahead2 += d.z * d.z; across2 += d.x * d.x; contacts++;
            }
            if (contacts == 0) return;
            float limit = profile.multileg ? .5f : .35f;
            Blend(ref groundRise, Mathf.Clamp(sum / contacts, -.25f, .35f), 8f, dt);
            Blend(ref groundPitch, Mathf.Clamp(-Mathf.Atan(forward / Mathf.Max(.01f, ahead2)), -limit, limit), 8f, dt);
            Blend(ref groundRoll, Mathf.Clamp(Mathf.Atan(lateral / Mathf.Max(.01f, across2)), -limit, limit), 8f, dt);
            Shift(Rig.waist, Vector3.up * (groundRise - Rig.stanceLowering * Mathf.Abs(Rig.model.lossyScale.y))); Rotate(Rig.waist, groundPitch, 0f, groundRoll);
        }
        void Torso()
        {
            float t = Driver.time, gain = Driver.weight, length = Rig.legs.Length > 0 ? Rig.legs[0].length : profile.displayHeight * .5f;
            bool crawling = profile.crawl != CrawlStyle.None || profile.multileg || profile.bounce || profile.ooze || profile.burrow;
            float upright = crawling ? 0f : 1f;
            float lean = upright * (SpeedShare * walk * .12f + swim * (Rig.FourLegged ? .12f : .5f)) + climb * .32f
                       + flying * ((profile.hover ? 0f : .1f) + SpeedShare * .35f - Mathf.Clamp(Driver.vertical / 2.6f, -1f, 1f) * .35f);
            lean += Pose[BodyPoseMode.Dodge] * .32f; BodyLean = lean;
            Rotate(Rig.waist, lean, 0f, -Driver.turn * .035f * walk + flying * Mathf.Clamp(-Driver.turn * .25f, -.7f, .7f));
            Rotate(Rig.spine, Mathf.Sin(t * 2.1f) * .012f * idle - Pose[BodyPoseMode.Land] * .08f, Driver.turn * .025f);
            float size = Rig.legs.Length > 0 ? Vector3.Distance(Rig.legs[0].upper.position, Rig.legs[0].lower.position) : Mathf.Abs(Rig.model.lossyScale.y);
            Shift(Rig.waist, Vector3.up * (size * (Mathf.Sin(t * 2.1f) * .018f * idle - Pose[BodyPoseMode.Land] * Pose.landing * .12f)));
            if (!crawling && !Rig.FourLegged && Rig.legs.Length > 0)
            {
                float shift = Mathf.Sin(t * .9f) * idle;
                Shift(Rig.waist, Rig.model.right * (shift * size * .1f));
                Rotate(Rig.waist, 0f, 0f, -shift * .04f); Rotate(Rig.spine, Mathf.Sin(t * 2.1f) * .03f * idle, 0f, shift * .05f);
                Shift(Rig.waist, Rig.model.right * (Mathf.Sin(Driver.phase) * length * .045f * gain) + Vector3.up * ((1f - Mathf.Cos(Driver.phase * 2f)) * length * .025f * gain));
            }
            if (Rig.FourLegged)
            {
                var steps = new LocomotionMath.Footing[Rig.legs.Length]; bool planted = false; float since = float.PositiveInfinity, until = float.PositiveInfinity;
                for (int i = 0; i < steps.Length; i++) { steps[i] = Step(i); planted |= steps[i].stance; since = Mathf.Min(since, steps[i].cycle - OwnDuty(i)); until = Mathf.Min(until, 1f - steps[i].cycle); }
                float u = steps[0].cycle, gallop = Driver.weights.z;
                float rise = -.04f * (1f - gallop) * (1f - Mathf.Cos(4f * Mathf.PI * (u - Driver.duty / 2f))) * .5f;
                if (!planted) rise += since * until * 4f;
                float flex = .12f * gallop * Mathf.Cos(LocomotionMath.Tau * (u - .8f - Driver.duty / 2f));
                Shift(Rig.waist, Rig.model.right * (Mathf.Sin(Driver.phase) * length * .01f * gain) + Vector3.up * (rise * length * gain));
                Rotate(Rig.waist, -flex / 2f * gain); Rotate(Rig.spine, flex * gain);
            }
        }
        float OwnDuty(int index) { return Driver.duties.Length > index ? Driver.duties[index] : Driver.duty; }
        LocomotionMath.Footing Step(int index)
        {
            float offset = Driver.offsets.Length > index ? Driver.offsets[index] : Rig.legs[index].offset;
            return LocomotionMath.Step(Driver.phase + offset, OwnDuty(index), Rig.FourLegged || profile.multileg);
        }
        void Hover(float dt)
        {
            Blend(ref hoverWeight, flying, 4f, dt);
            Shift(Rig.waist, Vector3.up * (profile.hoverLift + .035f * profile.displayHeight * Mathf.Sin(Driver.time * 1.7f)) * hoverWeight);
            Rotate(Rig.waist, 0f, 0f, .04f * Mathf.Sin(Driver.time * .9f) * hoverWeight);
        }
        void Wave(float dt)
        {
            float length = Mathf.Max(.01f, Rig.bodyLength), wavelength = length / 1.25f;
            // Serpentine motion must respond to slow analog/NPC movement too; the old
            // threshold left the whole body at rest pose until speed exceeded 0.3 units/s.
            bool moving = Driver.speed > .03f;
            wavePhase = Mathf.Repeat(wavePhase + LocomotionMath.Tau * Driver.distance / (wavelength * (swim > .5f ? .7f : 1f)) + (swim > .5f && !moving ? 2.4f * dt : 0f), LocomotionMath.Tau);
            Blend(ref waveWeight, moving || swim > .5f ? 1f : 0f, 5f, dt); Blend(ref stroke, moving ? 1f : 0f, 5f, dt);
            float goal = Mathf.Atan2(Heading.x, Heading.z);
            if (!hasCourse || waveWeight < .05f) { course = goal; bend = 0f; hasCourse = true; }
            else {
                float turned = LocomotionMath.Wrap(goal - course) * LocomotionMath.Damp(8f, dt); course = LocomotionMath.Wrap(course + turned);
                Blend(ref bend, Mathf.Clamp(turned / dt / Mathf.Max(Driver.speed, 1f) * length, -1.6f, 1.6f), 8f, dt);
            }
            float amplitude = .5f * (swim > .5f ? 1.25f * (.35f + .65f * stroke) : 1f);
            float previous = 0f;
            // Head remains on course; nested tail joints receive only the residual angle.
            for (int i = 2; i < Rig.body.Length - 1; i++)
            {
                float s = (bodyAlong[i] + bodyAlong[i + 1]) * .5f, u = s / length;
                float swing = amplitude * (profile.verticalWave ? .35f : 1f) * LocomotionMath.Smooth(u, 0f, .2f) * (1f - .35f * u * u) * Mathf.Sin(wavePhase - LocomotionMath.Tau * s / wavelength);
                float angle = swing * waveWeight + .04f * idle * (1f - waveWeight) * Mathf.Sin(Driver.time * .9f - LocomotionMath.Tau * s / wavelength);
                float turn = LocomotionMath.Wrap(course - transform.eulerAngles.y * Mathf.Deg2Rad) + bend * (.5f - u);
                Rotate(Rig.body[i], profile.verticalWave ? angle - previous : 0f, profile.verticalWave ? turn * waveWeight / Rig.body.Length : angle - previous + turn * waveWeight / Rig.body.Length);
                previous = angle;
            }
        }
        void TailBeat(float dt)
        {
            bool moving = Driver.speed > .3f;
            beatPhase = Mathf.Repeat(beatPhase + LocomotionMath.Tau * Driver.distance / (1.1f * profile.displayHeight) + (moving ? 0f : LocomotionMath.Tau * .9f * dt), LocomotionMath.Tau);
            Blend(ref stroke, moving ? 1f : 0f, 5f, dt);
            float width = .42f * (.3f + .7f * stroke) * swim, previous = 0f;
            for (int i = 2; i < Rig.body.Length - 1; i++)
            {
                float u = (float)(i - 2) / Mathf.Max(1, Rig.body.Length - 3);
                float angle = width * (.25f + .75f * u * u) * Mathf.Sin(beatPhase - LocomotionMath.Tau * .75f * u) + Mathf.Clamp(-Driver.turn * .12f, -.5f, .5f) * swim * u;
                Rotate(Rig.body[i], profile.verticalWave ? angle - previous : 0f, profile.verticalWave ? 0f : angle - previous); previous = angle;
            }
        }
        void Contact(float dt)
        {
            if (flying > .5f || climb > .5f) return;
            // Primitive supports are measured renderer bounds, not source skinned-vertex weights.
            float raise = 0f; RaycastHit hit;
            if (GroundAt(Rig.waist.position, out hit))
            {
                float target = hit.point.y + Bottom(Rig.waist) - Rig.waist.position.y;
                if (swim > .5f) target -= profile.displayHeight * .35f;
                raise = target;
                float slopePitch = -Mathf.Atan2(Vector3.Dot(hit.normal, Rig.model.forward), hit.normal.y);
                float slopeRoll = Mathf.Atan2(Vector3.Dot(hit.normal, Rig.model.right), hit.normal.y);
                Blend(ref groundPitch, Mathf.Clamp(slopePitch, -.5f, .5f), 10f, dt); Blend(ref groundRoll, Mathf.Clamp(slopeRoll, -.5f, .5f), 10f, dt);
                Rotate(Rig.waist, groundPitch, 0f, groundRoll);
            }
            Blend(ref groundRise, raise, 10f, dt); Shift(Rig.waist, Vector3.up * (groundRise + swim * .018f * profile.displayHeight * Mathf.Sin(Driver.time * 2.2f)));
            if (profile.crawl == CrawlStyle.Slither) for (int i = 2; i < Rig.body.Length; i++) KeepAbove(Rig.body[i], .08f);
        }
        void KeepAbove(Transform joint, float clearance)
        {
            RaycastHit hit;
            if (GroundAt(joint.position, out hit))
            {
                float below = hit.point.y + clearance - joint.position.y;
                if (below > 0f) Shift(joint, Vector3.up * below);
            }
        }
        static Vector3 RotationVector(Quaternion q)
        {
            float sign = q.w < 0f ? -1f : 1f;
            var axis = new Vector3(q.x, q.y, q.z) * sign; float s = axis.magnitude;
            return s < 1e-12f ? axis * 2f : axis * (2f * Mathf.Atan2(s, q.w * sign) / s);
        }
        static Quaternion RotationOf(Vector3 vector) { return vector.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.AngleAxis(vector.magnitude * Mathf.Rad2Deg, vector.normalized); }
        void Roll(float dt)
        {
            dt = Mathf.Min(dt, .1f);
            float radius = Mathf.Max(.05f, profile.rollerRadius * Mathf.Abs(Rig.model.lossyScale.x));
            if (flying > .5f) spin = RotationOf(spinRate * dt) * spin;
            else if (Driver.speed > .3f)
            {
                Vector3 covered = Heading * Driver.distance, across = Vector3.Cross(Vector3.up, covered).normalized;
                Vector3 step = Rig.model.InverseTransformDirection(across) * (Driver.distance / radius) * (swim > .5f ? .3f : 1f);
                spin = RotationOf(step) * spin; spinRate = Vector3.Lerp(spinRate, step / Mathf.Max(.00001f, dt), LocomotionMath.Damp(20f, dt));
            }
            else { spinRate = (spinRate - RotationVector(spin) * 64f * dt) * Mathf.Exp(-8f * dt); spinRate = Vector3.ClampMagnitude(spinRate, 4.5f); spin = RotationOf(spinRate * dt) * spin; }
            spin = spin.normalized;
            Rig.waist.rotation = Rig.model.rotation * spin * Quaternion.Inverse(Rig.model.rotation) * Rig.waist.rotation;
            RaycastHit hit;
            if (GroundAt(transform.position, out hit) && swim < .5f)
            {
                // Ring support sampling, as roll.js restingHeight; rough primitive radius, not skinned bounds.
                float restHeight = hit.point.y + radius;
                for (int ring = 1; ring <= 10; ring++) for (int k = 0; k < 16; k++)
                {
                    float r = ring == 10 ? .97f : ring * .1f, a = k * LocomotionMath.Tau / 16f;
                    Vector3 at = transform.position + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * (r * radius);
                    if (GroundAt(at, out hit)) restHeight = Mathf.Max(restHeight, hit.point.y + radius * Mathf.Sqrt(1f - r * r));
                }
                Shift(Rig.waist, Vector3.up * (restHeight - Rig.waist.position.y));
            }
            Rotate(Rig.waist, .05f * idle * Mathf.Sin(Driver.time * 2.3f), 0f, .05f * idle * Mathf.Sin(Driver.time * 2.3f * .71f + 1f));
            Shift(Rig.waist, Vector3.up * (.02f * profile.displayHeight * idle * (1f - Mathf.Cos(Driver.time * 4.6f)) * .5f));
        }
        void Hop(float dt, bool grounded)
        {
            bool bounce = profile.bounce;
            hopCycle = Mathf.Repeat(hopCycle + Driver.distance / ((bounce ? .9f : .45f) * profile.displayHeight), 1f);
            Blend(ref hopWeight, grounded && swim < .5f && Driver.speed > .3f ? 1f : 0f, 8f, dt);
            var hop = LocomotionMath.HopArc(hopCycle, bounce);
            Volume(Rig.waist, 1f - hop.squash * hopWeight);
            Shift(Rig.waist, Vector3.up * (hop.lift * (bounce ? .22f : .1f) * profile.displayHeight * hopWeight));
            if (bounce)
            {
                float old = feltSpeed; Blend(ref feltSpeed, Driver.speed, 12f, dt);
                hopPitch.Advance((.2f * Mathf.Min(1f, Driver.speed / 4.2f) + hop.pitch) * hopWeight, 5.5f, .28f, .3f * (feltSpeed - old) / dt, Mathf.Min(dt, 1f / 30f), .45f);
                hopRoll.Advance(-.12f * Driver.turn * Mathf.Min(1f, Driver.speed / 4.2f), 5.5f, .28f, 0f, Mathf.Min(dt, 1f / 30f), .45f);
                // A pendulum swings about the head, rather than the feet/root.
                Vector3 pivot = Rig.head ? Rig.head.position : Rig.waist.position;
                Quaternion q = Quaternion.AngleAxis(hopRoll.value * Mathf.Rad2Deg, Rig.model.forward) * Quaternion.AngleAxis(hopPitch.value * Mathf.Rad2Deg, Rig.model.right);
                Rig.waist.position = pivot + q * (Rig.waist.position - pivot); Rig.waist.rotation = q * Rig.waist.rotation;
            }
            else Rotate(Rig.waist, hop.pitch * hopWeight);
        }
        static float Hash(string name, int channel)
        {
            unchecked {
                uint h = 0x811c9dc5u ^ ((uint)(channel + 1) * 0x9e3779b1u);
                for (int i = 0; i < name.Length; i++) h = (h ^ name[i]) * 0x01000193u;
                h = (h ^ (h >> 16)) * 0x85ebca6bu; h = (h ^ (h >> 13)) * 0xc2b2ae35u;
                return (h ^ (h >> 16)) / 4294967296f;
            }
        }
        void Group(float dt, bool grounded)
        {
            hopCycle = Mathf.Repeat(hopCycle + Driver.distance / (.45f * profile.displayHeight), 1f);
            Blend(ref hopWeight, grounded && Driver.speed > .3f ? 1f : 0f, 8f, dt);
            for (int i = 0; i < Rig.members.Length; i++)
            {
                Transform member = Rig.members[i]; float phase = Mathf.Repeat(hopCycle + memberOffsets[i], 1f);
                var hop = LocomotionMath.HopArc(phase, false);
                Blend(ref memberSpeeds[i], Driver.speed, 1f / memberLag[i], dt);
                float trail = Mathf.Clamp((Driver.speed - memberSpeeds[i]) * memberLag[i], -.3f * profile.displayHeight, .3f * profile.displayHeight);
                Vector3 arm = member.position - Rig.waist.position; arm.y = 0f;
                float twist = Mathf.Clamp(Driver.turn * memberLag[i], -.3f, .3f);
                Shift(member, Quaternion.AngleAxis(-twist * Mathf.Rad2Deg, Vector3.up) * arm - arm - Heading * trail);
                Rotate(member, hop.pitch * hopWeight + .05f * idle * Mathf.Sin(Driver.time * (1.3f + Hash(member.name, 1)) + i), 0f, .05f * idle * Mathf.Sin(Driver.time * 1.7f + i * 2f));
                Volume(member, 1f - hop.squash * hopWeight + .02f * idle * Mathf.Sin(Driver.time * 2.1f + i));
                RaycastHit hit;
                if (GroundAt(member.position, out hit)) Shift(member, Vector3.up * (hit.point.y + Bottom(member) + hop.lift * .1f * profile.displayHeight * hopWeight - member.position.y));
            }
        }
        void Pulse(float dt)
        {
            pulsePhase = Mathf.Repeat(pulsePhase + LocomotionMath.Tau * Driver.distance / (.9f * profile.displayHeight) + (Driver.speed > .3f ? 0f : LocomotionMath.Tau * .55f * dt), LocomotionMath.Tau);
            Blend(ref pulseWeight, swim, 5f, dt);
            float squeeze = LocomotionMath.Contraction(pulsePhase) * pulseWeight;
            if (Rig.paddles.Length == 0) { float across = 1f - .12f * squeeze; Rig.waist.localScale = Vector3.Scale(Rig.waist.localScale, new Vector3(across, 1f / (across * across), across)); }
            Shift(Rig.waist, Heading * ((squeeze - .5f * pulseWeight) * .08f * profile.displayHeight * Mathf.Clamp01(Driver.speed)) + Vector3.up * (squeeze * .025f * profile.displayHeight));
        }
        void Ooze(float dt, bool grounded)
        {
            oozeCycle = Mathf.Repeat(oozeCycle + Driver.distance / (.8f * profile.displayHeight), 1f);
            Blend(ref oozeWeight, grounded && Driver.speed > .3f ? 1f : 0f, 6f, dt);
            float leaning = .12f * Mathf.Min(1f, Driver.speed / 4.2f) * oozeWeight;
            slosh.Advance(leaning, 10f, .18f, -1.2f * (Driver.speed - previousSpeed) / dt / 4.2f, dt, .35f);
            float stretch = -.07f * Mathf.Cos(oozeCycle * LocomotionMath.Tau) * oozeWeight + .6f * (slosh.value - leaning);
            float along = Mathf.Max(.4f, 1f + stretch), breath = 1f + .025f * idle * Mathf.Sin(Driver.time * 1.7f);
            float height = Mathf.Pow(along, -.7f) * breath;
            Rig.waist.localScale = Vector3.Scale(Rig.waist.localScale, new Vector3(Mathf.Pow(along, -.3f) / Mathf.Sqrt(breath), height, along / Mathf.Sqrt(breath)));
            Shift(Rig.waist, Vector3.up * (-(1f - height) * Bottom(Rig.waist)));
            Rotate(Rig.spine, slosh.value);
            // Source uses skinned support weights; the generated primitive uses explicit rim pivots.
            for (int i = 3; i < Rig.mass.Length; i++)
            {
                Transform lobe = Rig.mass[i]; Vector3 relative = Rig.model.InverseTransformDirection(lobe.position - Rig.waist.position);
                float x = relative.z / (.5f * profile.displayHeight), crest = 1.4f * (2f * oozeCycle - 1f);
                float u = (x - crest) / .55f, bulge = Mathf.Exp(-u * u) * oozeWeight;
                Shift(lobe, Vector3.up * (.05f * profile.displayHeight * bulge) + (lobe.position - Rig.waist.position).normalized * (.035f * profile.displayHeight * bulge));
                KeepAbove(lobe, .08f);
            }
        }
        void Burrow(float dt, bool grounded)
        {
            burrowCycle = Mathf.Repeat(burrowCycle + Driver.distance / (.8f * profile.displayHeight), 1f);
            bool moving = grounded && Driver.speed > (burrowMoving ? .25f : .6f);
            duckTime = moving == burrowMoving ? duckTime + dt : 0f; burrowMoving = moving;
            for (int i = 0; i < Rig.columns.Length; i++)
            {
                var column = Rig.columns[i]; if (!column.head || !column.mound) continue;
                float delay = i * .07f;
                ducks[i].Advance(duckTime >= delay && duckTime < delay + .12f ? .7f : 0f, 20f, .4f, 0f, dt, .9f);
                float bob = .5f * (1f - Mathf.Cos(LocomotionMath.Tau * (burrowCycle + (float)i / Rig.columns.Length)));
                float sink = .14f * bob * Mathf.Clamp01(Driver.speed) + ducks[i].value;
                float height = Mathf.Max(0f, column.head.position.y - column.mound.position.y);
                Shift(column.head, Vector3.down * (sink * height)); KeepAbove(column.mound, .03f);
            }
        }
        void Paddles()
        {
            float phase = profile.crawl == CrawlStyle.Slither ? wavePhase : profile.swim == SwimStyle.Undulate ? beatPhase : profile.swim == SwimStyle.Pulse ? pulsePhase : Driver.travel / (.8f * profile.displayHeight) * LocomotionMath.Tau + (Driver.speed < .3f ? Driver.time * LocomotionMath.Tau * .6f : 0f);
            for (int i = 0; i < Rig.paddles.Length; i++)
            {
                var paddle = Rig.paddles[i]; if (paddle.joints.Length == 0) continue;
                float angle = phase - paddle.set * Mathf.PI, pace = Mathf.Clamp01(Driver.speed / 4.2f), sweep = .14f + .41f * pace, lift = .3f, fold = 0f;
                if (profile.swim == SwimStyle.Undulate) { sweep = .1f + .12f * pace; lift = .12f; fold = .45f; }
                if (profile.swim == SwimStyle.Pulse) { sweep = .12f + .28f * pace; lift = .2f; fold = .15f; }
                if (profile.swim == SwimStyle.Drift) { sweep = .1f + .1f * pace; lift = .1f; }
                sweep = sweep * swim + .45f * walk; lift = lift * swim + .35f * walk;
                float side = paddle.right ? 1f : -1f;
                Rotate(paddle.joints[0], 0f, side * (sweep * Mathf.Sin(angle) - fold * swim), side * lift * Mathf.Max(0f, Mathf.Cos(angle)));
                if (paddle.joints.Length > 1) Rotate(paddle.joints[1], 0f, side * sweep * .5f * Mathf.Sin(angle - .9f));
                foreach (var joint in paddle.joints) if (swim < .5f) KeepAbove(joint, .015f * profile.displayHeight);
            }
        }
        void Legs(bool canPlant)
        {
            GroundedFeet = 0;
            for (int i = 0; i < Rig.legs.Length; i++)
            {
                var leg = Rig.legs[i]; var moment = Step(i);
                Vector3 home = Rig.model.TransformPoint(leg.restFoot);
                Vector3 hip = leg.upper.position;
                Vector3 lateralOffset = Vector3.ProjectOnPlane(home - hip, Heading); lateralOffset.y = 0f;
                float lateral = lateralOffset.magnitude;
                float drop = Mathf.Min(leg.length, Mathf.Max(0f, hip.y - home.y));
                float reach = Mathf.Max(.35f * leg.length, Mathf.Sqrt(Mathf.Max(0f, leg.length * leg.length - lateral * lateral - Mathf.Pow(Mathf.Max(0f, drop - .2f * leg.length), 2f))));
                float half = Mathf.Min(profile.stride / 4f, reach);
                float lift = moment.lift * leg.length * (.18f + (Rig.FourLegged ? Driver.weights.z : Mathf.Clamp01((Driver.speed - 4.2f) / 2.73f)) * .09f) * Driver.weight;
                Vector3 target = home + Heading * (moment.sweep * half * Driver.weight) + Vector3.up * lift;
                Quaternion sole = Rig.model.rotation * leg.restSole;
                if (canPlant)
                {
                    if (moment.stance || Driver.speed < .08f)
                    {
                        Vector3 fromHome = leg.plant - home; fromHome.y = 0f;
                        if (!leg.planted || fromHome.magnitude > half * 1.5f + .001f) { leg.plant = target; leg.planted = true; }
                        target.x = leg.plant.x; target.z = leg.plant.z;
                    }
                    else leg.planted = false;
                    RaycastHit hit;
                    if (GroundAt(target, out hit))
                    {
                        target.y = Mathf.Clamp(hit.point.y + leg.sole + lift, home.y - leg.length * .45f, home.y + leg.length * .45f);
                        sole = Quaternion.FromToRotation(Vector3.up, hit.normal) * sole;
                        if (leg.planted) GroundedFeet++;
                    }
                }
                else { leg.planted = false; target = home; }
                if (swim > .001f || flying > .001f || climb > .001f)
                {
                    float air = Mathf.Max(swim, flying), kick = Mathf.Sin(Driver.time * 4f + leg.offset) * .12f;
                    target += Rig.model.forward * kick * swim + Vector3.up * (.2f * leg.length * air);
                }
                LocomotionMath.SolveLeg(leg.upper, leg.lower, leg.foot, target, Rig.model.TransformDirection(leg.pole), sole);
                float stroke = Mathf.Sin(Driver.time * 5.5f + (leg.right ? Mathf.PI : 0));
                float jump = Pose[BodyPoseMode.Jump], fall = Pose[BodyPoseMode.Fall], land = Pose[BodyPoseMode.Land];
                if (jump + fall + flying + climb > .001f) {
                    Rotate(leg.upper, jump * .38f + fall * .1f + flying * .45f + swim * stroke * .35f - climb * stroke * .45f);
                    Rotate(leg.lower, -jump * .45f - flying * .9f - land * .18f + climb * stroke * .2f);
                }
                Rig.settledFeet[i] = leg.foot.position;
            }
        }
        // arena-3d/src/body/arms.js: geometry-derived relaxed orientations,
        // not local-axis guesses. The saved bind/rest pose is never overwritten.
        void BuildArmRests()
        {
            armRests.Clear();
            Quaternion inverse = Quaternion.Inverse(Rig.model.rotation);
            foreach (var chain in Rig.arms)
            {
                if (chain.joints.Length < 2) continue;
                var upper = chain.joints[0]; var lower = chain.joints.Length >= 3 ? chain.joints[1] : null;
                var hand = chain.joints[chain.joints.Length - 1];
                Vector3 shoulder = Rig.model.InverseTransformPoint(upper.position);
                Vector3 elbow = Rig.model.InverseTransformPoint(lower ? lower.position : hand.position);
                Vector3 wrist = Rig.model.InverseTransformPoint(hand.position);
                float side = Mathf.Abs(shoulder.x) > .00001f ? Mathf.Sign(shoulder.x) : chain.right ? -1f : 1f;
                float spread = Mathf.Clamp(chain.spread, .2f, 1.2f);
                Vector3 down = Quaternion.AngleAxis(-.1f * Mathf.Rad2Deg, Vector3.right) * new Vector3(side * Mathf.Sin(spread), -Mathf.Cos(spread), 0);
                var item = new ArmRest { chain = chain, upper = upper, lower = lower, hand = hand,
                    upperRelaxed = Quaternion.FromToRotation((elbow - shoulder).normalized, down) * inverse * upper.rotation,
                    lowerRelaxed = lower ? Quaternion.FromToRotation((wrist - elbow).normalized, Quaternion.AngleAxis(-.25f * Mathf.Rad2Deg, Vector3.right) * down) * inverse * lower.rotation : Quaternion.identity,
                    jointRests = new Quaternion[chain.restJoints.Length] };
                for (int i = 0; i < item.jointRests.Length; i++) if (chain.restJoints[i]) item.jointRests[i] = chain.restJoints[i].localRotation;
                armRests.Add(item);
            }
        }
        void Arms()
        {
            float running = Mathf.Clamp01((Driver.speed - 4.2f) / 2.73f);
            float swing = Driver.weight * Mathf.Min(1f, profile.armSwing);
            // No authored idle clip is played in this adapter. Use the same source
            // hanging geometry as its clip-less neutral baseline, then add idle sway.
            float relaxation = Mathf.Clamp01(swing + idle + Pose[BodyPoseMode.Jump] + Pose[BodyPoseMode.Fall] + Pose[BodyPoseMode.Land] + Pose[BodyPoseMode.Dodge]);
            float amplitude = profile.armSwing * (.25f + .2f * running) * Mathf.Min(1f, Driver.speed / 4.2f) * Driver.weight * Driver.localDirection.z;
            foreach (var arm in armRests)
            {
                int side = arm.chain.right ? 1 : 0;
                float offset = side == 1 ? Mathf.PI : 0;
                float angle = amplitude * Mathf.Sin(Driver.phase + offset);
                for (int i = 0; i < arm.chain.restJoints.Length; i++) if (arm.chain.restJoints[i])
                    arm.chain.restJoints[i].localRotation = Quaternion.Slerp(arm.chain.restJoints[i].localRotation, arm.jointRests[i], relaxation);
                Quaternion inverse = Quaternion.Inverse(Rig.model.rotation);
                Quaternion upper = inverse * arm.upper.rotation, lower = arm.lower ? inverse * arm.lower.rotation : Quaternion.identity;
                var pendulum = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, Vector3.right);
                arm.upper.rotation = Rig.model.rotation * pendulum * Quaternion.Slerp(upper, arm.upperRelaxed, relaxation);
                if (arm.lower) {
                    var flex = Quaternion.AngleAxis(-( .5f * running * swing + .5f * Mathf.Max(0, -angle)) * Mathf.Rad2Deg, Vector3.right);
                    arm.lower.rotation = Rig.model.rotation * pendulum * flex * Quaternion.Slerp(lower, arm.lowerRelaxed, relaxation);
                }
                float stroke = Mathf.Sin(Driver.time * 5.5f + side * Mathf.PI);
                float jump = Pose[BodyPoseMode.Jump], fall = Pose[BodyPoseMode.Fall];
                float sway = Mathf.Sin(Driver.time * 2.1f + side * .7f) * .05f * idle;
                Rotate(arm.upper, swim * stroke * .65f + climb * (.35f + stroke * .5f) - jump * .22f - fall * .12f + sway,
                    0, (side == 0 ? 1 : -1) * (fall * .22f + swim * .12f));
                if (arm.lower) Rotate(arm.lower, climb * (-.3f + stroke * .25f) + swim * .15f);
            }
        }
        void Head()
        {
            for (int i = 0; i < Rig.neck.Length; i++) if (idle > 0 && swim > 0) {
                float sway = Mathf.Sin(Driver.time * .55f - i * .5f) * .07f * idle;
                Rotate(Rig.neck[i], sway * .4f, sway);
            }
            float shift = Mathf.Sin(Driver.time * .9f) * idle;
            Rotate(Rig.head, -(BodyLean + groundPitch) * .5f, Driver.turn * .04f + Mathf.Sin(Driver.time * .43f) * .14f * idle,
                Mathf.Sin(Driver.time * 1.7f) * .012f * idle + shift * .02f);
        }
        void Tails()
        {
            for (int k = 0; k < Rig.tails.Length; k++)
            {
                var tail = Rig.tails[k]; float share = Mathf.Min(1f, 3f / Mathf.Max(1, tail.joints.Length));
                for (int i = 0; i < tail.joints.Length; i++)
                    Rotate(tail.joints[i], -flying * .07f * share, (Mathf.Sin(Driver.time * 2.6f - (i + 1) * .6f * share - k * 1.3f) * .035f - Driver.turn * .035f) * share * (.5f + SpeedShare));
            }
        }
        static Quaternion WingOrientation(Vector3 restSpan, Vector3 restNormal, Vector3 span, Vector3 normal)
        {
            var turn = Quaternion.FromToRotation(restSpan, span);
            Vector3 from = Vector3.ProjectOnPlane(turn * restNormal, span).normalized, to = Vector3.ProjectOnPlane(normal, span).normalized;
            if (Vector3.Dot(from, to) < 0f) to = -to;
            return from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f ? turn : Quaternion.FromToRotation(from, to) * turn;
        }
        void Wings(float dt)
        {
            if (Rig.wings.Length == 0) return;
            float cruise = Mathf.Clamp01(Driver.speed / 3f), share = flying > .001f ? Mathf.Clamp01((flying - glide) / flying) : 1f;
            float rate = takingOff ? 1.7f : boosting ? 1.8f : Mathf.Lerp(1.3f, 1f, cruise), reach = takingOff ? .95f : boosting ? .85f : Mathf.Lerp(.7f, .5f, cruise);
            Blend(ref wingRate, Mathf.Lerp(.35f, rate, share), 6f, dt); Blend(ref wingReach, flying * Mathf.Lerp(.07f, reach, share), 6f, dt); Blend(ref wingLift, flying * Mathf.Lerp(.06f, .12f, share), 6f, dt);
            wingPhase = Mathf.Repeat(wingPhase + LocomotionMath.Tau * wingRate * Mathf.Clamp(Mathf.Sqrt(.4f / Mathf.Max(.001f, profile.wingArea)), .8f, 2f) * dt, LocomotionMath.Tau);
            foreach (var wing in Rig.wings)
            {
                if (wing.joints.Length == 0) continue;
                float side = wing.right ? 1f : -1f, phase = wingPhase - wing.set * .5f;
                Vector3 mirror = new Vector3(side, 1f, 1f);
                Quaternion fold = WingOrientation(wing.span, wing.normal, Vector3.Scale(new Vector3(.35f, -.2f, -1f).normalized, mirror), Vector3.Scale(new Vector3(1f, 0f, .35f).normalized, mirror));
                Vector3 spreadDirection = Vector3.Scale(new Vector3(1f, .2f, -.25f).normalized, mirror);
                Quaternion spread = Quaternion.FromToRotation(wing.span, spreadDirection);
                float angle = Vector3.Angle(wing.span, spreadDirection) * Mathf.Deg2Rad;
                spread = angle < .45f ? Quaternion.identity : Quaternion.Slerp(Quaternion.identity, spread, (angle - .45f) / angle);
                Quaternion basis = Quaternion.Slerp(fold, spread, flying);
                wing.joints[0].rotation = Rig.model.rotation * Quaternion.AngleAxis(side * (wingReach * Mathf.Sin(phase) + wingLift) * Mathf.Rad2Deg, Vector3.forward)
                                      * Quaternion.AngleAxis(.3f * wingReach * Mathf.Cos(phase) * Mathf.Rad2Deg, Vector3.right) * basis
                                      * Quaternion.Inverse(Rig.model.rotation) * wing.joints[0].rotation;
                for (int i = 1; i < wing.joints.Length; i++)
                {
                    float curl = (1f - flying) * 1.3f / Mathf.Max(1, wing.joints.Length - 1), lag = wingReach * .6f / Mathf.Max(1, wing.joints.Length - 1) * Mathf.Sin(phase - .9f * i);
                    Rotate(wing.joints[i], 0f, side * curl, side * lag);
                }
            }
        }
        void Appendages(float dt)
        {
            float height = Rig.waist.position.y, rise = hasHeight ? Mathf.Clamp((height - previousHeight) / dt / profile.displayHeight, -3f, 3f) : 0f;
            hasHeight = true; previousHeight = height;
            foreach (var chain in Rig.appendages)
            {
                for (int i = 0; i < chain.joints.Length; i++)
                {
                    var joint = chain.joints[i]; LocomotionMath.Spring spring;
                    chainSprings.TryGetValue(joint, out spring);
                    float trail = -.35f * Mathf.Min(1f, Driver.speed / 5f) / Mathf.Max(1, chain.joints.Length);
                    float target = profile.bounce ? (-.2f * rise + (chain.leaf ? .35f * swim * Mathf.Sin(LocomotionMath.Tau * hopCycle) : trail)) / Mathf.Max(1, chain.joints.Length)
                                                 : trail + .04f * Mathf.Sin(Driver.time * 1.7f - i * .7f);
                    spring.Advance(target, 14f / (1f + .5f * i), .35f, 0f, Mathf.Min(dt, 1f / 30f), .7f);
                    chainSprings[joint] = spring;
                    Rotate(joint, spring.value, .03f * Mathf.Sin(Driver.time * 2f - i * .8f) - Driver.turn * .015f);
                    if (swim < .5f && flying < .5f) KeepAbove(joint, .01f * profile.displayHeight);
                }
            }
        }
    }
}
