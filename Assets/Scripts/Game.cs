using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Pokemon3D.Combat;

namespace Pokemon3D
{
    //el script se ejecuta con prioridad
    [DefaultExecutionOrder(-1000)]
    public class Game : MonoBehaviour
    {
        // Singleton
        private static Game instance;
        public static Game Instance
        {
            get
            {
                return instance;
            }
        }

        [Header("Referencias Principales")]
        public OrbitCamera cam;
        public Creature companion; // criatura activa en la escena
        public List<Creature> creatures = new List<Creature>(); //lista de criaturas en la escena
        public SaveData save = new SaveData();
        public bool paused;
        public CompanionCombatCatalog combatCatalog; //lista de movimietnos que aprende cada criatura

        private PlayerMotor player;
        private CompanionControl control; //controles del compañero
        private readonly Dictionary<string, float[]> attackCooldowns = new Dictionary<string, float[]>();
        //lista de cd de cada pokemon

        private bool partyOpen;
        private float nextBall;


        // *****************************************************
        // *****************************************************

        public bool PlayingAsCreature
        {
            get
            {
                if (control != null)
                {
                    return control.IsPossessing;
                }
                return false;
            }
        }

        public Transform ControlledTransform
        {
            get
            {
                if (PlayingAsCreature)
                {
                    return control.Actor.transform;
                }

                if (player != null)
                {
                    return player.transform;
                }

                return null;
            }
        }

        public CompanionControl Control
        {
            get
            {
                //si no tiene un controlador
                if (control == null)
                {
                    //lo busca
                    control = GetComponent<CompanionControl>();
                    if (control == null)
                    {
                        //si no lo encuentra lo crea
                        control = gameObject.AddComponent<CompanionControl>();
                    }
                    control.Initialize(this);
                }
                //lo devuelve
                return control;
            }
        }

        public string SavePath
        {
            get
            {
                return Path.Combine(Application.persistentDataPath, "pokemon3d-save.json");
            }
        }

        // *****************************************************
        // *****************************************************

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("Existe una instancia activa. Destruyendo duplicado.");
                Destroy(gameObject);
                return;
            }

            instance = this;

            /*var owner = AuthoredWorld.OwnerInLoadedScenes();
            if (owner != null && owner != this)
            {
                Destroy(gameObject);
                return;
            }*/

            //obtenemos al player
            player = FindAnyObjectByType<PlayerMotor>();

            //si no hay save
            if (save == null)
            {
                save = new SaveData();
            }

            if (save.party == null)
            {
                save.party = new List<CreatureRecord>();
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void Start()
        {
            //configuracion del raton para camara en tercera persona
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void FixedUpdate()
        {
            //pausar y despausar
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                paused = !paused;

                if (paused)
                {
                    Time.timeScale = 0f;
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Time.timeScale = 1f;
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }

            if (paused)
            {
                return;
            }

            if (Input.GetKeyDown(KeyCode.I))
            {
                partyOpen = !partyOpen;
                openParty();
            }

            if (Input.GetKeyDown(KeyCode.F5))
            {
                Save();
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                Load(true);
            }

            // Bloquear camara en objeitivo
            if (Input.GetKeyDown(KeyCode.Tab))
            {
                // Busca cuál es el enemigo más cercano, centrado y visible
                Creature best = FindBestTarget();

                // Si ya estábamos apuntando a ese mismo enemigo, quitamos el fijado (null)
                if (cam.target == best)
                {
                    cam.target = null;
                }
                // Si no lo teníamos fijado (o es un enemigo nuevo), lo fijamos como objetivo
                else
                {
                    cam.target = best;
                }
            }

            //detecta si se está apuntando
            cam.aiming = Input.GetMouseButton(1);


            // Acciones de Compañero
            if (Input.GetKeyDown(KeyCode.E))
            {
                ToggleCompanion();
            }

            if (Input.GetKeyDown(KeyCode.Q))
            {
                if (companion != null)
                {
                    companion.forcedTarget = null;
                }
                cam.target = null;
                Debug.Log("Target cleared");
            }

            if (Input.GetKeyDown(KeyCode.R) || (Input.GetMouseButtonDown(0) && cam.aiming))
            {
                ThrowBall();
            }

            for (int i = 0; i < 4; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i)))
                {
                    UseAbility(i);
                }
            }

            // Gestión del Equipo
            if (partyOpen && save.party.Count > 0)
            {
                if (Input.GetKeyDown(KeyCode.RightArrow))
                {
                    save.selected = (save.selected + 1) % save.party.Count;
                    Dismiss();
                }

                if (Input.GetKeyDown(KeyCode.LeftArrow))
                {
                    save.selected = (save.selected + save.party.Count - 1) % save.party.Count;
                    Dismiss();
                }
            }

