using System;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Pokemon3D.Locomotion;
using Pokemon3D.Pokemon;

namespace Pokemon3D.Editor
{
    [CustomEditor(typeof(ProceduralBodyAnimator))]
    public sealed class BodyMovementInspector : UnityEditor.Editor
    {
        BodyMovementDraft draft; VisualElement root, anatomy; HelpBox result;
        ProceduralBodyAnimator Actor => (ProceduralBodyAnimator)target;
        void OnEnable() { Undo.undoRedoPerformed+=Reload; }
        void OnDisable() { Undo.undoRedoPerformed-=Reload; if(draft)DestroyImmediate(draft); }
        void Reload() { if(!target)return; if(draft)DestroyImmediate(draft);draft=BodyMovementDraft.From(Actor);if(root!=null)Build(); }
        public override VisualElement CreateInspectorGUI() { root=new VisualElement { name="bodyMovementPanel" }; Reload(); return root; }
        public BodyMovementDraft Draft => draft;
        static ObjectField Bone(string label,Transform value,Action<Transform> set,string name=null) {
            var field=new ObjectField(label){objectType=typeof(Transform),allowSceneObjects=true,value=value,name=name??label.Replace(" ","")};
            field.RegisterValueChangedCallback(e=>set(e.newValue as Transform));return field;
        }
        static FloatField Number(string label,float value,Action<float> set) { var field=new FloatField(label){value=value};field.RegisterValueChangedCallback(e=>set(e.newValue));return field; }
        static Toggle Flag(string label,bool value,Action<bool> set) { var field=new Toggle(label){value=value};field.RegisterValueChangedCallback(e=>set(e.newValue));return field; }
        static EnumField Choice(string label,Enum value,Action<Enum> set) { var field=new EnumField(label,value);field.RegisterValueChangedCallback(e=>set(e.newValue));return field; }
        void Build() {
            root.Clear(); var sheet=AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/BodyMovement/BodyMovementInspector.uss");if(sheet)root.styleSheets.Add(sheet);root.AddToClassList("body-movement-panel");
            root.Add(new Label("Body Movement"){name="panelTitle"});
            root.Add(new HelpBox("Detect Model and Body Type, then Auto-Configure Selected Model; or edit this draft and Configure and Validate manually. Opening this panel or changing draft fields does not change the scene.",HelpBoxMessageType.Info));
            string warning=BodyMovementAuthoring.StaleWarning(Actor);if(warning.Length>0)root.Add(new HelpBox(warning,HelpBoxMessageType.Warning){name="staleRigWarning"});
            root.Add(Bone("Model",draft.model,v=>{ if(draft.model!=v){draft.model=v;draft.rig=new BodyRig();BuildAnatomy();} },"movementModel"));
            root.Add(Choice("Body Type",draft.profile.archetype,v=>{
                if(draft.profile.archetype==(BodyArchetype)v)return;var next=BodyMotionProfile.For((BodyArchetype)v);next.displayHeight=draft.profile.displayHeight;next.stride=draft.profile.stride;draft.profile=next;Build();
            }));
            anatomy=new VisualElement {name="anatomy"};root.Add(anatomy);BuildAnatomy();
            var tuning=new Foldout {text="Advanced tuning",value=false,name="advancedTuning"};root.Add(tuning);
            tuning.Add(Number("Display Height",draft.profile.displayHeight,v=>draft.profile.displayHeight=v));
            tuning.Add(Number("Stride",draft.profile.stride,v=>draft.profile.stride=v));
            tuning.Add(Number("Stance Lowering",draft.rig.stanceLowering,v=>draft.rig.stanceLowering=v));
            tuning.Add(Choice("Traversal Mode",draft.traversalMode,v=>draft.traversalMode=(TraversalMode)v));
            var mask=new LayerMaskField("Terrain Mask",draft.terrainMask.value);mask.RegisterValueChangedCallback(e=>draft.terrainMask=e.newValue);tuning.Add(mask);
            tuning.Add(Choice("Crawl",draft.profile.crawl,v=>draft.profile.crawl=(CrawlStyle)v));
            tuning.Add(Choice("Swim",draft.profile.swim,v=>draft.profile.swim=(SwimStyle)v));
            tuning.Add(Flag("Hover",draft.profile.hover,v=>draft.profile.hover=v));tuning.Add(Flag("Multi-leg",draft.profile.multileg,v=>draft.profile.multileg=v));
            tuning.Add(Flag("Bounce",draft.profile.bounce,v=>draft.profile.bounce=v));tuning.Add(Flag("Ooze",draft.profile.ooze,v=>draft.profile.ooze=v));tuning.Add(Flag("Burrow",draft.profile.burrow,v=>draft.profile.burrow=v));
            tuning.Add(Flag("Vertical Wave",draft.profile.verticalWave,v=>draft.profile.verticalWave=v));
            tuning.Add(Number("Hover Lift",draft.profile.hoverLift,v=>draft.profile.hoverLift=v));tuning.Add(Number("Wing Area",draft.profile.wingArea,v=>draft.profile.wingArea=v));
            tuning.Add(Number("Roller Radius",draft.profile.rollerRadius,v=>draft.profile.rollerRadius=v));tuning.Add(Number("Arm Swing",draft.profile.armSwing,v=>draft.profile.armSwing=v));
            result=new HelpBox("Configure validates before applying. Invalid drafts leave the actor untouched. No scene or prefab is saved automatically.",HelpBoxMessageType.Info){name="configurationResult"};root.Add(result);
            var detectButton=new Button(()=>{
                try {
                    var detected=PokemonBodyMovementAutoRig.Detect(Actor,draft.model);
                    if(draft.model!=detected.model){draft.model=detected.model;draft.rig=new BodyRig();}
                    if(draft.profile.archetype!=detected.type){var next=BodyMotionProfile.For(detected.type);next.displayHeight=draft.profile.displayHeight;next.stride=draft.profile.stride;draft.profile=next;}
                    Build();result.text="Detected '"+detected.model.name+"' as "+detected.type+" in this draft only. No bones were configured or scene objects changed. Use Auto-Configure Selected Model or configure the draft manually.";result.messageType=HelpBoxMessageType.Info;
                }
                catch(Exception e){result.text=e.Message;result.messageType=HelpBoxMessageType.Error;}
            }){text="Detect Model and Body Type",name="detectMovementModel"};detectButton.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);root.Add(detectButton);
            var autoButton=new Button(()=>{
                try{PokemonBodyMovementAutoRig.Apply(Actor,draft.model,draft.profile.archetype);Reload();result.text="Automatically configured and validated from the selected model. Use Ctrl+Z to undo; save the scene when ready.";result.messageType=HelpBoxMessageType.Info;}
                catch(Exception e){result.text=e.Message;result.messageType=HelpBoxMessageType.Error;}
            }){text="Auto-Configure Selected Model",name="autoConfigureMovement"};autoButton.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);root.Add(autoButton);
            var button=new Button(()=>{
                try{BodyMovementAuthoring.Apply(Actor,draft);Reload();result.text="Configured and validated. References are synchronized; use Ctrl+Z to undo. Save the scene when ready.";result.messageType=HelpBoxMessageType.Info;}
                catch(Exception e){result.text=e.Message;result.messageType=HelpBoxMessageType.Error;}
            }){text="Configure and Validate",name="configureMovement"};button.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);root.Add(button);
            if(EditorApplication.isPlayingOrWillChangePlaymode)root.Add(new HelpBox("Stop Play yourself before configuring. This Inspector never changes Play state.",HelpBoxMessageType.Warning));
        }
        void BuildAnatomy() {
            anatomy.Clear();anatomy.Add(Bone("Waist",draft.rig.waist,v=>draft.rig.waist=v));anatomy.Add(Bone("Spine (optional)",draft.rig.spine,v=>draft.rig.spine=v));anatomy.Add(Bone("Head (optional)",draft.rig.head,v=>draft.rig.head=v));
            var type=draft.profile.archetype;
            if(type==BodyArchetype.Biped||type==BodyArchetype.Quadruped) {
                draft.SetLegCount(type==BodyArchetype.Biped?2:4);
                string[] names=type==BodyArchetype.Biped?new[]{"Left Leg","Right Leg"}:new[]{"Rear Left","Rear Right","Front Left","Front Right"};
                for(int i=0;i<names.Length;i++)Leg(anatomy,names[i],draft.rig.legs[i],false);
                if(type==BodyArchetype.Biped) Chains(anatomy,"Optional arms",()=>draft.rig.arms,v=>draft.rig.arms=v,true);
            } else if(type==BodyArchetype.Winged||type==BodyArchetype.Arthropod||type==BodyArchetype.Hopper) {
                var legs=new Foldout{text="Walking leg chains",value=false};anatomy.Add(legs);
                var count=new IntegerField("Leg count"){value=draft.rig.legs.Length};legs.Add(count);
                count.RegisterValueChangedCallback(e=>{draft.SetLegCount(Mathf.Clamp(e.newValue,0,16));BuildAnatomy();});
                for(int i=0;i<draft.rig.legs.Length;i++)Leg(legs,"Leg "+(i+1),draft.rig.legs[i],true);
            }
            if(type==BodyArchetype.Winged||type==BodyArchetype.Hopper)Chains(anatomy,"Optional arms",()=>draft.rig.arms,v=>draft.rig.arms=v,true);
            if(type==BodyArchetype.Winged)Chains(anatomy,"Wings",()=>draft.rig.wings,v=>draft.rig.wings=v);
            if(type==BodyArchetype.Serpentine||type==BodyArchetype.Aquatic)Joints(anatomy,"Body (head to tail)",()=>draft.rig.body,v=>draft.rig.body=v);
            if(type==BodyArchetype.Aquatic)Chains(anatomy,"Paddles",()=>draft.rig.paddles,v=>draft.rig.paddles=v);
            if(type==BodyArchetype.Group)Joints(anatomy,"Members",()=>draft.rig.members,v=>draft.rig.members=v);
            if(type==BodyArchetype.Amorphous)Joints(anatomy,"Mass",()=>draft.rig.mass,v=>draft.rig.mass=v);
            if(type==BodyArchetype.Burrower) {
                var columns=new Foldout{text="Burrow columns",value=false};anatomy.Add(columns);
                for(int i=0;i<draft.rig.columns.Length;i++){var c=draft.rig.columns[i];columns.Add(Bone("Mound "+(i+1),c.mound,v=>c.mound=v));columns.Add(Bone("Head "+(i+1),c.head,v=>c.head=v));}
                columns.Add(new Button(()=>{draft.rig.columns=draft.rig.columns.Concat(new[]{new BurrowColumn()}).ToArray();BuildAnatomy();}){text="Add column"});
                columns.Add(new Button(()=>{draft.rig.columns=draft.rig.columns.Take(Math.Max(0,draft.rig.columns.Length-1)).ToArray();BuildAnatomy();}){text="Remove last column"});
            }
            Chains(anatomy,"Optional tails",()=>draft.rig.tails,v=>draft.rig.tails=v);
            Chains(anatomy,"Optional appendages",()=>draft.rig.appendages,v=>draft.rig.appendages=v);
            Joints(anatomy,"Optional neck",()=>draft.rig.neck,v=>draft.rig.neck=v);
        }
        void Leg(VisualElement parent,string title,LimbRig leg,bool flags) {
            var row=new Foldout{text=title,value=true,name=title.Replace(" ","")};parent.Add(row);
            row.Add(Bone("Upper",leg.upper,v=>leg.upper=v));row.Add(Bone("Lower",leg.lower,v=>leg.lower=v));row.Add(Bone("Foot",leg.foot,v=>leg.foot=v));
            var detail=new Foldout{text="Leg clearance",value=false};row.Add(detail);detail.Add(Number("Sole Height",leg.soleHeight,v=>leg.soleHeight=v));
            if(flags){detail.Add(Flag("Right",leg.right,v=>leg.right=v));detail.Add(Flag("Front",leg.front,v=>leg.front=v));var set=new IntegerField("Set"){value=leg.set};set.RegisterValueChangedCallback(e=>leg.set=e.newValue);detail.Add(set);}
        }
        void Joints(VisualElement parent,string title,Func<Transform[]> get,Action<Transform[]> set) {
            var section=new Foldout{text=title,value=false};parent.Add(section);
            var values=get();for(int i=0;i<values.Length;i++){int index=i;section.Add(Bone("Joint "+(i+1),values[i],v=>get()[index]=v));}
            section.Add(new Button(()=>{set(get().Concat(new Transform[]{null}).ToArray());BuildAnatomy();}){text="Add joint"});
            section.Add(new Button(()=>{set(get().Take(Math.Max(0,get().Length-1)).ToArray());BuildAnatomy();}){text="Remove last joint"});
        }
        void Chains(VisualElement parent,string title,Func<JointChain[]> get,Action<JointChain[]> set,bool arms=false) {
            var section=new Foldout{text=title,value=false,name=arms?"optionalArms":null};parent.Add(section);
            for(int i=0;i<get().Length;i++) {
                var chain=get()[i];var row=new Foldout{text="Chain "+(i+1),value=false};section.Add(row);
                Joints(row,arms?"Upper Arm / Forearm / Hand":"Joints (root to tip)",()=>chain.joints,v=>chain.joints=v);
                row.Add(Flag("Right",chain.right,v=>chain.right=v));if(arms)row.Add(Number("Spread",chain.spread,v=>chain.spread=v));
                if(arms)Joints(row,"Optional shoulder / wrist rest joints",()=>chain.restJoints,v=>chain.restJoints=v);
                if(title=="Wings"){var span=new Vector3Field("Span"){value=chain.span};span.RegisterValueChangedCallback(e=>chain.span=e.newValue);row.Add(span);var normal=new Vector3Field("Normal"){value=chain.normal};normal.RegisterValueChangedCallback(e=>chain.normal=e.newValue);row.Add(normal);}
            }
            section.Add(new Button(()=>{set(get().Concat(new[]{new JointChain()}).ToArray());BuildAnatomy();}){text="Add chain"});
            section.Add(new Button(()=>{set(get().Take(Math.Max(0,get().Length-1)).ToArray());BuildAnatomy();}){text="Remove last chain"});
        }
    }
    [CustomEditor(typeof(Creature))]
    public sealed class CreatureMovementInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() {
            var root=new VisualElement();if(((Creature)target).GetComponent<ProceduralBodyAnimator>())root.Add(new HelpBox("Change the visible Model in Body Movement on Procedural Body Animator. Configure and Validate synchronizes it here; gameplay species is not a Pokemon dex number.",HelpBoxMessageType.Info));
            var p=serializedObject.GetIterator();p.NextVisible(true);while(p.NextVisible(false)){if(p.name=="model"&&((Creature)target).GetComponent<ProceduralBodyAnimator>())continue;root.Add(new PropertyField(p.Copy()));}return root;
        }
    }
    [CustomEditor(typeof(PlayerMotor))]
    public sealed class PlayerMovementInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() {
            var root=new VisualElement();bool managed=((PlayerMotor)target).GetComponent<ProceduralBodyAnimator>();
            if(managed)root.Add(new HelpBox("Change the trainer Model in Body Movement on Procedural Body Animator. Configure and Validate synchronizes the controller visual without changing controls.",HelpBoxMessageType.Info));
            var p=serializedObject.GetIterator();p.NextVisible(true);while(p.NextVisible(false)){if(managed&&p.name=="visual")continue;root.Add(new PropertyField(p.Copy()));}return root;
        }
    }
    [CustomEditor(typeof(PokemonCreatureVisualOverride))]
    public sealed class CreatureVisualRollbackInspector : VisualRollbackInspector {}
    [CustomEditor(typeof(TrainerVisualOverride))]
    public sealed class TrainerVisualRollbackInspector : VisualRollbackInspector {}
    public class VisualRollbackInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI() {
            var root=new VisualElement();root.Add(new HelpBox("Visual replacement is managed by Body Movement. Original rollback references are preserved.",HelpBoxMessageType.Info));
            var backup=new Foldout{text="Internal rollback (read only)",value=false};root.Add(backup);var p=serializedObject.GetIterator();p.NextVisible(true);while(p.NextVisible(false))backup.Add(new PropertyField(p.Copy()));backup.contentContainer.SetEnabled(false);return root;
        }
    }
}
