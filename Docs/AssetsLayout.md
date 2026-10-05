# Assets layout

Use one root per asset type. Authoring tools use these paths; do not recreate the old `Assets/Pokemon` or singular `Assets/Prefab` folders.

```text
Assets/
  Data/Pokemon/           Catalog151.json and source Profiles/
  Editor/                Editor-only tools
  Materials/
    Pokemon/             Specimen and gallery materials
    Trainer/             Trainer materials
    Valley/              Environment materials
  Models/
    Pokemon/<rig>/       Original FBX and companion PNG files
    Trainer/             ptrainer.fbx, images/, source notice and catalog
  Prefabs/
    Pokemon/             151 static specimen prefabs
    Trainer/             PokemonTrainer.prefab
    Wild.prefab          Existing gameplay prefab
  Resources/             ToonStyle.asset and UnityToonDefault.mat
  Scenes/                Valley.unity
  Scripts/               Runtime code
  Shaders/               Project-owned shaders
```

Source textures intentionally stay with their models: FBX texture discovery depends on relative source locations. Keep each imported bundle intact; standalone textures can use a future `Assets/Textures` root. Materials and prefabs are shared assets outside those bundles.

Move assets through Unity's Project window or `AssetDatabase.MoveAsset`, retaining their `.meta` files and GUIDs. Do not copy/regenerate assets to organize them. Keep `Resources` names stable because runtime loading uses them. Keep editor tools under `Editor` to preserve compilation semantics.

The one-time, explicit **Wildbound > Assets > Consolidate Asset Folders** command preflights its fixed migration, refuses collisions, validates GUIDs/local IDs/dependencies and never saves scenes. It is idempotent once this layout exists. Evidence is written to ignored `Library/AssetOrganization`, not `Temp`.
