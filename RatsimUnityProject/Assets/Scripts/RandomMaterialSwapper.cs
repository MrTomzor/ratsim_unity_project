using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Swaps the object's material with a random one selected from a specified folder at spawntime.
/// Designed for objects with exactly one material.
/// Supports both editor-time pre-serialization (for standalone builds) and dynamic runtime loading (if placed in Resources).
/// </summary>
[RequireComponent(typeof(Renderer))]
public class RandomMaterialSwapper : MonoBehaviour
{
    [Header("Folder Settings")]
    [SerializeField]
    [Tooltip("The root folder containing the swappable material directories.")]
    private string rootFolder = "Assets/Materials/swappable";

    [SerializeField]
    [Tooltip("The subfolder (under the root folder) from which to load the materials.")]
    private string subFolder = "";

    [Header("Material Cache")]
    [SerializeField]
    [Tooltip("Materials found in the target folder. Auto-populated in the Editor, but can be adjusted manually.")]
    private List<Material> materials = new List<Material>();

    [Header("Settings")]
    [SerializeField]
    [Tooltip("If true, the material swap will occur in Awake. Otherwise, it will occur in Start.")]
    private bool swapInAwake = true;

    private void Awake()
    {
        if (swapInAwake)
        {
            ExecuteSwap();
        }
    }

    private void Start()
    {
        if (!swapInAwake)
        {
            ExecuteSwap();
        }
    }

    /// <summary>
    /// Executes the material swap with a random material from the loaded list.
    /// </summary>
    public void ExecuteSwap()
    {
        // Try to load materials dynamically if the list is empty (e.g. dynamic spawning in Editor or Resources fallback)
        if (materials == null || materials.Count == 0)
        {
            LoadMaterials();
        }

        if (materials == null || materials.Count == 0)
        {
            Debug.LogWarning($"[RandomMaterialSwapper] No materials available to swap on {gameObject.name}. " +
                             $"Ensure materials are located in the path: {GetFullPath()} and loaded.", this);
            return;
        }

        Renderer renderer = GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogError($"[RandomMaterialSwapper] Renderer component not found on {gameObject.name}.", this);
            return;
        }

        // Select a random material from the cache
        Material randomMat = materials[Random.Range(0, materials.Count)];
        if (randomMat != null)
        {
            renderer.sharedMaterial = randomMat;
        }
        else
        {
            Debug.LogWarning($"[RandomMaterialSwapper] Selected material is null on {gameObject.name}.", this);
        }
    }

    /// <summary>
    /// Constructs the full path of the target folder.
    /// </summary>
    public string GetFullPath()
    {
        string cleanRoot = rootFolder.Replace('\\', '/').TrimEnd('/');
        string cleanSub = subFolder.Replace('\\', '/').Trim('/');

        if (string.IsNullOrEmpty(cleanSub))
        {
            return cleanRoot;
        }
        return $"{cleanRoot}/{cleanSub}";
    }

    /// <summary>
    /// Loads materials from the specified folder.
    /// Works via AssetDatabase in the Editor, and falls back to Resources.LoadAll if path is inside a Resources directory.
    /// </summary>
    [ContextMenu("Load Materials")]
    public void LoadMaterials()
    {
        materials.Clear();

        string fullPath = GetFullPath();

        // 1. Editor-time loading using AssetDatabase
#if UNITY_EDITOR
        if (AssetDatabase.IsValidFolder(fullPath))
        {
            string[] guids = AssetDatabase.FindAssets("t:Material", new[] { fullPath });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
                if (mat != null && !materials.Contains(mat))
                {
                    materials.Add(mat);
                }
            }
            
            // Mark object dirty if we are in the editor and not playing to save changes
            if (!Application.isPlaying)
            {
                EditorUtility.SetDirty(this);
            }
            return;
        }
#endif

        // 2. Runtime fallback: If inside a Resources folder, load using Resources.LoadAll
        string resourcesPath = GetResourcesRelativePath(fullPath);
        if (!string.IsNullOrEmpty(resourcesPath))
        {
            Material[] loadedMats = Resources.LoadAll<Material>(resourcesPath);
            if (loadedMats != null && loadedMats.Length > 0)
            {
                materials.AddRange(loadedMats);
            }
        }
    }

    private string GetResourcesRelativePath(string fullPath)
    {
        string normalizedPath = fullPath.Replace('\\', '/');
        
        // Match "/Resources/"
        int resourcesIndex = normalizedPath.IndexOf("/Resources/", System.StringComparison.OrdinalIgnoreCase);
        if (resourcesIndex != -1)
        {
            return normalizedPath.Substring(resourcesIndex + 11);
        }
        
        // Match "Assets/Resources/"
        if (normalizedPath.StartsWith("Assets/Resources/", System.StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath.Substring(17);
        }
        
        // Match starting with "Resources/"
        if (normalizedPath.StartsWith("Resources/", System.StringComparison.OrdinalIgnoreCase))
        {
            return normalizedPath.Substring(10);
        }

        return null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Don't run AssetDatabase loading during Play Mode to avoid overhead/errors
        if (EditorApplication.isPlaying) return;

        // Auto-load materials when fields change in the Editor
        LoadMaterials();
    }
#endif
}
