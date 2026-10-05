using System.Collections.Generic;
using UnityEngine;
using Unity.Cinemachine;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Pokemon3D.Combat
{
    [DefaultExecutionOrder(-80)]
    public sealed class CompanionControl : MonoBehaviour
    {
        public Creature Actor { get; private set; }
        public bool IsPossessing => Game.CanControl(Actor) && game && game.companion == Actor;
        public StarterAssetsInputs Input { get; private set; }
        Game game;
        ThirdPersonController trainerController;
        bool controllerWasEnabled, rootMotion;
        Animator trainerAnimator;
        Transform pivot;
        float yaw, pitch;
        readonly List<CameraBinding> cameras = new List<CameraBinding>();
        readonly List<ColliderTag> colliderTags = new List<ColliderTag>();
        sealed class ColliderTag { public GameObject owner; public string original, applied; }
        sealed class CameraBinding
        {
            public CinemachineVirtualCameraBase camera;
            public Transform follow, lookAt;
            public bool changedLookAt;
            public CameraTarget originalTarget;
            public bool modern;
            public Cinemachine3rdPersonFollow legacyBody;
            public CinemachineThirdPersonFollow body;
            public string originalIgnoreTag, appliedIgnoreTag;
        }

        public void Initialize(Game owner) { game = owner; }

        // Explicit candidates keep isolated preview fixtures independent of global DontSave discovery.
        public void Possess(Creature creature, CinemachineVirtualCameraBase[] cameraCandidates = null)
        {
            Release();
            if (!game || !game.player || !Game.CanControl(creature) || game.companion != creature) return;
            Actor = creature;
            try
            {
                trainerController = game.player.GetComponent<ThirdPersonController>();
                Input = game.player.GetComponent<StarterAssetsInputs>();
                trainerAnimator = game.player.GetComponent<Animator>();
                if (trainerController) { controllerWasEnabled = trainerController.enabled; trainerController.enabled = false; }
                if (Input) Input.jump = false;
                if (trainerAnimator)
                {
                    rootMotion = trainerAnimator.applyRootMotion;
                    trainerAnimator.applyRootMotion = false;
                    SetMovementAnimation(0);
                }
                Transform trainerPivot = trainerController && trainerController.CinemachineCameraTarget ? trainerController.CinemachineCameraTarget.transform : game.player.transform;
                pivot = new GameObject("Companion Camera Target").transform;
                pivot.SetParent(creature.transform, false);
                var cc = creature.GetComponent<CharacterController>();
                pivot.localPosition = Vector3.up * (cc ? cc.center.y + cc.height * .5f : 1.4f);
                var rotation = trainerPivot.rotation.eulerAngles;
                yaw = rotation.y; pitch = Mathf.Clamp(Mathf.DeltaAngle(0, rotation.x), -30, 70);
                pivot.rotation = Quaternion.Euler(pitch, yaw, 0);
                foreach (var camera in cameraCandidates ?? FindObjectsByType<CinemachineVirtualCameraBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (!camera || (camera.Follow != trainerPivot && camera.Follow != game.player.transform)) continue;
                    var binding = new CameraBinding { camera = camera, follow = camera.Follow, lookAt = camera.LookAt };
                    if (camera is CinemachineCamera modern) { binding.modern = true; binding.originalTarget = modern.Target; }
                    var legacy = camera as CinemachineVirtualCamera;
                    var legacyBody = legacy ? legacy.GetCinemachineComponent<Cinemachine3rdPersonFollow>() : null;
                    var body = camera.GetComponent<CinemachineThirdPersonFollow>();
                    if (legacyBody && legacyBody.enabled && legacyBody.CameraCollisionFilter.value != 0) { binding.legacyBody = legacyBody; binding.originalIgnoreTag = legacyBody.IgnoreTag; }
                    else if (body && body.enabled && body.AvoidObstacles.Enabled && body.AvoidObstacles.CollisionFilter.value != 0) { binding.body = body; binding.originalIgnoreTag = body.AvoidObstacles.IgnoreTag; }
                    cameras.Add(binding);
                    camera.Follow = pivot;
                    binding.changedLookAt = binding.lookAt == trainerPivot || binding.lookAt == game.player.transform;
                    if (binding.changedLookAt) camera.LookAt = pivot;
                    camera.PreviousStateIsValid = false;
                }
                ExcludeControlledColliders();
            }
            catch { Release(); throw; }
        }

        void ExcludeControlledColliders()
        {
            string ignoreTag = null;
            foreach (var binding in cameras)
                if ((binding.legacyBody || binding.body) && !string.IsNullOrEmpty(binding.originalIgnoreTag) && binding.originalIgnoreTag != "Untagged") { ignoreTag = binding.originalIgnoreTag; break; }
            if (ignoreTag == null)
            {
                if (!cameras.Exists(binding => binding.legacyBody || binding.body)) return;
                ignoreTag = "Player";
            }
            Actor.CompareTag(ignoreTag); // Validate the existing tag before changing any collider.
            foreach (var binding in cameras)
            {
                if (!binding.legacyBody && !binding.body) continue;
                binding.appliedIgnoreTag = ignoreTag;
                if (binding.legacyBody) binding.legacyBody.IgnoreTag = ignoreTag;
                else { var settings = binding.body.AvoidObstacles; settings.IgnoreTag = ignoreTag; binding.body.AvoidObstacles = settings; }
            }
            var owners = new HashSet<GameObject>();
            foreach (var collider in Actor.GetComponentsInChildren<Collider>(true))
            {
                if (collider.GetComponentInParent<Creature>() != Actor || !owners.Add(collider.gameObject) || collider.CompareTag(ignoreTag)) continue;
                colliderTags.Add(new ColliderTag { owner = collider.gameObject, original = collider.tag, applied = ignoreTag });
                collider.gameObject.tag = ignoreTag;
            }
        }

        public void Release()
        {
            foreach (var binding in cameras)
            {
                if (binding.legacyBody && binding.legacyBody.IgnoreTag == binding.appliedIgnoreTag) binding.legacyBody.IgnoreTag = binding.originalIgnoreTag;
                if (binding.body && binding.body.AvoidObstacles.IgnoreTag == binding.appliedIgnoreTag) { var settings = binding.body.AvoidObstacles; settings.IgnoreTag = binding.originalIgnoreTag; binding.body.AvoidObstacles = settings; }
                if (!binding.camera) continue;
                if (binding.modern && binding.camera is CinemachineCamera modern) modern.Target = binding.originalTarget;
                else { binding.camera.Follow = binding.follow; if (binding.changedLookAt) binding.camera.LookAt = binding.lookAt; }
                binding.camera.PreviousStateIsValid = false;
            }
            cameras.Clear();
            foreach (var tag in colliderTags) if (tag.owner && tag.owner.tag == tag.applied) tag.owner.tag = tag.original;
            colliderTags.Clear();
            if (trainerController) trainerController.enabled = controllerWasEnabled;
            if (trainerAnimator) trainerAnimator.applyRootMotion = rootMotion;
            if (Input) Input.jump = false;
            trainerController = null; trainerAnimator = null; Input = null; Actor = null;
            if (pivot) { if (Application.isPlaying) Destroy(pivot.gameObject); else DestroyImmediate(pivot.gameObject); }
            pivot = null;
        }

        void SetMovementAnimation(float value)
        {
            if (!trainerAnimator || !trainerAnimator.runtimeAnimatorController) return;
            foreach (var parameter in trainerAnimator.parameters)
                if (parameter.type == AnimatorControllerParameterType.Float && (parameter.name == "Speed" || parameter.name == "MotionSpeed")) trainerAnimator.SetFloat(parameter.nameHash, value);
        }

        void LateUpdate()
        {
            if (!Game.CanControl(Actor) || !game || game.companion != Actor) { if (game && Actor) game.ReleaseCompanion(Actor); else if (pivot) Release(); return; }
            if (!game || game.paused || !pivot || !Input) return;
            if (cameras.Count == 0) return; // An enabled legacy OrbitCamera owns its own rotation.
            float multiplier = Time.deltaTime;
#if ENABLE_INPUT_SYSTEM
            var playerInput = game.player.GetComponent<PlayerInput>();
            if (playerInput && playerInput.currentControlScheme == "KeyboardMouse") multiplier = 1;
#endif
            yaw += Input.look.x * multiplier;
            pitch = Mathf.Clamp(pitch + Input.look.y * multiplier, -30, 70);
            pivot.rotation = Quaternion.Euler(pitch, yaw, 0);
        }

        public Vector3 MovementDirection(Vector2 move, Transform camera)
        {
            var forward = camera ? camera.forward : Vector3.forward;
            forward.y = 0;
            if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
            forward.Normalize();
            return Vector3.ClampMagnitude(forward * move.y + Vector3.Cross(Vector3.up, forward) * move.x, 1);
        }

        void OnDisable() { Release(); }
        void OnDestroy() { Release(); }
    }
}
