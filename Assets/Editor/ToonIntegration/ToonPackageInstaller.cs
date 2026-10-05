using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Wildbound.Editor
{
    public static class ToonPackageInstaller
    {
        public const string Package = "com.unity.toonshader@0.15.1-preview";
        static AddRequest request;
        static double deadline;

        public static void Install()
        {
            Debug.Log("[ToonIntegration] Installing " + Package);
            request = Client.Add(Package);
            deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!request.IsCompleted && EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Poll;
            if (request.IsCompleted && request.Status == StatusCode.Success)
            {
                Debug.Log("[ToonIntegration] Installed " + request.Result.packageId);
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[ToonIntegration] Package installation failed: " + (request.Error?.message ?? "timeout"));
                EditorApplication.Exit(1);
            }
        }
    }
}
