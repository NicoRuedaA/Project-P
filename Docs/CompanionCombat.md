# Companion control and attacks

Press **E** to summon and directly control the selected companion. Movement is camera-relative, with the trainer stationary; **Shift** sprints. Press **E** again to withdraw and return trainer control. **Tab** selects a target, **Q** clears it, and **1–4** use the companion's configured attack slots. Companions do not automatically attack while controlled. Casting, stun, capture, and pause suppress movement/actions as appropriate. Companion jumping is not implemented.

Capture capsules and camp interaction are trainer-only: withdraw before using them. Defeat, party selection, loading, respawning, disabling/destroying the companion, and game shutdown restore trainer/camera ownership.

## Configure in the Inspector

1. Choose **Wildbound > Combat > Select Attack Catalog**. This selects `Assets/Resources/CompanionCombatCatalog.asset`; its direct asset references are available in built players, not just the Editor.
2. Open the desired default loadout under `Assets/Data/Combat/Loadouts/`. Its **Attacks** array has up to four slots corresponding to keys 1–4. An empty slot has no attack.
3. Open an attack under `Assets/Data/Combat/Attacks/`. Configure **Effect**, **Damage** (healing amount for Heal), **Damage Per Level**, **Cooldown**, **Cast Delay**, **Range**, **Radius**, **Stun Duration**, and **Projectile Speed**. Drag an effect prefab from `Assets/Movements/Prefabs/Emerald/` into **Vfx**. Missing VFX is safe: gameplay still works without presentation.
4. To create new assets, use **Create > Wildbound > Combat > Attack / Companion Loadout**. Give each distinct loadout a unique, stable **Id**. Add custom loadouts to the catalog's **Additional Loadouts**; do not rename their IDs after they have been captured/saved.

For an individual authored wild creature, set its **Creature > Loadout** override. Capture stores that loadout's ID, and later summons resolve it through the catalog. Without an override, the catalog selects the current gameplay species' default. Old saves continue to resolve their species default; no save-format migration is required. Removing an identified loadout falls back to the species default.

The three shipped loadouts are explicitly **compatibility examples**, not Pokémon learnsets. Each has independent attack assets, so editing Bramble's attacks does not change Emberfox's or Tidehorn's. They retain projectile, area, binding, and recovery behaviors with assigned Movements effects. Existing gameplay species are Bramble/Emberfox/Tidehorn (0–2); a Pokémon model's visual dex does not change identity or assign attacks. Runtime companions retain their existing primitive models.

## Timing and effects

Gameplay uses **Cast Delay** for execution and projectile collision for projectile damage; the VFX's artistic timeline does not define damage, range, or cooldown. Adjust the cast delay to suit the effect's anticipation/impact, allowing for projectile travel. Session cooldowns are keyed by saved companion ID and survive withdrawal/re-summoning; restarting the game resets them. Stun/cancellation does not refund a cooldown.

Effects use the imported Emerald local anchors, are instantiated once per cast, and clean up after finishing/cancellation/owner removal. Integration strips timeline slow-motion and shake cues recursively before playback because the imported director writes global `Time.timeScale`. Imported VFX assets remain unchanged. Non-uniform X scaling fits attacker/target separation and can stretch effect geometry.

## Focused validation

**Wildbound > Combat > Validate Companion Combat** runs deterministic checks in temporary Edit-mode preview fixtures without saving or changing live scenes or Play mode. The same operation is exposed by the local bridge:

```json
{"id":"unique-safe-id","command":"validate_companion_combat"}
```

Publish that request atomically to the bridge's fixed `Temp/LocalValidationBridge/request.json`, after confirming the Editor is idle. Read the matching `result.json`; do not repeat request IDs. Checks cover ownership restoration, camera targets, configured loadouts, legacy/captured save identity, action origin, session cooldowns, constraints, and pause-safe VFX cues. These checks do **not** prove live input feel, runtime collisions, rendered VFX, or a build.
