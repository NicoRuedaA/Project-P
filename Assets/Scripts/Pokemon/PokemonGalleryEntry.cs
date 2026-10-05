using UnityEngine;

namespace Wildbound.Pokemon
{
    public sealed class PokemonGalleryEntry : MonoBehaviour
    {
        public int dex;
        public string speciesName, archetype;
        public TextAsset sourceProfile;
        public GameObject sourceModel;
        public Transform visual;
        public float displayHeight;
    }
}
