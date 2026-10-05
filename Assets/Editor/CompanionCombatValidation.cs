using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Cinemachine;
using StarterAssets;
using BotwVfx;
using UnityEngine.UIElements;
using Pokemon3D.Combat;

namespace Pokemon3D.Editor
{
    public static class CompanionCombatValidation
    {
        static int checks;
        static void Require(bool value, string message) { checks++; if (!value) throw new InvalidOperationException("Companion combat: " + message); }
        static void Invoke(object target, string method) { target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null); }
        static GameObject Fixture(string name, Scene scene)
        {
            var result = new GameObject("Temporary combat validation / " + name) { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(result, scene);
            return result;
        }
        static void ValidateAssets(CompanionCombatCatalog catalog)
        {
            Require(catalog, "Resources/CompanionCombatCatalog is missing.");
            var ids = new HashSet<string>();
            for (int species = 0; species < 3; species++)
            {
                var loadout = catalog.Resolve(null, species);
                Require(loadout && !string.IsNullOrEmpty(loadout.id) && ids.Add(loadout.id), "Each gameplay species needs a unique stable default loadout.");
                Require(loadout.attacks != null && loadout.attacks.Length <= 4, "A default loadout exceeds four slots.");
                for (int slot = 0; slot < 4; slot++)
                {
                    var attack = loadout.AttackAt(slot);
                    Require(attack && attack.IsValid, "Invalid default attack in species " + species + ", slot " + slot + ".");
                    Require(attack.vfx && AssetDatabase.GetAssetPath(attack.vfx).StartsWith("Assets/Movements/", StringComparison.Ordinal), "Default VFX must reference the existing Movements asset.");
                }
            }
        }

        static void CaptureCamera(CinemachineVirtualCamera camera, Scene scene, string path)
        {
            var obj = Fixture("third-person capture", scene);
            var render = obj.AddComponent<Camera>(); render.CopyFrom(Camera.main); render.enabled = false; render.scene = scene;
            render.transform.SetPositionAndRotation(camera.State.RawPosition, camera.State.RawOrientation); render.fieldOfView = camera.m_Lens.FieldOfView;
            var lightObject = Fixture("capture light", scene); var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 2; lightObject.transform.rotation = Quaternion.Euler(45, -30, 0);
            var target = new RenderTexture(640, 480, 24); var pixels = new Texture2D(640, 480, TextureFormat.RGB24, false); var previous = RenderTexture.active;
            try { render.targetTexture = target; render.Render(); RenderTexture.active = target; pixels.ReadPixels(new Rect(0, 0, 640, 480), 0, 0); pixels.Apply(); System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG()); }
            finally { RenderTexture.active = previous; render.targetTexture = null; UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels); UnityEngine.Object.DestroyImmediate(obj); UnityEngine.Object.DestroyImmediate(lightObject); }
        }

        public static string RunThirdPersonCamera()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Third-person camera validation requires idle Edit mode.");
            checks = 0; var originalGame = Game.Instance;
            var snapshots = new Dictionary<Scene, Dictionary<EntityId, string>>(); var dirty = new Dictionary<Scene, bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++) { var scene = SceneManager.GetSceneAt(i); if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue; snapshots.Add(scene, FullBodyAuthoring.Snapshot(scene)); dirty.Add(scene, scene.isDirty); }
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var sourceCamera = UnityEngine.Object.FindObjectsByType<CinemachineVirtualCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(c => c.name == "PlayerFollowCamera" && !EditorSceneManager.IsPreviewScene(c.gameObject.scene));
                var trainerObject = UnityEngine.Object.Instantiate(sourceCamera.Follow.root.gameObject); trainerObject.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(trainerObject, preview);
                var trainer = trainerObject.GetComponent<PlayerMotor>(); var trainerController = trainerObject.GetComponent<ThirdPersonController>(); var trainerPivot = trainerController.CinemachineCameraTarget.transform;
                var sourceActor = UnityEngine.Object.FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(c => c.name == "Wild12" && !EditorSceneManager.IsPreviewScene(c.gameObject.scene));
                var actorObject = UnityEngine.Object.Instantiate(sourceActor.gameObject); actorObject.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(actorObject, preview); actorObject.transform.position = new Vector3(300, 100, 300);
                var actor = actorObject.GetComponent<Creature>(); actor.ally = true; actor.dead = false; actor.health = 100; actor.capturing = false; actor.BindRuntime();
                var actorTag = actor.tag; var modelTag = actor.model.tag; var geometryCount = actorObject.GetComponentsInChildren<Transform>(true).Length;
                var extra = Fixture("owned collider", preview); extra.transform.SetParent(actor.transform, false); extra.tag = "Respawn"; extra.AddComponent<SphereCollider>().enabled = false;
                var other = Fixture("nested unrelated actor", preview).AddComponent<Creature>(); other.transform.SetParent(actor.transform, false); other.transform.localPosition = Vector3.right * 50; other.tag = "Respawn";
                var owner = Fixture("third-person owner", preview).AddComponent<Game>(); owner.player = trainer; owner.companion = actor; owner.enabled = true; Game.Instance = owner;
                var cameraObject = UnityEngine.Object.Instantiate(sourceCamera.gameObject); cameraObject.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(cameraObject, preview);
                var camera = cameraObject.GetComponent<CinemachineVirtualCamera>(); camera.enabled = false; camera.Follow = trainerPivot; var originalLookAt = camera.LookAt;
                var body = camera.GetCinemachineComponent<Cinemachine3rdPersonFollow>(); var bodySettings = EditorJsonUtility.ToJson(body); var lens = JsonUtility.ToJson(camera.m_Lens);
                var candidates = new CinemachineVirtualCameraBase[] { camera }; var control = owner.Control;
                control.Possess(actor, candidates); Physics.SyncTransforms(); camera.UpdateCameraState(Vector3.up, -1);
                float distance = Vector3.Distance(camera.State.RawPosition, camera.Follow.position);
                Require(distance > body.CameraDistance * .95f, "Actual camera rig must retain third-person distance, not collide with its own actor.");
                Require(EditorJsonUtility.ToJson(body) == bodySettings && JsonUtility.ToJson(camera.m_Lens) == lens, "Camera distance/shoulder/damping/lens/collision tuning must remain exactly configured.");
                Require(actor.tag == body.IgnoreTag && extra.tag == body.IgnoreTag && other.tag == "Respawn" && actor.model.tag == modelTag, "Only owned collider GOs may be assigned the camera exclusion tag.");
                CaptureCamera(camera, preview, "Library/CompanionCameraFix/green-third-person.png");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.hideFlags = HideFlags.HideAndDontSave; SceneManager.MoveGameObjectToScene(wall, preview);
                wall.transform.position = camera.Follow.position + new Vector3(0, 0, -2); wall.transform.localScale = new Vector3(5, 5, .2f); Physics.SyncTransforms(); camera.PreviousStateIsValid = false; camera.UpdateCameraState(Vector3.up, -1);
                float wallDistance = Vector3.Distance(camera.State.RawPosition, camera.Follow.position);
                Require(wallDistance < 2.1f && wallDistance > .5f && wall.tag == "Untagged", "An unrelated wall must still push the camera forward without changing its tag.");
                UnityEngine.Object.DestroyImmediate(wall); Physics.SyncTransforms();
                control.Release();
                Require(actor.tag == actorTag && extra.tag == "Respawn" && other.tag == "Respawn", "Withdrawal must restore every original owned collider tag.");
                Require(camera.Follow == trainerPivot && camera.LookAt == originalLookAt && trainerController.enabled, "Withdrawal must restore exact trainer camera references and movement.");
                Require(EditorJsonUtility.ToJson(body) == bodySettings && JsonUtility.ToJson(camera.m_Lens) == lens, "Withdrawal must restore all camera component settings.");
                for (int i = 0; i < 3; i++) { control.Possess(actor, candidates); control.Release(); }
                Require(actorObject.GetComponentsInChildren<Transform>(true).Length == geometryCount + 2 && actor.tag == actorTag, "Repeated possession must not leak camera pivots or tags.");
                control.Possess(actor, candidates); actor.tag = "Respawn"; control.Release();
                Require(actor.tag == "Respawn", "Release must preserve a tag changed externally after possession."); actor.tag = actorTag;
                foreach (var reason in new[] { "uncheck", "dead", "disabled", "destroy-control", "dismiss" })
                {
                    owner.companion = actor; control.Possess(actor, candidates);
                    if (reason == "uncheck") { actor.ally = false; Invoke(control, "LateUpdate"); actor.ally = true; }
                    else if (reason == "dead") { actor.dead = true; Invoke(control, "LateUpdate"); actor.dead = false; }
                    else if (reason == "disabled") { actor.enabled = false; Invoke(actor, "OnDisable"); actor.enabled = true; }
                    else if (reason == "destroy-control") Invoke(control, "OnDestroy");
                    else owner.Dismiss();
                    Require(actor && actor.tag == actorTag && extra.tag == "Respawn" && camera.Follow == trainerPivot && trainerController.enabled && !control.IsPossessing, "Cleanup failed for " + reason);
                }
                var modernObject = Fixture("modern third-person", preview); var modern = modernObject.AddComponent<CinemachineCamera>(); modern.enabled = false; modern.Follow = trainerPivot;
                var modernBody = modernObject.AddComponent<CinemachineThirdPersonFollow>(); var settings = modernBody.AvoidObstacles; settings.Enabled = true; settings.CollisionFilter = 1; settings.IgnoreTag = ""; modernBody.AvoidObstacles = settings;
                var originalTarget = modern.Target; var modernSettings = EditorJsonUtility.ToJson(modernBody);
                owner.companion = actor; control.Possess(actor, new CinemachineVirtualCameraBase[] { modern });
                Require(actor.tag == "Player" && modernBody.AvoidObstacles.IgnoreTag == "Player", "A modern body with no exclusion tag must use an existing valid tag, not ignore all Untagged walls.");
                control.Release(); Require(actor.tag == actorTag && modernBody.AvoidObstacles.IgnoreTag == "" && EditorJsonUtility.ToJson(modernBody) == modernSettings && modern.Target.TrackingTarget == originalTarget.TrackingTarget, "Modern camera settings/target must restore exactly.");
                owner.companion = actor; control.Possess(actor, new CinemachineVirtualCameraBase[] { modern }); settings = modernBody.AvoidObstacles; settings.IgnoreTag = "Respawn"; modernBody.AvoidObstacles = settings; control.Release();
                Require(modernBody.AvoidObstacles.IgnoreTag == "Respawn" && actor.tag == actorTag, "External camera IgnoreTag edits must not be overwritten on release.");
                body.IgnoreTag = "Undefined companion camera validation tag"; owner.companion = actor; bool rejected = false;
                try { control.Possess(actor, candidates); } catch (UnityException) { rejected = true; }
                Require(rejected && actor.tag == actorTag && extra.tag == "Respawn" && camera.Follow == trainerPivot && trainerController.enabled && !control.IsPossessing, "Failed possession must restore targets/controller/tags and remove its pivot."); body.IgnoreTag = "Player";
                owner.companion = actor; control.Possess(actor, candidates); UnityEngine.Object.DestroyImmediate(actorObject);
                Require(!control.IsPossessing && trainerController.enabled && camera.Follow == trainerPivot, "Destroying actor must restore third-person trainer state.");
                owner.companion = null; control.Release();
                System.IO.File.WriteAllText("Library/CompanionCameraFix/green-measurements.json", "{\"thirdPersonDistance\":" + distance.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"wallDistance\":" + wallDistance.ToString(System.Globalization.CultureInfo.InvariantCulture) + ",\"cameraDistance\":" + body.CameraDistance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}");
            }
            finally
            {
                Game.Instance = originalGame; EditorSceneManager.ClosePreviewScene(preview);
                foreach (var pair in snapshots) { FullBodyAuthoring.AssertSnapshot(pair.Value, FullBodyAuthoring.Snapshot(pair.Key)); Require(pair.Key.isDirty == dirty[pair.Key], "Camera validation changed a live scene dirty flag."); }
            }
            return "Third-person camera: " + checks + " native checks passed using actual trainer, Pokemon and camera rig copies; live scenes and Play unchanged.";
        }

        public static string RunAuthoredAllies()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Authored Ally validation requires idle Edit mode.");
            checks = 0;
            var originalGame = Game.Instance;
            var snapshots = new Dictionary<Scene, Dictionary<EntityId, string>>();
            var dirty = new Dictionary<Scene, bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                snapshots.Add(scene, FullBodyAuthoring.Snapshot(scene)); dirty.Add(scene, scene.isDirty);
            }
            var preview = EditorSceneManager.NewPreviewScene();
            Creature fallback = null;
            UnityEditor.Editor inspector = null;
            try
            {
                var trainerObject = Fixture("ally trainer", preview);
                trainerObject.SetActive(false);
                trainerObject.AddComponent<CharacterController>();
                trainerObject.AddComponent<StarterAssetsInputs>();
                var trainerController = trainerObject.AddComponent<ThirdPersonController>();
                var trainerPivot = Fixture("ally trainer pivot", preview).transform;
                trainerPivot.SetParent(trainerObject.transform, false);
                trainerController.CinemachineCameraTarget = trainerPivot.gameObject;
                var trainer = trainerObject.AddComponent<PlayerMotor>();
                trainerObject.SetActive(true);
                var game = Fixture("ally owner", preview).AddComponent<Game>();
                game.player = trainer; game.enabled = true; Game.Instance = game;
                game.cam = Fixture("ally orbit camera", preview).AddComponent<OrbitCamera>();
                game.cam.enabled = false;
                game.combatCatalog = Resources.Load<CompanionCombatCatalog>(CompanionCombatCatalog.ResourceName);
                var source = UnityEngine.Object.FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => !EditorSceneManager.IsPreviewScene(c.gameObject.scene) && c.model && c.model.name == "018-pidgeot");
                Require(source, "A real authored Pidgeot fixture is required.");
                var clone = UnityEngine.Object.Instantiate(source.gameObject);
                clone.name = "Temporary combat validation / authored Pidgeot";
                clone.hideFlags = HideFlags.HideAndDontSave;
                SceneManager.MoveGameObjectToScene(clone, preview);
                var actor = clone.GetComponent<Creature>();
                actor.ally = false; actor.dead = false; actor.capturing = false; actor.health = 100;
                actor.GetComponent<CharacterController>().enabled = true;
                actor.BindRuntime(); game.creatures.Add(actor);
                var model = actor.model;
                var animator = actor.GetComponent<Locomotion.ProceduralBodyAnimator>();
                var rig = JsonUtility.ToJson(animator);
                var geometry = clone.GetComponentsInChildren<Transform>(true);
                var rests = new Dictionary<EntityId, string>();
                foreach (var t in geometry) rests[t.GetEntityId()] = EditorJsonUtility.ToJson(t);
                var loadout = actor.loadout; var record = actor.record; var cooldowns = game.Cooldowns(actor);
                var serialized = new SerializedObject(actor);
                inspector = UnityEditor.Editor.CreateEditor(actor);
                var ui = inspector.CreateInspectorGUI();
                Require(ui.Query<UnityEditor.UIElements.PropertyField>().ToList().Exists(f => f.bindingPath == "ally"), "The real Creature Inspector must expose Ally.");
                serialized.FindProperty("ally").boolValue = true; serialized.ApplyModifiedPropertiesWithoutUndo();
                game.save.party.Clear(); game.ToggleCompanion();
                Require(game.IsPossessing && game.companion == actor && game.Control.Actor == actor, "Inspector Ally then E must possess the actual authored actor without a party record.");
                Require(!game.OwnsCompanion(actor) && actor.model == model && actor.loadout == loadout && actor.record == record, "Authored actor ownership/model/loadout/record identity must be retained.");
                Require(!trainerController.enabled, "E must disable trainer movement.");
                Require(JsonUtility.ToJson(animator) == rig, "Possession must not rebuild or retune the procedural rig.");
                var virtualCamera = Fixture("ally camera", preview).AddComponent<CinemachineCamera>();
                virtualCamera.enabled = false; virtualCamera.Follow = trainerPivot; virtualCamera.LookAt = trainerPivot;
                var cameras = new CinemachineVirtualCameraBase[] { virtualCamera };
                game.Control.Possess(actor, cameras);
                Require(virtualCamera.Follow && virtualCamera.Follow.IsChildOf(actor.transform), "Authored Ally must receive the camera target.");
                game.ToggleCompanion();
                Require(actor && actor.ally && !game.companion && !game.IsPossessing && trainerController.enabled, "E again must retain the actor as an Ally and restore the trainer.");
                Require(virtualCamera.Follow == trainerPivot && virtualCamera.LookAt == trainerPivot, "E withdrawal must restore exact camera references.");
                Require(actor.forcedTarget == null && clone.GetComponentsInChildren<Transform>(true).Length == geometry.Length, "Withdrawal must clear commanded target and remove its camera pivot.");
                foreach (var t in geometry) Require(EditorJsonUtility.ToJson(t) == rests[t.GetEntityId()], "Possession changed authored transform " + t.name);
                for (int i = 0; i < 3; i++) { game.ToggleCompanion(); game.ToggleCompanion(); }
                Require(actor && clone.GetComponentsInChildren<Transform>(true).Length == geometry.Length && ReferenceEquals(cooldowns, game.Cooldowns(actor)), "Repeated E must not leak pivots or reset cooldowns.");
                game.companion = actor; game.Dismiss();
                typeof(Game).GetMethod("ApplyLoadedSave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { null, false });
                Require(actor && game.creatures.Contains(actor) && game.save.party.Count == 1, "Load's shared dismissal/default-state path must preserve the authored Ally and registry.");
                game.ToggleCompanion(); Require(game.companion == actor, "A default party must not replace the authored Ally."); game.Dismiss();
                var farther = Fixture("farther Ally", preview).AddComponent<Creature>();
                farther.model = Fixture("farther visual", preview).transform; farther.model.SetParent(farther.transform, false);
                farther.Init(0, true); farther.transform.position = trainer.transform.position + Vector3.right * 100;
                game.creatures.Insert(0, farther); actor.transform.position = trainer.transform.position + Vector3.right;
                game.ToggleCompanion(); Require(game.companion == actor, "Nearest eligible Ally must win over registry order and save.party."); game.Dismiss();
                farther.transform.position = actor.transform.position;
                game.ToggleCompanion(); Require(game.companion == farther, "Equal-distance ties must follow stable registry order."); game.Dismiss();
                farther.gameObject.SetActive(false);
                game.ToggleCompanion(); Require(game.companion == actor, "Inactive Ally must not be selected.");
                serialized.Update(); serialized.FindProperty("ally").boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
                Invoke(game.Control, "LateUpdate");
                Require(!game.companion && !game.IsPossessing && trainerController.enabled && actor, "Unchecking Ally must release control without deleting the actor.");
                actor.ally = true; game.ToggleCompanion(); actor.health = 0; Invoke(game.Control, "LateUpdate");
                Require(!game.companion && trainerController.enabled && actor, "Zero health must release without removing an authored actor.");
                actor.health = 100; game.ToggleCompanion(); actor.capturing = true; Invoke(game.Control, "LateUpdate");
                Require(!game.companion && trainerController.enabled, "Capturing invalidation must release control.");
                actor.capturing = false; game.ToggleCompanion(); actor.enabled = false; Invoke(actor, "OnDisable");
                Require(!game.companion && trainerController.enabled && actor, "Disabling an Ally must release without deleting it.");
                actor.enabled = true; game.ToggleCompanion(); actor.GetComponent<CharacterController>().enabled = false; Invoke(game.Control, "LateUpdate");
                Require(!game.companion && trainerController.enabled, "A disabled CharacterController must release control.");
                actor.GetComponent<CharacterController>().enabled = true; game.ToggleCompanion();
                actor.health = 1; actor.Hurt(2, null);
                Require(actor && actor.dead && !game.companion && trainerController.enabled, "Authored Ally KO must retain actor and return control.");
                actor.dead = false; actor.health = 100; actor.GetComponent<CharacterController>().enabled = true;
                game.ToggleCompanion(); game.companion = farther; Invoke(game.Control, "LateUpdate");
                Require(!game.IsPossessing && trainerController.enabled && game.companion == farther, "Changing current companion must release old control without clobbering the new reference.");
                game.Dismiss(); actor.ally = false;
                game.ToggleCompanion(); fallback = game.companion;
                Require(fallback && fallback != actor && game.OwnsCompanion(fallback) && game.IsPossessing, "No eligible scene Ally must retain the legacy party-spawn fallback.");
                SceneManager.MoveGameObjectToScene(fallback.gameObject, preview);
                game.ToggleCompanion(); Require(!fallback && actor && !game.companion && trainerController.enabled, "Only Game-owned spawned fallback must be destroyed on withdrawal.");
                actor.ally = true; game.ToggleCompanion(); game.Control.Possess(actor, cameras);
                UnityEngine.Object.DestroyImmediate(actor.gameObject);
                Require(!game.IsPossessing && !game.companion && trainerController.enabled && virtualCamera.Follow == trainerPivot, "Destroying the authored actor must release cameras and trainer safely.");
                Require(!EditorApplication.isPlaying, "Validation must never enter Play.");
                game.companion = null; game.Control.Release();
            }
            finally
            {
                if (inspector) UnityEngine.Object.DestroyImmediate(inspector);
                if (fallback) UnityEngine.Object.DestroyImmediate(fallback.gameObject);
                Game.Instance = originalGame;
                EditorSceneManager.ClosePreviewScene(preview);
                foreach (var pair in snapshots)
                {
                    FullBodyAuthoring.AssertSnapshot(pair.Value, FullBodyAuthoring.Snapshot(pair.Key));
                    Require(pair.Key.isDirty == dirty[pair.Key], "Ally validation changed a live scene dirty flag.");
                }
            }
            return "Authored Ally: " + checks + " native Edit-mode checks passed; preview fixtures removed, live scenes and save files untouched. Live keyboard input/camera feel remain untested.";
        }

        [MenuItem("Wildbound/Combat/Validate Companion Combat")]
        public static void RunMenu() { Debug.Log(Run()); }
        [MenuItem("Wildbound/Combat/Select Attack Catalog")]
        public static void SelectCatalog()
        {
            Selection.activeObject = Resources.Load<CompanionCombatCatalog>(CompanionCombatCatalog.ResourceName);
            if (Selection.activeObject) EditorGUIUtility.PingObject(Selection.activeObject);
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Companion combat validation requires idle Edit mode; no mode will be changed.");
            checks = 0;
            var originalGame = Game.Instance;
            float originalTimeScale = Time.timeScale;
            var snapshots = new Dictionary<Scene, Dictionary<EntityId, string>>();
            var dirty = new Dictionary<Scene, bool>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)) continue;
                snapshots.Add(scene, FullBodyAuthoring.Snapshot(scene)); dirty.Add(scene, scene.isDirty);
            }
            var preview = EditorSceneManager.NewPreviewScene();
            CompanionAttack attack = null;
            CompanionLoadout custom = null;
            CompanionCombatCatalog customCatalog = null;
            try
            {
                var catalog = Resources.Load<CompanionCombatCatalog>(CompanionCombatCatalog.ResourceName);
                ValidateAssets(catalog);
                var trainerObject = Fixture("trainer", preview);
                trainerObject.SetActive(false);
                trainerObject.AddComponent<CharacterController>();
                var input = trainerObject.AddComponent<StarterAssetsInputs>();
                var trainerController = trainerObject.AddComponent<ThirdPersonController>();
                var trainerPivot = Fixture("trainer pivot", preview).transform;
                trainerPivot.SetParent(trainerObject.transform, false);
                trainerController.CinemachineCameraTarget = trainerPivot.gameObject;
                var trainer = trainerObject.AddComponent<PlayerMotor>();
                trainerObject.SetActive(true);
                var cameraObject = Fixture("camera", preview);
                var virtualCamera = cameraObject.AddComponent<CinemachineCamera>();
                virtualCamera.enabled = false;
                virtualCamera.Follow = trainerPivot;
                virtualCamera.LookAt = trainerPivot;
                var game = Fixture("owner", preview).AddComponent<Game>();
                game.player = trainer; game.combatCatalog = catalog; Game.Instance = game;
                var creature = Fixture("companion", preview).AddComponent<Creature>();
                creature.transform.position = new Vector3(10, 0, 0);
                creature.model = Fixture("companion visual", preview).transform; creature.model.SetParent(creature.transform, false);
                creature.Init(0, true, new CreatureRecord(0)); game.companion = creature;
                // HideAndDontSave preview cameras are intentionally absent from FindObjectsByType.
                var fixtureCameras = new CinemachineVirtualCameraBase[] { virtualCamera };
                var control = game.Control;
                control.Possess(creature, fixtureCameras);
                Require(game.IsPossessing && game.ControlledTransform == creature.transform, "Summoning must transfer control to the companion.");
                Require(!trainerController.enabled && input.enabled, "Trainer movement must stop while input collection stays enabled.");
                Require(virtualCamera.Follow != trainerPivot && virtualCamera.LookAt == virtualCamera.Follow, "Camera follow/look must transfer to the companion.");
                Require(virtualCamera.Follow.IsChildOf(creature.transform), "Possession camera target must belong to the companion.");
                var direction = control.MovementDirection(Vector2.up, cameraObject.transform);
                Require((direction - Vector3.forward).sqrMagnitude < .0001f, "Movement must be camera-relative.");
                Require((game.AttackPoint(creature.AttackAt(0), null) - (creature.transform.position + creature.transform.forward * creature.AttackAt(0).range)).sqrMagnitude < .0001f, "Untargeted attacks must originate at the companion, not trainer.");
                Require(!creature.IsCasting, "Possession must not auto-cast an attack.");

                attack = ScriptableObject.CreateInstance<CompanionAttack>(); attack.hideFlags = HideFlags.HideAndDontSave;
                attack.effect = AttackEffect.Heal; attack.damage = 9; attack.cooldown = 7; attack.castDelay = 10;
                custom = ScriptableObject.CreateInstance<CompanionLoadout>(); custom.hideFlags = HideFlags.HideAndDontSave;
                custom.id = "validation-custom"; custom.attacks = new[] { attack };
                customCatalog = ScriptableObject.CreateInstance<CompanionCombatCatalog>(); customCatalog.hideFlags = HideFlags.HideAndDontSave;
                customCatalog.speciesLoadouts = catalog.speciesLoadouts; customCatalog.additionalLoadouts = new[] { custom };
                game.combatCatalog = customCatalog; creature.loadout = custom;
                Require(creature.AttackAt(1) == null, "Empty configured slots must remain unavailable.");
                Require(creature.Begin(0, creature.transform.position, null), "A ready valid configured attack must cast.");
                Require(creature.ReadyAt(0) >= Time.time + 6.9f, "Cooldown must come from the configured attack.");
                creature.CancelCast();
                Require(!creature.Begin(0, creature.transform.position, null), "Cancelling a cast must not bypass cooldown.");
                var record = game.CapturedRecord(creature);
                var restoredRecord = JsonUtility.FromJson<CreatureRecord>(JsonUtility.ToJson(record));
                Require(restoredRecord.combatLoadoutId == custom.id && game.ResolveLoadout(restoredRecord, restoredRecord.species) == custom, "Captured loadout identity must survive save/load and resolve for summoning.");
                var legacy = JsonUtility.FromJson<CreatureRecord>("{\"species\":1,\"health\":100,\"level\":1}");
                Require(game.ResolveLoadout(legacy, legacy.species) == catalog.Resolve(null, 1), "Legacy saves must resolve their species default without migration.");
                var ready = game.Cooldowns(creature); var sameRecord = creature.record;
                control.Release();
                Require(!game.IsPossessing && trainerController.enabled && virtualCamera.Follow == trainerPivot && virtualCamera.LookAt == trainerPivot, "Withdrawal must restore exact controller/camera targets.");
                creature.record = sameRecord; control.Possess(creature, fixtureCameras);
                Require(ReferenceEquals(ready, game.Cooldowns(creature)), "Re-summoning must retain per-companion session cooldowns.");
                creature.health = 1; creature.Hurt(2, null);
                Require(!game.IsPossessing && game.companion == null && trainerController.enabled && virtualCamera.Follow == trainerPivot, "Companion KO must return trainer control.");
                creature.dead = false; creature.health = 100; creature.GetComponent<CharacterController>().enabled = true;
                game.companion = creature; trainerController.enabled = false; control.Possess(creature, fixtureCameras); control.Release();
                Require(!trainerController.enabled, "An originally disabled trainer controller must remain disabled after release.");
                trainerController.enabled = true; game.companion = creature; control.Possess(creature, fixtureCameras);
                Invoke(creature, "OnDisable");
                Require(!game.IsPossessing && trainerController.enabled, "Unexpected companion disable must restore trainer control.");
                game.companion = creature; control.Possess(creature, fixtureCameras);
                Invoke(game, "OnDisable");
                Require(!game.IsPossessing && trainerController.enabled && virtualCamera.Follow == trainerPivot, "Game shutdown must restore controller and camera targets.");
                game.companion = creature; control.Possess(creature, fixtureCameras);
                game.paused = true;
                Require(!creature.Begin(0, Vector3.zero, null), "Paused combat must reject casts.");
                game.paused = false; creature.stunUntil = Time.time + 1;
                Require(!creature.Begin(0, Vector3.zero, null), "Stunned combat must reject casts.");
                creature.stunUntil = 0; creature.capturing = true;
                Require(!creature.Begin(0, Vector3.zero, null), "Capturing combat must reject casts.");
                creature.capturing = false;

                int directors = UnityEngine.Object.FindObjectsByType<VfxDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
                var fx = Fixture("VFX", preview).AddComponent<EmeraldMoveVfx>();
                var child = Fixture("nested VFX", preview).AddComponent<VfxTimeline>(); child.transform.SetParent(fx.transform, false);
                fx.shakes.Add(new VfxTimeline.ShakeCue { time = 0 }); fx.slowMotion.Add(new VfxTimeline.SlowMotionCue { time = 0 });
                child.shakes.Add(new VfxTimeline.ShakeCue { time = 0 }); child.slowMotion.Add(new VfxTimeline.SlowMotionCue { time = 0 });
                fx.subEffects.Add(new VfxTimeline.TimelineCue { timeline = child, time = 0 });
                CompanionAttackVfx.PrepareForGameplay(fx);
                Time.timeScale = 0; fx.Play();
                Require(fx.shakes.Count == 0 && fx.slowMotion.Count == 0 && child.shakes.Count == 0 && child.slowMotion.Count == 0, "Global time/ shake cues must be stripped recursively.");
                Require(Time.timeScale == 0 && directors == UnityEngine.Object.FindObjectsByType<VfxDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length, "VFX playback must not create a global director or overwrite pause.");
                fx.StopAndClear();
                control.Release();
                game.companion = null;
            }
            finally
            {
                // Cleanup stays inside the preview; never save scenes, enter Play, or dispose live actors.
                Game.Instance = originalGame; Time.timeScale = originalTimeScale;
                EditorSceneManager.ClosePreviewScene(preview);
                if (customCatalog) UnityEngine.Object.DestroyImmediate(customCatalog);
                if (custom) UnityEngine.Object.DestroyImmediate(custom);
                if (attack) UnityEngine.Object.DestroyImmediate(attack);
                foreach (var pair in snapshots)
                {
                    FullBodyAuthoring.AssertSnapshot(pair.Value, FullBodyAuthoring.Snapshot(pair.Key));
                    Require(pair.Key.isDirty == dirty[pair.Key], "Validation changed a live scene dirty flag.");
                }
            }
            return "Companion combat: " + checks + " deterministic Edit-mode checks passed; temporary preview fixtures removed, live scenes and Play mode unchanged. This does not prove live input, animation, camera feel, or VFX rendering.";
        }
    }
}
