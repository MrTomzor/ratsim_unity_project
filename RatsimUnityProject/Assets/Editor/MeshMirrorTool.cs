using UnityEngine;
using UnityEditor;
using System.IO;

public class MeshMirrorTool : EditorWindow
{
    [MenuItem("Tools/Mesh/Duplicate and Flip Normals")]
    public static void DuplicateAndFlipMesh()
    {
        // Get the selected GameObject
        GameObject selectedTarget = Selection.activeGameObject;

        if (selectedTarget == null)
        {
            EditorUtility.DisplayDialog("Error", "Please select a GameObject with a MeshFilter first.", "OK");
            return;
        }

        MeshFilter meshFilter = selectedTarget.GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
        {
            EditorUtility.DisplayDialog("Error", "The selected GameObject does not have a valid MeshFilter or Mesh.", "OK");
            return;
        }

        // 1. Get the source mesh and duplicate it
        Mesh sourceMesh = meshFilter.sharedMesh;
        Mesh flippedMesh = Instantiate(sourceMesh);
        flippedMesh.name = sourceMesh.name + "_Flipped";

        // 2. Flip the Normals
        Vector3[] normals = flippedMesh.normals;
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = -normals[i];
        }
        flippedMesh.normals = normals;

        // 3. Reverse Triangle Winding Order (Stops backface culling from making it invisible)
        for (int m = 0; m < flippedMesh.subMeshCount; m++)
        {
            int[] triangles = flippedMesh.GetTriangles(m);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int temp = triangles[i];
                triangles[i] = triangles[i + 1];
                triangles[i + 1] = temp;
            }
            flippedMesh.SetTriangles(triangles, m);
        }

        // 4. Recalculate bounds and tangents for lighting/built-in shaders
        flippedMesh.RecalculateBounds();
        flippedMesh.RecalculateTangents();

        // 5. Save the mesh into your project files permanently
        string savePath = "Assets/" + flippedMesh.name + ".asset";
        
        // Ensure we don't overwrite blindly if file exists
        savePath = AssetDatabase.GenerateUniqueAssetPath(savePath);
        
        AssetDatabase.CreateAsset(flippedMesh, savePath);
        AssetDatabase.SaveAssets();

        // 6. Create a duplicate GameObject in the scene using the new mesh
        GameObject duplicatedObject = Instantiate(selectedTarget, selectedTarget.transform.parent);
        duplicatedObject.name = selectedTarget.name + "_Flipped";
        duplicatedObject.GetComponent<MeshFilter>().sharedMesh = flippedMesh;

        // If it has a mesh collider, update that too
        MeshCollider meshCollider = duplicatedObject.GetComponent<MeshCollider>();
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = flippedMesh;
        }

        Selection.activeGameObject = duplicatedObject;
        
        EditorUtility.DisplayDialog("Success!", $"Mesh successfully flipped and saved to:\n{savePath}", "Awesome");
    }
}