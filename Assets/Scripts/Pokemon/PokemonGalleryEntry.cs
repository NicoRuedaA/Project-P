using UnityEngine;

namespace Pokemon3D.Pokemon
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
