using UnityEngine;
namespace Pokemon3D {
public class OrbitCamera:MonoBehaviour {
 public Creature target;[SerializeField] float yaw=30,pitch=24;[SerializeField] float followDistance=6.5f;public bool aiming;public Camera cameraComponent;
 public void BindRuntime(Transform player){cameraComponent=GetComponent<Camera>();yaw=transform.eulerAngles.y;pitch=Mathf.Clamp(Mathf.DeltaAngle(0,transform.eulerAngles.x),8,65);followDistance=Mathf.Max(.1f,Vector3.Distance(transform.position,player.position+Vector3.up*1.7f));}
 void LateUpdate(){if(!Game.Instance||!Game.Instance.player)return; if(!Game.Instance.paused){yaw+=Input.GetAxis("Mouse X")*3;pitch=Mathf.Clamp(pitch-Input.GetAxis("Mouse Y")*2,8,65);}
 if(target&&target.dead)target=null;
 var pivot=Game.Instance.ControlledTransform.position+Vector3.up*(Game.Instance.IsPossessing?1.4f:1.7f);
 if(target&&!aiming){var d=target.transform.position-pivot;yaw=Mathf.LerpAngle(yaw,Mathf.Atan2(d.x,d.z)*Mathf.Rad2Deg,Time.deltaTime*7);}
 Quaternion rot=Quaternion.Euler(pitch,yaw,0);float length=aiming?3.2f:followDistance;
 Vector3 desired=pivot+rot*new Vector3(aiming?.7f:0,0,-length);
 RaycastHit hit;var delta=desired-pivot;if(Physics.SphereCast(pivot,.2f,delta.normalized,out hit,delta.magnitude,1<<8))desired=hit.point+hit.normal*.25f;
 transform.position=Vector3.Lerp(transform.position,desired,1-Mathf.Exp(-Time.deltaTime*18));transform.rotation=rot;
 cameraComponent.fieldOfView=Mathf.Lerp(cameraComponent.fieldOfView,aiming?48:62,Time.deltaTime*10);
 }
}
}