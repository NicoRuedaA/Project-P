# Rename namespace Wildbound -> Pokemon3D

## Objective
Rename every C# **namespace identifier** from `Wildbound*` to `Pokemon3D*` across `Assets/**/*.cs`.

## Authorized scope
- namespace declarations, using directives, namespace-qualified type references.

## Explicitly NOT changed (not namespaces)
- string literals: menu paths (`"Wildbound/..."`), shader names (`"Wildbound/Pokemon Gallery"`, `"Wildbound/Toon"`), GameObject name `"Wildbound / bootstrap"`, tag `"WildboundToonShadeBaseline"`, SessionState prefix `"Wildbound.LocalValidationBridge.completed."`, build output names (`"Wildbound.exe"`, `"Wildbound.app"`, `"WildboundBuild"`, `"Wildbound"`).
- comments, file/asset names, `.asmdef` assembly names.

## Exact substitutions (code only, never inside quotes)
1. `namespace Wildbound` -> `namespace Pokemon3D` (covers `.Editor`, `.Combat`, `.Locomotion`, `.Pokemon`)
2. `using Wildbound` -> `using Pokemon3D`
3. `Wildbound.Pokemon.TrainerVisualOverride` -> `Pokemon3D.Pokemon.TrainerVisualOverride` (`Assets/Editor/FullBodyAuthoring.cs`)
4. `Wildbound.Locomotion.ProceduralBodyAnimator` -> `Pokemon3D.Locomotion.ProceduralBodyAnimator` (`Assets/Editor/PokemonGalleryAuthoring.cs`, `Assets/Editor/TrainerModelAuthoring.cs`)

## Affected files (~43)
- `Assets/Scripts/**`: Creature, Game, GameData, World, PlayerMotor, OrbitCamera, Projectile, ToonStyle, ToonMaterials; `Locomotion/` (BodyRig, LocomotionMath, BodyPoseDriver, ProceduralBodyAnimator); `Pokemon/` (PokemonGallery, PokemonGalleryEntry, PokemonCreatureVisualOverride, TrainerVisualOverride); `Combat/` (CompanionAttack, CompanionAttackVfx, CompanionCombatCatalog, CompanionControl, CompanionLoadout).
- `Assets/Editor/**`: AssetFolderOrganization, StarterAssetsCleanup, EditableSceneAuthoring, FullBodyAuthoring, FullBodyValidation, EditorCreatureMotionDiagnostics, PokemonPrefabAuthoring, PokemonGalleryAuthoring, ProceduralLocomotionValidation, ProjectSetup, LocalValidationBridge, TrainerModelAuthoring, CompanionCombatValidation, `BodyMovement/` (3), `ToonIntegration/` (5).

## Checklist
- [ ] T1 Apply substitutions to all affected files
- [ ] T2 Verify 0 matches: `git grep -nE "namespace Wildbound|using Wildbound|Wildbound\.(Pokemon|Locomotion)\."`
- [ ] T3 Verify string literals untouched: `git grep -n '"Wildbound'` still lists the known literals
- [ ] T4 Unity recompiles (owner step)

## Checks / evidence
- `git grep -n "Pokemon3D" -- "*.cs"` shows namespace/using in every previously-Wildbound file.
- No behavior change. Unity MonoBehaviour references are GUID-based, so scenes/prefabs keep resolving.

## Constraints
- Unity 6000.6.4f1. No behavior change. Commit only if explicitly requested.
