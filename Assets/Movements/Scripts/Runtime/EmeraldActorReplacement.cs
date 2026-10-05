using UnityEngine;

namespace BotwVfx
{
    /// <summary>Temporarily hides an explicitly bound scene actor while this effect replaces it.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(EmeraldMoveVfx))]
    public sealed class EmeraldActorReplacement : MonoBehaviour
    {
        public float visibleFrom = .8f;
        public float visibleUntil = 2.75f;
        Renderer[] actorRenderers;
        bool[] originalVisibility;
        bool hidden;
        EmeraldMoveVfx timeline;
        bool subscribed;

        void OnEnable() => Attach();

        void Attach()
        {
            if (subscribed) return;
            timeline = GetComponent<EmeraldMoveVfx>();
            timeline.Evaluated += Evaluate;
            subscribed = true;
        }

        public void Bind(Transform actor)
        {
            Attach(); // Also needed for edit-mode prefab capture, where OnEnable need not run.
            Restore();
            actorRenderers = actor != null ? actor.GetComponentsInChildren<Renderer>(true) : null;
            originalVisibility = actorRenderers != null ? new bool[actorRenderers.Length] : null;
            if (actorRenderers != null)
                for (int i = 0; i < actorRenderers.Length; i++) originalVisibility[i] = actorRenderers[i].enabled;
        }

        void Evaluate(float time)
        {
            bool hide = time >= visibleFrom && time <= visibleUntil;
            if (!hide) { Restore(); return; }
            if (actorRenderers == null || hidden) return;
            for (int i = 0; i < actorRenderers.Length; i++)
                if (actorRenderers[i] != null) actorRenderers[i].enabled = false;
            hidden = true;
        }

        void Restore()
        {
            if (!hidden || actorRenderers == null) return;
            for (int i = 0; i < actorRenderers.Length; i++)
                if (actorRenderers[i] != null) actorRenderers[i].enabled = originalVisibility[i];
            hidden = false;
        }

        void OnDisable()
        {
            if (timeline != null && subscribed) timeline.Evaluated -= Evaluate;
            subscribed = false;
            Restore();
        }

        void OnDestroy() => Restore();
    }
}
