using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Wildbound.Editor
{
    // Same-user local automation only: fixed files and explicitly allowlisted operations.
    // No network, credentials, caller-supplied paths/code, or play-mode control.
    // Scene authoring can save only the fixed Valley scene after native non-destructive inspection.
    [InitializeOnLoad]
    public static class LocalValidationBridge
    {
        const int MaxRequestBytes = 4096;
        const string ReplayPrefix = "Wildbound.LocalValidationBridge.completed.";
        static readonly string DirectoryPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/LocalValidationBridge"));
        static readonly string RequestPath = Path.Combine(DirectoryPath, "request.json");
        static readonly string ClaimedPath = Path.Combine(DirectoryPath, "processing.json");
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        // Deliberately restricted JSON schema: exactly two distinct plain-string fields.
        static readonly Regex Shape = new Regex(@"\A\s*\{\s*""(?<first>id|command)""\s*:\s*""(?<value1>[^""\\\r\n]*)""\s*,\s*""(?<second>id|command)""\s*:\s*""(?<value2>[^""\\\r\n]*)""\s*\}\s*\z", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));
        static double nextPoll, nextHeartbeat;
        static bool warned;
        [Serializable]
        sealed class EditorStatus
        {
            public string project, version;
            public bool compiling, updating, playing, changingPlayMode;
        }
        [Serializable]
        sealed class Response
        {
            public string id, command, error, report;
            public bool success;
            public EditorStatus editor;
        }
        sealed class Request
        {
            public string id, command;
        }
        static LocalValidationBridge() { EditorApplication.update += Poll; }
        static EditorStatus Status()
        {
            return new EditorStatus {
                project = Path.GetFileName(Path.GetDirectoryName(Application.dataPath)), version = Application.unityVersion,
                compiling = EditorApplication.isCompiling, updating = EditorApplication.isUpdating,
                playing = EditorApplication.isPlaying, changingPlayMode = EditorApplication.isPlayingOrWillChangePlaymode
            };
        }
        static void Poll()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < nextPoll) return;
            nextPoll = now + .25;
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                if (now >= nextHeartbeat) { Publish("ready.json", JsonUtility.ToJson(Status(), true)); nextHeartbeat = now + 1; }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                // A domain reload/crash after claiming must never execute the operation again.
                if (File.Exists(ClaimedPath))
                {
                    Request stale; string ignored;
                    TryRead(ClaimedPath, out stale, out ignored);
                    if (stale != null) SessionState.SetBool(ReplayPrefix + stale.id, true);
                    Respond(stale, false, "interrupted", "A previously claimed request was interrupted; it was not executed again.");
                    File.Delete(ClaimedPath);
                    return;
                }
                if (!File.Exists(RequestPath)) return;
                File.Move(RequestPath, ClaimedPath); // Atomic same-directory claim; the producer also publishes atomically.
                HandleClaim();
                warned = false;
            }
            catch (Exception)
            {
                // Keep the claim when publication fails. Replay protection prevents another execution.
                if (!warned) { warned = true; Debug.LogWarning("Local validation bridge I/O failed. Check the fixed Temp/LocalValidationBridge directory; no operation will be retried automatically."); }
            }
        }
        static void HandleClaim()
        {
            Request request; string error;
            if (!TryRead(ClaimedPath, out request, out error))
            {
                Respond(request, false, error, "Request rejected. Expected exactly {\"id\":\"safe-id\",\"command\":\"status|validate_companion_combat|validate_locomotion|refresh|inspect_scene|build_editable_scene|prepare_pokemon_gallery|import_pokemon_gallery|inspect_pokemon_gallery|render_pokemon_gallery\"}.");
                File.Delete(ClaimedPath); return;
            }
            if (SessionState.GetBool(ReplayPrefix + request.id, false))
            {
                Respond(request, false, "replayed_id", "This request ID was already claimed in this editor session. The operation was not repeated.");
                File.Delete(ClaimedPath); return;
            }
            // Mark before dispatch, including failures/unknown operations; survives domain reload.
            SessionState.SetBool(ReplayPrefix + request.id, true);
            bool refresh = false;
            try
            {
                switch (request.command)
                {
                    case "status": Respond(request, true, "", "Editor status read."); break;
                    case "validate_companion_combat": Respond(request, true, "", CompanionCombatValidation.Run()); break;
                    case "validate_locomotion":
                        if (EditorApplication.isPlayingOrWillChangePlaymode)
                            Respond(request, false, "play_mode_active", "Validation requires Edit mode. The bridge will not change play mode.");
                        else Respond(request, true, "", ProceduralLocomotionValidation.Run());
                        break;
                    case "validate_body_movement_inspector": Respond(request, true, "", BodyMovementInspectorValidation.Run()); break;
                    case "inspect_creature_motion": Respond(request, true, "", EditorCreatureMotionDiagnostics.Inspect()); break;
                    case "inspect_scene": Respond(request, true, "", EditableSceneAuthoring.Inspect()); break;
                    case "build_editable_scene": Respond(request, true, "", EditableSceneAuthoring.Build()); break;
                    case "prepare_pokemon_gallery": Respond(request, true, "", PokemonGalleryAuthoring.Prepare()); break;
                    case "import_pokemon_gallery": Respond(request, true, "", PokemonGalleryAuthoring.Build()); break;
                    case "inspect_pokemon_gallery": Respond(request, true, "", PokemonGalleryAuthoring.Inspect()); break;
                    case "render_pokemon_gallery": Respond(request, true, "", PokemonGalleryAuthoring.ContactSheet()); break;
                    case "export_pokemon_prefabs": Respond(request, true, "", PokemonPrefabAuthoring.Export()); break;
                    case "replace_creature_visuals": Respond(request, true, "", PokemonPrefabAuthoring.Replace()); break;
                    case "inspect_pokemon_prefabs": Respond(request, true, "", PokemonPrefabAuthoring.Inspect()); break;
                    case "replace_trainer_visual": Respond(request, true, "", TrainerModelAuthoring.Replace()); break;
                    case "inspect_trainer_visual": Respond(request, true, "", TrainerModelAuthoring.Inspect()); break;
                    case "save_current_valley": Respond(request, true, "", FullBodyAuthoring.SaveCurrent()); break;
                    case "upgrade_fullbody": Respond(request, true, "", FullBodyAuthoring.Upgrade()); break;
                    case "validate_fullbody_regions": Respond(request, true, "", FullBodyValidation.Run()); break;
                    case "validate_fullbody": Respond(request, true, "", FullBodyAuthoring.ValidateArmRegression()); break;
                    case "apply_toon_style": Respond(request, true, "", ToonStyleAuthoring.Apply()); break;
                    case "validate_toon_style": Respond(request, true, "", ToonStyleValidation.Run()); break;
                    case "inspect_starter_cleanup": Respond(request, true, "", StarterAssetsCleanup.Inspect()); break;
                    case "apply_starter_cleanup": Respond(request, true, "", StarterAssetsCleanup.Apply()); break;
                    case "verify_starter_cleanup": Respond(request, true, "", StarterAssetsCleanup.Verify()); break;
                    case "remove_starter_cleanup_harness": Respond(request, true, "", StarterAssetsCleanup.RemoveHarness()); break;
                    case "refresh": Respond(request, true, "", "Asset refresh scheduled after this response."); refresh = true; break;
                    default: Respond(request, false, "unknown_command", "Only status, validate_companion_combat, validate_locomotion, refresh, inspect_scene, build_editable_scene, prepare_pokemon_gallery, import_pokemon_gallery, inspect_pokemon_gallery, and render_pokemon_gallery, export_pokemon_prefabs, replace_creature_visuals, inspect_pokemon_prefabs, replace_trainer_visual, inspect_trainer_visual, apply_toon_style, and validate_toon_style are supported."); break;
                }
            }
            catch (Exception exception)
            {
                // Validation assertions are useful diagnostics; omit stack traces and bound the report.
                string report = exception is InvalidOperationException ? exception.Message : "The allowlisted operation failed.";
                if (report.Length > 2048) report = report.Substring(0, 2048);
                Respond(request, false, "command_failed", report);
            }
            File.Delete(ClaimedPath);
            if (refresh) EditorApplication.delayCall += AssetDatabase.Refresh;
        }
        static bool TryRead(string path, out Request request, out string error)
        {
            request = null; error = "malformed_request";
            byte[] bytes = new byte[MaxRequestBytes + 1]; int count = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                while (count < bytes.Length) { int read = stream.Read(bytes, count, bytes.Length - count); if (read == 0) break; count += read; }
            }
            if (count > MaxRequestBytes) { error = "request_too_large"; return false; }
            string json;
            try { json = Utf8.GetString(bytes, 0, count); } catch (DecoderFallbackException) { error = "invalid_utf8"; return false; }
            Match match = Shape.Match(json);
            if (!match.Success || match.Groups["first"].Value == match.Groups["second"].Value) return false;
            string id = match.Groups["first"].Value == "id" ? match.Groups["value1"].Value : match.Groups["value2"].Value;
            string command = match.Groups["first"].Value == "command" ? match.Groups["value1"].Value : match.Groups["value2"].Value;
            if (id.Length == 0 || id.Length > 64) { error = "invalid_id"; return false; }
            foreach (char c in id) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '_' || c == '-')) { error = "invalid_id"; return false; }
            request = new Request { id = id, command = command };
            if (command.Length == 0 || command.Length > 64) { error = "invalid_command"; return false; }
            error = ""; return true;
        }
        static void Respond(Request request, bool success, string error, string report)
        {
            Publish("result.json", JsonUtility.ToJson(new Response { id = request == null ? "" : request.id, command = request == null ? "" : request.command,
                success = success, error = error, report = report, editor = Status() }, true));
        }
        static void Publish(string filename, string json)
        {
            string target = Path.Combine(DirectoryPath, filename), temporary = target + ".tmp";
            File.WriteAllText(temporary, json, Utf8);
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }
    }
}
