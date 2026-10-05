using System.IO;
using OneWood.Dev;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OneWood.Editor
{
    public static class DevSceneBuilder
    {
        public const string ShotFeedbackScenePath = "Assets/_Project/Scenes/Dev_ShotFeedback.unity";

        [MenuItem("One Wood/Dev/Create Shot Feedback Scene")]
        public static void CreateShotFeedbackScene()
        {
            if (File.Exists(ShotFeedbackScenePath) && !Application.isBatchMode &&
                !EditorUtility.DisplayDialog("Shot Feedback Scene", $"{ShotFeedbackScenePath} exists. Overwrite?", "Overwrite", "Cancel"))
                return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("Shot Feedback Monitor").AddComponent<ShotFeedbackMonitor>();

            Directory.CreateDirectory(Path.GetDirectoryName(ShotFeedbackScenePath));
            EditorSceneManager.SaveScene(scene, ShotFeedbackScenePath);
            Debug.Log($"Created {ShotFeedbackScenePath}");
        }
    }
}