            // Campamento y Recuperación
            if (!PlayingAsCreature && Input.GetKeyDown(KeyCode.F))
            {
                if (Vector3.Distance(player.transform.position, World.Camp) < 5f)
                {
                    player.health = 100;
                    player.stamina = 100;
                    Dismiss();

                    foreach (var r in save.party)
                    {
                        r.health = 100 + (r.level - 1) * 12;
                    }

                    save.balls = Mathf.Max(save.balls, 20);
                    Save();
                    Debug.Log("Campamento: equipo recuperado, cápsulas repuestas y partida guardada");
                }
            }
        }

        // --- Combate y Habilidades ---

        public Vector3 AttackPoint(CompanionAttack attack, Creature target)
        {
            var actor = ControlledTransform;
            float range = 8f;

            if (attack != null)
            {
                range = attack.range;
            }

            if (target != null)
            {
                return target.transform.position;
            }

            return actor.position + actor.forward * range;
        }

        private void UseAbility(int i)
        {
            if (!PlayingAsCreature || companion == null)
            {
                Debug.Log("Press E to control a companion");
                return;
            }

            var attack = companion.AttackAt(i);
            if (attack == null)
            {
                Debug.Log("No attack assigned to this slot");
                return;
            }

            var target = cam.target;
            companion.Begin(i, AttackPoint(attack, target), target);
        }

        public void ToggleCompanion()
        {
        }

        public void Dismiss()
        {
        }

        private Creature FindBestTarget()
        {
            Creature best = null;
            float bestScore = 999f;
            Vector3 myPos = ControlledTransform.position;
            Vector3 camPos = cam.transform.position;

            foreach (var c in creatures)
            {
                // 1. Descartar criaturas no válidas
                if (c == null || c.ally || c.dead || c.capturing)
                {
                    continue;
                }

                // 2. Comprobar distancia básica
                Vector3 delta = c.transform.position - myPos;
                float dist = delta.magnitude;
                if (dist >= 28f)
                {
                    continue;
                }

                // 3. Comprobar ángulo de visión
                float angle = Vector3.Angle(cam.transform.forward, delta);
                if (angle >= 65f)
                {
                    continue;
                }

                // 4. ¿Mejora la puntuación actual?
                float score = angle + dist;
                if (score >= bestScore)
                {
                    continue;
                }

                // 5. Solo gastamos el rayo de física si es un candidato ganador
                Vector3 eyeTarget = c.transform.position + Vector3.up;
                if (Physics.Linecast(camPos, eyeTarget, 1 << 8))
                {
                    continue; // Hay un obstáculo en medio
                }

                // Nuevo líder
                bestScore = score;
                best = c;
            }

            return best;
        }

        // --- Captura y Proyectiles ---

        private void ThrowBall()
        {
            if (PlayingAsCreature)
            {
                return;
            }

            if (player.health <= 0 || Time.time < nextBall)
            {
                return;
            }

            if (save.balls <= 0)
            {
                Debug.Log("Sin cápsulas. Regresa al campamento");
                return;
            }

            nextBall = Time.time + 0.7f;
            save.balls--;

            Vector3 origin = player.transform.position + Vector3.up * 1.45f + cam.transform.right * 0.55f;
            Vector3 destination = cam.transform.position + cam.transform.forward * 40f;

            RaycastHit hit;
            if (Physics.Raycast(cam.transform.position, cam.transform.forward, out hit, 50f))
            {
                destination = hit.point;
            }

            Vector3 dir = (destination - origin).normalized;
            SpawnShot(origin, dir, null, true);
        }

        public void Capture(Creature c)
        {
            if (c.ally || c.dead || c.capturing)
            {
                return;
            }

            float captureRate = Species.CaptureBase(c.species);
            float healthModifier = Mathf.Lerp(0.45f, 1.5f, 1f - (c.health / c.MaxHealth));

            float stunModifier = 1f;
            if (Time.time < c.stunUntil)
            {
                stunModifier = 1.35f;
            }

            float chance = Mathf.Clamp01(captureRate * healthModifier * stunModifier);
            StartCoroutine(CaptureSequence(c, chance));
        }

        private IEnumerator CaptureSequence(Creature c, float chance)
        {
            c.CancelActions();
            c.capturing = true;
            Burst(c.transform.position + Vector3.up, Color.yellow, 1.8f);

            yield return new WaitForSeconds(0.8f);

            if (c == null)
            {
                yield break;
            }

            if (UnityEngine.Random.value < chance)
            {
                var r = CapturedRecord(c);
                save.party.Add(r);
                Debug.Log("¡Capturado! Pulsa I para abrir tu equipo");
                Destroy(c.gameObject);
                Save();
            }
            else
            {
                c.capturing = false;
                Debug.Log("Se ha escapado. Debilítalo o usa Atadura (3)");
            }
        }

        // --- Guardado y Carga ---

        public void Save()
        {
        }

        public void Load(bool restorePosition = true)
        {
        }
    }
}
