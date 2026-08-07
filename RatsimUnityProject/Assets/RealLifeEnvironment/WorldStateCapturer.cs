using UnityEngine;
using System.IO;
using System.Collections.Generic;

namespace RealLifeEnvironment
{
    [System.Serializable]
    public class WorldStateSetting
    {
        public string captureName = "Capture_01";
        
        [Range(0, 24)]
        public float timeOfDay = 12f;
        public bool enableFog = true;
        public float fogDensityMultiplier = 0.0f;
        public bool heavyFogMode = false;
        public float heavyFogDensity = 0.0f;
        
        [Tooltip("Name of the season to activate (leave empty for none).")]
        public string activeSeasonName = "";
    }

    public class WorldStateCapturer : MonoBehaviour
    {
        [Tooltip("Reference to the WorldChanger component in the scene.")]
        public WorldChanger worldChanger;
        
        [Tooltip("The camera to capture from. If using post-processing, make sure the camera has the necessary components.")]
        public Camera captureCamera;

        [Header("Capture Settings")]
        public int resolutionWidth = 1920;
        public int resolutionHeight = 1080;
        [Tooltip("Folder path relative to project root.")]
        public string saveFolder = "Assets/WorldCaptures";

        [Header("States to Capture")]
        public List<WorldStateSetting> statesToCapture = new List<WorldStateSetting>();

        private bool isCapturing = false;

        [ContextMenu("Capture All States")]
        public void CaptureAll()
        {
            if (isCapturing) return;
            
            if (worldChanger == null || captureCamera == null)
            {
                Debug.LogError("WorldChanger or CaptureCamera is not assigned.", this);
                return;
            }

            if (!Directory.Exists(saveFolder))
            {
                Directory.CreateDirectory(saveFolder);
            }

            if (!Application.isPlaying)
            {
                Debug.LogError("CaptureAll must be used in Play Mode to ensure all natural rendering logic occurs properly.", this);
                return;
            }

            StartCoroutine(CaptureSequenceRoutine());
        }

        private System.Collections.IEnumerator CaptureSequenceRoutine()
        {
            isCapturing = true;

            // Save original state
            float origTime = worldChanger.timeOfDay;
            bool origFog = worldChanger.enableFog;
            float origFogDensityMultiplier = worldChanger.fogDensityMultiplier;
            bool origHeavyFog = worldChanger.heavyFogMode;
            float origHeavyFogDensity = worldChanger.heavyFogDensity;
            
            Dictionary<string, bool> origSeasons = new Dictionary<string, bool>();
            foreach (var season in worldChanger.seasonalChanges)
            {
                origSeasons[season.seasonName] = season.isActive;
            }

            try
            {
                for (int i = 0; i < statesToCapture.Count; i++)
                {
                    var state = statesToCapture[i];
                    
                    // Apply state settings
                    worldChanger.timeOfDay = state.timeOfDay;
                    worldChanger.enableFog = state.enableFog;
                    worldChanger.fogDensityMultiplier = state.fogDensityMultiplier;
                    worldChanger.heavyFogMode = state.heavyFogMode;
                    worldChanger.heavyFogDensity = state.heavyFogDensity;

                    // Apply season settings
                    foreach (var season in worldChanger.seasonalChanges)
                    {
                        if (!string.IsNullOrEmpty(state.activeSeasonName))
                        {
                            season.isActive = season.seasonName.Equals(state.activeSeasonName, System.StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            season.isActive = false;
                        }
                    }

                    // Force WorldChanger to update objects and lighting
                    worldChanger.ApplySeasonalChanges();
                    worldChanger.UpdateLighting();

                    // Wait a couple of frames for LODs, object instantiation, and rendering state to catch up
                    yield return null;
                    yield return null;

                    // Setup render texture for the capture camera
                    RenderTexture rt = new RenderTexture(resolutionWidth, resolutionHeight, 24);
                    captureCamera.targetTexture = rt;

                    // Wait for the rendering pipeline to naturally finish for this frame
                    yield return new WaitForEndOfFrame();

                    Texture2D screenShot = new Texture2D(resolutionWidth, resolutionHeight, TextureFormat.RGB24, false);
                    RenderTexture.active = rt;
                    screenShot.ReadPixels(new Rect(0, 0, resolutionWidth, resolutionHeight), 0, 0);
                    screenShot.Apply();
                    
                    captureCamera.targetTexture = null;
                    RenderTexture.active = null;
                    Destroy(rt);
                    
                    byte[] bytes = screenShot.EncodeToPNG();
                    string filename = $"{saveFolder}/{state.captureName}.png";
                    File.WriteAllBytes(filename, bytes);
                    Destroy(screenShot);
                }
                
                Debug.Log($"Successfully captured {statesToCapture.Count} states to {saveFolder}");
            }
            finally
            {
                // Restore original state
                worldChanger.timeOfDay = origTime;
                worldChanger.enableFog = origFog;
                worldChanger.fogDensityMultiplier = origFogDensityMultiplier;
                worldChanger.heavyFogMode = origHeavyFog;
                worldChanger.heavyFogDensity = origHeavyFogDensity;

                foreach (var season in worldChanger.seasonalChanges)
                {
                    if (origSeasons.TryGetValue(season.seasonName, out bool wasActive))
                    {
                        season.isActive = wasActive;
                    }
                }
                
                worldChanger.ApplySeasonalChanges();
                worldChanger.UpdateLighting();
                
                isCapturing = false;
            }
        }
    }
}

#if UNITY_EDITOR
namespace RealLifeEnvironment
{
    using UnityEditor;

    [CustomEditor(typeof(WorldStateCapturer))]
    public class WorldStateCapturerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            WorldStateCapturer capturer = (WorldStateCapturer)target;

            EditorGUILayout.Space();
            if (GUILayout.Button("Capture All States", GUILayout.Height(40)))
            {
                capturer.CaptureAll();
            }
        }
    }
}
#endif
