using UnityEngine;

namespace RealLifeEnvironment
{
    public class BiomeModifier : MonoBehaviour
    {
        [Tooltip("The XZ radius around this object to override the biome.")]
        public float radius = 10f;

        [Tooltip("The biome value to set (e.g., 50 for Built-up).")]
        public float biomeValue = 50f;
        
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
            
            // Draw a circle on the XZ plane to represent the radius
            int segments = 32;
            float angle = 0f;
            float step = (Mathf.PI * 2f) / segments;
            Vector3 pos = transform.position;
            
            Vector3 lastPoint = pos + new Vector3(Mathf.Cos(0) * radius, 0, Mathf.Sin(0) * radius);
            for (int i = 1; i <= segments; i++)
            {
                angle += step;
                Vector3 nextPoint = pos + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(lastPoint, nextPoint);
                lastPoint = nextPoint;
            }
        }
    }
}
