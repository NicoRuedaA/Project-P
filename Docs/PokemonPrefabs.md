# Reusable Pokemon prefabs and moving scene specimens

`Assets/Prefabs/Pokemon` contains one reusable model prefab per first-generation Pokemon. These centered, bounds-grounded assets retain the original imported skeleton, persistent gallery materials, and source-profile metadata; they contain no gameplay controller or automatic procedural binding.

The original nine moving Valley creatures use **Machop (066)** as a visual substitute. Their gameplay species IDs, health, AI, abilities, actor transforms, and controller authority are unchanged. Press Play to see the existing wandering/combat controllers drive the imported skin through the procedural locomotion animator.

## Tested binding boundary

Only Machop is explicitly adapted. Its source profile names two unique three-joint leg chains (`LThigh/LLeg/LFoot`, `RThigh/RLeg/RFoot`), arms, spine, head, and tail. Native binding verifies actual hierarchy and measures world-space segment vectors and ankle-to-ground clearance. Two-bone IK uses those vectors, not assumed imported local axes. Bone rest rotations and mesh bind poses are preserved; ordinary Animator playback remains disabled.

The optional `LimbRig.soleHeight` stores calibrated ankle clearance in model units. Zero keeps the original primitive-rig behavior; runtime visual scaling applies clearance once. Machop's nearly extended 0.637 m legs require a small standing compression (`BodyRig.stanceLowering`) and a measured-length stride cap; the source 1.89 m stride otherwise requests unreachable planted feet. Original bone rest transforms and bind poses are not rewritten.

This is not a universal adapter for all 151 skeletons. Their reusable prefabs remain static unless a compatible binding is explicitly implemented and verified. Dynamically spawned companions still use the original game factory visuals; this change substitutes only the nine initial authored actors.

## Safe authoring and rollback

The local bridge exposes only the fixed typed operations `export_pokemon_prefabs`, `replace_creature_visuals`, and `inspect_pokemon_prefabs`. First construction refuses unrelated unsaved scene edits or conflicting prefab metadata; existing prefabs are inspected without overwriting authored changes.

Each substituted actor keeps a `PokemonCreatureVisualOverride` with its disabled original visual, original rig/profile, replacement model, and prefab references. To restore all nine originals, save unrelated edits first, then use **Wildbound > Restore Original Creature Visuals**. This restores original model/rig/profile references and removes only the substituted visuals and markers; the scene is saved natively. The rollback is also exercised in an isolated saved-scene preview. Do not edit scene YAML.

The gallery and existing combat logic are not replaced. Original source materials retain the appearance limitations documented in [PokemonGallery.md](PokemonGallery.md).

## Verification boundary

Native validation uses an isolated preview actor and a hidden temporary physics floor, without entering Play or writing gameplay save files. It checks actual-displacement phase, blocked phase, real bone and baked-skin deformation, finite joints, calibrated foot contact, suppression/rest restoration, and resume discontinuities. Saved-scene preview reload and repeated authored initialization check that the replacement and rollback references survive startup.

Native evidence lives only under `Temp/LocalValidationBridge`. Automated proof does not replace a visual Play-mode check.


## Imported trainer

`Assets/Prefabs/Trainer/PokemonTrainer.prefab` is the requested male `ptrainer.fbx`, with five genuinely referenced source PNGs and six persistent Standard materials. The original source files remain unchanged. The FBX includes a Pokeball material dependency; no separate Pokeball or female model was imported.

The visual is centered, grounded, normalized to 1.8 m, and aligned using measured head/foot/toe bone directions. Only the whole visual wrapper is rotated; source bone rests and bind poses stay intact. Alternate mouth/blink and left-hand ball meshes are retained but disabled for a neutral face. The original player visual is retained inactive through `TrainerVisualOverride`; the player root, CharacterController, camera references, movement logic, health and stamina are unchanged.

The trainer and nine Machop actors now share skeletal procedural locomotion, including relaxed arms, elbow flexion, torso/head response and non-attack traversal weights. The player keeps its original control movement and dodge logic; its old whole-visual bob is bypassed only with an active bound animator. No authored walking clips were added. Standard color-map rendering is not Smash's original specialized eye/material shader fidelity. Attribution is retained in `Assets/Models/Trainer/SourceNotice.txt`; local import does not grant redistribution rights. See [PokemonProceduralSetup.md](PokemonProceduralSetup.md) for the manual Inspector workflow.

Rollback: save unrelated edits and use **Wildbound > Restore Original Trainer Visual**. This operation and repeated saved-scene authored initialization are verified in an isolated preview without entering Play.

## Native evidence

- Imported Machop: 7,327 checks, 120 frames with changed bone rotations; baked vertices change by up to 0.3485 model units, proving deformation beyond root motion. Maximum planted ankle clearance error: 0.000033 m.
- Locomotion regression: 1,686 checks across the twelve body families.
- `Temp/LocalValidationBridge/pokemon-machop-rest-stride.png`: isolated native rendered rest/stride comparison.
- `Temp/LocalValidationBridge/pokemon-trainer-preview.png`: native rendered trainer visual.

Open saved Valley and press Play to inspect the nine wandering Machop actors and the trainer. Automated validation does not enter real Play mode or write the player's save file.

Full-body correction: region-isolated weighted torso/head/upper-arm/forearm/hand deformation and bend/contraphase checks supplement the earlier leg-only/global-skin test. The source bind/rest pose remains unchanged and returns exactly under suppression.
