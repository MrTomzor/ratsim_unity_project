using UnityEngine;

namespace RealLifeEnvironment
{
    [System.Serializable]
    public class SeasonalChange
    {
        [Tooltip("Name of the season/event.")]
        public string seasonName = "New Season";
        [Tooltip("Toggle this to enable/disable the objects in the lists below.")]
        public bool isActive = false;
        public System.Collections.Generic.List<GameObject> objectsToEnable = new System.Collections.Generic.List<GameObject>();
        public System.Collections.Generic.List<GameObject> objectsToDisable = new System.Collections.Generic.List<GameObject>();
    }

    [ExecuteAlways]
    public class WorldChanger : WorldDataProvider
    {
        public override WorldDataType[] Provides => new[] { WorldDataType.Lighting };
        [Header("Time Settings")]
        [Range(0, 24)]
        [Tooltip("Time of day in hours. 6 = dawn, 12 = noon, 18 = dusk, 24/0 = midnight.")]
        public float timeOfDay = 12f;

        [Tooltip("How many in-game hours pass per real second. Set to 0 for static time.")]
        public float timeAdvanceRate = 1f;

        [Header("References")]
        public Light directionalLight;
        [Tooltip("The skybox material to modify (works best with Procedural skybox).")]
        public Material skyboxMaterial;

        [Header("Sun Colors & Intensity")]
        public Gradient sunColor;
        public AnimationCurve sunIntensity = new AnimationCurve(
            new Keyframe(0f, 0f), 
            new Keyframe(0.25f, 0.5f), 
            new Keyframe(0.5f, 1f), 
            new Keyframe(0.75f, 0.5f), 
            new Keyframe(1f, 0f)
        );
        [Tooltip("Compass direction of the sun.")]
        public float sunAzimuth = 45f;

