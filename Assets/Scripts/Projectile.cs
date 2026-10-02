using UnityEngine;
namespace Wildbound {
public class Projectile:MonoBehaviour {
 public Creature source;public bool capture;Vector3 velocity;float expiry;
 public void Init(Vector3 v,Creature owner,bool ball){velocity=v;source=owner;capture=ball;expiry=Time.time+4;}
 void Update(){if(Game.Instance.paused)return;if(Time.time>expiry){Destroy(gameObject);return;}if(capture)velocity+=Physics.gravity*Time.deltaTime;
 var step=velocity*Time.deltaTime;RaycastHit[] hits=Physics.SphereCastAll(transform.position,.15f,step.normalized,step.magnitude,~0,QueryTriggerInteraction.Ignore);
 System.Array.Sort(hits,(a,b)=>a.distance.CompareTo(b.distance));
 foreach(var hit in hits){var c=hit.collider.GetComponentInParent<Creature>();var player=hit.collider.GetComponentInParent<PlayerMotor>();if(source&&c==source||source&&source.ally&&player||c&&c.ally&&capture||c&&c.dead)continue;
 if(c){if(capture)Game.Instance.Capture(c);else if(!source||c.ally!=source.ally)c.Hurt(18+(source&&source.record!=null?source.record.level*2:0),source);else continue;}
 else if(player){if(capture)continue;player.Hurt(12);}Game.Instance.Burst(hit.point,capture?Color.yellow:Color.white,.7f);Destroy(gameObject);return;}
 transform.position+=step;transform.Rotate(180*Time.deltaTime,90*Time.deltaTime,0);
 }
}
}