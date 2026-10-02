using UnityEngine;
namespace Wildbound {
public class OrbitCamera:MonoBehaviour {
 public Creature target;float yaw=30,pitch=24;public bool aiming;public Camera cameraComponent;
 void LateUpdate(){if(!Game.Instance||!Game.Instance.player)return; if(!Game.Instance.paused){yaw+=Input.GetAxis("Mouse X")*3;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,8,65);}
 if(target&&target.dead)target=null;
 var pivot=Game.Instance.player.transform.position+Vector3.up*1.7f;
 if(target&&!aiming){var d=target.transform.position-pivot;yaw=Mathf.LerpAngle(yaw,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg,Time.deltaTime*7);}
 Quaternion rot=Quaternion.Euler(pitch,yaw,0);float length=aiming?3.2f:6.5f;
 Vector3 desired=pivot+rot*new Vector3(aiming?.7f:0,0,-length);
 RaycastHit hit;var delta=desired-pivot;if(Physics.SphereCast(pivot,.2f,delta.normalized,out hit,delta.magnitude,1<<8))desired=hit.point+hit.normal*.25f;
 transform.position=Vector3.Lerp(transform.position,desired,1-Mathf.Exp(-Time.deltaTime*18));transform.rotation=rot;
 cameraComponent.fieldOfView=Mathf.Lerp(cameraComponent.fieldOfView,aiming?48:62,Time.deltaTime*10);
 }
}
}