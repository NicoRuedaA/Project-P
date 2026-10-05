using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wildbound.Locomotion;
using Wildbound.Pokemon;

namespace Wildbound.Editor
{
    public sealed class BodyMovementDraft : ScriptableObject
    {
        public Transform model;
        public BodyMotionProfile profile;
        public BodyRig rig;
        public TraversalMode traversalMode;
        public LayerMask terrainMask;
        public static BodyMovementDraft From(ProceduralBodyAnimator animator) {
            var seed = CreateInstance<BodyMovementDraft>(); seed.hideFlags = HideFlags.HideAndDontSave;
            seed.model = animator.GetComponent<Creature>() ? animator.GetComponent<Creature>().model : animator.GetComponent<PlayerMotor>() ? BodyMovementAuthoring.PlayerVisual(animator.GetComponent<PlayerMotor>()) : animator.SavedRig?.model;
            seed.profile = animator.profile; seed.rig = animator.SavedRig ?? new BodyRig();
            seed.traversalMode = animator.traversalMode; seed.terrainMask = animator.terrainMask;
            var copy = Instantiate(seed); copy.hideFlags = HideFlags.HideAndDontSave; DestroyImmediate(seed);
            if(copy.model && copy.rig.model && copy.model!=copy.rig.model)copy.rig=new BodyRig();
            return copy;
        }
        public void SetLegCount(int count) {
            var legs = new LimbRig[count]; for (int i=0;i<count;i++) legs[i] = i<rig.legs.Length ? rig.legs[i] : new LimbRig(); rig.legs=legs;
        }
    }
    [InitializeOnLoad]
    public static class BodyMovementAuthoring
    {
        static readonly HashSet<ProceduralBodyAnimator> configured = new HashSet<ProceduralBodyAnimator>();
        static BodyMovementAuthoring() { Undo.undoRedoPerformed += InvalidateConfiguredCaches; }
        static void InvalidateConfiguredCaches() { foreach (var a in configured.ToArray()) { if (a) a.InvalidateAuthoringBinding(); else configured.Remove(a); } }
        static bool Inside(Transform t, Transform model) => t && model && (t==model || t.IsChildOf(model));
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        internal static bool Weighted(Transform joint, SkinnedMeshRenderer[] skins) {
            foreach (var skin in skins) {
                var bones = skin.bones;
                bool Owned(int index,float weight) => weight>.001f && index>=0 && index<bones.Length && Inside(bones[index],joint);
                foreach (var w in skin.sharedMesh.boneWeights) if (Owned(w.boneIndex0,w.weight0)||Owned(w.boneIndex1,w.weight1)||Owned(w.boneIndex2,w.weight2)||Owned(w.boneIndex3,w.weight3)) return true;
            }
            return false;
        }
        public static string StaleWarning(ProceduralBodyAnimator a) {
            var creature = a.GetComponent<Creature>(); var visible = creature ? creature.model : PlayerVisual(a.GetComponent<PlayerMotor>());
            if (visible && a.SavedRig != null && a.SavedRig.model != visible)
                return "Visible model is '"+visible.name+"', but movement still targets '"+(a.SavedRig.model?a.SavedRig.model.name:"None")+"'. Assign bones from the visible model, then click Configure and Validate.";
            if (a.SavedRig?.model && !a.SavedRig.model.gameObject.activeInHierarchy) return "Movement targets an inactive model. Enable the intended visible model and configure its own bones.";
            return "";
        }
        public static Transform PlayerVisual(PlayerMotor player) => player ? new SerializedObject(player).FindProperty("visual").objectReferenceValue as Transform : null;
        public static BodyRig Validate(ProceduralBodyAnimator actor, BodyMovementDraft draft) {
            Require(actor && draft && draft.model, "Assign Model: the visible child under this moving actor.");
            Require(draft.model!=actor.transform && draft.model.IsChildOf(actor.transform), "Model must be a child of this actor, not the controller root or another actor.");
            Require(draft.model.gameObject.activeInHierarchy, "Enable the selected Model before configuring movement.");
            Require(draft.profile != null && float.IsFinite(draft.profile.stride) && draft.profile.stride>0 && float.IsFinite(draft.profile.displayHeight) && draft.profile.displayHeight>0, "Display Height and Stride must be finite and positive.");
            var copy=UnityEngine.Object.Instantiate(draft); BodyRig rig=copy.rig; var profile=copy.profile; UnityEngine.Object.DestroyImmediate(copy);
            rig.model=draft.model; rig.profile=profile;
            Require(rig.model.GetComponentsInChildren<Renderer>(true).Any(r=>r.enabled&&r.gameObject.activeInHierarchy), "Model has no active visible renderer.");
            Require(new[]{profile.hoverLift,profile.wingArea,profile.rollerRadius,profile.armSwing}.All(float.IsFinite), "Advanced motion values must be finite.");
            var skins=rig.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s=>s.enabled&&s.gameObject.activeInHierarchy&&s.sharedMesh).ToArray();
            bool skinned=rig.model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length>0;
            Require(!skinned || skins.Length>0, "Model has no active visible skin. Enable its intended renderers first.");
            void Joint(Transform t,string label,bool weighted=false) {
                Require(Inside(t,rig.model), label+" must be a real bone inside the selected Model (not the old model).");
                Require(float.IsFinite(t.position.x)&&float.IsFinite(t.position.y)&&float.IsFinite(t.position.z), label+" has a non-finite position.");
                Require(!skinned || !weighted || Weighted(t,skins), label+" has no influence on the active visible skin. Choose its actual deforming chain.");
            }
            Joint(rig.waist,"Waist",true); if(rig.spine)Joint(rig.spine,"Spine",true); if(rig.head)Joint(rig.head,"Head",true);
            int required=profile.archetype==BodyArchetype.Quadruped?4:profile.archetype==BodyArchetype.Biped?2:-1;
            Require(required<0||rig.legs.Length==required, "This body type needs exactly "+required+" leg chains.");
            if(profile.archetype==BodyArchetype.Quadruped)rig.arms=new JointChain[0];
            for(int i=0;i<rig.legs.Length;i++) {
                var l=rig.legs[i]; Require(l!=null,"Leg "+(i+1)+" is missing.");
                Joint(l.upper,"Leg "+(i+1)+" Upper",true); Joint(l.lower,"Leg "+(i+1)+" Lower",true); Joint(l.foot,"Leg "+(i+1)+" Foot");
                Require(l.lower.IsChildOf(l.upper)&&l.foot.IsChildOf(l.lower),"Leg "+(i+1)+": Lower must descend from Upper, and Foot from Lower.");
                Require(Vector3.Distance(l.upper.position,l.lower.position)>.0001f&&Vector3.Distance(l.lower.position,l.foot.position)>.0001f,"Leg "+(i+1)+" has a zero-length segment.");
                Require(float.IsFinite(l.soleHeight)&&l.soleHeight>=0,"Leg sole clearance must be finite and nonnegative.");
                if(required>0){l.right=i%2==1;l.front=required==4&&i>=2;l.set=0;}
            }
            foreach(var chain in rig.arms.Concat(rig.tails).Concat(rig.wings).Concat(rig.appendages).Concat(rig.paddles)) {
                Require(chain!=null,"A joint chain is missing.");
                Require(float.IsFinite(chain.spread)&&chain.spread>=.2f&&chain.spread<=1.2f,"Chain Spread must be finite and between 0.2 and 1.2 radians."); foreach(var t in chain.joints)Joint(t,"Chain joint"); foreach(var t in chain.restJoints)Joint(t,"Rest joint");
            }
            foreach(var wing in rig.wings) {
                bool Direction(Vector3 v) => float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z)&&float.IsFinite(v.sqrMagnitude)&&v.sqrMagnitude>1e-8f;
                Require(Direction(wing.span),"Wing Span must be a finite nonzero direction (not NaN, infinity or zero).");
                Require(Direction(wing.normal),"Wing Normal must be a finite nonzero direction (not NaN, infinity or zero).");
            }
            foreach(var arm in rig.arms) { Require(arm.joints.Length>=3,"An arm needs Upper Arm, Forearm and Hand (three ordered joints).");
                Require(arm.joints[1].IsChildOf(arm.joints[0])&&arm.joints[2].IsChildOf(arm.joints[1]),"Arm joints must follow Upper Arm > Forearm > Hand hierarchy."); Joint(arm.joints[0],"Upper Arm",true);
                Require(Vector3.Distance(arm.joints[0].position,arm.joints[1].position)>.0001f&&Vector3.Distance(arm.joints[1].position,arm.joints[2].position)>.0001f,"Arm has a zero-length segment."); }
            foreach(var t in rig.body.Concat(rig.neck).Concat(rig.members).Concat(rig.mass))Joint(t,"Body chain joint");
            foreach(var column in rig.columns){Require(column!=null,"Burrow column is missing."); Joint(column.mound,"Burrow mound");Joint(column.head,"Burrow head");}
            switch(profile.archetype) {
                case BodyArchetype.Serpentine: Require(rig.body.Length>=3,"Serpentine needs at least three ordered Body joints.");break;
                case BodyArchetype.Winged: Require(rig.wings.Length>0&&rig.wings.All(w=>w.joints.Length>=1),"Winged needs actual wing chains (at least one deforming joint each).");break;
                case BodyArchetype.Aquatic: Require(rig.body.Length>=3||rig.paddles.Length>0||profile.swim==SwimStyle.Pulse,"Aquatic needs an undulating Body chain, actual Paddles, or a pulse-capable body.");break;
                case BodyArchetype.Group: Require(rig.members.Length>0,"Group needs its actual Members.");break;
                case BodyArchetype.Amorphous: Require(rig.mass.Length>0,"Amorphous needs its actual Mass transforms.");break;
                case BodyArchetype.Burrower: Require(rig.columns.Length>0,"Burrower needs its actual Columns.");break;
                case BodyArchetype.Arthropod: Require(rig.legs.Length>=4,"Arthropod needs actual multi-leg chains.");break;
            }
            Require(float.IsFinite(rig.stanceLowering)&&rig.stanceLowering>=0,"Stance Lowering must be finite and nonnegative.");
            Require(!rig.model.GetComponentsInChildren<Animator>(true).Any(a=>a.enabled&&a.gameObject.activeInHierarchy&&a.runtimeAnimatorController),"An enabled Animator controller can overwrite these bones. Disable its clips before configuring procedural movement.");
            rig.Measure(); return rig;
        }
        public static void Apply(ProceduralBodyAnimator a, BodyMovementDraft draft) {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode,"Configure outside Play; no Play state will be changed.");
            var rig=Validate(a,draft); var creature=a.GetComponent<Creature>(); var player=a.GetComponent<PlayerMotor>();
            var marker=a.GetComponent<PokemonCreatureVisualOverride>(); var trainer=a.GetComponent<TrainerVisualOverride>();
            Undo.IncrementCurrentGroup(); int group=Undo.GetCurrentGroup(); Undo.SetCurrentGroupName("Configure procedural body movement");
            var owners=new List<UnityEngine.Object>{a}; if(creature)owners.Add(creature);if(player)owners.Add(player);if(marker)owners.Add(marker);if(trainer)owners.Add(trainer);
            Undo.RegisterCompleteObjectUndo(owners.ToArray(),"Configure procedural body movement");
            try {
                a.InvalidateAuthoringBinding(); a.Bind(rig,creature); a.traversalMode=draft.traversalMode; a.terrainMask=draft.terrainMask;
                if(creature)creature.model=rig.model;
                if(player){var data=new SerializedObject(player);data.FindProperty("visual").objectReferenceValue=rig.model;data.ApplyModifiedPropertiesWithoutUndo();}
                if(marker){marker.replacementModel=rig.model; var entry=rig.model.GetComponent<PokemonGalleryEntry>();if(entry){marker.visualDex=entry.dex;marker.modelPrefab=PrefabUtility.GetCorrespondingObjectFromSource(rig.model.gameObject);}}
                if(trainer){trainer.replacementVisual=rig.model;trainer.proceduralAnimator=a;}
                foreach(var owner in owners){EditorUtility.SetDirty(owner);PrefabUtility.RecordPrefabInstancePropertyModifications(owner);}
                if(a.gameObject.scene.IsValid())EditorSceneManager.MarkSceneDirty(a.gameObject.scene);
                configured.Add(a); Undo.CollapseUndoOperations(group);
            } catch { Undo.RevertAllDownToGroup(group);a.InvalidateAuthoringBinding();throw; }
        }
    }

    public static class PokemonBodyMovementAutoRig
    {
        static void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        static Newtonsoft.Json.Linq.JToken[] Items(Newtonsoft.Json.Linq.JToken value)
        {
            if (value == null || value.Type == Newtonsoft.Json.Linq.JTokenType.Null) return new Newtonsoft.Json.Linq.JToken[0];
            Require(value is Newtonsoft.Json.Linq.JArray, "A source anatomy chain must be an array; configure this model manually.");
            return value.ToArray();
        }
        static bool Present(Newtonsoft.Json.Linq.JToken value) => value != null && value.Type != Newtonsoft.Json.Linq.JTokenType.Null;
        static string Text(Newtonsoft.Json.Linq.JToken value) => Present(value) ? value.ToObject<string>() : null;
        static bool Flag(Newtonsoft.Json.Linq.JToken value, bool fallback) => Present(value) ? value.Type == Newtonsoft.Json.Linq.JTokenType.Object || value.ToObject<bool>() : fallback;
        static float Number(Newtonsoft.Json.Linq.JToken value, float fallback) => Present(value) ? value.ToObject<float>() : fallback;
        static Transform Unique(Transform model, string name)
        {
            Require(!string.IsNullOrEmpty(name), "The source profile has an incomplete bone chain. Assign its real bones manually, then Configure and Validate.");
            var matches = model.GetComponentsInChildren<Transform>(true).Where(t => t.name == name).ToArray();
            Require(matches.Length == 1, "Bone '" + name + "' must occur exactly once in the selected Model (found " + matches.Length + "). Select an unambiguous model or assign its bones manually.");
            return matches[0];
        }
        static Transform[] Joints(Transform model, Newtonsoft.Json.Linq.JToken value) => Items(value).Select(t => Unique(model, Text(t))).ToArray();
        static JointChain[] Chains(Transform model, Newtonsoft.Json.Linq.JToken value, bool leaf = false) => Items(value).Select(t =>
        {
            var joints = Joints(model, t);
            Require(joints.Length > 0, "The source profile has an empty chain; configure this model manually.");
            return new JointChain { joints = joints, leaf = leaf };
        }).ToArray();
        static Newtonsoft.Json.Linq.JObject ReadSource(Transform model)
        {
            var entry = model ? model.GetComponent<PokemonGalleryEntry>() : null;
            Require(entry && entry.sourceProfile && entry.visual && entry.visual.IsChildOf(model),
                "The selected Model needs a PokemonGalleryEntry with a matching source profile and visual child. Select that model explicitly, or configure its bones manually.");
            Newtonsoft.Json.Linq.JObject source;
            try { source = Newtonsoft.Json.Linq.JObject.Parse(entry.sourceProfile.text); }
            catch (Newtonsoft.Json.JsonException e) { throw new InvalidOperationException("The source profile is not valid JSON; select a valid profile/model or configure manually.", e); }
            Require(source.Value<int?>("dex") == entry.dex && source["skeleton"] is Newtonsoft.Json.Linq.JObject,
                "The source profile does not match this Model. Select its matching model/profile or configure its bones manually.");
            return source;
        }
        static BodyArchetype SourceBodyType(Newtonsoft.Json.Linq.JObject source)
        {
            BodyArchetype type;
            Require(Enum.TryParse(Text(source["archetype"]), true, out type) && Enum.IsDefined(typeof(BodyArchetype), type),
                "This source body type is not supported by the runtime. Configure a supported Body Type manually.");
            return type;
        }
        static bool OwnedBy(ProceduralBodyAnimator actor, Transform model)
        {
            if (!model || model == actor.transform || !model.IsChildOf(actor.transform)) return false;
            for (var current = model; current != actor.transform; current = current.parent)
                if (current.GetComponent<ProceduralBodyAnimator>() || current.GetComponent<Creature>() || current.GetComponent<PlayerMotor>()) return false;
            return true;
        }
        static bool VisibleWithin(ProceduralBodyAnimator actor, Transform visual)
        {
            for (var current = visual; current != actor.transform; current = current.parent)
                if (!current || !current.gameObject.activeSelf) return false;
            return true;
        }
        public static (Transform model, BodyArchetype type) Detect(ProceduralBodyAnimator actor, Transform selectedModel)
        {
            FullBodyAuthoring.RequireIdle();
            Require(actor, "Select a Procedural Body Animator first.");
            if (selectedModel) {
                Require(OwnedBy(actor, selectedModel), "Selected Model must belong to this actor, not another actor subtree. Clear Model to search this actor, or select its intended model explicitly.");
                return (selectedModel, SourceBodyType(ReadSource(selectedModel)));
            }
            var candidates = actor.GetComponentsInChildren<PokemonGalleryEntry>(true).Where(e => OwnedBy(actor, e.transform) && e.visual && e.visual.IsChildOf(e.transform) &&
                e.visual.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && VisibleWithin(actor, r.transform))).ToArray();
            Require(candidates.Length > 0, "No visible source-backed Model was found under this actor. Enable the intended model child (the actor may stay inactive), or select Model explicitly. Models without source metadata must be configured manually.");
            Require(candidates.Length == 1, "Multiple visible Models were found: " + string.Join(", ", candidates.Select(e => e.name)) + ". Select the intended Model explicitly, or disable the alternative model children.");
            var model = candidates[0].transform;
            return (model, SourceBodyType(ReadSource(model)));
        }
        static bool WingDirection(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.sqrMagnitude) && value.sqrMagnitude > 1e-8f;
        public static Vector3 CalibrateWingSpan(Transform model, JointChain wing)
        {
            Require(model && wing != null, "A wing needs its selected Model and source chain.");
            Require(WingDirection(wing.span) && WingDirection(wing.normal), "Source wing Span and Normal must be finite nonzero directions; correct the source or configure this wing manually.");
            Require(wing.joints != null && wing.joints.Length > 0 && wing.joints.Distinct().Count() == wing.joints.Length, "A source wing needs a nonempty chain of distinct joints.");
            var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.enabled && s.sharedMesh).ToArray();
            for (int i = 0; i < wing.joints.Length; i++) {
                var joint = wing.joints[i];
                Require(joint && joint != model && joint.IsChildOf(model), "Wing joints must belong to the selected Model.");
                if (i > 0) Require(joint.IsChildOf(wing.joints[i - 1]), "Wing joints must follow their actual root-to-tip ancestry.");
                Require(skins.Length == 0 || BodyMovementAuthoring.Weighted(joint, skins), "A wing joint has no influence on the selected skin; use its actual deforming chain.");
            }
            if (wing.joints.Length == 1) return wing.span;
            var span = model.InverseTransformPoint(wing.joints[wing.joints.Length - 1].position) - model.InverseTransformPoint(wing.joints[0].position);
            Require(WingDirection(span), "The imported wing chain has no measurable root-to-tip span. Correct its chain or configure this wing manually.");
            return span.normalized;
        }
        static float Floor(Transform visual)
        {
            var renderers = visual.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            Require(renderers.Length > 0, "Enable the selected Model: it has no active renderer from which to measure clearance.");
            return renderers.Min(r => r.bounds.min.y);
        }
        public static BodyMovementDraft BuildDraft(ProceduralBodyAnimator actor) => BuildDraft(actor, null, false, null);
        public static BodyMovementDraft BuildDraft(ProceduralBodyAnimator actor, Transform selectedModel) => BuildDraft(actor, selectedModel, true, null);
        public static BodyMovementDraft BuildDraft(ProceduralBodyAnimator actor, Transform selectedModel, BodyArchetype selectedType) => BuildDraft(actor, selectedModel, true, selectedType);
        static BodyMovementDraft BuildDraft(ProceduralBodyAnimator actor, Transform selectedModel, bool explicitSelection, BodyArchetype? selectedType)
        {
            Require(actor, "Select a Procedural Body Animator first.");
            var draft = BodyMovementDraft.From(actor);
            try
            {
                if (explicitSelection) draft.model = selectedModel;
                var model = draft.model;
                Require(model, "Select a Model before automatic configuration.");
                var entry = model.GetComponent<PokemonGalleryEntry>();
                var source = ReadSource(model);
                var type = SourceBodyType(source);
                Require(!selectedType.HasValue || selectedType.Value == type,
                    "Selected Body Type is " + selectedType + ", but this Model's source profile is " + type + ". Set Body Type to " + type + " for Auto-Configure, or configure compatible bones manually.");
                var skeleton = source["skeleton"]; var motion = source["motion"] as Newtonsoft.Json.Linq.JObject ?? new Newtonsoft.Json.Linq.JObject();
                Require(Items(skeleton["ambiguous"]).Length == 0,
                    "The source anatomy is ambiguous. Assign unambiguous bones from the selected Model manually, then Configure and Validate.");
                Require(!Flag(motion["sideways"], false),
                    "This source requests sideways movement, which the runtime does not implement. Configure its bones manually for standard forward movement instead.");
                var profile = BodyMotionProfile.For(type);
                profile.displayHeight = entry.displayHeight;
                profile.stride = Number(source["metrics"]?["stride"], profile.stride);
                profile.hover = Flag(motion["hover"], profile.hover);
                profile.multileg = Flag(motion["multileg"], profile.multileg);
                profile.bounce = Flag(motion["bounce"], profile.bounce);
                profile.ooze = Flag(motion["ooze"], profile.ooze);
                profile.burrow = Flag(motion["burrow"], profile.burrow);
                profile.verticalWave = Text(motion["wave"]) == "vertical";
                profile.hoverLift = Number(motion["hoverLift"], profile.hoverLift);
                profile.armSwing = Number(motion["armSwing"], profile.armSwing);
                CrawlStyle crawl; SwimStyle swim;
                if (Present(motion["crawl"]) && Enum.TryParse(Text(motion["crawl"]), true, out crawl)) profile.crawl = crawl;
                if (Present(motion["swim"])) {
                    Require(Enum.TryParse(Text(motion["swim"]), true, out swim) && Enum.IsDefined(typeof(SwimStyle), swim), "Unknown source swim module; configure this model manually.");
                    profile.swim = swim;
                }
                Require(float.IsFinite(profile.displayHeight) && profile.displayHeight > 0 && float.IsFinite(profile.stride) && profile.stride > 0,
                    "Source Display Height and Stride must be finite and positive.");
                Require(float.IsFinite(profile.armSwing) && profile.armSwing >= 0 && profile.armSwing <= 1, "Source Arm Swing must be between zero and one.");
                var spine = Joints(model, skeleton["spine"]);
                var rig = new BodyRig { model = model, waist = Unique(model, Text(skeleton["waist"])),
                    spine = spine.FirstOrDefault(), head = Present(skeleton["head"]) ? Unique(model, Text(skeleton["head"])) : null,
                    profile = profile, neck = Joints(model, skeleton["neck"]), body = Joints(model, skeleton["body"]),
                    tails = Chains(model, skeleton["tails"]) };
                var floor = Floor(entry.visual); var scale = Mathf.Abs(model.lossyScale.y);
                Require(float.IsFinite(scale) && scale > .0001f, "The selected Model has an invalid vertical scale.");
                var legs = Items(skeleton["legs"]);
                if (type == BodyArchetype.Biped || type == BodyArchetype.Quadruped) {
                    int count = type == BodyArchetype.Biped ? 2 : 4;
                    Require(legs.Length == count, "This source body type needs exactly " + count + " complete leg chains. Configure this model manually.");
                    legs = Enumerable.Range(0, count).Select(i => {
                        var matches = legs.Where(l => Text(l["role"]) == (i < 2 ? "leg" : "front-leg") && Text(l["side"]) == (i % 2 == 0 ? "L" : "R")).ToArray();
                        Require(matches.Length == 1, "The source profile needs exactly one chain for each leg role and side; configure this model manually.");
                        return matches[0];
                    }).ToArray();
                }
                int leftSet = 0, rightSet = 0;
                rig.legs = legs.Select(l => {
                    var side = Text(l["side"]); Require(side == "L" || side == "R", "A source leg has an ambiguous side; configure this model manually.");
                    var upper = Unique(model, Text(l["upper"])); var lower = Unique(model, Text(l["lower"])); var foot = Unique(model, Text(l["effector"]));
                    Require(upper.IsChildOf(rig.waist) && lower.IsChildOf(upper) && foot.IsChildOf(lower), "Source leg ancestry does not match the selected Model. Assign its actual deforming chains manually.");
                    var clearance = (foot.position.y - floor) / scale;
                    Require(float.IsFinite(clearance) && clearance >= -.001f && clearance <= entry.displayHeight * .3f, "Source foot clearance is outside the selected Model bounds. Configure clearance manually.");
                    return new LimbRig { upper = upper, lower = lower, foot = foot, right = side == "R", front = Text(l["role"]) == "front-leg",
                        set = profile.multileg ? (side == "R" ? rightSet++ : leftSet++) : 0, soleHeight = Mathf.Max(.001f, clearance) };
                }).ToArray();
                Require(rig.legs.SelectMany(l => new[] { l.upper, l.lower, l.foot }).Distinct().Count() == rig.legs.Length * 3,
                    "Source stepping chains share bones. Assign independent leg chains manually.");
                rig.arms = Items(skeleton["arms"]).Select(a => {
                    var side = Text(a["side"]); Require(side == "L" || side == "R", "A source arm has an ambiguous side; configure this model manually.");
                    float spread = Number(a["spread"], .3f); Require(float.IsFinite(spread), "Source arm spread must be finite.");
                    return new JointChain { right = side == "R", spread = Mathf.Clamp(spread, .2f, 1.2f),
                        joints = new[] { Unique(model, Text(a["upper"])), Unique(model, Text(a["lower"])), Unique(model, Text(a["effector"])) },
                        restJoints = Joints(model, a["chain"]) };
                }).ToArray();
                rig.wings = Items(skeleton["wings"]).Select((w, i) => {
                    Vector3 Direction(string key) { var values = Items(w[key]); Require(values.Length == 3, "A source wing needs three-component Span and Normal directions."); return new Vector3(values[0].ToObject<float>(), values[1].ToObject<float>(), values[2].ToObject<float>()); }
                    var side = Text(w["side"]); Require(side == "L" || side == "R", "A source wing has an ambiguous side; configure it manually.");
                    var wing = new JointChain { joints = Joints(model, w["chain"]), right = side == "R", set = i / 2, span = Direction("span"), normal = Direction("normal") };
                    wing.span = CalibrateWingSpan(model, wing);
                    return wing;
                }).ToArray();
                if (rig.wings.Length > 0) profile.wingArea = Items(skeleton["wings"]).Sum(w => Number(w["area"], .4f));
                var appendages = Chains(model, Present(motion["sway"]) ? motion["sway"] : skeleton["appendages"])
                    .Concat(Chains(model, motion["vines"]))
                    .Concat(Chains(model, motion["bounce"] is Newtonsoft.Json.Linq.JObject ? motion["bounce"]["leaves"] : null, true))
                    .Concat(Chains(model, motion["bounce"] is Newtonsoft.Json.Linq.JObject ? motion["bounce"]["hanging"] : null))
                    .Concat(Items(motion["ooze"] is Newtonsoft.Json.Linq.JObject ? motion["ooze"]["arms"] : null).Select(a => new JointChain { joints = Joints(model, a["chain"]), right = Text(a["side"]) == "R" }))
                    .ToArray();
                rig.members = Items(motion["members"]).Select(c => { var joints = Joints(model, c); Require(joints.Length > 0, "A source group member chain is empty."); return joints[0]; }).ToArray();
                rig.mass = type == BodyArchetype.Amorphous ? rig.body.Concat(Items(skeleton["appendages"]).SelectMany(c => Joints(model, c))).Distinct().ToArray() : new Transform[0];
                rig.columns = Items(motion["burrow"] is Newtonsoft.Json.Linq.JObject ? motion["burrow"]["columns"] : null).Select(c => {
                    var joints = Joints(model, c["joints"]); Require(joints.Length > 0, "A source burrow column needs actual joints.");
                    var mound = Unique(model, Text(c["base"])); Require(joints[0].IsChildOf(mound), "The source burrow column does not descend from its base; configure it manually.");
                    return new BurrowColumn { mound = mound, head = joints[0] };
                }).ToArray();
                rig.paddles = Flag(motion["paddles"], false) ? Chains(model, skeleton["appendages"]) : new JointChain[0];
                for (int i = 0; i < rig.paddles.Length; i++) { rig.paddles[i].right = i % 2 == 1; rig.paddles[i].set = i / 2; }
                var leafRoots = appendages.Where(c => c.leaf).Select(c => c.joints[0]).ToHashSet();
                rig.arms = rig.arms.Where(c => !leafRoots.Contains(c.joints[0])).ToArray();
                var owned = rig.arms.Concat(rig.wings).Concat(rig.paddles).SelectMany(c => c.joints).Concat(rig.members).Concat(rig.columns.Select(c => c.head)).ToHashSet();
                rig.appendages = appendages.Where(c => !c.joints.Any(owned.Contains)).GroupBy(c => c.joints[0]).Select(g => g.First()).ToArray();
                if (type == BodyArchetype.Roller) profile.rollerRadius = entry.visual.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).Max(r => Mathf.Max(r.bounds.extents.x, r.bounds.extents.z)) / scale;
                rig.Measure();
                if (rig.legs.Length > 0) {
                    rig.stanceLowering = Mathf.Max(0f, rig.legs.Max(l => l.upper.position.y - floor - l.sole - l.length * .78f)) / scale;
                    profile.stride = Mathf.Min(profile.stride, rig.legs.Min(l => l.length) * 1.5f);
                }
                Require(float.IsFinite(profile.stride) && profile.stride > 0 && float.IsFinite(rig.stanceLowering), "The source rig could not produce finite movement measurements.");
                draft.model = model; draft.rig = rig; draft.profile = profile;
                BodyMovementAuthoring.Validate(actor, draft);
                return draft;
            }
            catch { UnityEngine.Object.DestroyImmediate(draft); throw; }
        }
        public static void Apply(ProceduralBodyAnimator actor) => ApplyDraft(BuildDraft(actor), actor);
        public static void Apply(ProceduralBodyAnimator actor, Transform selectedModel) => ApplyDraft(BuildDraft(actor, selectedModel), actor);
        public static void Apply(ProceduralBodyAnimator actor, Transform selectedModel, BodyArchetype selectedType) => ApplyDraft(BuildDraft(actor, selectedModel, selectedType), actor);
        static void ApplyDraft(BodyMovementDraft draft, ProceduralBodyAnimator actor)
        {
            try { BodyMovementAuthoring.Apply(actor, draft); }
            finally { UnityEngine.Object.DestroyImmediate(draft); }
        }
    }
}
