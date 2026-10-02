using UnityEngine;
namespace Wildbound {
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor:MonoBehaviour {
 public float health=100, stamina=100;public bool invulnerable;
 CharacterController cc; Transform visual;float vertical,dodgeUntil;Vector3 dodgeDir;float stride;
 public void Init(Transform model){visual=model;cc=GetComponent<CharacterController>();}
 void Update(){if(Game.Instance.paused||health<=0)return;
 var cam=Game.Instance.cam;Vector3 forward=cam.transform.forward;forward.y=0;forward.Normalize();Vector3 right=cam.transform.right;right.y=0;right.Normalize();
 Vector3 dir=Vector3.ClampMagnitude(forward*Input.GetAxisRaw("Vertical")+right*Input.GetAxisRaw("Horizontal"),1);
 if(Input.GetKeyDown(KeyCode.Space)&&cc.isGrounded&&stamina>=12){vertical=7;stamina-=12;}
 if(Input.GetKeyDown(KeyCode.LeftControl)&&stamina>=25&&Time.time>dodgeUntil){dodgeDir=dir.sqrMagnitude>.1f?dir:transform.forward;dodgeUntil=Time.time+.38f;stamina-=25;}
 invulnerable=Time.time<dodgeUntil;bool sprint=Input.GetKey(KeyCode.LeftShift)&&stamina>0&&dir.sqrMagnitude>.1f;
 if(sprint)stamina-=Time.deltaTime*14;else stamina=Mathf.Min(100,stamina+Time.deltaTime*18);
 if(cc.isGrounded&&vertical<0)vertical=-2;vertical+=Physics.gravity.y*2*Time.deltaTime;
 Vector3 motion=invulnerable?dodgeDir*13:dir*(sprint?8:5);motion.y=vertical;cc.Move(motion*Time.deltaTime);
 if(dir.sqrMagnitude>.1f)transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(dir),Time.deltaTime*12);
 stride+=Time.deltaTime*motion.magnitude*2;
 if(visual){visual.localPosition=new Vector3(0,Mathf.Sin(stride)*.035f*dir.magnitude,0);visual.localRotation=Quaternion.Euler(invulnerable?55:0,0,Mathf.Sin(stride)*3*dir.magnitude);}
 if(transform.position.y<-10)Game.Instance.Respawn();
 }
 public void Hurt(float amount){if(invulnerable||health<=0)return;health=Mathf.Max(0,health-amount);Game.Instance.Message("Te han golpeado: -"+amount+" PV");}
}
}