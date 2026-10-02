using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
namespace Wildbound {
public class Game:MonoBehaviour {
 public static Game Instance;public PlayerMotor player;public OrbitCamera cam;public Creature companion;public List<Creature> creatures=new List<Creature>();public SaveData save=new SaveData();public bool paused;
 float[] ready=new float[4];string message="Explora el valle. TAB fija objetivo, E invoca a tu compañero.";float messageUntil=15;bool partyOpen;float nextBall;
 public string SavePath {get{return Path.Combine(Application.persistentDataPath,"wildbound-save.json");}}
 void Awake(){Instance=this;}
 void Start(){World.Build(this);Load();Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
 void Update(){if(Input.GetKeyDown(KeyCode.Escape)){paused=!paused;Time.timeScale=paused?0:1;Cursor.lockState=paused?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=paused;}
 if(paused)return;if(Input.GetKeyDown(KeyCode.I))partyOpen=!partyOpen;
 if(Input.GetKeyDown(KeyCode.F5))Save();if(Input.GetKeyDown(KeyCode.F9))Load();
 if(Input.GetKeyDown(KeyCode.Tab)){Creature best=null;float score=999;foreach(var c in creatures){if(!c||c.ally||c.dead||c.capturing)continue;Vector3 delta=c.transform.position-player.transform.position;float dist=delta.magnitude;float angle=Vector3.Angle(cam.transform.forward,delta);RaycastHit h;if(dist<28&&angle<65&&!Physics.Linecast(cam.transform.position,c.transform.position+Vector3.up,out h,1<<8)){float s=angle+dist;if(s<score){score=s;best=c;}}}cam.target=cam.target==best?null:best;if(companion)companion.forcedTarget=cam.target;}
 cam.aiming=Input.GetMouseButton(1);
 if(Input.GetKeyDown(KeyCode.E))ToggleCompanion();
 if(Input.GetKeyDown(KeyCode.Q)){if(companion)companion.forcedTarget=null;cam.target=null;Message("Compañero: vuelve conmigo");}
 if(Input.GetKeyDown(KeyCode.R)||Input.GetMouseButtonDown(0)&&cam.aiming)ThrowBall();
 for(int i=0;i<4;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))UseAbility(i);
 if(partyOpen&&save.party.Count>0){if(Input.GetKeyDown(KeyCode.RightArrow)){save.selected=(save.selected+1)%save.party.Count;Dismiss();}if(Input.GetKeyDown(KeyCode.LeftArrow)){save.selected=(save.selected+save.party.Count-1)%save.party.Count;Dismiss();}}
 if(Input.GetKeyDown(KeyCode.F)&&Vector3.Distance(player.transform.position,World.Camp)<5){player.health=100;player.stamina=100;Dismiss();foreach(var r in save.party)r.health=100+(r.level-1)*12;save.balls=Mathf.Max(save.balls,20);Save();Message("Campamento: equipo recuperado, cápsulas repuestas y partida guardada");}
 }
 void UseAbility(int i){if(!companion){Message("Pulsa E para invocar una criatura");return;}if(Time.time<ready[i])return;var target=cam.target;Vector3 point=target?target.transform.position:player.transform.position+player.transform.forward*8;if(companion.Begin(i,point,target)){ready[i]=Time.time+new float[]{2,6,9,14}[i];companion.forcedTarget=target;}}
 public void ToggleCompanion(){if(companion){Dismiss();return;}if(save.party.Count==0)return;save.selected=Mathf.Clamp(save.selected,0,save.party.Count-1);var r=save.party[save.selected];if(r.health<=0){Message("Tu criatura necesita descansar en el campamento (F)");return;}companion=World.MakeCreature(this,r.species,player.transform.position+player.transform.right*2,true,r);companion.forcedTarget=cam.target;}
 void Dismiss(){if(!companion)return;companion.record.health=companion.health;Destroy(companion.gameObject);companion=null;}
 void ThrowBall(){if(player.health<=0||Time.time<nextBall)return;if(save.balls<=0){Message("Sin cápsulas. Regresa al campamento");return;}nextBall=Time.time+.7f;save.balls--;Vector3 origin=player.transform.position+Vector3.up*1.45f+cam.transform.right*.55f;RaycastHit hit;Vector3 destination=cam.transform.position+cam.transform.forward*40;if(Physics.Raycast(cam.transform.position,cam.transform.forward,out hit,50))destination=hit.point;Vector3 dir=(destination-origin).normalized;SpawnShot(origin,dir,null,true);}
 // Adaptación del lanzamiento físico y apuntado de CatcherController (PokemonCatcher).
 public void SpawnShot(Vector3 position,Vector3 direction,Creature source,bool ball){var obj=World.Part(ball?"Capture capsule":"Element projectile",PrimitiveType.Sphere,position,Vector3.one*(ball?.27f:.34f),ball?Color.yellow:Species.Colors[source.species],null);Destroy(obj.GetComponent<Collider>());obj.AddComponent<Projectile>().Init(direction*(ball?23:15)+Vector3.up*(ball?1.6f:0),source,ball);}
 // Base especie × modificadores, ampliada desde CaptureManager (PokemonCatcher).
 public void Capture(Creature c){if(c.ally||c.dead||c.capturing)return;float chance=Mathf.Clamp01(Species.CaptureBase(c.species)*Mathf.Lerp(.45f,1.5f,1-c.health/c.MaxHealth)*(Time.time<c.stunUntil?1.35f:1));StartCoroutine(CaptureSequence(c,chance));}
 IEnumerator CaptureSequence(Creature c,float chance){c.capturing=true;Burst(c.transform.position+Vector3.up,Color.yellow,1.8f);yield return new WaitForSeconds(.8f);if(!c)yield break;if(UnityEngine.Random.value<chance){var r=new CreatureRecord(c.species);r.health=Mathf.Max(30,c.health);save.party.Add(r);Message("¡"+Species.Names[c.species]+" capturado! I abre tu equipo");Destroy(c.gameObject);Save();}else{c.capturing=false;Message("Se ha escapado. Debilítalo o usa Atadura (3)");}}
 public void Burst(Vector3 position,Color color,float scale){var o=World.Part("Impact",PrimitiveType.Sphere,position,Vector3.one*.1f,color,null);Destroy(o.GetComponent<Collider>());StartCoroutine(AnimateBurst(o,scale));}
 IEnumerator AnimateBurst(GameObject o,float size){float t=0;while(t<.25f&&o){t+=Time.deltaTime;o.transform.localScale=Vector3.one*Mathf.Lerp(.1f,size,t/.25f);yield return null;}if(o)Destroy(o);}
 public void Message(string text){message=text;messageUntil=Time.time+5;}
 public void Respawn(){player.GetComponent<CharacterController>().enabled=false;player.transform.position=World.Camp+Vector3.back*3;player.GetComponent<CharacterController>().enabled=true;player.health=100;Dismiss();Message("Has vuelto al campamento");}
 public void Save(){if(companion)companion.record.health=companion.health;Vector3 pos=player.transform.position;save.x=pos.x;save.y=pos.y;save.z=pos.z;try{string tmp=SavePath+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(save,true));File.Copy(tmp,SavePath,true);File.Delete(tmp);Message("Partida guardada");}catch(Exception e){Message("No se pudo guardar: "+e.Message);}}
 public void Load(){Dismiss();SaveData loaded=null;if(File.Exists(SavePath)){try{loaded=JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));if(loaded==null||loaded.version!=1||loaded.party==null)throw new Exception("Formato incompatible");}catch(Exception e){Message("No se pudo cargar: "+e.Message);return;}}
 if(loaded==null){save=new SaveData();save.party.Add(new CreatureRecord(0));save.x=World.Camp.x;save.z=World.Camp.z-3;save.y=2;}else save=loaded;
 save.selected=Mathf.Clamp(save.selected,0,Mathf.Max(0,save.party.Count-1));Array.Clear(ready,0,ready.Length);player.GetComponent<CharacterController>().enabled=false;player.transform.position=new Vector3(Mathf.Clamp(save.x,-65,65),Mathf.Clamp(save.y,1,20),Mathf.Clamp(save.z,-65,65));player.GetComponent<CharacterController>().enabled=true;player.health=100;cam.target=null;}
 void OnApplicationQuit(){if(player)Save();Time.timeScale=1;}
 void OnGUI(){float scale=Mathf.Clamp(Screen.width/1280f,.65f,1.6f);GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);float w=Screen.width/scale,h=Screen.height/scale;
 GUI.color=new Color(.07f,.14f,.17f,.95f);GUI.Box(new Rect(20,20,330,113),"");GUI.color=Color.white;GUI.Label(new Rect(36,30,300,25),"W I L D B O U N D  /  VALLE DEL ALBA");GUI.Label(new Rect(36,60,300,22),"Salud "+Mathf.CeilToInt(player.health)+"   •   Energía "+Mathf.CeilToInt(player.stamina));GUI.Label(new Rect(36,88,300,22),"Cápsulas "+save.balls+"   /   Criaturas "+save.party.Count);
 if(cam.target){GUI.Box(new Rect(w/2-140,25,280,56),Species.Names[cam.target.species]+"\n"+Mathf.CeilToInt(cam.target.health)+" / "+cam.target.MaxHealth+" PV");}
 GUI.Label(new Rect(w/2-8,h/2-13,30,30),cam.aiming?"⊕":"·");
 string[] names={"Pulso","Barrido","Atadura","Recuperación"};for(int i=0;i<4;i++){float remaining=Mathf.Max(0,ready[i]-Time.time);GUI.Box(new Rect(w/2-230+i*118,h-85,112,55),(i+1)+"  "+names[i]+"\n"+(remaining>0?remaining.ToString("0.0")+" s":"LISTO"));}
 if(companion)GUI.Label(new Rect(w/2-220,h-115,500,25),Species.Names[companion.species]+"  Nv."+companion.record.level+"  •  "+Mathf.CeilToInt(companion.health)+" PV");
 GUI.Label(new Rect(24,h-115,330,105),"WASD mover · Ratón cámara\nShift correr · Ctrl esquivar · Espacio saltar\nTAB objetivo · E invocar · Q retirar orden\nR cápsula · Botón derecho apuntar\nI equipo · F campamento · F5/F9 guardar/cargar");
 if(Time.time<messageUntil)GUI.Box(new Rect(w/2-340,h-160,680,32),message);
 if(partyOpen){GUI.Box(new Rect(w-320,110,295,Mathf.Min(450,70+save.party.Count*35)),"EQUIPO  /  ← → seleccionar");int max=Mathf.Min(save.party.Count,10);for(int i=0;i<max;i++){var r=save.party[i];GUI.Label(new Rect(w-300,145+i*35,270,30),(i==save.selected?"► ":"  ")+Species.Names[r.species]+"  Nv."+r.level+"  PV "+Mathf.CeilToInt(r.health));}}
 if(player.health<=0){GUI.Box(new Rect(w/2-170,h/2-55,340,110),"HAS CAÍDO");if(GUI.Button(new Rect(w/2-140,h/2-10,280,40),"Volver al campamento"))Respawn();Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}else if(!paused){Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
 if(paused){GUI.Box(new Rect(w/2-180,h/2-130,360,260),"PAUSA");if(GUI.Button(new Rect(w/2-150,h/2-80,300,40),"Continuar")){paused=false;Time.timeScale=1;}if(GUI.Button(new Rect(w/2-150,h/2-25,300,40),"Guardar"))Save();if(GUI.Button(new Rect(w/2-150,h/2+30,300,40),"Cargar"))Load();}
 }
}
}