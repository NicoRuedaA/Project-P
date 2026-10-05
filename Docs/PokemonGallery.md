# Editable first-generation Pokemon gallery

The 151 original model assets are displayed as a standalone exhibit in `Valley`, beside the eastern edge of the original world. Expand **Pokemon / first 151 gallery** in the Hierarchy: its numbered specimen roots remain editable before Play.

## Included

- `Assets/Models/Pokemon`: 151 original binary FBX files and 432 companion PNG textures, copied without changing bytes.
- `Assets/Data/Pokemon/Profiles`: 151 source JSON profiles retained as reference metadata.
- `Assets/Data/Pokemon/Catalog151.json`: bounded first-generation catalog, including diffuse texture relationships read directly from FBX connections.
- Each specimen retains its imported skin/skeleton and persistent compatible materials. Its profile display height determines uniform scaling; bounds ground it on the exhibit platform.

These are **model exhibits**, not gameplay `Creature` instances. No attacks, AI, dex-to-gameplay mappings, or automatic procedural rig bindings are attached. Source animation data remains in the original FBX bytes; Unity animation-clip import and playback are disabled for this static gallery.

## Material adaptations

| Source behavior | Gallery adaptation |
| --- | --- |
| Color atlases incorrectly assigned to bump/normal slots | Only explicit diffuse relationships are used; no atlas is treated as a normal map. |
| Packed non-opacity alpha in opaque color atlases | Ignore alpha for opaque skin; explicit flame opacity remains independent. |
| Venusaur's eyes in the upper UV tile | Dedicated shader selects its eye atlas for that tile, without changing the source mesh. |
| Packed fire masks | Emissive, double-sided orange transparent approximation matching the source adapter, not a full packed-mask shader. |
| Material definitions with no diffuse relationship | Retain their original solid diffuse color; do not invent a texture mapping. |

The lightweight Built-in Render Pipeline shader provides diffuse directional shading, not the source game's complete toon/outline/shadow pipeline. The gallery adds no lighting or global rendering-setting changes.

### Known source appearance limitations

Three specimens are not material-fidelity complete: Grimer (088) and Muk (089) have specialized `Beto` mask-overlay slots that appear dark without their original effect shader. Gastly (092) exports `Body01` pointing to `Eye1`, although a separate `Body1` image exists. The imported relationships are preserved rather than guessing a packed shader or silently reassigning textures. Their geometry, bones, and texture assets are present and inspectable.

## Safe local authoring

The fixed-file Editor bridge additionally allows `prepare_pokemon_gallery`, `import_pokemon_gallery`, `inspect_pokemon_gallery`, and `render_pokemon_gallery`. Requests still contain only a safe ID and an allowlisted command; caller paths, code, network, and Play-mode control are unavailable.

Import modifies only the loaded fixed `Assets/Scenes/Valley.unity` target and dedicated Pokemon assets. It refuses unsaved Valley edits before first construction. An existing complete gallery is inspected and framed without rebuilding or resetting its edits. Original Valley transforms, components, and material references are checked before saving. Imported materials are never overwritten on repeated construction.

Native reports and the optional dex-ordered 13-column contact sheet are written only under `Temp/LocalValidationBridge`. Original assets outside the project remain unchanged.

## Verified import

Native build/save and preview reload validated all 151 specimens: 229 skinned renderers, 8,107 bone references, 535,997 vertices, and 345 persistent specimen materials (plus one platform material). No missing materials, texture references, scripts, invalid skin bindings, or unsupported shaders were found. Seven source slots have no diffuse map by design and retain source solid colors.

Repeated import preserved the saved scene and all 346 material files byte-for-byte. Final Valley inspection is active and clean: 16 roots, 8,630 objects, 913 renderers; original nine gameplay creatures and their nine procedural rigs/18 legs remain unchanged. The native contact sheet is dex-ordered, 13 columns. Visual inspection found and corrected packed-alpha clipping; the three appearance limitations above remain explicit. Play-mode behavior and original specialized shader fidelity are not claimed.

## Rollback

Remove the gallery root from Valley and save through Unity, then remove dedicated `Assets/Data/Pokemon`, `Assets/Models/Pokemon`, `Assets/Materials/Pokemon`, `Assets/Prefabs/Pokemon`, `Assets/Scripts/Pokemon`, `Assets/Editor/PokemonGalleryAuthoring.cs`, and `Assets/Shaders/PokemonGallery.shader`. Remove only the four gallery-specific bridge dispatch cases. Existing world/gameplay/locomotion assets do not depend on the gallery.
