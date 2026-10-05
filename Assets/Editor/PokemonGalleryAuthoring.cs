using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Pokemon3D.Pokemon;

namespace Pokemon3D.Editor
{
    // Fixed local assets and Valley scene only; no caller paths, code, network, or gameplay wiring.
    public static class PokemonGalleryAuthoring
    {
        const string DataFolder = "Assets/Data/Pokemon";
        const string ModelsFolder = "Assets/Models/Pokemon";
        const string MaterialsFolder = "Assets/Materials/Pokemon";
        const string GalleryName = "Pokemon / first 151 gallery";
        const string ShaderName = "Wildbound/Pokemon Gallery";
        static string EvidenceFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LocalValidationBridge"));
        [Serializable] sealed class Catalog { public Species[] species; }
        [Serializable] sealed class Species {
            public int dex; public string slug, name, rig, archetype;
            public float displayHeight; public Mapping[] materials; public Tile[] uvTiles;
        }
        [Serializable] sealed class Mapping { public string name, diffuse, alpha; public Rgb color; public float opacity; }
        [Serializable] sealed class Rgb { public float r, g, b; }
        [Serializable] sealed class Tile { public string mesh, texture; }
        [Serializable] sealed class Report {
            public string action, scene, verification, contactSheet;
            public int specimens, fbx, textures, profiles, renderers, skinnedRenderers, bones, vertices, materials,
                missingMaterials, unsupportedShaders, missingTextures, invalidSkins, missingScripts,
                gameplayCreatures, proceduralAnimators, sourceUntexturedSlots, uvTileSlots, flameSlots;
            public bool saved, originalWorldPreserved;
            public string[] materialNotes;
        }
        sealed class Snapshot {
            public Transform transform; public Transform parent;
            public Vector3 position, scale; public Quaternion rotation;
            public Component[] components; public Material[] materials;
        }
        static void RequireIdle() {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Pokemon gallery authoring requires the idle editor in Edit mode.");
        }
        static Catalog ReadCatalog() {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(DataFolder + "/Catalog151.json");
            if (!asset) throw new InvalidOperationException("The fixed first-generation catalog has not been imported.");
            var catalog = JsonUtility.FromJson<Catalog>(asset.text);
            if (catalog == null || catalog.species == null || catalog.species.Length != 151)
                throw new InvalidOperationException("Expected exactly 151 catalog species.");
            for (int i = 0; i < 151; i++) {
                var s = catalog.species[i];
                if (s.dex != i + 1 || s.rig != "pm" + s.dex.ToString("0000") + "_00_Rig" || !Finite(s.displayHeight) || s.displayHeight <= 0 || s.displayHeight > 20)
                    throw new InvalidOperationException("Invalid fixed catalog entry at dex " + (i + 1));
                foreach (var m in s.materials) { SafeFilename(m.diffuse); SafeFilename(m.alpha); }
                foreach (var t in s.uvTiles) SafeFilename(t.texture);
            }
            return catalog;
        }
        static void SafeFilename(string value) {
            if (string.IsNullOrEmpty(value)) return;
            if (Path.GetFileName(value) != value || value.Contains("..") || !value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid catalog texture filename.");
        }
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static string ModelPath(Species s) => ModelsFolder + "/" + s.rig + "/" + s.rig + ".fbx";
        static Texture2D Texture(Species s, string name) {
            if (string.IsNullOrEmpty(name)) return null;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ModelsFolder + "/" + s.rig + "/" + name);
            if (!texture) throw new InvalidOperationException("Missing color texture for dex " + s.dex + ": " + name);
            return texture;
        }
        static void EnsureFolder(string path) {
            var parts = path.Split('/'); string parent = parts[0];
            for (int i = 1; i < parts.Length; i++) { var next = parent + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, parts[i]); parent = next; }
        }
        public static string Prepare() {
            RequireIdle(); var catalog = ReadCatalog();
            foreach (var s in catalog.species) {
                var importer = AssetImporter.GetAtPath(ModelPath(s)) as ModelImporter;
                if (!importer) throw new InvalidOperationException("Missing native FBX importer for dex " + s.dex);
                // Preserve the source skeleton without Humanoid retargeting or hidden animation playback.
                bool change = importer.animationType != ModelImporterAnimationType.Generic || importer.optimizeGameObjects || importer.importAnimation || !importer.isReadable;
                importer.animationType = ModelImporterAnimationType.Generic; importer.optimizeGameObjects = false;
                importer.importAnimation = false; importer.isReadable = true;
                if (change) importer.SaveAndReimport();
            }
            AssetDatabase.SaveAssets();
            return JsonUtility.ToJson(new Report { action = "native_models_prepared", fbx = 151, textures = 413, profiles = 151,
                verification = "Native Generic skeletons; animation clips not imported or played; source FBX and PNG bytes preserved." }, true);
        }
        static Scene TargetScene() {
            var scene = SceneManager.GetSceneByPath(EditableSceneAuthoring.ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("The saved Valley scene must already be open; no other scene was opened or replaced.");
            if (scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<AuthoredWorld>(true).Length) != 1)
                throw new InvalidOperationException("Expected one authored world in Valley.");
            return scene;
        }
        static PokemonGallery Gallery(Scene scene) {
            var galleries = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PokemonGallery>(true)).ToArray();
            if (galleries.Length > 1) throw new InvalidOperationException("Multiple Pokemon galleries found; no gallery was rebuilt.");
            return galleries.FirstOrDefault();
        }
        static List<Snapshot> Capture(Scene scene) {
            var result = new List<Snapshot>();
            foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true))
                result.Add(new Snapshot { transform = t, parent = t.parent, position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                    components = t.GetComponents<Component>(), materials = t.GetComponent<Renderer>() ? t.GetComponent<Renderer>().sharedMaterials : null });
            return result;
        }
        static void AssertPreserved(List<Snapshot> before) {
            foreach (var s in before) {
                if (!s.transform || s.transform.parent != s.parent || s.transform.localPosition != s.position || s.transform.localRotation != s.rotation || s.transform.localScale != s.scale || !s.transform.GetComponents<Component>().SequenceEqual(s.components))
                    throw new InvalidOperationException("An existing Valley object was changed during gallery import.");
                if (s.materials != null && !s.transform.GetComponent<Renderer>().sharedMaterials.SequenceEqual(s.materials))
                    throw new InvalidOperationException("An existing Valley material reference changed during gallery import.");
            }
        }
        static Bounds BoundsOf(GameObject model) {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Imported model has no renderers: " + model.name);
            Bounds bounds = renderers[0].bounds; foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            if (!Finite(bounds.size.y) || bounds.size.y <= .0001f) throw new InvalidOperationException("Imported model has invalid bounds: " + model.name);
            return bounds;
        }
        static Material MaterialFor(Species s, Material original, Renderer renderer) {
            if (!original) throw new InvalidOperationException("Missing source material for dex " + s.dex);
            var mapping = s.materials.FirstOrDefault(m => m.name == original.name);
            if (mapping == null) throw new InvalidOperationException("Unknown native material slot for dex " + s.dex + ": " + original.name);
            var tile = s.uvTiles.FirstOrDefault(t => t.mesh == renderer.name || (renderer is SkinnedMeshRenderer && t.mesh == ((SkinnedMeshRenderer)renderer).sharedMesh.name));
            bool flame = mapping.name.StartsWith("Fire", StringComparison.Ordinal);
            string safeName = new string(mapping.name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_').ToArray());
            string path = MaterialsFolder + "/" + s.dex.ToString("0000") + "_" + safeName + (tile == null ? "" : "_UVtile") + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) {
                if (!existing.shader || (existing.shader.name != ShaderName && existing.shader.name != ToonMaterials.ShaderName)) throw new InvalidOperationException("Existing material conflict at " + path);
                return existing; // Preserve authored material edits on repeated import.
            }
            var shader = Shader.Find(ShaderName);
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Gallery shader is unavailable or has compilation errors.");
            var material = new Material(shader) { name = s.dex.ToString("000") + " " + mapping.name };
            material.mainTexture = Texture(s, mapping.diffuse);
            material.color = string.IsNullOrEmpty(mapping.diffuse) && mapping.color != null ? new Color(mapping.color.r, mapping.color.g, mapping.color.b, 1) : Color.white;
            if (tile != null) { material.SetTexture("_TileTex", Texture(s, tile.texture)); material.SetFloat("_UseTile", 1); }
            if (flame) {
                // These mapped combo textures are the flame diffuse maps, not normal maps.
                material.color = new Color(1f, 1f, 1f, .9f); material.SetFloat("_Flame", 1);
                material.SetFloat("_HasFlameTexture", material.mainTexture ? 1 : 0);
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0); material.renderQueue = 3000; material.SetOverrideTag("RenderType", "Transparent");
            }
            if (tile == null && !flame) {
                var color = material.color; var texture = material.mainTexture;
                material.shader = Shader.Find(ToonMaterials.ShaderName);
                ToonMaterials.Configure(material, color, texture, Vector2.one, Vector2.zero, true);
            }
            AssetDatabase.CreateAsset(material, path); return material;
        }
        public static string Build() {
            RequireIdle(); var scene = TargetScene(); var existing = Gallery(scene);
            if (existing) { var reused = Validate(existing); reused.action = "existing_gallery_preserved"; reused.originalWorldPreserved = true; Frame(existing.gameObject); return Publish(reused); }
            if (scene.isDirty) throw new InvalidOperationException("Valley has unsaved edits; save those edits before importing the gallery. Nothing was changed.");
            var catalog = ReadCatalog(); var before = Capture(scene);
            EnsureFolder(MaterialsFolder);
            foreach (var s in catalog.species) if (!AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(s))) throw new InvalidOperationException("Native model import not ready for dex " + s.dex);
            var parent = new GameObject(GalleryName); SceneManager.MoveGameObjectToScene(parent, scene);
            var marker = parent.AddComponent<PokemonGallery>();
            try {
                // Standalone exhibit beside the eastern edge, outside all original scenery.
                parent.transform.position = new Vector3(125, 0, 0);
                foreach (var s in catalog.species) {
                    var cell = new GameObject(s.dex.ToString("000") + " " + s.name); cell.transform.SetParent(parent.transform, false);
                    cell.transform.localPosition = new Vector3((s.dex - 1) % 13 * 6f, 0, (s.dex - 1) / 13 * 6f - 33f);
                    var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath(s));
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene); model.transform.SetParent(cell.transform, false);
                    model.name = "Model / " + s.name;
                    foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                    foreach (var animation in model.GetComponentsInChildren<Animation>(true)) animation.enabled = false;
                    foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) {
                        var materials = renderer.sharedMaterials;
                        for (int i = 0; i < materials.Length; i++) materials[i] = MaterialFor(s, materials[i], renderer);
                        renderer.sharedMaterials = materials;
                        if (renderer is SkinnedMeshRenderer) ((SkinnedMeshRenderer)renderer).updateWhenOffscreen = true;
                    }
                    var bounds = BoundsOf(model); float scale = s.displayHeight / bounds.size.y;
                    model.transform.localScale *= scale; bounds = BoundsOf(model);
                    model.transform.position += new Vector3(cell.transform.position.x - bounds.center.x, .26f - bounds.min.y, cell.transform.position.z - bounds.center.z);
                    var entry = cell.AddComponent<PokemonGalleryEntry>(); entry.dex = s.dex; entry.speciesName = s.name; entry.archetype = s.archetype;
                    entry.sourceProfile = AssetDatabase.LoadAssetAtPath<TextAsset>(DataFolder + "/Profiles/" + s.dex.ToString("0000") + ".json");
                    entry.sourceModel = modelAsset; entry.visual = model.transform; entry.displayHeight = s.displayHeight;
                }
                CreateFloor(parent.transform);
                var report = Validate(marker); AssertPreserved(before);
                Undo.RegisterCreatedObjectUndo(parent, "Import first-generation Pokemon gallery");
                AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Gallery scene save failed.");
                report.saved = true; report.originalWorldPreserved = true; report.action = "gallery_built_and_saved";
                report.verification = VerifySaved(); Frame(parent); return Publish(report);
            }
            catch { if (parent) UnityEngine.Object.DestroyImmediate(parent); throw; }
        }
        static void CreateFloor(Transform parent) {
            const string path = MaterialsFolder + "/GalleryFloor.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = ToonMaterials.Create(new Color(.38f, .45f, .48f)); AssetDatabase.CreateAsset(material, path); }
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Gallery platform"; floor.transform.SetParent(parent, false);
            floor.transform.localPosition = new Vector3(36, 0, 0); floor.transform.localScale = new Vector3(80, .5f, 74); floor.layer = 8;
            floor.GetComponent<Renderer>().sharedMaterial = material;
        }
        static Report Validate(PokemonGallery gallery) {
            var report = new Report { action = "gallery_inspected", scene = gallery.gameObject.scene.path, fbx = 151, textures = 413, profiles = 151 };
            var catalog = ReadCatalog(); var entries = gallery.GetComponentsInChildren<PokemonGalleryEntry>(true).OrderBy(e => e.dex).ToArray();
            report.specimens = entries.Length; var materials = new HashSet<Material>(); var notes = new List<string>();
            if (entries.Length != 151) throw new InvalidOperationException("Expected exactly 151 gallery specimens.");
            for (int i = 0; i < entries.Length; i++) {
                var entry = entries[i]; var s = catalog.species[i];
                if (entry.dex != i + 1 || !entry.visual || !entry.sourceModel || !entry.sourceProfile) throw new InvalidOperationException("Missing gallery metadata/model/profile for dex " + (i + 1));
                report.gameplayCreatures += entry.GetComponentsInChildren<Creature>(true).Length;
                report.proceduralAnimators += entry.GetComponentsInChildren<Pokemon3D.Locomotion.ProceduralBodyAnimator>(true).Length;
                foreach (var t in entry.GetComponentsInChildren<Transform>(true)) report.missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                var bounds = BoundsOf(entry.visual.gameObject);
                if (Mathf.Abs(bounds.size.y - entry.displayHeight) > .01f) throw new InvalidOperationException("Display height normalization failed for dex " + entry.dex);
                foreach (var renderer in entry.visual.GetComponentsInChildren<Renderer>(true)) {
                    report.renderers++;
                    Mesh mesh;
                    if (renderer is SkinnedMeshRenderer) {
                        var skin = (SkinnedMeshRenderer)renderer; mesh = skin.sharedMesh; report.skinnedRenderers++; report.bones += skin.bones.Length;
                        if (!mesh || skin.bones.Length == 0 || skin.bones.Any(b => !b) || mesh.bindposes.Length != skin.bones.Length) report.invalidSkins++;
                    } else { var filter = renderer.GetComponent<MeshFilter>(); mesh = filter ? filter.sharedMesh : null; }
                    if (!mesh || mesh.vertexCount == 0) throw new InvalidOperationException("Missing mesh vertices for dex " + entry.dex);
                    report.vertices += mesh.vertexCount;
                    if (renderer.sharedMaterials.Length != mesh.subMeshCount) throw new InvalidOperationException("Material/submesh slot mismatch for dex " + entry.dex);
                    foreach (var m in renderer.sharedMaterials) {
                        if (!m || !AssetDatabase.Contains(m)) { report.missingMaterials++; continue; }
                        materials.Add(m);
                        if (!m.shader || !m.shader.isSupported || ShaderUtil.ShaderHasError(m.shader)) report.unsupportedShaders++;
                        if (m.shader.name != ShaderName && m.shader.name != ToonMaterials.ShaderName) throw new InvalidOperationException("Unexpected specimen shader for dex " + entry.dex);
                        if (m.HasProperty("_UseTile") && m.GetFloat("_UseTile") > .5f) { report.uvTileSlots++; if (!m.GetTexture("_TileTex")) report.missingTextures++; }
                        if (m.HasProperty("_Flame") && m.GetFloat("_Flame") > .5f) report.flameSlots++;
                        else if (!m.mainTexture) { report.sourceUntexturedSlots++; notes.Add(entry.dex.ToString("000") + " " + m.name + " has no source diffuse map; source solid color retained."); }
                    }
                }
            }
            report.materials = materials.Count; report.materialNotes = notes.Distinct().ToArray();
            if (report.missingMaterials + report.unsupportedShaders + report.missingTextures + report.invalidSkins + report.missingScripts + report.gameplayCreatures + report.proceduralAnimators != 0)
                throw new InvalidOperationException("Gallery validation failed: " + JsonUtility.ToJson(report));
            return report;
        }
        public static string Inspect() { RequireIdle(); var gallery = Gallery(TargetScene()); if (!gallery) throw new InvalidOperationException("No first-generation gallery is present."); return Publish(Validate(gallery)); }
        static string VerifySaved() {
            var preview = EditorSceneManager.OpenPreviewScene(EditableSceneAuthoring.ScenePath);
            try { var report = Validate(Gallery(preview)); return "Native saved-scene preview reload passed: " + report.specimens + " specimens; valid skin/bone/material/profile references; no gameplay or procedural components."; }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static string Publish(Report report) {
            Directory.CreateDirectory(EvidenceFolder); string json = JsonUtility.ToJson(report, true);
            File.WriteAllText(Path.Combine(EvidenceFolder, "pokemon151-report.json"), json); return json;
        }
        static void Frame(GameObject gallery) {
            var view = SceneView.lastActiveSceneView; if (!view) view = EditorWindow.GetWindow<SceneView>();
            view.rotation = Quaternion.Euler(48, 180, 0); view.Frame(new Bounds(gallery.transform.position + new Vector3(36, 2, 0), new Vector3(84, 12, 80)), false);
            Selection.activeGameObject = gallery; SceneView.RepaintAll();
        }
        public static string ContactSheet() {
            RequireIdle(); var gallery = Gallery(TargetScene()); if (!gallery) throw new InvalidOperationException("Gallery not present.");
            Validate(gallery); const int size = 256, columns = 13, rows = 12;
            var sheet = new Texture2D(size * columns, size * rows, TextureFormat.RGB24, false);
            var entries = gallery.GetComponentsInChildren<PokemonGalleryEntry>(true).OrderBy(e => e.dex).ToArray();
            try {
                for (int i = 0; i < entries.Length; i++) {
                    var utility = new PreviewRenderUtility(); Texture2D picture = null;
                    try {
                        var copy = UnityEngine.Object.Instantiate(entries[i].visual.gameObject); copy.transform.SetParent(null);
                        copy.transform.position = Vector3.zero; utility.AddSingleGO(copy);
                        var bounds = BoundsOf(copy); float radius = bounds.size.magnitude * .5f;
                        utility.camera.fieldOfView = 35; utility.camera.nearClipPlane = .01f; utility.camera.farClipPlane = 100;
                        utility.camera.transform.position = bounds.center + new Vector3(.6f, .28f, 1).normalized * (radius / Mathf.Sin(17.5f * Mathf.Deg2Rad) * 1.15f);
                        utility.camera.transform.LookAt(bounds.center); utility.camera.backgroundColor = new Color(.19f, .23f, .27f);
                        utility.camera.clearFlags = CameraClearFlags.SolidColor; utility.lights[0].intensity = 1.2f; utility.lights[0].transform.rotation = Quaternion.Euler(30, 35, 0);
                        utility.BeginStaticPreview(new Rect(0, 0, size, size)); utility.Render(); picture = utility.EndStaticPreview();
                        sheet.SetPixels(i % columns * size, (rows - 1 - i / columns) * size, size, size, picture.GetPixels());
                    } finally { if (picture) UnityEngine.Object.DestroyImmediate(picture); utility.Cleanup(); }
                }
                sheet.Apply(); Directory.CreateDirectory(EvidenceFolder); var path = Path.Combine(EvidenceFolder, "pokemon151-contact-sheet.png"); File.WriteAllBytes(path, sheet.EncodeToPNG());
                return "Native 151-model contact sheet rendered in dex order (13 columns): " + path;
            } finally { UnityEngine.Object.DestroyImmediate(sheet); }
        }
    }
}
