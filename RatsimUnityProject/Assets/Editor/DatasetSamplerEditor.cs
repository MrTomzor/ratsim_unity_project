using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(DatasetSampler))]
public class DatasetSamplerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        DatasetSampler sampler = (DatasetSampler)target;
        
        GUI.enabled = Application.isPlaying;
        if (GUILayout.Button("Sample Dataset"))
        {
            sampler.Sample();
        }
        if (GUILayout.Button("Generate Camera Matrices"))
        {
            sampler.GenerateMatrices();
        }
        GUI.enabled = true;
    }
}
