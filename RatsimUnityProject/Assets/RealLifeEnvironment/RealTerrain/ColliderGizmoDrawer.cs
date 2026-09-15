using UnityEngine;

namespace RealLifeEnvironment
{
    public class ColliderGizmoDrawer : MonoBehaviour
    {
        public Vector3[] vertices;
        public int resolution;

        void OnDrawGizmos()
        {
            if (vertices == null || vertices.Length == 0 || resolution <= 0) return;

            Gizmos.color = Color.green;
            Gizmos.matrix = transform.localToWorldMatrix;
            
            int resPlusOne = resolution + 1;

            // Draw horizontal and vertical lines for the grid
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int i = z * resPlusOne + x;
                    
                    Vector3 current = vertices[i];
                    Vector3 right = vertices[i + 1];
                    Vector3 up = vertices[i + resPlusOne];
                    
                    Gizmos.DrawLine(current, right);
                    Gizmos.DrawLine(current, up);
                }
            }
            
            // Draw the last right edge
            for (int z = 0; z < resolution; z++)
            {
                int i = z * resPlusOne + resolution;
                Gizmos.DrawLine(vertices[i], vertices[i + resPlusOne]);
            }
            
            // Draw the last top edge
            for (int x = 0; x < resolution; x++)
            {
                int i = resolution * resPlusOne + x;
                Gizmos.DrawLine(vertices[i], vertices[i + 1]);
            }
        }
    }
}
