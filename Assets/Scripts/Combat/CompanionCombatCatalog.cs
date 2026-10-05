using System;
using UnityEngine;

namespace Pokemon3D.Combat
{
    [CreateAssetMenu(menuName = "Wildbound/Combat/Catalog", fileName = "CompanionCombatCatalog")]
    public sealed class CompanionCombatCatalog : ScriptableObject
    {
        public const string ResourceName = "CompanionCombatCatalog";
        [Serializable]
        public sealed class SpeciesLoadout
        {
            [Range(0, 2)] public int species;
            public CompanionLoadout loadout;
        }
        [Tooltip("One default loadout for each current gameplay species. Pokemon visual dex is unrelated.")]
        public SpeciesLoadout[] speciesLoadouts = new SpeciesLoadout[0];
        [Tooltip("Additional loadouts available to captured companions by their stable save identifier.")]
        public CompanionLoadout[] additionalLoadouts = new CompanionLoadout[0];

        public CompanionLoadout Resolve(string id, int species)
        {
            if (!string.IsNullOrEmpty(id))
            {
                if (speciesLoadouts != null)
                    foreach (var entry in speciesLoadouts)
                        if (entry != null && entry.loadout && entry.loadout.id == id) return entry.loadout;
                if (additionalLoadouts != null)
                    foreach (var loadout in additionalLoadouts)
                        if (loadout && loadout.id == id) return loadout;
                // A removed saved loadout falls back to this species rather than an unrelated creature.
            }
            if (speciesLoadouts != null)
                foreach (var entry in speciesLoadouts)
                    if (entry != null && entry.species == species && entry.loadout) return entry.loadout;
            return null;
        }
    }
}
