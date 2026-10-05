# Emerald Moves — 165 runtime effects

Import this package into a **Unity 6000.6.4f1 / URP 17.6.0** project. These are the source project's versions, not a promise of compatibility with older/newer releases. Install Universal RP through Package Manager and configure your own URP pipeline before rendering. The package does not contain registry packages or overwrite ProjectSettings.

## Quick start
1. Import all package entries, keeping their GUIDs and relative paths. Back up conflicting existing BotwVFX/EmeraldMoveVFX folders first.
2. Use any prefab in `Assets/BotwVFX/Prefabs/Emerald`. All 165 contain their own recipes and referenced runtime assets.
3. Instantiate and call `Play()`; each timeline lasts 3.6 seconds. Stop before returning an instance to a pool.

```csharp
using BotwVfx;
using UnityEngine;

public sealed class PlayEmeraldMove : MonoBehaviour
{
    public EmeraldMoveVfx prefab;
    public Transform attacker;
    public Transform target;

    public void PlayMove()
    {
        var delta = target.position - attacker.position;
        if (delta.sqrMagnitude < 0.0001f) return;
        var fx = Instantiate(prefab, (attacker.position + target.position) * 0.5f,
            Quaternion.FromToRotation(Vector3.right, delta.normalized));
        fx.transform.localScale = new Vector3(delta.magnitude / 6f, 1f, 1f);
        if (fx.TryGetComponent<EmeraldActorReplacement>(out var replacement))
            replacement.Bind(attacker);
        fx.Finished += () => { fx.StopAndClear(); Destroy(fx.gameObject); };
        fx.Play();
    }
}
```

Local ground anchors are (-3,0,0) and (+3,0,0). Non-uniform X scaling stretches effect geometry. Keep anchors horizontal for upright effects. Do not call the nested baked player's SetEndpoints: place the whole prefab instead. `Preview(seconds)` supports seeks; `StopAndClear()` restores rest state. Substitute hides renderers below the explicitly bound attacker only while its model is visible; unbind with `Bind(null)` when transferring pooled ownership.

## Integration requirements
- Shaders require Universal RP and its Core dependency; standard Unity particle, physics and rendering modules are needed. No Input System, Cinemachine, VFX Graph, DOTween, gallery UI or editor-generation code is required.
- Use Linear colour space to match the source look. This is guidance, not an automatic settings change.
- `VfxDirector` is created lazily by impact cues and **writes global Time.timeScale**. For integration with your own time manager, clear the instance's `slowMotion` and `shakes` cue lists before Play; otherwise configure the director deliberately. Setting enableSlowMotion=false alone does not stop its baseTimeScale assignment. Camera shake application is off by default.
- No gallery/demo scene, capture tests, source-generation pipeline or original standalone BotW effects are included. Baked data, meshes, materials, shaders, textures, runtime scripts and the Substitute FBX are included.
- All 165 effects passed playback in the source project. Four-phase artistic review and final global colour/size polish remain incomplete; packaging does not imply artistic completion.

## Attribution and rights
Retain `Assets/EmeraldMoveVFX/UPSTREAM-LICENSE.txt` and its README. That license explicitly limits its scope and does not relicense upstream Pokemon material. Do not treat this package as a blanket commercial license.

Retain `Assets/BotwVFX/Models/Substitute/LICENSE.txt` and the bundled original license. Required credit:

This work is based on "Substitute" (https://sketchfab.com/3d-models/substitute-a21a74a4d5cd42479beb0dab4944ee95) by LunaEagle (https://sketchfab.com/LunaEagle) licensed under CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)

The supplied glTF was converted to FBX, grounded/scaled and assigned the project's toon material; vertex colours were normalized for that shader. Runtime does not depend on the original Downloads folder.
