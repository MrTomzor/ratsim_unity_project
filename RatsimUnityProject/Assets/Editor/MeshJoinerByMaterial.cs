using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class MeshJoinerByMaterial : Editor
{
    private const string EXPORT_FOLDER = "OptimizedHouses";

    [MenuItem("Tools/Join Mesh of Selected Object", false, 1)]
    [MenuItem("GameObject/Join Mesh of Selected Object", false, 10)]
    public static void CombineSelectedHouse()
    {
        // 1. Get the currently selected GameObject
        GameObject selectedRoot = Selection.activeGameObject;

        if (selectedRoot == null)
        {
            Debug.LogError("House Combiner: Please select a GameObject in your Hierarchy or Project window first!");
            return;
        }

        // 2. Duplicate the entire original asset structure to preserve lights, characters, scale, etc.
        GameObject combinedRoot = GameObject.Instantiate(selectedRoot);
        combinedRoot.name = selectedRoot.name + "_OPTIMIZED";

        // Find all MeshFilters inside our new duplicated copy
        MeshFilter[] meshFilters = combinedRoot.GetComponentsInChildren<MeshFilter>();
        if (meshFilters.Length <= 0)
        {
            Debug.LogWarning($"House Combiner: '{selectedRoot.name}' doesn't have any meshes inside it to combine.");
            DestroyImmediate(combinedRoot);
            return;
        }

        // Setup material mapping and keep track of objects that contributed geometry
        Dictionary<Material, List<CombineInstance>> materialToCombineMap = new Dictionary<Material, List<CombineInstance>>();
        HashSet<GameObject> objectsToCleanUp = new HashSet<GameObject>();
        
        Matrix4x4 rootMatrix = combinedRoot.transform.worldToLocalMatrix;

        foreach (var filter in meshFilters)
        {
            // Skip the root container node itself if it happens to have a mesh filter
            if (filter.transform == combinedRoot.transform)
                continue;

            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null || !renderer.enabled)
                continue;

            Mesh mesh = filter.sharedMesh;
            if (mesh == null)
                continue;

            Material[] sharedMaterials = renderer.sharedMaterials;

            // Sort submeshes into material groups
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (i >= sharedMaterials.Length) break;

                Material mat = sharedMaterials[i];
                if (mat == null) continue;

                CombineInstance ci = new CombineInstance();
                ci.mesh = mesh;
                ci.subMeshIndex = i;
                ci.transform = rootMatrix * filter.transform.localToWorldMatrix;

                if (!materialToCombineMap.ContainsKey(mat))
                {
                    materialToCombineMap[mat] = new List<CombineInstance>();
                }
                materialToCombineMap[mat].Add(ci);
            }

            // RULE 1: Track this GameObject to delete it completely later
            objectsToCleanUp.Add(filter.gameObject);
        }

        // 3. Setup the Save Directory
        string folderPath = Path.Combine("Assets", EXPORT_FOLDER);
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", folderPath);
        }

        int uniqueMeshId = 0;
        List<Mesh> generatedMeshes = new List<Mesh>();
        List<MeshFilter> combinedMeshFilters = new List<MeshFilter>();

        // 4. Create and combine meshes group-by-material
        foreach (var kvp in materialToCombineMap)
        {
            Material mat = kvp.Key;
            List<CombineInstance> instances = kvp.Value;

            Mesh combinedMesh = new Mesh();
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // Allows >65k vertices
            combinedMesh.name = $"{selectedRoot.name}_{mat.name}_Mesh_{uniqueMeshId}";
            
            combinedMesh.CombineMeshes(instances.ToArray(), true, true);

            combinedMesh.RecalculateNormals();
            combinedMesh.RecalculateTangents();
            combinedMesh.RecalculateBounds();

            generatedMeshes.Add(combinedMesh);

            // Create a dedicated child object holding this specific combined material group
            GameObject childMatObject = new GameObject("Combined_" + mat.name);
            childMatObject.transform.SetParent(combinedRoot.transform, false);

            MeshFilter mf = childMatObject.AddComponent<MeshFilter>();
            combinedMeshFilters.Add(mf);

            MeshRenderer mr = childMatObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;

            uniqueMeshId++;
        }

        // 5. DELETION RULE 1: Force delete all GameObjects whose meshes contributed to combination
        foreach (GameObject obj in objectsToCleanUp)
        {
            if (obj != null)
            {
                DestroyImmediate(obj);
            }
        }

        // 6. DELETION RULE 2: Recursively prune all empty GameObjects that have no children left
        PruneEmptyObjects(combinedRoot.transform);

        // 7. Save sequence to embed meshes cleanly directly inside the Prefab Asset container
        string prefabPath = Path.Combine(folderPath, $"{combinedRoot.name}.prefab");
        GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(combinedRoot, prefabPath);

        if (prefabAsset != null)
        {
            for (int i = 0; i < combinedMeshFilters.Count; i++)
            {
                if (i < generatedMeshes.Count)
                {
                    Mesh currentMesh = generatedMeshes[i];

                    // Embed the raw mesh asset directly into the prefab architecture file
                    AssetDatabase.AddObjectToAsset(currentMesh, prefabAsset);

                    // Re-link the scene component to point directly to the project asset
                    combinedMeshFilters[i].sharedMesh = currentMesh;
                }
            }

            // Save our linked configurations back over the prefab asset container cleanly
            PrefabUtility.SaveAsPrefabAsset(combinedRoot, prefabPath);
            
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"<color=green><b>Success!</b></color> Baked '{selectedRoot.name}'. Cleaned up old meshes and empty hierarchies! Saved to: {prefabPath}");
        }

        // Clean up our temporary working copy from the active open scene hierarchy
        DestroyImmediate(combinedRoot);
    }

    // Helper method to recursively clean up empty objects from the bottom up
    private static void PruneEmptyObjects(Transform current)
    {
        // Clean children first so that if a parent becomes empty, it gets caught on the way up
        for (int i = current.childCount - 1; i >= 0; i--)
        {
            PruneEmptyObjects(current.GetChild(i));
        }

        // Do not delete the root container object itself
        if (current.parent == null) return;

        // An object qualifies for deletion if it has 0 children AND only has a Transform component
        if (current.childCount == 0)
        {
            Component[] components = current.GetComponents<Component>();
            if (components.Length == 1 && components[0] is Transform)
            {
                DestroyImmediate(current.gameObject);
            }
        }
    }
}