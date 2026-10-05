using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Wildbound.Combat;
namespace Wildbound {
[DefaultExecutionOrder(-1000)]
public class Game:MonoBehaviour {
 public static Game Instance;public PlayerMotor player;public OrbitCamera cam;public Creature companion;public List<Creature> creatures=new List<Creature>();public SaveData save=new SaveData();public bool paused;
 public CompanionCombatCatalog combatCatalog;
 Creature summonedCompanion;
 CompanionControl control; readonly Dictionary<string,float[]> attackCooldowns=new Dictionary<string,float[]>();
 public bool IsPossessing {get{return control&&control.IsPossessing;}}
 public Transform ControlledTransform {get{return IsPossessing?control.Actor.transform:player?player.transform:null;}}
 public CompanionControl Control {get{if(!control){control=GetComponent<CompanionControl>();if(!control)control=gameObject.AddComponent<CompanionControl>();control.Initialize(this);}return control;}}
 public CompanionLoadout ResolveLoadout(CreatureRecord record,int species){if(!combatCatalog)combatCatalog=Resources.Load<CompanionCombatCatalog>(CompanionCombatCatalog.ResourceName);return combatCatalog?combatCatalog.Resolve(record!=null?record.combatLoadoutId:null,species):null;}
 public float[] Cooldowns(Creature creature){if(creature.record==null||string.IsNullOrEmpty(creature.record.id))return creature.LocalCooldowns;float[] result;if(!attackCooldowns.TryGetValue(creature.record.id,out result)){result=new float[4];attackCooldowns.Add(creature.record.id,result);}return result;}
 public bool OwnsCompanion(Creature creature){return creature&&summonedCompanion==creature;}
 public static bool CanControl(Creature creature){if(!creature||!creature.isActiveAndEnabled||!creature.ally||creature.dead||creature.capturing||!(creature.health>0)||float.IsInfinity(creature.health)||!creature.model)return false;var controller=creature.GetComponent<CharacterController>();return controller&&controller.enabled;}
 public void ReleaseCompanion(Creature creature){if(companion!=creature&&(!control||control.Actor!=creature))return;if(creature)creature.CancelActions();if(control)control.Release();if(companion==creature)companion=null;if(cam)cam.target=null;}
 string message="Explora el valle. TAB fija objetivo, E invoca a tu compañero.";float messageUntil=15;bool partyOpen;float nextBall;
 public string SavePath {get{return Path.Combine(Application.persistentDataPath,"wildbound-save.json");}}
 void Awake(){if(!player||!player.gameObject.activeInHierarchy)player=FindAnyObjectByType<PlayerMotor>();if(save==null)save=new SaveData();if(save.party==null)save.party=new List<CreatureRecord>();var owner=AuthoredWorld.OwnerInLoadedScenes();if(owner&&owner!=this){enabled=false;return;}Instance=this;}
 public bool InitializeWorld(){var authored=GetComponent<AuthoredWorld>();if(authored){authored.BindRuntime(this);return true;}World.Build(this);return false;}
 void Start(){bool authored=InitializeWorld();Load(!authored);Cursor.lockState=CursorLockMode.Locked;Cursor.visible=false;}
 void Update(){if(Input.GetKeyDown(KeyCode.Escape)){paused=!paused;Time.timeScale=paused?0:1;Cursor.lockState=paused?CursorLockMode.None:CursorLockMode.Locked;Cursor.visible=paused;}
 if(paused)return;if(Input.GetKeyDown(KeyCode.I))partyOpen=!partyOpen;
 if(Input.GetKeyDown(KeyCode.F5))Save();if(Input.GetKeyDown(KeyCode.F9))Load();
 if(Input.GetKeyDown(KeyCode.Tab)){Creature best=null;float score=999;foreach(var c in creatures){if(!c||c.ally||c.dead||c.capturing)continue;Vector3 delta=c.transform.position-ControlledTransform.position;float dist=delta.magnitude;float angle=Vector3.Angle(cam.transform.forward,delta);RaycastHit h;if(dist<28&&angle<65&&!Physics.Linecast(cam.transform.position,c.transform.position+Vector3.up,out h,1<<8)){float s=angle+dist;if(s<score){score=s;best=c;}}}cam.target=cam.target==best?null:best;if(companion)companion.forcedTarget=cam.target;}
 cam.aiming=Input.GetMouseButton(1);
 if(Input.GetKeyDown(KeyCode.E))ToggleCompanion();
 if(Input.GetKeyDown(KeyCode.Q)){if(companion)companion.forcedTarget=null;cam.target=null;Message("Target cleared");}
 if(Input.GetKeyDown(KeyCode.R)||Input.GetMouseButtonDown(0)&&cam.aiming)ThrowBall();
 for(int i=0;i<4;i++)if(Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1+i)))UseAbility(i);
 if(partyOpen&&save.party.Count>0){if(Input.GetKeyDown(KeyCode.RightArrow)){save.selected=(save.selected+1)%save.party.Count;Dismiss();}if(Input.GetKeyDown(KeyCode.LeftArrow)){save.selected=(save.selected+save.party.Count-1)%save.party.Count;Dismiss();}}
 if(!IsPossessing&&Input.GetKeyDown(KeyCode.F)&&Vector3.Distance(player.transform.position,World.Camp)<5){player.health=100;player.stamina=100;Dismiss();foreach(var r in save.party)r.health=100+(r.level-1)*12;save.balls=Mathf.Max(save.balls,20);Save();Message("Campamento: equipo recuperado, cápsulas repuestas y partida guardada");}
 }
 public Vector3 AttackPoint(CompanionAttack attack,Creature target){var actor=ControlledTransform;float range=attack?attack.range:8;return target?target.transform.position:actor.position+actor.forward*range;}
 void UseAbility(int i){if(!IsPossessing||!companion){Message("Press E to control a companion");return;}var attack=companion.AttackAt(i);if(!attack){Message("No attack assigned to this slot");return;}var target=cam.target;companion.Begin(i,AttackPoint(attack,target),target);}
 public void ToggleCompanion(){
 if(paused||!isActiveAndEnabled||!player)return;
 if(companion||IsPossessing){Dismiss();return;}
 if(summonedCompanion)Dismiss();
 Creature nearest=null;float distance=float.PositiveInfinity;
 foreach(var candidate in creatures){if(!CanControl(candidate))continue;float next=(candidate.transform.position-player.transform.position).sqrMagnitude;if(next<distance){distance=next;nearest=candidate;}}
 if(nearest)companion=nearest;
 else {if(save==null||save.party==null||save.party.Count==0)return;save.selected=Mathf.Clamp(save.selected,0,save.party.Count-1);var r=save.party[save.selected];if(r==null||r.species<0||r.species>=Species.Names.Length||!(r.health>0)){Message("No available companion. Mark a healthy scene Creature as Ally or recover your party at camp.");return;}companion=World.MakeCreature(this,r.species,player.transform.position+player.transform.right*2,true,r);summonedCompanion=companion;}
 companion.forcedTarget=cam?cam.target:null;Control.Possess(companion);
 }
 public void Dismiss(){var current=companion?companion:summonedCompanion;if(!current){if(control)control.Release();return;}if(current.record!=null)current.record.health=current.health;current.CancelActions();current.forcedTarget=null;ReleaseCompanion(current);if(control)control.Release();if(OwnsCompanion(current)){summonedCompanion=null;if(Application.isPlaying)Destroy(current.gameObject);else DestroyImmediate(current.gameObject);}}
 void ThrowBall(){if(IsPossessing)return;if(player.health<=0||Time.time<nextBall)return;if(save.balls<=0){Message("Sin cápsulas. Regresa al campamento");return;}nextBall=Time.time+.7f;save.balls--;Vector3 origin=player.transform.position+Vector3.up*1.45f+cam.transform.right*.55f;RaycastHit hit;Vector3 destination=cam.transform.position+cam.transform.forward*40;if(Physics.Raycast(cam.transform.position,cam.transform.forward,out hit,50))destination=hit.point;Vector3 dir=(destination-origin).normalized;SpawnShot(origin,dir,null,true);}
 // Adaptación del lanzamiento físico y apuntado de CatcherController (PokemonCatcher).
 public void SpawnShot(Vector3 position,Vector3 direction,Creature source,bool ball,CompanionAttack attack=null){var obj=World.Part(ball?"Capture capsule":"Element projectile",PrimitiveType.Sphere,position,Vector3.one*(ball?.27f:.34f),ball?Color.yellow:Species.Colors[source.species],null);Destroy(obj.GetComponent<Collider>());obj.AddComponent<Projectile>().Init(direction*(ball?23:attack?attack.projectileSpeed:15)+Vector3.up*(ball?1.6f:0),source,ball,attack);}
 // Base especie × modificadores, ampliada desde CaptureManager (PokemonCatcher).
 public void Capture(Creature c){if(c.ally||c.dead||c.capturing)return;float chance=Mathf.Clamp01(Species.CaptureBase(c.species)*Mathf.Lerp(.45f,1.5f,1-c.health/c.MaxHealth)*(Time.time<c.stunUntil?1.35f:1));StartCoroutine(CaptureSequence(c,chance));}
 public CreatureRecord CapturedRecord(Creature creature){var result=new CreatureRecord(creature.species);result.combatLoadoutId=creature.loadout?creature.loadout.id:null;result.health=Mathf.Max(30,creature.health);return result;}
 IEnumerator CaptureSequence(Creature c,float chance){c.CancelActions();c.capturing=true;Burst(c.transform.position+Vector3.up,Color.yellow,1.8f);yield return new WaitForSeconds(.8f);if(!c)yield break;if(UnityEngine.Random.value<chance){var r=CapturedRecord(c);save.party.Add(r);Message("¡"+Species.Names[c.species]+" capturado! I abre tu equipo");Destroy(c.gameObject);Save();}else{c.capturing=false;Message("Se ha escapado. Debilítalo o usa Atadura (3)");}}
 public void Burst(Vector3 position,Color color,float scale){var o=World.Part("Impact",PrimitiveType.Sphere,position,Vector3.one*.1f,color,null);Destroy(o.GetComponent<Collider>());StartCoroutine(AnimateBurst(o,scale));}
 IEnumerator AnimateBurst(GameObject o,float size){float t=0;while(t<.25f&&o){t+=Time.deltaTime;o.transform.localScale=Vector3.one*Mathf.Lerp(.1f,size,t/.25f);yield return null;}if(o)Destroy(o);}
 public void Message(string text){message=text;messageUntil=Time.time+5;}
 public void Respawn(){player.GetComponent<CharacterController>().enabled=false;player.transform.position=World.Camp+Vector3.back*3;player.GetComponent<CharacterController>().enabled=true;player.health=100;Dismiss();Message("Has vuelto al campamento");}
 public void Save(){if(companion&&companion.record!=null)companion.record.health=companion.health;Vector3 pos=player.transform.position;save.x=pos.x;save.y=pos.y;save.z=pos.z;try{string tmp=SavePath+".tmp";File.WriteAllText(tmp,JsonUtility.ToJson(save,true));File.Copy(tmp,SavePath,true);File.Delete(tmp);Message("Partida guardada");}catch(Exception e){Message("No se pudo guardar: "+e.Message);}}
 public void Load(bool restorePosition=true){Dismiss();SaveData loaded=null;if(File.Exists(SavePath)){try{loaded=JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));if(loaded==null||loaded.version!=1||loaded.party==null)throw new Exception("Formato incompatible");}catch(Exception e){Message("No se pudo cargar: "+e.Message);return;}}
 ApplyLoadedSave(loaded,restorePosition);}
 void ApplyLoadedSave(SaveData loaded,bool restorePosition){
 if(loaded==null){save=new SaveData();save.party.Add(new CreatureRecord(0));save.x=World.Camp.x;save.z=World.Camp.z-3;save.y=2;}else save=loaded;
 save.selected=Mathf.Clamp(save.selected,0,Mathf.Max(0,save.party.Count-1));if(restorePosition){player.GetComponent<CharacterController>().enabled=false;player.transform.position=new Vector3(Mathf.Clamp(save.x,-65,65),Mathf.Clamp(save.y,1,20),Mathf.Clamp(save.z,-65,65));player.GetComponent<CharacterController>().enabled=true;}player.health=100;cam.target=null;}
 void OnDisable(){if(companion)companion.CancelActions();if(control)control.Release();}
 void OnDestroy(){if(companion)companion.CancelActions();if(control)control.Release();if(Instance==this)Instance=null;}
 void OnApplicationQuit(){if(player)Save();Time.timeScale=1;}

}
}
