using UnityEngine;
using Wildbound.Locomotion;

namespace Wildbound.Pokemon
{
    // Scene-only visual substitution: gameplay species and combat data are unchanged.
    public sealed class PokemonCreatureVisualOverride : MonoBehaviour
    {
        public int visualDex = 66;
        public GameObject modelPrefab;
        public Transform originalModel, replacementModel;
        public BodyRig originalRig;
        public BodyMotionProfile originalProfile;
    }
}
