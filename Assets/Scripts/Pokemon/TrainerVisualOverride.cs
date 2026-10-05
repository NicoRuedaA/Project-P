using UnityEngine;

namespace Pokemon3D.Pokemon
{
    // Visual-only replacement; movement and the existing controller remain authoritative.
    [DisallowMultipleComponent]
    public sealed class TrainerVisualOverride : MonoBehaviour
    {
        public Transform originalVisual, replacementVisual;
        public GameObject modelPrefab;
        public Locomotion.ProceduralBodyAnimator proceduralAnimator;
    }
}
