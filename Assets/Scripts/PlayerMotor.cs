using UnityEngine;
namespace Wildbound {
[RequireComponent(typeof(CharacterController))]
public class PlayerMotor:MonoBehaviour {
 public float health=100, stamina=100;public bool invulnerable;
 [SerializeField] Transform visual;StarterAssets.StarterAssetsInputs input;
 void Awake(){BindRuntime();}
 public void Init(Transform model){visual=model;BindRuntime();}
 public void BindRuntime(){if(!input)input=GetComponent<StarterAssets.StarterAssetsInputs>();if(!visual)visual=transform.Find("Trainer visual");}
 void Update(){var game=Game.Instance;if(!game||game.paused||health<=0)return;if(game.IsPossessing){stamina=Mathf.Min(100,stamina+Time.deltaTime*18);return;}
 bool moving=input&&input.move.sqrMagnitude>.01f;
 bool sprinting=input&&input.sprint&&moving;
 if(sprinting){stamina=Mathf.Max(0,stamina-Time.deltaTime*14);if(stamina<=0)input.sprint=false;}
 else stamina=Mathf.Min(100,stamina+Time.deltaTime*18);
 if(transform.position.y<-10)game.Respawn();
 }
 public void Hurt(float amount){if(invulnerable||health<=0)return;health=Mathf.Max(0,health-amount);Game.Instance.Message("Te han golpeado: -"+amount+" PV");}
}
}
