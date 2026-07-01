using UnityEngine;
using System.Collections.Generic;

namespace WorldGen.RiverGen
{
    public class LocalMinimumOptimizer : MonoBehaviour
    {
        [Header("Optimization Parameters (Adam)")]
        public bool runOnStartXZChange = true;
        public Vector2 startXZ = new Vector2(446.8f, 140.5f);
        public float learningRate = 63.5f;
        [Tooltip("Multiplies the learning rate by this value every step (e.g., 0.99 for decay, 1.0 for no decay)")]
        public float learningRateDecay = 0.99f;
        public int noiseIterMin = 2;

        [SerializeField, HideInInspector]
        private Vector2 _previousStartXZ;

        private void OnValidate()
        {
            if (runOnStartXZChange && startXZ != _previousStartXZ)
            {
                _previousStartXZ = startXZ;
                Optimize(false);
            }
            else
            {
                _previousStartXZ = startXZ;
            }
        }
        public int maxIterations = 1000;
        public float stopGradientMagnitude = 0.001f;
        [Space]
        public float beta1 = 0.94f;
        public float beta2 = 0.999f;
        public float epsilon = 0.1f;

        [Header("Gizmo Settings")]
        public Color pathColor = Color.blue;
        public float nodeRadius = 0.02f;

        [SerializeField, HideInInspector]
        private List<Vector3> _path = new List<Vector3>();

        public void Optimize(bool logResult = true)
        {
            _path.Clear();
            Vector2 currentXZ = startXZ;

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            List<string> stageLogs = new List<string>();

            for (int fbmIter = noiseIterMin; fbmIter <= ClipmapTerrain.TerrainNoise.FbmIterations; fbmIter++)
            {
                Vector2 m = Vector2.zero;
                Vector2 v = Vector2.zero;
                float currentLR = learningRate;

                int stepsTaken = 0;
                System.Diagnostics.Stopwatch stageSw = new System.Diagnostics.Stopwatch();
                stageSw.Start();

                for (int i = 1; i <= maxIterations; i++)
                {
                    stepsTaken++;
                    float height = ClipmapTerrain.TerrainNoise.GetTerrainHeightOriginal(new Vector2(currentXZ.x, currentXZ.y), fbmIter);
                    _path.Add(new Vector3(currentXZ.x, height, currentXZ.y));

                    Vector2 grad = ClipmapTerrain.TerrainNoise.GetNumericalGrad(currentXZ.x, currentXZ.y, fbmIter);
                    
                    if (grad.magnitude < stopGradientMagnitude)
                        break;

                    // Adam Optimization Update
                    m = beta1 * m + (1f - beta1) * grad;
                    v = beta2 * v + (1f - beta2) * new Vector2(grad.x * grad.x, grad.y * grad.y);

                    Vector2 mHat = m / (1f - Mathf.Pow(beta1, i));
                    Vector2 vHat = v / (1f - Mathf.Pow(beta2, i));

                    currentXZ.x -= currentLR * mHat.x / (Mathf.Sqrt(vHat.x) + epsilon);
                    currentXZ.y -= currentLR * mHat.y / (Mathf.Sqrt(vHat.y) + epsilon);

                    currentLR *= learningRateDecay;
                }

                stageSw.Stop();
                stageLogs.Add($"[FBM {fbmIter}]: {stepsTaken} iters, {stageSw.ElapsedMilliseconds}ms");
            }
            
            sw.Stop();
            if (logResult && _path.Count > 0)
            {
                Debug.Log($"Optimization finished in {sw.ElapsedMilliseconds}ms.\nStages:\n" + string.Join("\n", stageLogs) + $"\nFinal Position: {_path[_path.Count - 1]}");
            }
        }

        /// <summary>
        /// Optimizes a given XZ point using the component's default parameters without tracking the path.
        /// </summary>
        public Vector2 GetOptimizedXZ(Vector2 inputXZ)
        {
            return GetOptimizedXZ(inputXZ, learningRate, learningRateDecay, maxIterations, stopGradientMagnitude, noiseIterMin, beta1, beta2, epsilon);
        }

        /// <summary>
        /// Optimizes a given XZ point using explicitly provided Adam parameters.
        /// </summary>
        public static Vector2 GetOptimizedXZ(Vector2 inputXZ, float lr, float lrDecay, int maxIter, float stopGrad, int iterMin, float b1, float b2, float eps)
        {
            Vector2 currentXZ = inputXZ;

            for (int fbmIter = iterMin; fbmIter <= ClipmapTerrain.TerrainNoise.FbmIterations; fbmIter++)
            {
                Vector2 m = Vector2.zero;
                Vector2 v = Vector2.zero;
                float currentLR = lr;

                for (int i = 1; i <= maxIter; i++)
                {
                    Vector2 grad = ClipmapTerrain.TerrainNoise.GetNumericalGrad(currentXZ.x, currentXZ.y, fbmIter);
                    
                    if (grad.magnitude < stopGrad)
                        break;

                    // Adam Optimization Update
                    m = b1 * m + (1f - b1) * grad;
                    v = b2 * v + (1f - b2) * new Vector2(grad.x * grad.x, grad.y * grad.y);

                    Vector2 mHat = m / (1f - Mathf.Pow(b1, i));
                    Vector2 vHat = v / (1f - Mathf.Pow(b2, i));

                    currentXZ.x -= currentLR * mHat.x / (Mathf.Sqrt(vHat.x) + eps);
                    currentXZ.y -= currentLR * mHat.y / (Mathf.Sqrt(vHat.y) + eps);

                    currentLR *= lrDecay;
                }
            }

            return currentXZ;
        }

        private void OnDrawGizmos()
        {
            if (_path == null || _path.Count == 0) return;

            Gizmos.color = pathColor;
            for (int i = 0; i < _path.Count; i++)
            {
                Gizmos.DrawSphere(_path[i], nodeRadius);
                if (i > 0)
                {
                    Gizmos.DrawLine(_path[i - 1], _path[i]);
                }
            }
        }
    }
}

#if UNITY_EDITOR
namespace WorldGen.RiverGen
{
    using UnityEditor;

    [CustomEditor(typeof(LocalMinimumOptimizer))]
    public class LocalMinimumOptimizerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            LocalMinimumOptimizer script = (LocalMinimumOptimizer)target;

            GUILayout.Space(10);
            if (GUILayout.Button("Optimize / Find Minimum", GUILayout.Height(30)))
            {
                Undo.RecordObject(script, "Optimize Path");
                script.Optimize();
                EditorUtility.SetDirty(script);
                SceneView.RepaintAll();
            }
            
            if (GUILayout.Button("Clear Gizmos"))
            {
                Undo.RecordObject(script, "Clear Path");
                script.GetType().GetField("_path", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(script, new List<Vector3>());
                EditorUtility.SetDirty(script);
                SceneView.RepaintAll();
            }
        }
    }
}
#endif
