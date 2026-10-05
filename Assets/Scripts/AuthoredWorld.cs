using System;
using UnityEngine;

namespace Wildbound
{
    // A saved scene owns the initial world. Runtime startup binds it; it never regenerates it.
    [DisallowMultipleComponent, RequireComponent(typeof(Game))]
    public sealed class AuthoredWorld : MonoBehaviour
    {
        public const int CurrentVersion = 1;
        [SerializeField] int version = CurrentVersion;
        public int Version { get { return version; } }
        public static Game OwnerInLoadedScenes()
        {
            Game owner = null;
            foreach (var world in UnityEngine.Object.FindObjectsByType<AuthoredWorld>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                var candidate = world.GetComponent<Game>();
                if (!candidate) continue;
                if (owner && owner != candidate) throw new InvalidOperationException("More than one authored Wildbound world is loaded.");
                owner = candidate;
            }
            return owner;
        }
        public void BindRuntime(Game game)
        {
            if (game.gameObject != gameObject || version != CurrentVersion) throw new InvalidOperationException("Invalid authored world binding.");
            if (!game.player || !game.cam) throw new InvalidOperationException("The authored world needs its saved trainer and camera references.");
            game.player.BindRuntime(); game.cam.BindRuntime(game.player.transform);
            var playerAnimator = game.player.GetComponent<Locomotion.ProceduralBodyAnimator>();
            if (playerAnimator && playerAnimator.SavedRig != null) playerAnimator.BindSavedRig();
            game.creatures.Clear();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            {
                foreach (var creature in root.GetComponentsInChildren<Creature>(true))
                {
                    creature.BindRuntime(); game.creatures.Add(creature);
                    var animator = creature.GetComponent<Locomotion.ProceduralBodyAnimator>();
                    if (!animator || animator.SavedRig == null) throw new InvalidOperationException("An authored creature has no saved procedural rig.");
                    animator.BindSavedRig();
                }
            }
        }
    }
}
