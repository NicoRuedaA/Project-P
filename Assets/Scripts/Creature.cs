using UnityEngine;
namespace Wildbound {
[RequireComponent(typeof(CharacterController))]
public class Creature:MonoBehaviour {
 public int species;public bool ally,dead,capturing;public float health=100;public CreatureRecord record;
 public Transform model;public Creature forcedTarget; public float stunUntil;
 CharacterController cc;Vector3 home,wander;float nextWander,nextAttack,castAt;float phase;int queued=-1;Vector3 castPoint;Creature castTarget;
 public float MaxHealth {get{return 100+(record!=null?(record.level-1)*12:0);}}
 public void Init(int type,bool friendly,CreatureRecord data=null){species=type;ally=friendly;record=data;health=data!=null?data.health:100;cc=GetComponent<CharacterController>();home=transform.position;wander=home;}
 void Update(){if(dead||capturing||Game.Instance.paused)return;
 if(queued>=0&&Time.time>=castAt){Execute(queued,castPoint,castTarget);queued=-1;}
 if(Time.time<stunUntil)return;
 Vector3 destination=transform.position;Creature enemy=null;
 if(ally){enemy=forcedTarget;if(!enemy||enemy.dead||enemy.ally)enemy=null;destination=Game.Instance.player.transform.position-transform.right*2;
 if(enemy)destination=enemy.transform.position;}
 else {enemy=Game.Instance.companion;float pd=Vector3.Distance(transform.position,Game.Instance.player.transform.position);
 if(pd<13&&species!=0){destination=enemy&&!enemy.dead?enemy.transform.position:Game.Instance.player.transform.position;}
 else if(pd<5&&species==0&&health>55)destination=transform.position+(transform.position-Game.Instance.player.transform.position).normalized*4;
 else {if(Time.time>nextWander){nextWander=Time.time+Random.Range(3,7);wander=home+new Vector3(Random.Range(-7,7),0,Random.Range(-7,7));}destination=wander;enemy=null;}
 }
 Vector3 direction=destination-transform.position;direction.y=0;float distance=direction.magnitude;
 bool fighting=ally?enemy!=null:species!=0&&Vector3.Distance(transform.position,Game.Instance.player.transform.position)<13;
 if(distance>(fighting?5:ally?2:1)){var move=direction.normalized*(ally?5:3);move.y=-9;cc.Move(move*Time.deltaTime);transform.rotation=Quaternion.Slerp(transform.rotation,Quaternion.LookRotation(direction),Time.deltaTime*6);phase+=Time.deltaTime*10;}
 else cc.Move(Vector3.down*9*Time.deltaTime);
 if(model){model.localPosition=Vector3.up*(Mathf.Sin(phase)*.06f);model.localScale=Vector3.one*(queued>=0?1.07f:1);}
 if(fighting&&distance<8&&Time.time>nextAttack&&queued<0){Begin(0,destination,enemy);nextAttack=Time.time+Random.Range(2.5f,4);}
 }
 public bool Begin(int ability,Vector3 point,Creature enemy){if(dead||capturing||queued>=0||Time.time<stunUntil)return false;queued=ability;castAt=Time.time+(ability==1?.65f:.35f);castPoint=point;castTarget=enemy;transform.LookAt(new Vector3(point.x,transform.position.y,point.z));Game.Instance.Burst(transform.position+Vector3.up,Species.Colors[species],.3f);return true;}
 void Execute(int ability,Vector3 point,Creature enemy){
 if(ability==3){health=Mathf.Min(MaxHealth,health+30);Game.Instance.Burst(transform.position+Vector3.up,Color.green,1.6f);return;}
 if(ability==1){Vector3 center=transform.position+transform.forward*2;Game.Instance.Burst(center+Vector3.up*.4f,Species.Colors[species],2.6f);foreach(var c in Game.Instance.creatures.ToArray())if(c&&c!=this&&c.ally!=ally&&Vector3.Distance(center,c.transform.position)<3.5f)c.Hurt(30,this);if(!ally&&Vector3.Distance(center,Game.Instance.player.transform.position)<3.5f)Game.Instance.player.Hurt(18);return;}
 if(ability==2){if(enemy&&Vector3.Distance(transform.position,enemy.transform.position)<16){enemy.stunUntil=Time.time+2.5f;Game.Instance.Burst(enemy.transform.position+Vector3.up,Color.yellow,1.6f);}return;}
 Vector3 origin=transform.position+Vector3.up;Vector3 dir=((enemy?enemy.transform.position+Vector3.up:point+Vector3.up)-origin).normalized;Game.Instance.SpawnShot(origin,dir,this,false);
 }
 public void Hurt(float amount,Creature source){if(dead||capturing)return;health-=amount;if(!ally&&source&&source.ally)forcedTarget=source;
 Game.Instance.Burst(transform.position+Vector3.up,Color.white,.5f);
 if(health<=0){health=0;dead=true;cc.enabled=false;Game.Instance.Message(Species.Names[species]+" debilitado");if(source&&source.record!=null)source.record.GainXP(25);if(ally){if(record!=null)record.health=0;Game.Instance.companion=null;}Destroy(gameObject,ally?1:18);}
 }
 void OnDestroy(){if(Game.Instance)Game.Instance.creatures.Remove(this);}
}
}