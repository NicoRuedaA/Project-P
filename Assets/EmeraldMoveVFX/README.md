# Emerald Move VFX (importado)

165 efectos de movimientos Pokémon horneados como mallas animadas (`Data/NNN.bytes`, 121 muestras por efecto) y su reproductor (`Runtime/EmeraldVfxPlayer.cs`).

## Procedencia

- Copiado sin modificar (con sus `.meta` y GUID originales) desde el proyecto Unity `pokemon-emerald-unity-vfx` (`Assets/EmeraldMoveVFX/Data`, `Runtime` y `UPSTREAM-LICENSE.txt`).
- Fuente original según `Data/catalogue.json`: repositorio `NicoRuedaA/pokemon-emerald-arena`, commit `97d9f960321e686b597d462bd5f7c632e81e61eb`.
- Licencia y su alcance: `UPSTREAM-LICENSE.txt`. Los nombres y diseños de Pokémon no quedan cubiertos por esa licencia.

No se han copiado los prefabs, escenas, materiales, shader ni el editor del proyecto de origen.

## Prefabs y escena

Los prefabs con estilo BotW los genera el builder de este proyecto: **Tools → BotW VFX → Import Emerald Moves** (o `BotwVfx.EditorTools.EmeraldMoves.BuildBatch` por línea de comandos). Resultado:

- `Assets/BotwVFX/Prefabs/Emerald/NNN-Nombre.prefab` (animación horneada con el shader `BotwVFX/Emerald Toon` + capas Shuriken por tipo, gobernadas por `EmeraldMoveVfx`).
- `Assets/BotwVFX/Scenes/EmeraldMoves_Demo.unity` (galería).
