using System.Collections.Generic;
using UnityEngine;
namespace Pokemon3D {
public static class World {
 public static readonly Vector3 Camp=new Vector3(0,0,-28);
 static Dictionary<Color,Material> materials=new Dictionary<Color,Material>();
 public static GameObject Part(string name,PrimitiveType type,Vector3 position,Vector3 scale,Color color,Transform parent){var o=GameObject.CreatePrimitive(type);o.name=name;if(parent){o.transform.SetParent(parent,false);o.transform.localPosition=position;}else o.transform.position=position;o.transform.localScale=scale;Material m;if(!materials.TryGetValue(color,out m)||!m){m=ToonMaterials.Create(color);materials[color]=m;}ToonStyle.ApplyCurrent(m);o.GetComponent<Renderer>().sharedMaterial=m;return o;}
 static void Scenery(string name,PrimitiveType type,Vector3 pos,Vector3 size,Color color,Transform parent){var o=Part(name,type,pos,size,color,parent);o.layer=8;}
 public static void Build(Game game){Random.InitState(7042);materials.Clear();
 RenderSettings.ambientLight=new Color(.62f,.72f,.77f);RenderSettings.fog=true;RenderSettings.fogColor=new Color(.72f,.84f,.86f);RenderSettings.fogMode=FogMode.ExponentialSquared;RenderSettings.fogDensity=.004f;
 var sun=new GameObject("Sun").AddComponent<Light>();sun.type=LightType.Directional;sun.color=new Color(1,.94f,.79f);sun.intensity=1.15f;sun.shadows=LightShadows.Soft;sun.transform.rotation=Quaternion.Euler(42,-35,0);
 var env=new GameObject("Valley / environment").transform;Scenery("Meadow",PrimitiveType.Cube,new Vector3(0,-.5f,0),new Vector3(150,1,150),new Color(.48f,.65f,.37f),env);
 for(int i=0;i<42;i++){float angle=i*Mathf.PI*2/42;Vector3 pos=new Vector3(Mathf.Cos(angle)*76,6,Mathf.Sin(angle)*76);Scenery("Mountain ridge",PrimitiveType.Sphere,pos,new Vector3(Random.Range(15,25),Random.Range(18,30),Random.Range(15,25)),new Color(.36f,.49f,.48f),env);}
 // Elevated plateau, accessible by a broad ramp.
 Scenery("Plateau",PrimitiveType.Cube,new Vector3(30,2,28),new Vector3(20,4,20),new Color(.53f,.57f,.39f),env);
 var ramp=Part("Plateau ramp",PrimitiveType.Cube,new Vector3(30,1.5f,12),new Vector3(9,.7f,17),new Color(.65f,.62f,.45f),env);ramp.transform.rotation=Quaternion.Euler(-14,0,0);ramp.layer=8;
 Scenery("Lake",PrimitiveType.Cylinder,new Vector3(-29,.03f,22),new Vector3(28,.025f,22),new Color(.32f,.69f,.76f),env);
 for(int i=0;i<100;i++){Vector3 pos=new Vector3(Random.Range(-63,63),0,Random.Range(-63,63));if(Vector3.Distance(pos,Camp)<9||Vector3.Distance(pos,new Vector3(-29,0,22))<19||Vector3.Distance(pos,new Vector3(30,0,20))<23||Mathf.Abs(pos.x)<5)continue;
 float height=Random.Range(3.8f,7);Scenery("Tree trunk",PrimitiveType.Cylinder,pos+Vector3.up*height*.3f,new Vector3(.55f,height*.3f,.55f),new Color(.39f,.28f,.2f),env);
 for(int j=0;j<2;j++)Scenery("Tree canopy",PrimitiveType.Sphere,pos+Vector3.up*(height*.65f+j),new Vector3(3.7f-j*.7f,3-j*.3f,3.7f-j*.7f),new Color(.23f+j*.05f,.46f+j*.08f,.31f),env);
 }
 for(int i=0;i<55;i++){Vector3 pos=new Vector3(Random.Range(-65,65),.3f,Random.Range(-65,65));if(Vector3.Distance(pos,Camp)<9||Mathf.Abs(pos.x)<6)continue;Scenery("Rock",PrimitiveType.Sphere,pos,new Vector3(1.8f,1.2f,1.5f)*Random.Range(.5f,2),new Color(.57f,.61f,.57f),env);}
 for(int i=0;i<200;i++){var pos=new Vector3(Random.Range(-60,60),.13f,Random.Range(-60,60));var grass=Part("Meadow flowers",PrimitiveType.Cube,pos,new Vector3(.13f,.3f,.13f),i%3==0?new Color(1,.84f,.47f):new Color(.69f,.8f,.49f),env);RemoveVisualCollider(grass);}
 Scenery("Camp stone",PrimitiveType.Cylinder,Camp+Vector3.up*.2f,new Vector3(6,.2f,6),new Color(.72f,.68f,.55f),env);
 Scenery("Rest shrine",PrimitiveType.Cube,Camp+new Vector3(0,1.5f,2),new Vector3(1,3,1),new Color(.62f,.68f,.65f),env);
 var gem=Part("Shrine crystal",PrimitiveType.Sphere,Camp+new Vector3(0,3.3f,2),Vector3.one*.65f,new Color(.36f,.89f,.86f),env);RemoveVisualCollider(gem);
 var light=gem.AddComponent<Light>();light.color=Color.cyan;light.range=7;light.intensity=1.5f;
 var sign=new GameObject("Camp label");sign.transform.position=Camp+new Vector3(0,4,2);var text=sign.AddComponent<TextMesh>();text.text="CAMPAMENTO\nF · descansar y guardar";text.fontSize=35;text.characterSize=.065f;text.anchor=TextAnchor.MiddleCenter;text.color=new Color(.1f,.24f,.25f);sign.AddComponent<Billboard>();
 var player=new GameObject("Trainer");player.transform.position=Camp+Vector3.back*3+Vector3.up*2;var cc=player.AddComponent<CharacterController>();cc.height=1.8f;cc.radius=.32f;cc.center=Vector3.up*.9f;cc.stepOffset=.35f;game.player=player.AddComponent<PlayerMotor>();
 var model=new GameObject("Trainer visual").transform;model.SetParent(player.transform,false);
 VisualPart("Coat",PrimitiveType.Capsule,new Vector3(0,.95f,0),new Vector3(.65f,.65f,.45f),new Color(.2f,.48f,.63f),model);
 VisualPart("Head",PrimitiveType.Sphere,new Vector3(0,1.63f,0),Vector3.one*.38f,new Color(.91f,.72f,.51f),model);
 VisualPart("Hair",PrimitiveType.Sphere,new Vector3(0,1.77f,-.04f),new Vector3(.4f,.22f,.4f),new Color(.28f,.2f,.15f),model);
 for(int side=-1;side<=1;side+=2){VisualPart("Boot",PrimitiveType.Capsule,new Vector3(side*.18f,.35f,0),new Vector3(.22f,.38f,.25f),new Color(.23f,.27f,.29f),model);VisualPart("Arm",PrimitiveType.Capsule,new Vector3(side*.43f,1.05f,0),new Vector3(.2f,.35f,.2f),new Color(.2f,.48f,.63f),model);}
 VisualPart("Backpack",PrimitiveType.Cube,new Vector3(0,1.05f,-.35f),new Vector3(.48f,.6f,.22f),new Color(.58f,.37f,.23f),model);game.player.Init(model);
 var camera=new GameObject("Main Camera");camera.tag="MainCamera";var c=camera.AddComponent<Camera>();c.nearClipPlane=.1f;c.farClipPlane=250;c.clearFlags=CameraClearFlags.SolidColor;c.backgroundColor=RenderSettings.fogColor;camera.AddComponent<AudioListener>();game.cam=camera.AddComponent<OrbitCamera>();game.cam.cameraComponent=c;camera.transform.position=Camp+new Vector3(0,5,-8);
 Vector3[] points={new Vector3(-9,1,-8),new Vector3(10,1,2),new Vector3(-15,1,9),new Vector3(18,1,-12),new Vector3(40,1,0),new Vector3(-42,1,14),new Vector3(-18,1,37),new Vector3(30,5,28),new Vector3(6,1,36)};
 for(int i=0;i<points.Length;i++)MakeCreature(game,i%3,points[i],false);
 game.gameObject.AddComponent<WildSpawner>();
 }
 static void RemoveVisualCollider(GameObject o){var collider=o.GetComponent<Collider>();if(!collider)return;collider.enabled=false;if(Application.isPlaying)Object.Destroy(collider);else Object.DestroyImmediate(collider);}
 static void VisualPart(string name,PrimitiveType type,Vector3 pos,Vector3 size,Color color,Transform parent){var o=Part(name,type,pos,size,color,parent);RemoveVisualCollider(o);}
 public static Creature MakeCreature(Game game,int type,Vector3 pos,bool ally,CreatureRecord data=null){var root=new GameObject((ally?"Companion / ":"Wild / ")+Species.Names[type]);root.transform.position=pos;var cc=root.AddComponent<CharacterController>();cc.height=1.4f;cc.radius=.5f;cc.center=Vector3.up*.7f;cc.stepOffset=.4f;var c=root.AddComponent<Creature>();c.Init(type,ally,data);game.creatures.Add(c);
 var m=new GameObject("Visual").transform;m.SetParent(root.transform,false);c.model=m;
 // These custom creatures have two legs; body classification is target metadata, not a source species mapping.
 var profile=Locomotion.BodyMotionProfile.For(Locomotion.BodyArchetype.Biped);
 var rig=Locomotion.PrimitiveBodyRig.Build(m,type,profile);
 root.AddComponent<Locomotion.ProceduralBodyAnimator>().Bind(rig,c);return c;
 }
}
public class Billboard:MonoBehaviour {void LateUpdate(){if(Camera.main)transform.rotation=Camera.main.transform.rotation;}}
public class WildSpawner:MonoBehaviour {float next=45;void Update(){if(Time.time<next)return;next=Time.time+45;int count=0;foreach(var c in Game.Instance.creatures)if(c&&!c.ally&&!c.dead)count++;if(count<9){Vector3 pos=new Vector3(Random.Range(-45,45),2,Random.Range(-10,45));if(Vector3.Distance(pos,Game.Instance.player.transform.position)>18)World.MakeCreature(Game.Instance,Random.Range(0,3),pos,false);}}}
}