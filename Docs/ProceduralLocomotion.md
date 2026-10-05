# Procedural body locomotion

Every creature made by `World.MakeCreature` now uses displacement-driven procedural locomotion. Wild creatures, newly spawned creatures, and companions share the same animator. Attacks, projectiles, cooldowns, trainer movement, and controller/root authority are unchanged.

## Try it

1. Open `Assets/Scenes/Valley.unity`. The terrain, trainer, camera, nine creatures, and their pivot rigs are visible/editable before Play. Save your scene edits normally.
2. Watch a wild creature walk, stop, turn, and cross the ramp; summon a companion and compare movement.
3. Run **Wildbound → Validate Procedural Locomotion** for the explicit deterministic validation suite. It creates temporary unsaved rigs, checks all 12 families, and removes them. It does not run on reload.
4. Pause, stun, capture, and defeat a creature: the procedural layer should restore its pose without continuing to walk. Casting retains the existing model-scale effect.

The validation entry point is `Wildbound.Editor.ProceduralLocomotionValidation.Run()` (returns a report or throws). `RunMenu()` logs the report and is callable with Unity's editor execution tooling. Live visual review is still needed: scalar/math checks do not establish visual parity.

## Editable world

The complete valley is saved in `Assets/Scenes/Valley.unity`, with shared materials persisted under `Assets/Materials/Valley/`. Edit object transforms, creature rig joints, and materials directly in the Hierarchy/Inspector.

`AuthoredWorld` marks the saved bootstrap as the owner of the initial world. Play binds serialized trainer/camera/creature/rig references instead of calling the generator again. Startup preserves authored spawn transforms; party/inventory still load from the existing save. **F9/Load remains an explicit request to restore the saved player position.** Creature cast scaling and trainer visual motion are relative to their authored visual baselines.

**Wildbound → Create playable valley** is deliberate and idempotent: an existing authored valley is shown without regenerating/saving over edits. A proven generated bootstrap-only scene can be populated in place; otherwise a new target is created additively, preserving the original scene. Unknown existing Valley content is not overwritten. Scene creation never runs automatically on script reload. If an original bootstrap remains loaded alongside an authored world, only the authored `Game` runs at runtime.

Native creation readback on 2026-10-02 verified 15 roots, 824 objects, 683 renderers, one trainer/camera, nine creatures, nine persisted rigs/18 legs, and zero missing/transient materials. A saved-scene preview reload and repeated startup binding preserved object counts, transforms, and material references. This is serialization/startup evidence, not a visual Play-session test.

## Local validation bridge

After Unity imports `Assets/Editor/LocalValidationBridge.cs` once, the editor polls the fixed `Temp/LocalValidationBridge/request.json` every 0.25 seconds while idle. Publish a UTF-8 JSON file atomically into that path:

```json
{"id":"validation-unique-id","command":"validate_locomotion"}
```

Read the matching `id` in `result.json`; `success`, `error`, `report`, and `editor` contain the outcome. `ready.json` is a one-second editor-status heartbeat. Only `status`, `validate_locomotion`, `refresh`, `inspect_scene`, and `build_editable_scene` are accepted; refresh publishes its response before scheduling asset refresh. Validation requires Edit mode and creates/removes only the existing unsaved test fixtures.

Requests have exactly the two plain-string fields shown, at most 4 KiB; IDs contain 1–64 letters, digits, underscores, or hyphens. Use a fresh ID for each deliberate run. A claimed request is not executed again after domain reload, and session ID replay is rejected. Interrupted requests return an error rather than rerunning. No fresh request means **no automatic validation** on reload.

This is a same-user local file interface, not an authentication boundary. It accepts no code, arbitrary paths, shell commands, network input, credentials, arbitrary scene saves, or play-mode control. `build_editable_scene` inspects existing content and can save only the fixed `Assets/Scenes/Valley.unity` path through Unity APIs; `inspect_scene` only reports the loaded scene hierarchy. Its request/results are temporary editor files, not commit artifacts.

## What was ported

The source is `/home/nico/dev/pokemon-emerald-arena/arena-3d`, particularly `src/rig.js`, `locomotion.js`, `gait.js`, `multileg.js`, `procedural.js`, and `src/body/`. No attack/move modules, Pokémon models, source species mappings, or authored clips were imported.

