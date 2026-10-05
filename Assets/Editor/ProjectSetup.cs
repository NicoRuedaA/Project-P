using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace Wildbound.Editor
{
    // Scene generation is deliberate, never an import/domain-reload side effect.
    public static class ProjectSetup
    {
        [MenuItem("Wildbound/Create playable valley")]
        public static void Build() { Debug.Log(EditableSceneAuthoring.Build()); }
        [MenuItem("Wildbound/Build desktop")]
        public static void BuildDesktop()
        {
            if (!File.Exists(EditableSceneAuthoring.ScenePath)) { Debug.LogError("Create the playable valley before building."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string output = EditorUtility.SaveFolderPanel("Build destination", "", "WildboundBuild");
            if (string.IsNullOrEmpty(output)) return;
            var target = EditorUserBuildSettings.activeBuildTarget;
            string name = target == BuildTarget.StandaloneWindows64 ? "Wildbound.exe" : target == BuildTarget.StandaloneOSX ? "Wildbound.app" : "Wildbound";
            BuildPipeline.BuildPlayer(new[] { EditableSceneAuthoring.ScenePath }, Path.Combine(output, name), target, BuildOptions.None);
        }
    }
}
