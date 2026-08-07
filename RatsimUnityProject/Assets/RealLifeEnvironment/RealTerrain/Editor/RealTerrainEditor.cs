using UnityEngine;
using UnityEditor;
using System.IO;

namespace RealLifeEnvironment
{
    [CustomEditor(typeof(RealTerrain))]
    public class RealTerrainEditor : Editor
    {
        private bool showOfflineActions = false;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            GUI.enabled = false;
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            GUI.enabled = true;

            DrawPropertiesExcluding(serializedObject,
                "m_Script",
                "roadMaskTexture", "roadMaskMappings",
                "originalCroplandValue", "croplandValueTolerance", "croplandNeighborRadius", "randomizedCroplandValues",
                "distanceTargetBiomeValue", "distanceBiomeValueTolerance", "distanceFromOtherBiomes", "distanceTextureResolution", "distanceTextureFormat",
                "edgeBiomeToChange", "edgeBiomeToChangeTo", "edgeDistanceUnits");

            GUILayout.Space(10);

            showOfflineActions = EditorGUILayout.Foldout(showOfflineActions, "Offline Actions", true, EditorStyles.foldoutHeader);
            if (showOfflineActions)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(serializedObject.FindProperty("roadMaskTexture"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("roadMaskMappings"));

                GUILayout.Space(5);
                if (GUILayout.Button("Bake Road Mask to Biome Texture"))
                {
                    BakeRoadMask((RealTerrain)target);
                }

                GUILayout.Space(10);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("originalCroplandValue"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("croplandValueTolerance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("croplandNeighborRadius"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("randomizedCroplandValues"));

                GUILayout.Space(5);
                if (GUILayout.Button("Randomize Croplands"))
                {
                    RandomizeCroplands((RealTerrain)target);
                }

                GUILayout.Space(10);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("distanceTargetBiomeValue"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("distanceBiomeValueTolerance"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("distanceFromOtherBiomes"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("distanceTextureResolution"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("distanceTextureFormat"));

                GUILayout.Space(5);
                if (GUILayout.Button("Map Distance From Biome"))
                {
                    MapDistanceFromBiome((RealTerrain)target);
                }

                GUILayout.Space(10);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("edgeBiomeToChange"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("edgeBiomeToChangeTo"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("edgeDistanceUnits"));

                GUILayout.Space(5);
                if (GUILayout.Button("Change Biome Edge"))
                {
                    ChangeBiomeEdge((RealTerrain)target);
                }

                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void BakeRoadMask(RealTerrain terrain)
        {
            if (terrain.biomeTexture == null)
            {
                Debug.LogError("RealTerrain: Biome Texture is not assigned.");
                return;
            }

            if (terrain.roadMaskTexture == null)
            {
                Debug.LogError("RealTerrain: Road Mask Texture is not assigned.");
                return;
            }

            if (terrain.roadMaskMappings == null || terrain.roadMaskMappings.Count == 0)
            {
                Debug.LogError("RealTerrain: No Road Mask Mappings defined.");
                return;
            }

            if (!terrain.biomeTexture.isReadable)
            {
                Debug.LogError("RealTerrain: Cannot apply Road Mask because biomeTexture is not readable. Enable Read/Write in its import settings.");
                return;
            }

            if (!terrain.roadMaskTexture.isReadable)
            {
                Debug.LogError("RealTerrain: roadMaskTexture is not readable. Enable Read/Write in its import settings.");
                return;
            }

            int texWidth = terrain.biomeTexture.width;
            int texHeight = terrain.biomeTexture.height;

            if (terrain.roadMaskTexture.width != texWidth || terrain.roadMaskTexture.height != texHeight)
            {
                Debug.LogError($"RealTerrain: roadMaskTexture dimensions ({terrain.roadMaskTexture.width}x{terrain.roadMaskTexture.height}) do not match biomeTexture dimensions ({texWidth}x{texHeight}).");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(terrain.biomeTexture);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("RealTerrain: biomeTexture is not a valid asset on disk.");
                return;
            }

            string extension = Path.GetExtension(assetPath).ToLower();
            
            // Create a copy of the texture if we are dealing with raw formats so we don't accidentally ruin the asset in memory if saving fails.
            // For .asset files, we must modify the original directly since it's a serialized Unity asset.
            Texture2D textureToModify = extension == ".asset" ? terrain.biomeTexture : new Texture2D(texWidth, texHeight, terrain.biomeTexture.format, terrain.biomeTexture.mipmapCount > 1);
            
            if (extension != ".asset")
            {
                textureToModify.SetPixels(terrain.biomeTexture.GetPixels());
            }

            Color[] maskPixels = terrain.roadMaskTexture.GetPixels();
            Color[] biomePixels = textureToModify.GetPixels();
            bool maskModified = false;

            for (int i = 0; i < maskPixels.Length; i++)
            {
                Color maskCol = maskPixels[i];
                
                foreach (var mapping in terrain.roadMaskMappings)
                {
                    if (Mathf.Abs(maskCol.r - mapping.maskColor.r) <= mapping.colorTolerance &&
                        Mathf.Abs(maskCol.g - mapping.maskColor.g) <= mapping.colorTolerance &&
                        Mathf.Abs(maskCol.b - mapping.maskColor.b) <= mapping.colorTolerance)
                    {
                        biomePixels[i] = new Color(mapping.targetBiomeValue / 255f, 0f, 0f, 0f);
                        maskModified = true;
                        break;
                    }
                }
            }

            if (maskModified)
            {
                textureToModify.SetPixels(biomePixels);
                textureToModify.Apply();

                if (extension == ".asset")
                {
                    EditorUtility.SetDirty(terrain.biomeTexture);
                    AssetDatabase.SaveAssetIfDirty(terrain.biomeTexture);
                    // Fallback for older Unity versions if SaveAssetIfDirty doesn't exist or doesn't work:
                    AssetDatabase.SaveAssets(); 
                    Debug.Log($"RealTerrain: Successfully baked road mask to {assetPath}");
                }
                else
                {
                    byte[] bytes = null;

                    if (extension == ".exr")
                    {
                        bytes = textureToModify.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                    }
                    else if (extension == ".png")
                    {
                        bytes = textureToModify.EncodeToPNG();
                    }
                    else if (extension == ".jpg" || extension == ".jpeg")
                    {
                        bytes = textureToModify.EncodeToJPG();
                    }
                    else if (extension == ".tga")
                    {
                        bytes = textureToModify.EncodeToTGA();
                    }
                    else
                    {
                        Debug.LogError($"RealTerrain: Unsupported texture format for saving ({extension}). Please use .asset, EXR, PNG, JPG, or TGA.");
                        DestroyImmediate(textureToModify);
                        return;
                    }

                    if (bytes != null)
                    {
                        File.WriteAllBytes(assetPath, bytes);
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                        Debug.Log($"RealTerrain: Successfully baked road mask to {assetPath}");
                    }
                }
            }
            else
            {
                Debug.Log("RealTerrain: No matching colors found in road mask. Biome texture was not modified.");
            }

            if (extension != ".asset")
            {
                DestroyImmediate(textureToModify);
            }
        }

        private void RandomizeCroplands(RealTerrain terrain)
        {
            if (terrain.biomeTexture == null)
            {
                Debug.LogError("RealTerrain: Biome Texture is not assigned.");
                return;
            }

            if (!terrain.biomeTexture.isReadable)
            {
                Debug.LogError("RealTerrain: Cannot apply modifications because biomeTexture is not readable. Enable Read/Write in its import settings.");
                return;
            }

            if (terrain.randomizedCroplandValues == null || terrain.randomizedCroplandValues.Count == 0)
            {
                Debug.LogError("RealTerrain: No randomized cropland values defined.");
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(terrain.biomeTexture);
            if (string.IsNullOrEmpty(assetPath))
            {
                Debug.LogError("RealTerrain: biomeTexture is not a valid asset on disk.");
                return;
            }

            int texWidth = terrain.biomeTexture.width;
            int texHeight = terrain.biomeTexture.height;
            string extension = Path.GetExtension(assetPath).ToLower();
            
            Texture2D textureToModify = extension == ".asset" ? terrain.biomeTexture : new Texture2D(texWidth, texHeight, terrain.biomeTexture.format, terrain.biomeTexture.mipmapCount > 1);
            
            if (extension != ".asset")
            {
                textureToModify.SetPixels(terrain.biomeTexture.GetPixels());
            }

            Color[] biomePixels = textureToModify.GetPixels();
            
            int[] islandMap = new int[biomePixels.Length];
            System.Collections.Generic.List<System.Collections.Generic.List<int>> islands = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
            System.Collections.Generic.List<System.Collections.Generic.List<int>> islandBoundaries = new System.Collections.Generic.List<System.Collections.Generic.List<int>>();
            
            System.Collections.Generic.Queue<int> queue = new System.Collections.Generic.Queue<int>();

            for (int i = 0; i < biomePixels.Length; i++)
            {
                if (islandMap[i] == 0 && Mathf.Abs(biomePixels[i].r * 255f - terrain.originalCroplandValue) <= terrain.croplandValueTolerance)
                {
                    int islandId = islands.Count + 1; // 1-indexed
                    var islandPixels = new System.Collections.Generic.List<int>();
                    var boundaryPixels = new System.Collections.Generic.List<int>();

                    queue.Clear();
                    queue.Enqueue(i);
                    islandMap[i] = islandId;

                    while (queue.Count > 0)
                    {
                        int curr = queue.Dequeue();
                        islandPixels.Add(curr);

                        int cx = curr % texWidth;
                        int cy = curr / texWidth;
                        bool isBoundary = false;

                        // Check 4 neighbors
                        if (cy < texHeight - 1) 
                        {
                            int n = curr + texWidth;
                            if (Mathf.Abs(biomePixels[n].r * 255f - terrain.originalCroplandValue) <= terrain.croplandValueTolerance) 
                            {
                                if (islandMap[n] == 0) {
                                    islandMap[n] = islandId;
                                    queue.Enqueue(n);
                                }
                            } else isBoundary = true;
                        } else isBoundary = true;
                        
                        if (cy > 0) 
                        {
                            int n = curr - texWidth;
                            if (Mathf.Abs(biomePixels[n].r * 255f - terrain.originalCroplandValue) <= terrain.croplandValueTolerance) 
                            {
                                if (islandMap[n] == 0) {
                                    islandMap[n] = islandId;
                                    queue.Enqueue(n);
                                }
                            } else isBoundary = true;
                        } else isBoundary = true;
                        
                        if (cx > 0) 
                        {
                            int n = curr - 1;
                            if (Mathf.Abs(biomePixels[n].r * 255f - terrain.originalCroplandValue) <= terrain.croplandValueTolerance) 
                            {
                                if (islandMap[n] == 0) {
                                    islandMap[n] = islandId;
                                    queue.Enqueue(n);
                                }
                            } else isBoundary = true;
                        } else isBoundary = true;
                        
                        if (cx < texWidth - 1) 
                        {
                            int n = curr + 1;
                            if (Mathf.Abs(biomePixels[n].r * 255f - terrain.originalCroplandValue) <= terrain.croplandValueTolerance) 
                            {
                                if (islandMap[n] == 0) {
                                    islandMap[n] = islandId;
                                    queue.Enqueue(n);
                                }
                            } else isBoundary = true;
                        } else isBoundary = true;

                        if (isBoundary) boundaryPixels.Add(curr);
                    }
                    
                    islands.Add(islandPixels);
                    islandBoundaries.Add(boundaryPixels);
                }
            }

            int islandsFound = islands.Count;
            if (islandsFound > 0)
            {
                // Find adjacency using boundary pixels
                System.Collections.Generic.HashSet<int>[] adjacencyList = new System.Collections.Generic.HashSet<int>[islandsFound];
                for(int i = 0; i < islandsFound; i++) adjacencyList[i] = new System.Collections.Generic.HashSet<int>();

                int radius = terrain.croplandNeighborRadius;
                
                for (int i = 0; i < islandsFound; i++)
                {
                    foreach (int p in islandBoundaries[i])
                    {
                        int px = p % texWidth;
                        int py = p / texWidth;

                        int startX = Mathf.Max(0, px - radius);
                        int endX = Mathf.Min(texWidth - 1, px + radius);
                        int startY = Mathf.Max(0, py - radius);
                        int endY = Mathf.Min(texHeight - 1, py + radius);

                        for (int ny = startY; ny <= endY; ny++)
                        {
                            for (int nx = startX; nx <= endX; nx++)
                            {
                                int nIdx = ny * texWidth + nx;
                                int otherIsland = islandMap[nIdx];
                                if (otherIsland > 0 && otherIsland != (i + 1))
                                {
                                    adjacencyList[i].Add(otherIsland - 1);
                                    adjacencyList[otherIsland - 1].Add(i);
                                }
                            }
                        }
                    }
                }

                // Graph Coloring
                float[] assignedColors = new float[islandsFound];
                for (int i = 0; i < islandsFound; i++) assignedColors[i] = -1f;

                for (int i = 0; i < islandsFound; i++)
                {
                    System.Collections.Generic.HashSet<float> neighborColors = new System.Collections.Generic.HashSet<float>();
                    foreach (int neighbor in adjacencyList[i])
                    {
                        if (assignedColors[neighbor] != -1f)
                        {
                            neighborColors.Add(assignedColors[neighbor]);
                        }
                    }

                    System.Collections.Generic.List<float> availableColors = new System.Collections.Generic.List<float>();
                    foreach (float c in terrain.randomizedCroplandValues)
                    {
                        if (!neighborColors.Contains(c))
                        {
                            availableColors.Add(c);
                        }
                    }

                    if (availableColors.Count > 0)
                    {
                        assignedColors[i] = availableColors[Random.Range(0, availableColors.Count)];
                    }
                    else
                    {
                        // Fallback if we run out of colors (e.g. more than 4 neighbors touching mutually)
                        assignedColors[i] = terrain.randomizedCroplandValues[Random.Range(0, terrain.randomizedCroplandValues.Count)];
                    }
                }

                // Apply colors
                for (int i = 0; i < islandsFound; i++)
                {
                    Color c = new Color(assignedColors[i] / 255f, 0f, 0f, 0f);
                    foreach (int p in islands[i])
                    {
                        biomePixels[p] = c;
                    }
                }

                textureToModify.SetPixels(biomePixels);
                textureToModify.Apply();

                if (extension == ".asset")
                {
                    EditorUtility.SetDirty(terrain.biomeTexture);
                    AssetDatabase.SaveAssetIfDirty(terrain.biomeTexture);
                    AssetDatabase.SaveAssets(); 
                    Debug.Log($"RealTerrain: Successfully randomized {islandsFound} cropland islands to {assetPath}");
                }
                else
                {
                    byte[] bytes = null;

                    if (extension == ".exr") bytes = textureToModify.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                    else if (extension == ".png") bytes = textureToModify.EncodeToPNG();
                    else if (extension == ".jpg" || extension == ".jpeg") bytes = textureToModify.EncodeToJPG();
                    else if (extension == ".tga") bytes = textureToModify.EncodeToTGA();

                    if (bytes != null)
                    {
                        File.WriteAllBytes(assetPath, bytes);
                        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                        Debug.Log($"RealTerrain: Successfully randomized {islandsFound} cropland islands to {assetPath}");
                    }
                    else
                    {
                        Debug.LogError($"RealTerrain: Unsupported texture format for saving ({extension}). Please use .asset, EXR, PNG, JPG, or TGA.");
                    }
                }
            }
            else
            {
                Debug.Log("RealTerrain: No croplands found in the biome texture with the given original value.");
            }

            if (extension != ".asset")
            {
                DestroyImmediate(textureToModify);
            }
        }

        private void MapDistanceFromBiome(RealTerrain terrain)
        {
            if (terrain.biomeTexture == null)
            {
                Debug.LogError("RealTerrain: Biome Texture is not assigned.");
                return;
            }

            string path = EditorUtility.SaveFilePanelInProject("Save Distance Map", terrain.biomeTexture.name + "_DistanceMap", "exr", "Save distance map as EXR");
            if (string.IsNullOrEmpty(path)) return;

            if (!terrain.biomeTexture.isReadable)
            {
                Debug.LogError("RealTerrain: Cannot read biomeTexture because it is not readable. Enable Read/Write in its import settings.");
                return;
            }

            int bWidth = terrain.biomeTexture.width;
            int bHeight = terrain.biomeTexture.height;
            Color[] biomePixels = terrain.biomeTexture.GetPixels();

            bool[] conditionMap = new bool[bWidth * bHeight];
            for (int y = 0; y < bHeight; y++)
            {
                for (int x = 0; x < bWidth; x++)
                {
                    Color c = biomePixels[y * bWidth + x];
                    bool isTargetBiome = Mathf.Abs(c.r * 255f - terrain.distanceTargetBiomeValue) <= terrain.distanceBiomeValueTolerance;
                    conditionMap[y * bWidth + x] = terrain.distanceFromOtherBiomes ? !isTargetBiome : isTargetBiome;
                }
            }

            System.Collections.Generic.List<Vector2> targetPoints = new System.Collections.Generic.List<Vector2>();
            for (int y = 0; y < bHeight; y++)
            {
                for (int x = 0; x < bWidth; x++)
                {
                    if (conditionMap[y * bWidth + x])
                    {
                        bool isBoundary = false;
                        if (x > 0 && !conditionMap[y * bWidth + x - 1]) isBoundary = true;
                        else if (x < bWidth - 1 && !conditionMap[y * bWidth + x + 1]) isBoundary = true;
                        else if (y > 0 && !conditionMap[(y - 1) * bWidth + x]) isBoundary = true;
                        else if (y < bHeight - 1 && !conditionMap[(y + 1) * bWidth + x]) isBoundary = true;
                        else if (x == 0 || x == bWidth - 1 || y == 0 || y == bHeight - 1) isBoundary = true;

                        if (isBoundary)
                        {
                            float pU = (x + 0.5f) / bWidth;
                            float pV = (y + 0.5f) / bHeight;
                            float worldX = (pU - 0.5f) * terrain.sizeX + terrain.center.x;
                            float worldZ = (pV - 0.5f) * terrain.sizeZ + terrain.center.y;
                            targetPoints.Add(new Vector2(worldX, worldZ));
                        }
                    }
                }
            }

            if (targetPoints.Count == 0)
            {
                Debug.LogWarning("RealTerrain: No boundary pixels found matching the conditions.");
                // We'll continue anyway, everything will just be max distance
            }

            int outRes = terrain.distanceTextureResolution;
            TextureFormat format = terrain.distanceTextureFormat == DistanceTextureFormat.RHalf ? TextureFormat.RHalf : TextureFormat.RFloat;
            Texture2D distanceTex = new Texture2D(outRes, outRes, format, false, true);

            float minX = terrain.center.x - terrain.sizeX * 0.5f;
            float minZ = terrain.center.y - terrain.sizeZ * 0.5f;

            int gridSize = 100;
            float cellWidth = terrain.sizeX / gridSize;
            float cellHeight = terrain.sizeZ / gridSize;

            System.Collections.Generic.List<Vector2>[,] grid = new System.Collections.Generic.List<Vector2>[gridSize, gridSize];
            for (int i = 0; i < gridSize; i++) 
                for (int j = 0; j < gridSize; j++) 
                    grid[i, j] = new System.Collections.Generic.List<Vector2>();

            foreach (var pt in targetPoints)
            {
                int gx = Mathf.Clamp(Mathf.FloorToInt((pt.x - minX) / cellWidth), 0, gridSize - 1);
                int gz = Mathf.Clamp(Mathf.FloorToInt((pt.y - minZ) / cellHeight), 0, gridSize - 1);
                grid[gx, gz].Add(pt);
            }

            Color[] outPixels = new Color[outRes * outRes];
            float maxPossibleDistance = Mathf.Sqrt(terrain.sizeX * terrain.sizeX + terrain.sizeZ * terrain.sizeZ);

            for (int y = 0; y < outRes; y++)
            {
                if (y % 32 == 0)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Generating Distance Map", $"Row {y}/{outRes}", (float)y / outRes))
                    {
                        EditorUtility.ClearProgressBar();
                        return;
                    }
                }

                for (int x = 0; x < outRes; x++)
                {
                    float pU = (x + 0.5f) / outRes;
                    float pV = (y + 0.5f) / outRes;
                    float worldX = (pU - 0.5f) * terrain.sizeX + terrain.center.x;
                    float worldZ = (pV - 0.5f) * terrain.sizeZ + terrain.center.y;
                    
                    float bU = (worldX - terrain.center.x) / terrain.sizeX + 0.5f;
                    float bV = (worldZ - terrain.center.y) / terrain.sizeZ + 0.5f;
                    int bx = Mathf.Clamp(Mathf.FloorToInt(bU * bWidth), 0, bWidth - 1);
                    int by = Mathf.Clamp(Mathf.FloorToInt(bV * bHeight), 0, bHeight - 1);
                    
                    // If the point is already in a condition pixel, distance is 0
                    if (conditionMap[by * bWidth + bx])
                    {
                        outPixels[y * outRes + x] = new Color(0, 0, 0, 0);
                        continue;
                    }

                    Vector2 p = new Vector2(worldX, worldZ);

                    int cx = Mathf.Clamp(Mathf.FloorToInt((worldX - minX) / cellWidth), 0, gridSize - 1);
                    int cz = Mathf.Clamp(Mathf.FloorToInt((worldZ - minZ) / cellHeight), 0, gridSize - 1);

                    float minDistSq = float.MaxValue;
                    int searchRadius = 0;
                    bool found = false;

                    while (!found && searchRadius < gridSize)
                    {
                        int minGx = Mathf.Max(0, cx - searchRadius);
                        int maxGx = Mathf.Min(gridSize - 1, cx + searchRadius);
                        int minGz = Mathf.Max(0, cz - searchRadius);
                        int maxGz = Mathf.Min(gridSize - 1, cz + searchRadius);

                        if (minDistSq != float.MaxValue)
                        {
                            float distToBoundary = Mathf.Max(0, searchRadius - 1) * Mathf.Min(cellWidth, cellHeight);
                            if (distToBoundary * distToBoundary >= minDistSq)
                            {
                                found = true;
                                break;
                            }
                        }

                        for (int gx = minGx; gx <= maxGx; gx++)
                        {
                            for (int gz = minGz; gz <= maxGz; gz++)
                            {
                                if (gx == cx - searchRadius || gx == cx + searchRadius || gz == cz - searchRadius || gz == cz + searchRadius || searchRadius == 0)
                                {
                                    var cell = grid[gx, gz];
                                    foreach (var targetPt in cell)
                                    {
                                        float distSq = (p.x - targetPt.x) * (p.x - targetPt.x) + (p.y - targetPt.y) * (p.y - targetPt.y);
                                        if (distSq < minDistSq) minDistSq = distSq;
                                    }
                                }
                            }
                        }
                        searchRadius++;
                    }

                    float minDist = minDistSq == float.MaxValue ? maxPossibleDistance : Mathf.Sqrt(minDistSq);
                    outPixels[y * outRes + x] = new Color(minDist, 0, 0, 0);
                }
            }

            EditorUtility.ClearProgressBar();

            distanceTex.SetPixels(outPixels);
            distanceTex.Apply();

            byte[] bytes = distanceTex.EncodeToEXR(terrain.distanceTextureFormat == DistanceTextureFormat.RHalf ? Texture2D.EXRFlags.None : Texture2D.EXRFlags.OutputAsFloat);
            File.WriteAllBytes(path, bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = false;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            DestroyImmediate(distanceTex);
            Debug.Log("RealTerrain: Successfully saved distance map to " + path);
        }

        private void ChangeBiomeEdge(RealTerrain terrain)
        {
            if (terrain.biomeTexture == null)
            {
                Debug.LogError("RealTerrain: Biome Texture is not assigned.");
                return;
            }

            if (terrain.biomeTexture.format != TextureFormat.R8)
            {
                Debug.LogError("RealTerrain: Biome Texture must be in R8 format for this fast offline action.");
                return;
            }

            string originalPath = AssetDatabase.GetAssetPath(terrain.biomeTexture);
            if (string.IsNullOrEmpty(originalPath))
            {
                Debug.LogError("RealTerrain: biomeTexture is not a valid asset on disk.");
                return;
            }

            int texWidth = terrain.biomeTexture.width;
            int texHeight = terrain.biomeTexture.height;

            byte[] originalData = terrain.biomeTexture.GetRawTextureData();
            byte[] newData = new byte[originalData.Length];
            System.Array.Copy(originalData, newData, originalData.Length);

            byte targetByte = (byte)Mathf.Clamp(Mathf.RoundToInt(terrain.edgeBiomeToChange), 0, 255);
            byte newByte = (byte)Mathf.Clamp(Mathf.RoundToInt(terrain.edgeBiomeToChangeTo), 0, 255);

            System.Collections.Generic.List<int> boundaryIndices = new System.Collections.Generic.List<int>();

            for (int y = 0; y < texHeight; y++)
            {
                for (int x = 0; x < texWidth; x++)
                {
                    int idx = y * texWidth + x;
                    if (originalData[idx] == targetByte)
                    {
                        bool isBoundary = false;
                        if (x > 0 && originalData[idx - 1] != targetByte) isBoundary = true;
                        else if (x < texWidth - 1 && originalData[idx + 1] != targetByte) isBoundary = true;
                        else if (y > 0 && originalData[idx - texWidth] != targetByte) isBoundary = true;
                        else if (y < texHeight - 1 && originalData[idx + texWidth] != targetByte) isBoundary = true;
                        
                        if (isBoundary) boundaryIndices.Add(idx);
                    }
                }
            }

            if (boundaryIndices.Count == 0)
            {
                Debug.LogWarning("RealTerrain: No boundary found for the specified biome.");
                return;
            }

            float unitPerPixelX = terrain.sizeX / texWidth;
            float unitPerPixelZ = terrain.sizeZ / texHeight;
            int radiusX = Mathf.CeilToInt(terrain.edgeDistanceUnits / unitPerPixelX);
            int radiusZ = Mathf.CeilToInt(terrain.edgeDistanceUnits / unitPerPixelZ);
            float maxDistSq = terrain.edgeDistanceUnits * terrain.edgeDistanceUnits;

            System.Collections.Generic.List<Vector2Int> circleOffsets = new System.Collections.Generic.List<Vector2Int>();
            for (int y = -radiusZ; y <= radiusZ; y++)
            {
                for (int x = -radiusX; x <= radiusX; x++)
                {
                    float dx = x * unitPerPixelX;
                    float dz = y * unitPerPixelZ;
                    if (dx * dx + dz * dz <= maxDistSq)
                    {
                        circleOffsets.Add(new Vector2Int(x, y));
                    }
                }
            }

            int totalBoundaries = boundaryIndices.Count;
            for (int i = 0; i < totalBoundaries; i++)
            {
                if (i % 5000 == 0)
                {
                    if (EditorUtility.DisplayCancelableProgressBar("Changing Biome Edge", $"Processing boundaries {i}/{totalBoundaries}", (float)i / totalBoundaries))
                    {
                        EditorUtility.ClearProgressBar();
                        return;
                    }
                }

                int bIdx = boundaryIndices[i];
                int bx = bIdx % texWidth;
                int by = bIdx / texWidth;

                foreach (var offset in circleOffsets)
                {
                    int nx = bx + offset.x;
                    int ny = by + offset.y;

                    if (nx >= 0 && nx < texWidth && ny >= 0 && ny < texHeight)
                    {
                        int nIdx = ny * texWidth + nx;
                        if (originalData[nIdx] == targetByte)
                        {
                            newData[nIdx] = newByte;
                        }
                    }
                }
            }

            EditorUtility.ClearProgressBar();

            string dir = Path.GetDirectoryName(originalPath);
            string ext = Path.GetExtension(originalPath);
            string newName = terrain.biomeTexture.name + "_EdgeModified" + ext;
            string newPath = Path.Combine(dir, newName).Replace("\\", "/");

            if (ext.ToLower() == ".asset")
            {
                Texture2D newTex = new Texture2D(texWidth, texHeight, TextureFormat.R8, false, true);
                newTex.LoadRawTextureData(newData);
                newTex.Apply();
                AssetDatabase.CreateAsset(newTex, newPath);
                AssetDatabase.SaveAssets();
            }
            else
            {
                Texture2D newTex = new Texture2D(texWidth, texHeight, TextureFormat.R8, false, true);
                newTex.LoadRawTextureData(newData);
                newTex.Apply();
                byte[] bytes = null;
                if (ext.ToLower() == ".png") bytes = newTex.EncodeToPNG();
                else if (ext.ToLower() == ".exr") bytes = newTex.EncodeToEXR(Texture2D.EXRFlags.OutputAsFloat);
                else if (ext.ToLower() == ".jpg" || ext.ToLower() == ".jpeg") bytes = newTex.EncodeToJPG();
                else if (ext.ToLower() == ".tga") bytes = newTex.EncodeToTGA();

                if (bytes != null)
                {
                    File.WriteAllBytes(newPath, bytes);
                    AssetDatabase.ImportAsset(newPath, ImportAssetOptions.ForceUpdate);
                    
                    TextureImporter edgeImporter = AssetImporter.GetAtPath(newPath) as TextureImporter;
                    if (edgeImporter != null)
                    {
                        edgeImporter.textureType = TextureImporterType.Default;
                        edgeImporter.sRGBTexture = false;
                        edgeImporter.mipmapEnabled = false;
                        edgeImporter.SaveAndReimport();
                    }
                }
                else
                {
                    Debug.LogError("RealTerrain: Unsupported texture format for saving.");
                }
                DestroyImmediate(newTex);
            }

            Debug.Log("RealTerrain: Successfully created modified biome texture at " + newPath);
        }
    }
}
