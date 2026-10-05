using UnityEngine;
using Pokemon3D.Combat;
namespace Pokemon3D {
public class Projectile:MonoBehaviour {
 public Creature source;public bool capture;Vector3 velocity;float expiry;float damage;float range;Vector3 origin;bool sourceWasAlly;
 public void Init(Vector3 v,Creature owner,bool ball,CompanionAttack attack=null){velocity=v;source=owner;capture=ball;expiry=Time.time+(ball?4:attack?attack.range/Mathf.Max(.1f,v.magnitude):4);damage=attack?attack.damage+(owner&&owner.record!=null?owner.record.level*attack.damagePerLevel:0):18+(owner&&owner.record!=null?owner.record.level*2:0);range=attack?attack.range:60;origin=transform.position;sourceWasAlly=owner&&owner.ally;}
 void Update(){if(!Game.Instance||!Game.Instance.isActiveAndEnabled){Destroy(gameObject);return;}if(Game.Instance.paused)return;if(!capture&&(!source||source.dead||!source.isActiveAndEnabled||source.ally&&Game.Instance.companion!=source||Vector3.Distance(origin,transform.position)>range)){Destroy(gameObject);return;}if(Time.time>expiry){Destroy(gameObject);return;}if(capture)velocity+=Physics.gravity*Time.deltaTime;
 var step=velocity*Time.deltaTime;RaycastHit[] hits=Physics.SphereCastAll(transform.position,.15f,step.normalized,step.magnitude,~0,QueryTriggerInteraction.Ignore);
 System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
 foreach(var hit in hits){var c=hit.collider.GetComponentInParent<Creature>();var player=hit.collider.GetComponentInParent<PlayerMotor>();if(source&&c==source||source&&source.ally&&player||c&&c.ally&&capture||c&&c.dead)continue;
 if(c){if(capture)Game.Instance.Capture(c);else if(!source||c.ally!=source.ally)c.Hurt(damage,source);else continue;}
 else if(player){if(capture||sourceWasAlly)continue;player.Hurt(damage);}Game.Instance.Burst(hit.point,capture?Color.yellow:Color.white,.7f);Destroy(gameObject);return;}
 transform.position+=step;transform.Rotate(180*Time.deltaTime,90*Time.deltaTime,0);
 }
}
}