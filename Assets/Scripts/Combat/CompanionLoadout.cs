using UnityEngine;

namespace Pokemon3D.Combat
{
    [CreateAssetMenu(menuName = "Wildbound/Combat/Companion Loadout", fileName = "CompanionLoadout")]
    public sealed class CompanionLoadout : ScriptableObject
    {
        [Tooltip("Stable save identifier. Keep this unchanged after capturing companions using this loadout.")]
        public string id;
        [Tooltip("Keys 1-4, in order. Empty slots are unavailable, not compatibility attacks.")]
        public CompanionAttack[] attacks = new CompanionAttack[4];
        public CompanionAttack AttackAt(int slot) => slot >= 0 && slot < 4 && attacks != null && slot < attacks.Length ? attacks[slot] : null;
    }
}
