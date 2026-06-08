using UnityEngine;
using UnityEditor;

public class MaterialSwapper : EditorWindow
{
    public Material oldMaterial;
    public Material newMaterial;
    public string categoryTag = "Untagged";

    [MenuItem("Tools/Material Swapper")]
    public static void ShowWindow()
    {
        GetWindow<MaterialSwapper>("Material Swapper");
    }

    void OnGUI()
    {
        GUILayout.Label("Conditional Material Swap", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        GUILayout.Label("1. Define the materials to swap:");
        oldMaterial = (Material)EditorGUILayout.ObjectField("Material to Replace", oldMaterial, typeof(Material), false);
        newMaterial = (Material)EditorGUILayout.ObjectField("New Material", newMaterial, typeof(Material), false);
        
        EditorGUILayout.Space();
        GUILayout.Label("2. Filter by Category (Tag):");
        categoryTag = EditorGUILayout.TagField("Target Tag", categoryTag);

        EditorGUILayout.Space();
        if (GUILayout.Button("Swap Materials in Scene"))
        {
            SwapMaterials();
        }
    }

    void SwapMaterials()
    {
        if (oldMaterial == null || newMaterial == null)
        {
            Debug.LogWarning("Please assign both the Old and New materials.");
            return;
        }

        // Find all objects in the scene with your category tag
        GameObject[] targetObjects;
        if (categoryTag == "Untagged")
        {
            // Fetch all active GameObjects in the scene and filter for Untagged
            #if UNITY_2023_1_OR_NEWER
            GameObject[] allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            #else
            GameObject[] allObjects = Object.FindObjectsOfType<GameObject>();
            #endif
            var untaggedList = new System.Collections.Generic.List<GameObject>();
            foreach (var obj in allObjects)
            {
                if (obj.CompareTag("Untagged"))
                {
                    untaggedList.Add(obj);
                }
            }
            targetObjects = untaggedList.ToArray();
        }
        else
        {
            targetObjects = GameObject.FindGameObjectsWithTag(categoryTag);
        }
        
        int changedCount = 0;

        foreach (GameObject obj in targetObjects)
        {
            // Get all MeshRenderers on the object and its children
            MeshRenderer[] renderers = obj.GetComponentsInChildren<MeshRenderer>(true);
            
            foreach (MeshRenderer rend in renderers)
            {
                // We use sharedMaterials so we don't leak material instances in the editor
                Material[] mats = rend.sharedMaterials;
                bool materialWasChanged = false;

                // Loop through the material slots (in case there are multiple materials on one mesh)
                for (int i = 0; i < mats.Length; i++)
                {
                    // The core logic: ONLY swap if the current material matches the old one
                    if (mats[i] == oldMaterial)
                    {
                        mats[i] = newMaterial;
                        materialWasChanged = true;
                    }
                }

                if (materialWasChanged)
                {
                    // Register for Undo so you can Ctrl+Z if you make a mistake
                    Undo.RecordObject(rend, "Swap Material");
                    rend.sharedMaterials = mats;
                    
                    // Crucial step: Tells Unity to save this change to the Prefab instance in the scene
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rend);
                    changedCount++;
                }
            }
        }

        Debug.Log($"Successfully swapped materials on {changedCount} renderers.");
    }
}