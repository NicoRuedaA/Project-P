using UnityEngine;
using BotwVfx;

namespace Pokemon3D.Combat
{
    public enum AttackEffect { Projectile, Area, Stun, Heal }

    [CreateAssetMenu(menuName = "Wildbound/Combat/Attack", fileName = "CompanionAttack")]
    public sealed class CompanionAttack : ScriptableObject
    {
        public string displayName = "Attack";
        public AttackEffect effect;
        [Tooltip("Damage amount, or healing amount for Heal. Stun uses Stun Duration instead.")]
        [Min(0)] public float damage = 18;
        [Min(0)] public float damagePerLevel;
        [Min(0)] public float cooldown = 2;
        [Min(0)] public float castDelay = .35f;
        [Min(.1f)] public float range = 16;
        [Min(.1f)] public float radius = 3.5f;
        [Min(0)] public float stunDuration = 2.5f;
        [Min(.1f)] public float projectileSpeed = 15;
        [Tooltip("Assign an EmeraldMoveVfx prefab from Assets/Movements/Prefabs/Emerald. Visuals do not define damage.")]
        public EmeraldMoveVfx vfx;

        public bool IsValid => System.Enum.IsDefined(typeof(AttackEffect), effect) &&
            Finite(damage, 0) && Finite(damagePerLevel, 0) && Finite(cooldown, 0) && Finite(castDelay, 0) &&
            Finite(range, .1f) && Finite(radius, .1f) && Finite(stunDuration, 0) && Finite(projectileSpeed, .1f);

        static bool Finite(float value, float minimum) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= minimum;

        static readonly CompanionAttack[] defaults = new CompanionAttack[4];
        public static CompanionAttack CompatibilityDefault(int slot)
        {
            if (slot < 0 || slot >= defaults.Length) return null;
            if (defaults[slot]) return defaults[slot];
            var attack = CreateInstance<CompanionAttack>();
            attack.hideFlags = HideFlags.HideAndDontSave;
            attack.name = "Compatibility attack " + (slot + 1);
            attack.displayName = new[] { "Element Projectile", "Area Strike", "Binding", "Recovery" }[slot];
            attack.effect = (AttackEffect)slot;
            attack.cooldown = new[] { 2f, 6f, 9f, 14f }[slot];
            attack.castDelay = slot == 1 ? .65f : .35f;
            attack.damage = slot == 0 ? 18 : slot == 2 ? 0 : 30;
            attack.damagePerLevel = slot == 0 ? 2 : 0;
            attack.range = slot == 0 ? 60 : slot == 2 ? 16 : 3.5f;
            defaults[slot] = attack;
            return attack;
        }
    }
}
