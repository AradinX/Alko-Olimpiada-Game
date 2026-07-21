using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

public static class AddPackages
{
    static AddAndRemoveRequest req;

    public static void Run()
    {
        req = Client.AddAndRemove(new[]
        {
            "com.unity.render-pipelines.universal",
            "com.unity.inputsystem",
            "com.unity.ugui",
            "com.unity.netcode.gameobjects",
            "com.unity.services.vivox",
            "com.unity.services.authentication",
        });
        EditorApplication.update += Poll;
    }

    static void Poll()
    {
        if (!req.IsCompleted) return;
        EditorApplication.update -= Poll;
        if (req.Status == StatusCode.Success)
        {
            UnityEngine.Debug.Log("[AddPackages] OK");
            EditorApplication.Exit(0);
        }
        else
        {
            UnityEngine.Debug.LogError("[AddPackages] FAILED: " + req.Error.message);
            EditorApplication.Exit(1);
        }
    }
}
