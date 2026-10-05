# Unity Toon Shader integration

The project uses official **Unity Toon Shader 0.15.1-preview** (`Toon/Toon`) on
ordinary Valley, trainer and Pokemon materials, without migrating Built-in RP.
This is the latest Unity-registry version compatible with Unity 6000.0; the
publisher currently distributes this package as preview releases.

## Edit the global outline once

1. Open **Wildbound > Rendering > Toon Style**.
2. Set **Outline**, **Width** and **Color**.
3. Click **Apply to All Toon Materials** to update every saved `Toon/Toon`
   material, including the runtime template. Outlines are disabled by default.

The shared profile is `Assets/Resources/ToonStyle.asset`. New project-generated
materials read it automatically; existing materials update only when applied.
Applying changes only outline settings, preserving each material's base/shade
colors, textures, UV transforms, transparency, culling and render queue. Width
and color remain stored when the outline is off, so toggling it back on restores
them. The apply operation supports Undo and never saves scenes. Editing and
applying are disabled during Play mode or import/compilation.

Style-change validation and pre-change backups are in ignored
`Library/ToonStyle/`. No vendor package shader source is modified.

## Authoring

- Package Manager restores the pinned package and its dependencies automatically.
- `World.Part` and character/gallery authoring use `ToonMaterials` for the same
  three-band shading and source color/maps; outline settings come from the shared style profile.
- `Assets/Resources/UnityToonDefault.mat` retains the shader/variants for procedural
  builds. Keep this asset even when starting from a bootstrap-only scene.
- **Wildbound > Rendering > Apply Unity Toon Shader** converts supported material
  assets in place. Already-converted materials are not reset; GUIDs, texture
  transforms, base colors and material references stay intact.

## Intentional exceptions

The 21 gallery flame materials and Venusaur's upper-UV-tile eye material keep
`Wildbound/Pokemon Gallery`. It implements special transparent emissive flame
and two-tile atlas sampling that cannot be reproduced by simply changing the
shader. Source FBX-embedded materials, other custom shaders and non-opaque
Standard materials are not rewritten. Packed atlas alpha is not used as skin
opacity. No source textures, meshes, prefabs or saved scenes are regenerated.

## Verification and rollback boundary

The integration checks material color/map/UV preservation, procedural factory
settings, a Resources template, and saved Valley material references. Evidence
is written to ignored `Library/ToonIntegration/`; a graphics-enabled editor also
renders `valley-unity-toon.png`. Null-device batch checks do **not** prove GPU
shader compilation or visual appearance.

The change boundary is the package dependency/lock, shared material factory,
world/trainer/gallery authoring paths, converted material assets and Resources
template. Restore these together from a pre-change backup to roll back; do not
regenerate Valley or source models. Subsequent conversions retain original
material bytes under `Library/ToonIntegration/OriginalMaterials/` (not `Temp`,
which Unity clears on batch exit). The first migration's temporary backups were
cleared by Unity; do not treat that folder as its complete rollback backup.
