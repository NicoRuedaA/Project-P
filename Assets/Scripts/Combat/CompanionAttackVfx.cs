using UnityEngine;
using BotwVfx;

namespace Pokemon3D.Combat
{
    // Owns presentation only: gameplay impact remains controlled by Creature's cast delay and projectile collision.
    public sealed class CompanionAttackVfx : MonoBehaviour
    {
        EmeraldMoveVfx timeline;
        Creature owner;

        public static CompanionAttackVfx Play(CompanionAttack attack, Creature attacker, Vector3 target)
        {
            if (!attack || !attack.vfx || !attacker) return null;
            Vector3 origin = attacker.transform.position;
            target.y = origin.y;
            var delta = target - origin;
            if (delta.sqrMagnitude < .01f) { delta = attacker.transform.forward * 2; target = origin + delta; }
            var fx = Instantiate(attack.vfx, attacker.transform);
            fx.transform.SetParent(null, true);
            fx.transform.SetPositionAndRotation((origin + target) * .5f, Quaternion.FromToRotation(Vector3.right, delta.normalized));
            fx.transform.localScale = new Vector3(delta.magnitude / 6f, 1, 1);
            var handle = fx.gameObject.AddComponent<CompanionAttackVfx>();
            handle.owner = attacker;
            handle.timeline = fx;
            PrepareForGameplay(fx);
            if (fx.TryGetComponent<EmeraldActorReplacement>(out var replacement)) replacement.Bind(attacker.model ? attacker.model : attacker.transform);
            fx.Finished += handle.Cancel;
            fx.Play();
            return handle;
        }

        public static void PrepareForGameplay(EmeraldMoveVfx fx)
        {
            if (!fx) return;
            // Both cue types lazily create VfxDirector, which overwrites the game's global pause time scale.
            foreach (var child in fx.GetComponentsInChildren<VfxTimeline>(true))
            {
                child.shakes.Clear();
                child.slowMotion.Clear();
                child.loop = false;
                child.playOnStart = false;
            }
        }

        void Update()
        {
            if (!owner || owner.dead || owner.capturing || !owner.isActiveAndEnabled) Cancel();
        }

        public void Cancel()
        {
            if (timeline) { timeline.Finished -= Cancel; timeline.StopAndClear(); }
            if (Application.isPlaying) Destroy(gameObject); else DestroyImmediate(gameObject);
        }

        void OnDestroy()
        {
            if (timeline) { timeline.Finished -= Cancel; timeline.StopAndClear(); }
        }
    }
}
