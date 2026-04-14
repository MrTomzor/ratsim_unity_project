using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages sensor visualization UI overlays for human control mode.
/// Subscribes to the human control toggle and activates/deactivates
/// visualizers based on the first agent's active sensors.
/// Visualizers read data directly from sensor components (not via TCP topics).
/// </summary>
public class SensorVisualizationManager : MonoBehaviour
{
    [Header("Visualizer References")]
    public Lidar2DVisualizer lidar2DVisualizer;
    public CompassVisualizer compassVisualizer;
    public HeadDirectionCellsVisualizer headDirectionCellsVisualizer;
    public SectorSignalVisualizer sectorSignalVisualizer;
    public GameObject cameraBlocker;
    public GameObject scoreVisualizer;

    [Header("Settings")]
    public string humanControlTopic = "/enable_human_control";

    private bool visualizationEnabled = false;

    private Dictionary<string, System.Action<GameObject>> sensorToVisualizer;

    public string UIToggleKey = "g";

    void Start()
    {
        sensorToVisualizer = new Dictionary<string, System.Action<GameObject>>
        {
            { "lidar2d", SetupLidar2D },
            { "compass", SetupCompass },
            { "head_direction_cells", SetupHeadDirectionCells },
            { "sector_signal", SetupSectorSignal },
        };

        SetAllVisualizersActive(false);

        RoslikeTCPServer.GetInstance().Subscribe<BoolMessage>(humanControlTopic, OnHumanControlToggle);
    }

    void Update()
    {
        if (Input.GetKeyDown(UIToggleKey))
        {
            // Simply disable all children objects
            visualizationEnabled = !visualizationEnabled;
            if (visualizationEnabled)
            {
                EnableVisualizersForAgent();
                cameraBlocker.SetActive(true);
                scoreVisualizer.SetActive(true);
            }
            else
            {
                SetAllVisualizersActive(false);
                cameraBlocker.SetActive(false);
                scoreVisualizer.SetActive(false);
            }
        }
    }

    void OnHumanControlToggle(BoolMessage msg)
    {
        visualizationEnabled = msg.data;

        if (visualizationEnabled)
        {
            EnableVisualizersForAgent();
            cameraBlocker.SetActive(true);
            scoreVisualizer.SetActive(true);
        }
        else
        {
            SetAllVisualizersActive(false);
        }
    }

    void EnableVisualizersForAgent()
    {
        GameObject agent = WorldLoadingController.instance?.agentObject;
        if (agent == null)
        {
            Debug.LogWarning("SensorVisualizationManager: no agent found");
            return;
        }

        foreach (var kvp in sensorToVisualizer)
        {
            MonoBehaviour sensorComponent = GetSensorComponent(agent, kvp.Key);
            if (sensorComponent != null && sensorComponent.enabled)
            {
                kvp.Value(agent);
            }
        }
    }

    MonoBehaviour GetSensorComponent(GameObject agent, string sensorName)
    {
        switch (sensorName)
        {
            case "lidar2d": return agent.GetComponentInChildren<SemanticLidarSensor>(true);
            case "compass": return agent.GetComponentInChildren<CompassSensor>(true);
            case "head_direction_cells": return agent.GetComponentInChildren<HeadDirectionCellsSensor>(true);
            case "sector_signal": return agent.GetComponentInChildren<SectorSignalSensor>(true);
            default: return null;
        }
    }

    void SetupLidar2D(GameObject agent)
    {
        if (lidar2DVisualizer == null) return;
        var sensor = agent.GetComponentInChildren<SemanticLidarSensor>(true);
        lidar2DVisualizer.gameObject.SetActive(true);
        lidar2DVisualizer.Initialize(sensor);
    }

    void SetupCompass(GameObject agent)
    {
        if (compassVisualizer == null) return;
        var sensor = agent.GetComponentInChildren<CompassSensor>(true);
        compassVisualizer.gameObject.SetActive(true);
        compassVisualizer.Initialize(sensor);
    }

    void SetupHeadDirectionCells(GameObject agent)
    {
        if (headDirectionCellsVisualizer == null) return;
        var sensor = agent.GetComponentInChildren<HeadDirectionCellsSensor>(true);
        headDirectionCellsVisualizer.gameObject.SetActive(true);
        headDirectionCellsVisualizer.Initialize(sensor);
    }

    void SetupSectorSignal(GameObject agent)
    {
        if (sectorSignalVisualizer == null) return;
        var sensor = agent.GetComponentInChildren<SectorSignalSensor>(true);
        sectorSignalVisualizer.gameObject.SetActive(true);
        sectorSignalVisualizer.Initialize(sensor);
    }

    void SetAllVisualizersActive(bool active)
    {
        Debug.Log($"SensorVisualizationManager: setting all visualizers active={active}");
        if (lidar2DVisualizer != null) lidar2DVisualizer.gameObject.SetActive(active);
        if (compassVisualizer != null) compassVisualizer.gameObject.SetActive(active);
        if (headDirectionCellsVisualizer != null) headDirectionCellsVisualizer.gameObject.SetActive(active);
        if (sectorSignalVisualizer != null) sectorSignalVisualizer.gameObject.SetActive(active);
        cameraBlocker.SetActive(active);
        scoreVisualizer.SetActive(active);
    }
}
