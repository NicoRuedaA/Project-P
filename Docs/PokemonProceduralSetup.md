# Configure body movement in one Inspector

Select the **moving actor** (for example `Wild`), not its visible Pokemon child. Open **Procedural Body Animator > Body Movement**. This panel owns the visible model and skeleton references; do not also edit `Creature.Model` or the trainer visual.

## Quick path

1. Stop Play yourself. In **Model**, drag the visible model child under this actor.
2. Choose **Body Type**. Changing type supplies its defaults in the draft; it does not recognize bones automatically.
3. Assign **Waist**, optional **Spine / Head**, and the real joints shown below. Expand the model in Hierarchy and drag its bones into each field.
4. Click **Configure and Validate**. A red message tells you which field is wrong; invalid drafts change nothing. Success synchronizes model references, body parameters and visual replacement metadata together.
5. Save the scene yourself when ready, then press Play. The actor must actually move: this component poses bones, not AI or controls.

Opening the panel, changing draft fields and switching selection do **not** change the scene. Changes are applied only by the button; use **Ctrl+Z / Ctrl+Shift+Z** for Undo/Redo. Transient motion caches are invalidated on Undo/Redo and reconstructed on the next configuration or runtime startup, without altering saved bone poses. Nothing is saved automatically.

## Example: Bulbasaur on Wild

Use **Body Type = Quadruped**. There are four labeled leg rows and no separate Arms to duplicate:

| Field / row | Bones inside the visible Bulbasaur |
|---|---|
| Waist / Spine / Head | `Waist / Spine1 / Head` |
| Rear Left > Upper / Lower / Foot | `LThigh / LLeg / LFoot` |
| Rear Right > Upper / Lower / Foot | `RThigh / RLeg / RFoot` |
| Front Left > Upper / Lower / Foot | `LArm / LForeArm / LHand` |
| Front Right > Upper / Lower / Foot | `RArm / RForeArm / RHand` |

Left/right and front/rear flags are assigned automatically. The front limbs are **Legs**, despite the source bone names containing Arm/Hand. Leave optional tails empty: Bulbasaur has none. Source profile: `Assets/Data/Pokemon/Profiles/0001.json`; at the supplied display height 2.55 its source stride is 0.586, available under **Advanced tuning**.

If the warning says the visible model is Bulbasaur but movement targets an inactive Machop, the old bones are not a working setup. The draft starts with empty anatomy instead of copying the other model's bones. Assign Bulbasaur's own bones and click the button. The original rollback data remains untouched.

## Biped and other bodies

- **Biped:** two labeled legs; optional arm chains have three ordered joints: Upper Arm, Forearm, Hand. Use Right for the right arm. Machop example: `LThigh/LLeg/LFoot`, `LArm/LForeArm/LHand`, with optional shoulder/wrist rest joints `LShoulder/LHand`; right-side equivalents.
- **Winged / Arthropod / Hopper:** walking chains live in a collapsed section; Wings need actual wing joints and geometry. Multi-leg setups retain explicit side/front/set fields.
- **Serpentine / Aquatic / Group / Amorphous / Burrower:** the panel shows the corresponding Body, Paddles, Members, Mass or Columns sections. Supply real anatomy; choosing an enum does not create it. Existing unmodified specialized configuration is preserved.
- **Advanced tuning:** display height, stride, stance lowering, terrain mask and composition flags. Valley terrain uses layer 8. Per-leg sole clearance is under **Leg clearance**. Imported ground stance still needs appropriate measured clearance/reach; there is no universal all-model calibration or auto-binding button.

The model must be active, beneath this actor, **+Y up / +Z forward**, and use actual deforming skeleton joints. Disable an ordinary Animator controller that plays clips on the same bones. Wrong-model bones, invalid hierarchies, zero-length segments and invisible skin associations are rejected before application.

All 151 shipped Pokemon prefabs remain static models. This panel simplifies manual authoring; it does not claim all skeletons are automatically compatible. Save a configured actor as a new prefab in your own folder rather than overwriting the shipped static model or source FBX. Gameplay `Creature.Species` values are not Pokemon dex IDs; leave them unchanged.

## Optional editor validation

**Wildbound > Validate Body Movement Inspector** runs isolated preview checks for draft-only controls, stale references, biped/quadruped model synchronization, rollback backups and Undo/Redo. It does not configure or save the live scene and does not enter Play. Read the result in Console; it is not a substitute for visually testing your newly assigned skeleton.
