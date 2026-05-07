using UnityEngine;

[RequireComponent(typeof(SkinnedMeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class AutoBakeCollider : MonoBehaviour
{
    public bool bakeOnAwake = true;

    void Awake()
    {
        if (bakeOnAwake)
        {
            BakeNow();
        }
    }

    public void BakeNow()
    {
        SkinnedMeshRenderer skinnedMesh = GetComponent<SkinnedMeshRenderer>();
        MeshCollider meshCollider = GetComponent<MeshCollider>();

        Mesh bakedMesh = new Mesh();
        bakedMesh.name = gameObject.name + "_BakedCollision";
        
        skinnedMesh.BakeMesh(bakedMesh, true);
        
        meshCollider.sharedMesh = bakedMesh;

        Debug.Log($"[{gameObject.name}] MeshCollider baked.", this);
    }
}