        [Header("Sky & Ambient Settings")]
        [Tooltip("Controls _SkyTint or _Tint on the skybox material.")]
        public Gradient skyTint;
        [Tooltip("Controls _GroundColor on a Procedural skybox material.")]
        public Gradient groundColor;
        public AnimationCurve exposure = new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0.2f)
        );

        [Header("Environment Lighting (Gradient)")]
        public Gradient ambientSkyColor = new Gradient();
        public Gradient ambientEquatorColor = new Gradient();

        [Header("Environment Reflections")]
        public AnimationCurve reflectionIntensity = new AnimationCurve(
            new Keyframe(0f, 0.5f),
            new Keyframe(0.5f, 1f),
            new Keyframe(1f, 0.5f)
        );

        [Header("Fog Settings")]
        public bool enableFog = true;
        [Tooltip("The fog mode. Density only works with Exponential or ExponentialSquared.")]
        public FogMode fogMode = FogMode.ExponentialSquared;
        [Tooltip("Fog color over time. Make it match the horizon color during day and black at night.")]
        public Gradient fogColor;
        [Tooltip("Higher density at night, lower during the day. Normalized from 0 to 1.")]
        public AnimationCurve fogDensity = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.5f, 0.2f),
            new Keyframe(1f, 1f)
        );
        [Tooltip("Multiplier for the fog density curve.")]
        public float fogDensityMultiplier = 0.05f;

        [Header("Heavy Fog Mode")]
        [Tooltip("When enabled, uses the heavy fog density and disables 'exclude skybox' on the PostProcessLayer.")]
        public bool heavyFogMode = false;
        public float heavyFogDensity = 0.2f;
        [Tooltip("Assign the Camera that has the PostProcessLayer component on it.")]
        public Camera postProcessCamera;

        [Header("Seasonal Changes")]
        [Tooltip("List of seasonal configurations. Toggle 'isActive' to switch them on/off.")]
        public System.Collections.Generic.List<SeasonalChange> seasonalChanges = new System.Collections.Generic.List<SeasonalChange>();

        private bool _isEpisodeActive = false;

        public override void Generate()
        {
            _isEpisodeActive = true;
            
            // Optionally pull variables from the JSON config if provided
            timeOfDay = WorldLoadingController.GetParamFloat("worldchanger/time_of_day", timeOfDay);
            timeAdvanceRate = WorldLoadingController.GetParamFloat("worldchanger/time_advance_rate", timeAdvanceRate);
            heavyFogMode = WorldLoadingController.GetParamInt("worldchanger/heavy_fog", heavyFogMode ? 1 : 0) != 0;
            fogDensityMultiplier = WorldLoadingController.GetParamFloat("worldchanger/fog_density_multiplier", fogDensityMultiplier);
            heavyFogDensity = WorldLoadingController.GetParamFloat("worldchanger/heavy_fog_density", heavyFogDensity);

            string activeSeason = WorldLoadingController.GetParamString("worldchanger/season", "");
            if (!string.IsNullOrEmpty(activeSeason))
            {
                foreach (var season in seasonalChanges)
                {
                    season.isActive = (season.seasonName.Equals(activeSeason, System.StringComparison.OrdinalIgnoreCase));
                }
            }

            ApplySeasonalChanges();
            UpdateLighting();
        }

        public override void Clear()
        {
            _isEpisodeActive = false;
        }

        private void Update()
        {
            if (Application.isPlaying)
            {
                if (timeAdvanceRate > 0f && _isEpisodeActive)
                {
                    timeOfDay += timeAdvanceRate * Time.deltaTime;
                    timeOfDay %= 24f;
                }
            }
            else if (timeAdvanceRate > 0f)
            {
                // In Editor without playing, we usually don't want the time to blindly advance based on deltaTime
                // But you can scrub the slider in the inspector.
            }

            UpdateLighting();
        }

        private void OnValidate()
        {
            UpdateLighting();
        }

        public void UpdateLighting()
        {
            // Normalize time from 0 to 1
            float t = timeOfDay / 24f;

            // 1. Update Sun
            if (directionalLight != null)
            {
                directionalLight.color = sunColor.Evaluate(t);
                directionalLight.intensity = sunIntensity.Evaluate(t);

                // Rotations:
                // t = 0 (midnight) -> pointing straight up -> X = -90 (or 270)
                // t = 0.25 (dawn) -> horizon -> X = 0
                // t = 0.5 (noon) -> pointing straight down -> X = 90
                // t = 0.75 (dusk) -> horizon -> X = 180
                float xAngle = (t - 0.25f) * 360f;
                directionalLight.transform.rotation = Quaternion.Euler(xAngle, sunAzimuth, 0f);
            }

            // 2. Update Skybox Material
            if (skyboxMaterial != null)
            {
                Color evaluatedSkyTint = skyTint.Evaluate(t);
                if (skyboxMaterial.HasProperty("_Tint"))
                    skyboxMaterial.SetColor("_Tint", evaluatedSkyTint);
                if (skyboxMaterial.HasProperty("_SkyTint"))
                    skyboxMaterial.SetColor("_SkyTint", evaluatedSkyTint);
                if (skyboxMaterial.HasProperty("_GroundColor"))
                    skyboxMaterial.SetColor("_GroundColor", groundColor.Evaluate(t));
                if (skyboxMaterial.HasProperty("_Exposure"))
                    skyboxMaterial.SetFloat("_Exposure", exposure.Evaluate(t));
            }

            // 2.5 Update Environment Lighting & Reflections
            if (ambientSkyColor != null)
                RenderSettings.ambientSkyColor = ambientSkyColor.Evaluate(t);
            if (ambientEquatorColor != null)
                RenderSettings.ambientEquatorColor = ambientEquatorColor.Evaluate(t);
            if (reflectionIntensity != null)
                RenderSettings.reflectionIntensity = reflectionIntensity.Evaluate(t);

            // 3. Update Fog
            RenderSettings.fog = enableFog;
            if (enableFog)
            {
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogColor = fogColor.Evaluate(t);
                
                if (heavyFogMode)
                {
                    RenderSettings.fogDensity = heavyFogDensity;
                }
                else
                {
                    RenderSettings.fogDensity = fogDensity.Evaluate(t) * fogDensityMultiplier;
                }

                if (postProcessCamera != null)
                {
                    // Use reflection to set PostProcessLayer.fog.excludeSkybox safely without requiring assembly references
                    var ppl = postProcessCamera.GetComponent("PostProcessLayer");
                    if (ppl != null)
                    {
                        var fogField = ppl.GetType().GetField("fog", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                        if (fogField != null)
                        {
                            var fogObj = fogField.GetValue(ppl);
                            if (fogObj != null)
                            {
                                var excludeSkyboxField = fogObj.GetType().GetField("excludeSkybox", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                                if (excludeSkyboxField != null)
                                {
                                    // If heavy fog is ON, excludeSkybox should be FALSE
                                    excludeSkyboxField.SetValue(fogObj, !heavyFogMode);
                                }
                            }
                        }
                    }
                }
            }
        }

        public void ApplySeasonalChanges()
        {
            // First pass: apply inactive seasons to reset objects to their default state
            foreach (var season in seasonalChanges)
            {
                if (season.isActive) continue;

                if (season.objectsToEnable != null)
                {
                    foreach (var obj in season.objectsToEnable)
                    {
                        if (obj != null) obj.SetActive(false);
                    }
                }
                
                if (season.objectsToDisable != null)
                {
                    foreach (var obj in season.objectsToDisable)
                    {
                        if (obj != null) obj.SetActive(true);
                    }
                }
            }

            // Second pass: apply active seasons so they override any inactive season settings
            foreach (var season in seasonalChanges)
            {
                if (!season.isActive) continue;

                if (season.objectsToEnable != null)
                {
                    foreach (var obj in season.objectsToEnable)
                    {
                        if (obj != null) obj.SetActive(true);
                    }
                }
                
                if (season.objectsToDisable != null)
                {
                    foreach (var obj in season.objectsToDisable)
                    {
                        if (obj != null) obj.SetActive(false);
                    }
                }
            }
        }
    }
}
