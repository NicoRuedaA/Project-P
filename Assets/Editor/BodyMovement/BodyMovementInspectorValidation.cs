using System;
using Object = UnityEngine.Object;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEditor.UIElements;
using Pokemon3D.Locomotion;
using Pokemon3D.Pokemon;

namespace Pokemon3D.Editor
{
    public static class BodyMovementInspectorValidation
    {
        static string PreservedScene(Scene scene) => string.Join("\n", scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).SelectMany(t =>
            new[] { EditorJsonUtility.ToJson(t.gameObject) }.Concat(t.GetComponents<Component>().Select(c => c ? EditorJsonUtility.ToJson(c) : "missing"))));
        static Func<bool> Geometry(Transform model) {
            var poses = model.GetComponentsInChildren<Transform>(true).Select(t => new { t, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray();
            string Skin() => string.Join(";", model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(s => s.sharedMesh.GetEntityId() + ":" + string.Join(",", s.bones.Select(b => b ? b.GetEntityId() : EntityId.None))));
            var skin = Skin();
            return () => model.GetComponentsInChildren<Transform>(true).Length == poses.Length && Skin() == skin && poses.All(p => p.t &&
                Vector3.Distance(p.position, p.t.localPosition) <= .000001f && Vector3.Distance(p.scale, p.t.localScale) <= .000001f && Quaternion.Angle(p.rotation, p.t.localRotation) <= .01f);
        }
        static void Click(VisualElement ui, string name) => typeof(Clickable).GetMethod("Invoke", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(ui.Q<Button>(name).clickable, new object[] { null });
        public static string RunAllSourceBodyTypes()
        {
            FullBodyAuthoring.RequireIdle(); var live = FullBodyAuthoring.Scene(); var before = PreservedScene(live); bool dirty = live.isDirty; checks = 0;
            var preview = EditorSceneManager.NewPreviewScene(); GameObject actor = null, model = null; BodyMovementInspector editor = null; BodyMovementDraft draft = null;
            var completed = new System.Collections.Generic.List<string>();
            try {
                actor = new GameObject("Isolated all-source-body-types test"); SceneManager.MoveGameObjectToScene(actor, preview);
                var creature = actor.AddComponent<Creature>(); var animator = actor.AddComponent<ProceduralBodyAnimator>();
                var types = (BodyArchetype[])Enum.GetValues(typeof(BodyArchetype));
                int[] representatives = { 9, 1, 123, 10, 72, 74, 100, 102, 70, 50, 88, 46 };
                Check(types.Length == representatives.Length, "Every offered body type has a representative test");
                string Prefab(int dex) => AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Pokemon" }).Select(AssetDatabase.GUIDToAssetPath)
                    .Single(p => System.IO.Path.GetFileName(p).StartsWith(dex.ToString("D3") + "-", StringComparison.Ordinal));
                foreach (var type in types) {
                    model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(representatives[(int)type])), preview);
                    model.transform.SetParent(actor.transform, false); model.SetActive(true); animator.InvalidateAuthoringBinding();
                    var entry = model.GetComponent<PokemonGalleryEntry>(); var geometry = Geometry(model.transform);
                    draft = PokemonBodyMovementAutoRig.BuildDraft(animator, model.transform, type);
                    Check(draft.profile.archetype == type && draft.rig.profile == draft.profile, "AutoConfigure_" + type + " preserves matching source type");
                    Check(draft.rig.model == model.transform && draft.rig.waist.IsChildOf(model.transform), "AutoConfigure_" + type + " owns actual selected bones");
                    if (type == BodyArchetype.Biped) Check(draft.rig.spine == null && draft.rig.legs.Length == 2, "AutoConfigure_Biped permits actual spineless Blastoise");
                    if (type == BodyArchetype.Quadruped) Check(draft.rig.legs.Length == 4 && draft.rig.arms.Length == 0, "AutoConfigure_Quadruped retains four legs and no arm duplication");
                    if (type == BodyArchetype.Winged) Check(draft.rig.wings.Length > 0 && draft.rig.wings.All(w => w.span.sqrMagnitude > 0 && w.normal.sqrMagnitude > 0), "AutoConfigure_Winged source geometry");
                    if (type == BodyArchetype.Serpentine) Check(draft.rig.body.Length >= 3 && draft.profile.crawl == CrawlStyle.Slither && draft.profile.verticalWave, "AutoConfigure_Serpentine source body and wave plane");
                    if (type == BodyArchetype.Aquatic) Check(draft.rig.paddles.Length == 2 && draft.profile.swim == SwimStyle.Pulse, "AutoConfigure_Aquatic source pulse and paddles");
                    if (type == BodyArchetype.Floater) Check(draft.profile.hover && draft.profile.hoverLift > 0, "AutoConfigure_Floater source hover");
                    if (type == BodyArchetype.Roller) Check(draft.profile.crawl == CrawlStyle.Roll && draft.profile.rollerRadius > 0, "AutoConfigure_Roller measured radius");
                    if (type == BodyArchetype.Group) Check(draft.rig.members.Length == 6, "AutoConfigure_Group six independent source members");
                    if (type == BodyArchetype.Hopper) Check(draft.profile.bounce && draft.rig.appendages.Any(c => c.leaf), "AutoConfigure_Hopper source bouncing leaves");
                    if (type == BodyArchetype.Burrower) Check(draft.rig.columns.Length == 1 && draft.rig.columns[0].head.IsChildOf(draft.rig.columns[0].mound), "AutoConfigure_Burrower source column ancestry");
                    if (type == BodyArchetype.Amorphous) Check(draft.profile.ooze && draft.rig.mass.Length > 3, "AutoConfigure_Amorphous actual support mass");
                    if (type == BodyArchetype.Arthropod) Check(draft.profile.multileg && draft.rig.legs.Length == 6 && draft.rig.legs.Select(l => l.set).Distinct().Count() == 3, "AutoConfigure_Arthropod source leg sets");
                    Check(geometry(), "AutoConfigure_" + type + " building preserves geometry, rest transforms and bone arrays");
                    Object.DestroyImmediate(draft); draft = null;
                    editor = (BodyMovementInspector)UnityEditor.Editor.CreateEditor(animator); var ui = editor.CreateInspectorGUI();
                    editor.Draft.model = model.transform; editor.Draft.profile.archetype = type;
                    typeof(BodyMovementInspector).GetMethod("Build", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(editor, null);
                    Click(ui, "autoConfigureMovement");
                    Check(ui.Q<HelpBox>("configurationResult").messageType == HelpBoxMessageType.Info && animator.SavedRig.model == model.transform, "AutoButton_" + type + " actual callback succeeds");
                    string bound = JsonUtility.ToJson(animator.SavedRig); Click(ui, "autoConfigureMovement");
                    Check(JsonUtility.ToJson(animator.SavedRig) == bound, "IdempotentConfigure_" + type);
                    editor.Draft.profile.stride *= .9f; float manualStride = editor.Draft.profile.stride;
                    Click(ui, "configureMovement");
                    Check(ui.Q<HelpBox>("configurationResult").messageType == HelpBoxMessageType.Info && animator.profile.stride == manualStride && animator.profile.archetype == type,
                        "ManualButton_" + type + " preserves explicit manual tuning rather than rebuilding from source");
                    Check(geometry() && animator.transform.localPosition == Vector3.zero && animator.transform.localScale == Vector3.one,
                        "Configure_" + type + " preserves model/root geometry");
                    completed.Add(type.ToString()); Object.DestroyImmediate(editor); editor = null;
                    foreach (var owner in actor.GetComponents<Component>()) Undo.ClearUndo(owner);
                    Object.DestroyImmediate(model); model = null;
                }
                model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(9)), preview); model.transform.SetParent(actor.transform, false);
                animator.InvalidateAuthoringBinding(); var blastoise = model.GetComponent<PokemonGalleryEntry>(); var originalProfile = blastoise.sourceProfile;
                void Reject(string name, Action action) {
                    var state = PreservedScene(preview); bool rejected = false;
                    try { action(); } catch (InvalidOperationException e) { rejected = !string.IsNullOrEmpty(e.Message); }
                    Check(rejected && PreservedScene(preview) == state, name + " rejects before mutation");
                }
                Reject("SelectedTypeMismatch", () => PokemonBodyMovementAutoRig.Apply(animator, model.transform, BodyArchetype.Quadruped));
                Reject("ClearedSelectedModel", () => PokemonBodyMovementAutoRig.Apply(animator, null, BodyArchetype.Biped));
                void SourceReject(string name, Action<Newtonsoft.Json.Linq.JObject> mutate) {
                    var source = Newtonsoft.Json.Linq.JObject.Parse(originalProfile.text); mutate(source); var text = new TextAsset(source.ToString()); blastoise.sourceProfile = text;
                    try { Reject(name, () => PokemonBodyMovementAutoRig.Apply(animator, model.transform, BodyArchetype.Biped)); }
                    finally { blastoise.sourceProfile = originalProfile; Object.DestroyImmediate(text); }
                }
                SourceReject("SourceDexMismatch", s => s["dex"] = 66);
                SourceReject("AmbiguousSourceAnatomy", s => s["skeleton"]["ambiguous"] = new Newtonsoft.Json.Linq.JArray("Waist"));
                SourceReject("IncompleteRequiredLeg", s => s["skeleton"]["legs"][0]["upper"] = null);
                SourceReject("NonfiniteSourceStride", s => s["metrics"]["stride"] = float.PositiveInfinity);
                var duplicate = new GameObject("Waist"); SceneManager.MoveGameObjectToScene(duplicate, preview); duplicate.transform.SetParent(model.transform, false);
                Reject("ActualDuplicateBone", () => PokemonBodyMovementAutoRig.Apply(animator, model.transform, BodyArchetype.Biped)); Object.DestroyImmediate(duplicate);
                var controller = new AnimatorOverrideController(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Starter Assets" }).First()))); var blocking = model.AddComponent<Animator>(); blocking.runtimeAnimatorController = controller;
                Check(blocking.runtimeAnimatorController != null, "ActiveAnimatorController fixture has a real controller");
                Reject("ActiveAnimatorController", () => PokemonBodyMovementAutoRig.Apply(animator, model.transform, BodyArchetype.Biped)); Object.DestroyImmediate(blocking); Object.DestroyImmediate(controller);
                draft = PokemonBodyMovementAutoRig.BuildDraft(animator, model.transform, BodyArchetype.Biped); draft.rig.legs[0].lower = draft.rig.legs[0].upper;
                Reject("InvalidManualBiped", () => BodyMovementAuthoring.Apply(animator, draft)); Object.DestroyImmediate(draft); draft = null;
                Object.DestroyImmediate(model); model = null;
                model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(98)), preview); model.transform.SetParent(actor.transform, false);
                animator.InvalidateAuthoringBinding(); var crab = model.GetComponent<PokemonGalleryEntry>();
                Reject("UnsupportedSidewaysSource", () => PokemonBodyMovementAutoRig.Apply(animator, model.transform, BodyArchetype.Arthropod));
                var crabSource = Newtonsoft.Json.Linq.JObject.Parse(crab.sourceProfile.text); crabSource["motion"]["sideways"] = false;
                var standard = new TextAsset(crabSource.ToString()); var crabProfile = crab.sourceProfile; crab.sourceProfile = standard;
                try { draft = PokemonBodyMovementAutoRig.BuildDraft(animator, model.transform, BodyArchetype.Arthropod); }
                finally { crab.sourceProfile = crabProfile; Object.DestroyImmediate(standard); }
                BodyMovementAuthoring.Apply(animator, draft);
                Check(!animator.profile.sideways && crab.sourceProfile == crabProfile, "SidewaysSource_ManualStandardForwardContinuation is usable and preserves source metadata");
                Check(PreservedScene(live) == before && live.isDirty == dirty, "AllBodyTypes_LiveValleyObjectsAndDirtyStatePreserved");
                return "PASS " + checks + " native checks: AutoConfigure, actual AutoButton, ManualButton, idempotence and rest/root/mesh preservation for " + string.Join(", ", completed) +
                    "; mismatched type/dex, empty model, ambiguous/missing/duplicate bones, nonfinite stride, active controller and invalid manual chains rejected without mutation; sideways explicitly unsupported with tested manual standard-forward continuation. No real scene/model changes or saves.";
            } finally {
                if (editor) Object.DestroyImmediate(editor); if (draft) Object.DestroyImmediate(draft);
                if (actor) { foreach (var owner in actor.GetComponentsInChildren<Component>(true)) if (owner) Undo.ClearUndo(owner); Object.DestroyImmediate(actor); }
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        public static string RunDetection()
        {
            FullBodyAuthoring.RequireIdle(); var live=FullBodyAuthoring.Scene();var liveBefore=PreservedScene(live);bool dirty=live.isDirty;checks=0;
            var preview=EditorSceneManager.NewPreviewScene();GameObject actor=null,model=null,alternative=null,nested=null;BodyMovementInspector editor=null;
            try {
                actor=new GameObject("Isolated model detection test");SceneManager.MoveGameObjectToScene(actor,preview);actor.AddComponent<Creature>();var animator=actor.AddComponent<ProceduralBodyAnimator>();
                GameObject Model(int dex, Transform parent) {var path=AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets(dex.ToString("000")+"-",new[]{"Assets/Prefabs/Pokemon"}).First(g=>AssetDatabase.GUIDToAssetPath(g).EndsWith(".prefab")));var o=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),preview);o.transform.SetParent(parent,false);return o;}
                var types=(BodyArchetype[])Enum.GetValues(typeof(BodyArchetype));int[] representatives={9,1,123,10,72,74,100,102,70,50,88,46};
                for(int i=0;i<types.Length;i++) {
                    model=Model(representatives[i],actor.transform);animator.InvalidateAuthoringBinding();editor=(BodyMovementInspector)UnityEditor.Editor.CreateEditor(animator);var ui=editor.CreateInspectorGUI();
                    editor.Draft.model=null;editor.Draft.profile.archetype=BodyArchetype.Quadruped;editor.Draft.profile.stride=.42f;
                    var state=PreservedScene(preview);var geometry=Geometry(model.transform);Click(ui,"detectMovementModel");
                    Check(editor.Draft.model==model.transform&&editor.Draft.profile.archetype==types[i],"DetectButton_"+types[i]+" discovers matching source type");
                    Check(PreservedScene(preview)==state&&geometry(),"DetectButton_"+types[i]+" changes only draft, not actor/rest/model");
                    Check(ui.Q<ObjectField>("movementModel").value==model.transform&&editor.Draft.profile.stride==.42f,"DetectButton_"+types[i]+" refreshes model UI and preserves authored tuning");
                    Check(ui.Q<Button>("detectMovementModel")!=null&&ui.Q<Button>("autoConfigureMovement")!=null&&ui.Q<Button>("configureMovement")!=null,"ThreeDistinctButtons_"+types[i]);
                    Click(ui,"autoConfigureMovement");Check(ui.Q<HelpBox>("configurationResult").messageType==HelpBoxMessageType.Info&&animator.SavedRig.model==model.transform,"DetectThenAuto_"+types[i]);
                    editor.Draft.profile.stride*=.9f;float stride=editor.Draft.profile.stride;Click(ui,"configureMovement");Check(animator.profile.stride==stride&&ui.Q<HelpBox>("configurationResult").messageType==HelpBoxMessageType.Info,"DetectThenManualTuning_"+types[i]);
                    Object.DestroyImmediate(editor);editor=null;foreach(var owner in actor.GetComponents<Component>())Undo.ClearUndo(owner);Object.DestroyImmediate(model);model=null;
                }
                model=Model(9,actor.transform);alternative=Model(66,actor.transform);alternative.SetActive(false);actor.SetActive(false);animator.InvalidateAuthoringBinding();
                editor=(BodyMovementInspector)UnityEditor.Editor.CreateEditor(animator);var panel=editor.CreateInspectorGUI();editor.Draft.model=null;
                var before=PreservedScene(preview);Click(panel,"detectMovementModel");Check(editor.Draft.model==model.transform&&editor.Draft.profile.archetype==BodyArchetype.Biped&&PreservedScene(preview)==before,"InactiveActor_ActiveSelfVisualBeatsDisabledAlternative");
                Check(!actor.activeSelf&&!alternative.activeSelf,"DetectionNeverActivatesActorOrAlternative");
                editor.Draft.model=alternative.transform;editor.Draft.rig.waist=alternative.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="Waist");editor.Draft.profile.stride=.31f;var waist=editor.Draft.rig.waist;
                before=PreservedScene(preview);Click(panel,"detectMovementModel");Check(editor.Draft.model==alternative.transform&&editor.Draft.rig.waist==waist&&editor.Draft.profile.stride==.31f&&PreservedScene(preview)==before,"ExplicitValidDraftSelectionPreservesManualRigAndTuning");
                void RejectButton(string name) {var state=PreservedScene(preview);var draftBefore=JsonUtility.ToJson(editor.Draft);Click(panel,"detectMovementModel");Check(panel.Q<HelpBox>("configurationResult").messageType==HelpBoxMessageType.Error&&JsonUtility.ToJson(editor.Draft)==draftBefore&&PreservedScene(preview)==state,name+" rejects without draft or scene mutation");}
                editor.Draft.model=null;alternative.SetActive(true);RejectButton("AmbiguousVisibleModels");alternative.SetActive(false);
                var entry=model.GetComponent<PokemonGalleryEntry>();var source=entry.sourceProfile;entry.sourceProfile=null;RejectButton("MissingSourceProfile");entry.sourceProfile=source;
                var json=Newtonsoft.Json.Linq.JObject.Parse(source.text);json["archetype"]="unknown-family";var invalid=new TextAsset(json.ToString());entry.sourceProfile=invalid;
                try{RejectButton("UnknownSourceBodyType");}finally{entry.sourceProfile=source;Object.DestroyImmediate(invalid);}
                nested=new GameObject("Unrelated nested actor");SceneManager.MoveGameObjectToScene(nested,preview);nested.transform.SetParent(actor.transform,false);nested.AddComponent<ProceduralBodyAnimator>();var other=Model(1,nested.transform);
                editor.Draft.model=other.transform;RejectButton("ExplicitUnrelatedActorSelection");editor.Draft.model=null;before=PreservedScene(preview);Click(panel,"detectMovementModel");
                Check(editor.Draft.model==model.transform&&PreservedScene(preview)==before,"DiscoveryIgnoresOtherActorSubtrees");
                editor.Draft.model=null;model.SetActive(false);RejectButton("NoVisibleModel");
                Check(PreservedScene(live)==liveBefore&&live.isDirty==dirty,"Detection_LiveValleyAndDirtyStatePreserved");
                return "PASS "+checks+" native detection checks: actual third button for all12 source body types; Detect then Auto and manual tuning; inactive actor, disabled alternatives, explicit selection, missing/unknown/ambiguous source, unrelated actor and no visible model; detection changes draft only and preserves the live scene.";
            } finally {
                if(editor)Object.DestroyImmediate(editor);if(actor){foreach(var owner in actor.GetComponentsInChildren<Component>(true))if(owner)Undo.ClearUndo(owner);Object.DestroyImmediate(actor);}EditorSceneManager.ClosePreviewScene(preview);
            }
        }
        public static string RunWingCalibration()
        {
            FullBodyAuthoring.RequireIdle();var live=FullBodyAuthoring.Scene();var state=PreservedScene(live);bool dirty=live.isDirty;checks=0;
            var preview=EditorSceneManager.NewPreviewScene();GameObject actor=null,model=null;BodyMovementInspector editor=null;
            try {
                actor=new GameObject("Isolated wing calibration test");SceneManager.MoveGameObjectToScene(actor,preview);var a=actor.AddComponent<ProceduralBodyAnimator>();
                GameObject Model(int dex) {var path=AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets(dex.ToString("000")+"-",new[]{"Assets/Prefabs/Pokemon"}).First(g=>AssetDatabase.GUIDToAssetPath(g).EndsWith(".prefab")));var value=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),preview);value.transform.SetParent(actor.transform,false);return value;}
                foreach(int dex in new[]{18,6,17,16,123}) {
                    model=Model(dex);a.InvalidateAuthoringBinding();editor=(BodyMovementInspector)UnityEditor.Editor.CreateEditor(a);var ui=editor.CreateInspectorGUI();editor.Draft.model=model.transform;editor.Draft.profile.archetype=(BodyArchetype)Enum.Parse(typeof(BodyArchetype),Newtonsoft.Json.Linq.JObject.Parse(model.GetComponent<PokemonGalleryEntry>().sourceProfile.text).Value<string>("archetype"),true);var geometry=Geometry(model.transform);
                    Click(ui,"autoConfigureMovement");Check(ui.Q<HelpBox>("configurationResult").messageType==HelpBoxMessageType.Info,"WingAutoButton_"+dex);
                    var source=Newtonsoft.Json.Linq.JObject.Parse(model.GetComponent<PokemonGalleryEntry>().sourceProfile.text);var sourceWings=(Newtonsoft.Json.Linq.JArray)source["skeleton"]["wings"];
                    foreach(var wing in a.SavedRig.wings) {
                        var metadata=sourceWings[Array.IndexOf(a.SavedRig.wings,wing)];var normal=metadata["normal"].Select(t=>t.ToObject<float>()).ToArray();
                        Check(wing.normal==new Vector3(normal[0],normal[1],normal[2])&&wing.right==(metadata.Value<string>("side")=="R"),"WingCalibration_"+dex+" preserves normal and side");
                        if(wing.joints.Length>1) {var direction=(model.transform.InverseTransformPoint(wing.joints.Last().position)-model.transform.InverseTransformPoint(wing.joints[0].position)).normalized;
                            Check(Vector3.Dot(wing.span,direction)>.99999f,"WingCalibration_"+dex+" aligns with imported rest geometry");}
                        else {var span=metadata["span"].Select(t=>t.ToObject<float>()).ToArray();Check(wing.span==new Vector3(span[0],span[1],span[2]),"SingletonWing_"+dex+" preserves authored direction without guessing");}
                    }
                    string bound=JsonUtility.ToJson(a.SavedRig);Click(ui,"autoConfigureMovement");Check(JsonUtility.ToJson(a.SavedRig)==bound&&geometry()&&a.traversalMode==TraversalMode.Ground,"WingCalibration_"+dex+" is idempotent and preserves rest geometry/Ground");
                    Object.DestroyImmediate(editor);editor=null;foreach(var owner in actor.GetComponents<Component>())Undo.ClearUndo(owner);Object.DestroyImmediate(model);model=null;
                }
                model=Model(18);a.InvalidateAuthoringBinding();var entry=model.GetComponent<PokemonGalleryEntry>();var original=entry.sourceProfile;
                void Reject(string name,Action action){var before=PreservedScene(preview);bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Check(rejected&&PreservedScene(preview)==before,name+" rejects before mutation");}
                void SourceReject(string name,Action<Newtonsoft.Json.Linq.JObject> mutate){var source=Newtonsoft.Json.Linq.JObject.Parse(original.text);mutate(source);var text=new TextAsset(source.ToString());entry.sourceProfile=text;
                    try{Reject(name,()=>PokemonBodyMovementAutoRig.Apply(a,model.transform,BodyArchetype.Winged));}finally{entry.sourceProfile=original;Object.DestroyImmediate(text);}}
                SourceReject("ZeroSourceWingSpan",s=>s["skeleton"]["wings"][0]["span"]=new Newtonsoft.Json.Linq.JArray(0,0,0));
                SourceReject("NonfiniteSourceWingSpan",s=>s["skeleton"]["wings"][0]["span"]=new Newtonsoft.Json.Linq.JArray(float.NaN,0,0));
                SourceReject("ZeroSourceWingNormal",s=>s["skeleton"]["wings"][0]["normal"]=new Newtonsoft.Json.Linq.JArray(0,0,0));
                SourceReject("EmptySourceWingChain",s=>s["skeleton"]["wings"][0]["chain"]=new Newtonsoft.Json.Linq.JArray());
                SourceReject("DuplicateWingJoint",s=>s["skeleton"]["wings"][0]["chain"]=new Newtonsoft.Json.Linq.JArray("LShoulder","LArm","LShoulder"));
                SourceReject("ReversedWingAncestry",s=>s["skeleton"]["wings"][0]["chain"]=new Newtonsoft.Json.Linq.JArray("LHand","LForeArm","LArm"));
                SourceReject("AmbiguousWingSide",s=>s["skeleton"]["wings"][0]["side"]="unknown");
                var draft=PokemonBodyMovementAutoRig.BuildDraft(a,model.transform,BodyArchetype.Winged);
                try {var wing=draft.rig.wings[0];var outsider=actor.transform;var joints=wing.joints;wing.joints=new[]{joints[0],outsider};Reject("ForeignWingJoint",()=>PokemonBodyMovementAutoRig.CalibrateWingSpan(model.transform,wing));wing.joints=joints;
                    var saved=joints.Select(t=>t.localPosition).ToArray();for(int i=1;i<joints.Length;i++)joints[i].position=joints[0].position;
                    try{Reject("DegenerateMeasuredWingSpan",()=>PokemonBodyMovementAutoRig.CalibrateWingSpan(model.transform,wing));}finally{for(int i=1;i<joints.Length;i++)joints[i].localPosition=saved[i];}
                    var fake=new GameObject("Nondeforming wing bone");SceneManager.MoveGameObjectToScene(fake,preview);fake.transform.SetParent(model.transform,false);wing.joints=new[]{fake.transform};
                    try{Reject("NondeformingWingJoint",()=>PokemonBodyMovementAutoRig.CalibrateWingSpan(model.transform,wing));}finally{wing.joints=joints;Object.DestroyImmediate(fake);}
                } finally {Object.DestroyImmediate(draft);}
                Check(PreservedScene(live)==state&&live.isDirty==dirty,"WingCalibration_LiveSceneAndDirtyStatePreserved");
                return "PASS "+checks+" native wing-calibration checks: Pidgeot, Charizard, Pidgeotto, Pidgey, singleton Scyther; actual Auto callback, geometry alignment, source normal/side preservation, idempotence, unchanged Ground/rest/model, malformed source/ancestry/ownership/deformation/degenerate geometry rejected without mutation.";
            } finally {if(editor)Object.DestroyImmediate(editor);if(actor){foreach(var owner in actor.GetComponentsInChildren<Component>(true))if(owner)Undo.ClearUndo(owner);Object.DestroyImmediate(actor);}EditorSceneManager.ClosePreviewScene(preview);}
        }
        [MenuItem("Wildbound/Validate Body Movement Inspector")]
        static void ValidateMenu() { Debug.Log(Run()); }
        [MenuItem("Wildbound/Validate Body Movement Inspector", true)]
        static bool CanValidateMenu() => !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating;
        sealed class ValidationWindow : EditorWindow {}
        static int checks;
        static void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException("Body Movement Inspector: "+message);}
        public static string Run() {
            FullBodyAuthoring.RequireIdle(); var live=FullBodyAuthoring.Scene();bool dirty=live.isDirty;var snapshot=PreservedScene(live);checks=0;
            var selection=Selection.activeObject;var preview=EditorSceneManager.NewPreviewScene();GameObject actor=null;BodyMovementInspector editor=null;BodyMovementDraft draft=null;ValidationWindow window=null;
            try {
                actor=new GameObject("Isolated movement authoring test");SceneManager.MoveGameObjectToScene(actor,preview);
                var c=actor.AddComponent<Creature>();var a=actor.AddComponent<ProceduralBodyAnimator>();var player=actor.AddComponent<PlayerMotor>();
                GameObject Model(string path){var o=(GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path),preview);o.transform.SetParent(actor.transform,false);return o;}
                var machop=Model("Assets/Prefabs/Pokemon/066-machop.prefab");var bulb=Model("Assets/Prefabs/Pokemon/001-bulbasaur.prefab");
                c.model=bulb.transform;a.Bind(PokemonPrefabAuthoring.BindMachop(machop.transform),c);
                var marker=actor.AddComponent<PokemonCreatureVisualOverride>();marker.originalModel=machop.transform;marker.originalRig=a.SavedRig;marker.originalProfile=a.profile;marker.replacementModel=machop.transform;
                var trainer=actor.AddComponent<TrainerVisualOverride>();trainer.originalVisual=machop.transform;trainer.replacementVisual=machop.transform;
                string originalRig=JsonUtility.ToJson(marker.originalRig),originalProfile=JsonUtility.ToJson(marker.originalProfile);
                var baseline=FullBodyAuthoring.Snapshot(preview);
                editor=(BodyMovementInspector)UnityEditor.Editor.CreateEditor(a);var ui=editor.CreateInspectorGUI();
                window=ScriptableObject.CreateInstance<ValidationWindow>();window.titleContent=new GUIContent("Isolated Body Movement Validation");window.ShowUtility();window.rootVisualElement.Add(ui);
                Check(ui.panel!=null,"Inspector tree attached to an actual editor panel");
                Check(ui.Q<ObjectField>("movementModel")!=null&&ui.Q<Button>("configureMovement")!=null&&ui.Q<Button>("autoConfigureMovement")!=null,"model control and separate manual/automatic configure buttons");
                Check(ui.Q<HelpBox>("staleRigWarning")!=null,"actionable stale visible Bulbasaur versus Machop warning");
                var type=ui.Query<EnumField>().ToList().First(f=>f.label=="Body Type");
                type.value=BodyArchetype.Quadruped;
                Check(editor.Draft.profile.archetype==BodyArchetype.Quadruped,"body-type user callback changes draft");
                foreach(var label in new[]{"RearLeft","RearRight","FrontLeft","FrontRight"})Check(ui.Q<Foldout>(label)!=null,"quadruped row "+label);
                Check(ui.Q<Foldout>("optionalArms")==null,"quadruped does not show duplicate Arms");
                Check(editor.Draft.profile!=a.profile&&editor.Draft.rig!=a.SavedRig,"draft never aliases live profile or rig");
                FullBodyAuthoring.AssertSnapshot(baseline,FullBodyAuthoring.Snapshot(preview));checks++;
                bool rejected=false;try{BodyMovementAuthoring.Apply(a,editor.Draft);}catch(InvalidOperationException e){rejected=e.Message.Contains("old model")||e.Message.Contains("inside the selected");}
                Check(rejected,"stale old bones rejected before mutation");FullBodyAuthoring.AssertSnapshot(baseline,FullBodyAuthoring.Snapshot(preview));checks++;
                draft=BodyMovementDraft.From(a);draft.model=machop.transform;draft.rig=PokemonPrefabAuthoring.BindMachop(machop.transform);draft.profile=draft.rig.profile;
                void RejectNumeric(string label,Action mutate,Action restore) {
                    var before=FullBodyAuthoring.Snapshot(preview);mutate(); bool failed=false;
                    try { BodyMovementAuthoring.Apply(a,draft); } catch(InvalidOperationException e) { failed=e.Message.Contains(label); }
                    finally { restore(); }
                    Check(failed,"actionable invalid "+label+" rejected before mutation");
                    FullBodyAuthoring.AssertSnapshot(before,FullBodyAuthoring.Snapshot(preview));checks++;
                }
                float originalSpread=draft.rig.arms[0].spread;
                foreach(float invalid in new[]{float.NaN,float.PositiveInfinity,.1f,1.3f})
                    RejectNumeric("Spread",()=>draft.rig.arms[0].spread=invalid,()=>draft.rig.arms[0].spread=originalSpread);
                var wing=new JointChain { joints=new[]{draft.rig.arms[0].joints[0],draft.rig.arms[0].joints[1]} };draft.rig.wings=new[]{wing};
                foreach(var invalid in new[]{new Vector3(float.NaN,0,0),new Vector3(float.PositiveInfinity,0,0),Vector3.zero,new Vector3(float.MaxValue,0,0)}) {
                    RejectNumeric("Wing Span",()=>wing.span=invalid,()=>wing.span=Vector3.right);
                    RejectNumeric("Wing Normal",()=>wing.normal=invalid,()=>wing.normal=Vector3.up);
                }
                draft.rig.wings=new JointChain[0];
                BodyMovementAuthoring.Apply(a,draft);
                Check(c.model==machop.transform&&a.SavedRig.model==machop.transform&&BodyMovementAuthoring.PlayerVisual(player)==machop.transform,"valid biped synchronizes Creature, movement and player model");
                Check(a.Rig.profile==a.profile&&a.SavedRig.profile==a.profile,"biped cached/top-level profiles synchronized");
                var selectedModel=ui.Q<ObjectField>("movementModel");
                selectedModel.value=machop.transform;selectedModel.value=bulb.transform;
                Check(editor.Draft.model==bulb.transform&&c.model==machop.transform&&a.SavedRig.model==machop.transform,
                    "selected inspector Model overrides the still-live Machop without mutating it");
                var beforeAuto=FullBodyAuthoring.Snapshot(preview);
                rejected=false;try{PokemonBodyMovementAutoRig.Apply(a,(Transform)null);}catch(InvalidOperationException e){rejected=e.Message.Contains("Select a Model");}
                Check(rejected,"cleared explicit Model does not silently fall back to the live model");FullBodyAuthoring.AssertSnapshot(beforeAuto,FullBodyAuthoring.Snapshot(preview));checks++;
                var entry=bulb.GetComponent<PokemonGalleryEntry>();
                var sourceProfile=entry.sourceProfile;entry.sourceProfile=null;
                beforeAuto=FullBodyAuthoring.Snapshot(preview);
                rejected=false;try{PokemonBodyMovementAutoRig.Apply(a,editor.Draft.model);}catch(InvalidOperationException e){rejected=e.Message.Contains("source profile");}
                Check(rejected,"missing source profile rejected");FullBodyAuthoring.AssertSnapshot(beforeAuto,FullBodyAuthoring.Snapshot(preview));checks++;
                entry.sourceProfile=sourceProfile;
                var duplicate=new GameObject("LThigh");SceneManager.MoveGameObjectToScene(duplicate,preview);duplicate.transform.SetParent(bulb.transform,false);
                beforeAuto=FullBodyAuthoring.Snapshot(preview);rejected=false;
                try{PokemonBodyMovementAutoRig.Apply(a,editor.Draft.model);}catch(InvalidOperationException e){rejected=e.Message.Contains("exactly once");}
                Check(rejected,"duplicate source bone rejected");FullBodyAuthoring.AssertSnapshot(beforeAuto,FullBodyAuthoring.Snapshot(preview));checks++;
                UnityEngine.Object.DestroyImmediate(duplicate);
                var blockingAnimator=bulb.AddComponent<Animator>();var controller=new AnimatorOverrideController(AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:AnimatorController", new[] { "Assets/Starter Assets" }).First())));blockingAnimator.runtimeAnimatorController=controller;
                beforeAuto=FullBodyAuthoring.Snapshot(preview);rejected=false;
                try{PokemonBodyMovementAutoRig.Apply(a,editor.Draft.model);}catch(InvalidOperationException e){rejected=e.Message.Contains("Animator controller");}
                Check(rejected,"active Animator controller rejected");FullBodyAuthoring.AssertSnapshot(beforeAuto,FullBodyAuthoring.Snapshot(preview));checks++;
                UnityEngine.Object.DestroyImmediate(blockingAnimator);UnityEngine.Object.DestroyImmediate(controller);
                PokemonBodyMovementAutoRig.Apply(a,editor.Draft.model);
                UnityEngine.Object.DestroyImmediate(draft);draft=BodyMovementDraft.From(a);
                Check(draft.rig.legs.All(l=>float.IsFinite(l.soleHeight)&&l.soleHeight>0)&&float.IsFinite(draft.rig.stanceLowering)&&draft.rig.stanceLowering>=0&&
                    float.IsFinite(draft.profile.stride)&&draft.profile.stride>0&&draft.profile.stride<=a.Rig.legs.Min(l=>l.length)*1.5f+.0001f,
                    "automatic source profile yields finite measured sole, stance and stride");
                for(int i=0;i<4;i++)Check(draft.rig.legs[i].upper.name==new[]{"LThigh","RThigh","LArm","RArm"}[i],"automatic rear/front source role mapping "+i);
                Check(c.model==bulb.transform&&a.SavedRig.model==bulb.transform&&a.Rig.model==bulb.transform&&BodyMovementAuthoring.PlayerVisual(player)==bulb.transform,"valid quadruped synchronizes all model owners");
                Check(a.SavedRig.legs.Length==4&&a.SavedRig.arms.Length==0&&a.SavedRig.FourLegged,"quad four real legs and no free arms");
                for(int i=0;i<4;i++)Check(a.SavedRig.legs[i].right==(i%2==1)&&a.SavedRig.legs[i].front==(i>=2),"automatic leg side/front flags "+i);
                Check(marker.replacementModel==bulb.transform&&trainer.replacementVisual==bulb.transform&&trainer.proceduralAnimator==a,"visual replacement markers synchronized");
                Check(marker.originalModel==machop.transform&&JsonUtility.ToJson(marker.originalRig)==originalRig&&JsonUtility.ToJson(marker.originalProfile)==originalProfile&&trainer.originalVisual==machop.transform,"original rollback data preserved");
                Check(a.profile.archetype==BodyArchetype.Quadruped&&a.Rig.profile==a.profile&&a.SavedRig.profile==a.profile,"quad cached/profile agreement");
                Undo.PerformUndo();Check(c.model==machop.transform&&a.SavedRig.model==machop.transform&&a.profile.archetype==BodyArchetype.Biped&&marker.replacementModel==machop.transform&&a.Rig==null,"Undo restores the original live model and invalidates transient cache");
                Undo.PerformRedo();Check(c.model==bulb.transform&&a.SavedRig.model==bulb.transform&&a.profile.archetype==BodyArchetype.Quadruped&&marker.replacementModel==bulb.transform&&a.Rig==null,"Redo restores synchronized owners without stale transient cache");
                a.BindSavedRig();Check(a.Rig.model==bulb.transform&&a.Rig.profile==a.profile,"explicit rebind after Undo/Redo or runtime Awake reconstructs correct cache");
                var current=FullBodyAuthoring.Snapshot(preview);draft.rig.legs[0].lower=draft.rig.legs[0].upper;
                rejected=false;try{BodyMovementAuthoring.Apply(a,draft);}catch(InvalidOperationException){rejected=true;}Check(rejected,"zero/wrong chain rejected");FullBodyAuthoring.AssertSnapshot(current,FullBodyAuthoring.Snapshot(preview));checks++;
                Check(live.isDirty==dirty,"live dirty flag preserved");Check(snapshot==PreservedScene(live),"Live scene serialized snapshot preserved");checks++;
                return "PASS "+checks+" isolated native checks: UI Toolkit controls and draft-only selected Model overrides stale live model, automatic Bulbasaur source-role mapping and measured rig, missing profile/duplicate bone/active controller rejected atomically, real Machop/Bulbasaur skin validation, biped/quad owner+profile sync, original rollback preserved, Undo/Redo caches, invalid-chain and non-finite spread/wing geometry rejection. Live Valley snapshot and dirty state unchanged; no save or Play changes.";
            } finally {
                if(window)window.Close();if(window)UnityEngine.Object.DestroyImmediate(window);
                if(Selection.activeObject!=selection)Selection.activeObject=selection;
                if(editor)UnityEngine.Object.DestroyImmediate(editor);if(draft)UnityEngine.Object.DestroyImmediate(draft);
                if(actor){foreach(var owner in actor.GetComponents<Component>())if(owner)Undo.ClearUndo(owner);UnityEngine.Object.DestroyImmediate(actor);}
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }
    }
}
