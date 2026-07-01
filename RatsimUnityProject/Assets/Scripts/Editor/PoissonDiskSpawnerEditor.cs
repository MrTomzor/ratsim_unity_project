using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(PoissonDiskSpawner))]
public class PoissonDiskSpawnerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the default inspector properties
        DrawDefaultInspector();

        PoissonDiskSpawner spawner = (PoissonDiskSpawner)target;

        // Add some vertical space
        GUILayout.Space(15);

        // Draw the large button
        if (GUILayout.Button("Generate Points", GUILayout.Height(35)))
        {
            // Register the action so it can be undone (Ctrl+Z)
            Undo.RegisterFullObjectHierarchyUndo(spawner.gameObject, "Generate Poisson Points");
            
            spawner.GenerateAndSpawn();
            
            // Mark the scene as dirty so the changes are saved
            EditorUtility.SetDirty(spawner.gameObject);
        }
        if (GUILayout.Button("Optimize Points", GUILayout.Height(35)))
        {
            // Register the action so it can be undone (Ctrl+Z)
            Undo.RegisterFullObjectHierarchyUndo(spawner.gameObject, "Optimize Points");
            
            spawner.OptimizePoints();
            
            // Mark the scene as dirty so the changes are saved
            EditorUtility.SetDirty(spawner.gameObject);
        }
    }
}