| Family | Source-derived behavior | Required target anatomy |
| --- | --- | --- |
| Biped | Alternating steps, world-space foot plants, measured two-bone IK, idle weight shift, torso/head/tail motion | Two legs |
| Quadruped | Speed-relative walk/trot/gallop blend, stance-preserving footfalls/duty factors, suspension carriage/back flex | Four legs |
| Winged | Geometry-oriented fold/spread, hover/cruise/boost/takeoff beats, gliding, bank/lean | Wing chains; walking legs compose independently |
| Serpentine | Travel-phased, head-tapered body wave, turn curvature, vertical/lateral planes, swimming slip/treading | Connected body chain |
| Aquatic | Tail beat by water covered, narrow idle tread, turn trail, phased fin strokes; short-body ground hops | Body/tail chain and optional paddles |
| Floater | Smooth hovering bob/rock and lagged free appendages | Waist and optional appendages |
| Roller | Distance/radius rolling, terrain ring support, airborne spin, spring righting, idle wobble | Waist and configured primitive radius |
| Group | Deterministic member timing, ripple hops, speed/turn lag, independent contact, idle wobble | Separate member pivots |
| Hopper | Distance-driven hop arc, volume-preserving squash/stretch, head-pivot pendulum, leaf/chain spring lag | Waist/head and hanging/leaf chains |
| Burrower | Travel-phased head bob, start/stop duck springs and per-column stagger | Mound/head columns |
| Amorphous | Travel-phased rim bulge, volume-preserving stretch, acceleration-driven slosh spring, soft free chains | Mass/rim pivots |
| Arthropod | Slow metachronal wave → fast tripod, stance/duty transitions, terrain-aligned carapace | Multiple leg pairs (sideways travel is unsupported) |

Scalar gait bands, footfall values, settling rules, hop arcs, pulse contraction, and bounded spring integration follow the source equations. The adapter composes modules from **anatomy and profile flags**, not an exclusive archetype switch. For example, winged bodies still walk on their legs, and a swimming profile can also pulse, stroke paddles, and trail appendages.

## Target binding and limitations

The three existing custom species remain **bipeds**, matching their original two-legged anatomy. Their replacement primitive rig adds real upper/knee/foot pivots and a jointed tail; it keeps their colors, face, ears, and horns. There is no claimed equivalence between these species and source Pokémon. Generated strides are bounded by measured limb reach.

`PrimitiveBodyRig.Build(model, species, BodyMotionProfile.For(archetype))` creates demonstrator anatomy for any of the 12 presets. For an imported skeleton, construct `BodyRig` with its transforms, profile, limb sets, and wing geometry, then call `ProceduralBodyAnimator.Bind`. `BodyMotionProfile` flags can be combined; choosing an enum alone does not rebind a rig already in play.

`traversalMode` is an explicit presentation input (`Ground`, `Swim`, `Flight`, `Glide`, `Climb`); `boosting` and `takingOff` control wing effort. This port does **not** invent flying/swimming/climbing gameplay controllers or automatically classify the lake as water. Existing creatures remain ground-controlled.

Intentional Unity/primitive simplifications:

- Renderer bounds and explicit rim/mound pivots replace the source's FBX skin-weight support sampling, skin deformation/shear, detailed sole/sector contact, and per-vertex waterline search. Contact samples physics scenery on **layer 8**; there is no water-volume API.
- Limb IK measures actual joint geometry. Complex flat/shared-joint rigs, carried-young filtering, source `sitUp`, vine reach, authored clip restoration/mixing, full climbing/jump/landing poses, and imported-skeleton calibration are not included. Generated rigs avoid these unsupported layouts.
- Wings use supplied span/normal and source beat/fold constants; exact membrane-normal fold-curl axes and skin-derived areas require imported-rig calibration. Free-chain responses approximate source appendage roles rather than its complete annotation vocabulary.
- `BodyMotionProfile.sideways` is inactive reserved source metadata; this adapter does not apply sideways body-facing or crab travel.
- Group/ooze/serpent ground contact uses primitive support approximations; it does not reproduce the source's mesh-accurate slope/wall support or water behavior.
- Stun/death/capture/pause restore/reset the layer immediately. Resumption starts with a new displacement baseline. This deliberately prioritizes the current game's lifecycle over source authored-combat blending.

## Rollback boundary

Remove `Assets/Scripts/Locomotion/`, `Assets/Editor/ProceduralLocomotionValidation.cs`, and this document; restore only the locomotion/factory edits in `Creature.cs` and `World.cs`. Do not revert pre-existing package, project-version, scene, or asset changes. The separate Unity bridge setup and editable-scene authoring are not part of this locomotion rollback. Preserve the authored scene/material assets and any user edits when planning an authoring rollback; remove/rebind its procedural components before removing the locomotion library.
