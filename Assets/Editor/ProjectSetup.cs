using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace Wildbound.Editor {
[InitializeOnLoad] public static class ProjectSetup {
 static ProjectSetup(){EditorApplication.delayCall+=EnsureScene;}
 static void EnsureScene(){if(EditorApplication.isPlayingOrWillChangePlaymode)return;if(!System.IO.File.Exists("Assets/Scenes/Valley.unity"))Build();}
 [MenuItem("Wildbound/Create playable valley")]
 public static void Build(){if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
 var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("Wildbound / bootstrap").AddComponent<Game>();EditorSceneManager.SaveScene(scene,"Assets/Scenes/Valley.unity");EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Valley.unity",true)};
 PlayerSettings.companyName="Nico Rueda";PlayerSettings.productName="Wildbound";PlayerSettings.defaultScreenWidth=1280;PlayerSettings.defaultScreenHeight=720;PlayerSettings.runInBackground=true;
 var tags=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);var layers=tags.FindProperty("layers");layers.GetArrayElementAtIndex(8).stringValue="Environment";tags.ApplyModifiedProperties();AssetDatabase.SaveAssets();Debug.Log("Wildbound listo. Abre Valley y pulsa Play.");
 }
 [MenuItem("Wildbound/Build desktop")]
 public static void BuildDesktop(){EnsureScene();string output=EditorUtility.SaveFolderPanel("Build destination","","WildboundBuild");if(string.IsNullOrEmpty(output))return;var target=EditorUserBuildSettings.activeBuildTarget;string name=target==BuildTarget.StandaloneWindows64?"Wildbound.exe":target==BuildTarget.StandaloneOSX?"Wildbound.app":"Wildbound";BuildPipeline.BuildPlayer(EditorBuildSettings.scenes,System.IO.Path.Combine(output,name),target,BuildOptions.None);}
}
}