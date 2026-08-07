#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RealLifeEnvironment
{
    [CustomEditor(typeof(WorldChanger))]
    public class WorldChangerEditor : Editor
    {
        private Color sampledColor = Color.white;

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();

            WorldChanger controller = (WorldChanger)target;

            if (EditorGUI.EndChangeCheck())
            {
                controller.ApplySeasonalChanges();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Gradient Authoring Tools", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("1. Scroll 'Time Of Day' to your target time.\n2. Pick a color below.\n3. Click a button to insert that color into the gradient at the current time.", MessageType.Info);

            sampledColor = EditorGUILayout.ColorField("Sample Color", sampledColor);

            float t = controller.timeOfDay / 24f;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply to Sun Color"))
            {
                Undo.RecordObject(controller, "Modify Sun Gradient");
                controller.sunColor = InsertKeyIntoGradient(controller.sunColor, t, sampledColor);
                EditorUtility.SetDirty(controller);
            }
            if (GUILayout.Button("Apply to Sky Tint"))
            {
                Undo.RecordObject(controller, "Modify Sky Gradient");
                controller.skyTint = InsertKeyIntoGradient(controller.skyTint, t, sampledColor);
                EditorUtility.SetDirty(controller);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply to Ground Color"))
            {
                Undo.RecordObject(controller, "Modify Ground Gradient");
                controller.groundColor = InsertKeyIntoGradient(controller.groundColor, t, sampledColor);
                EditorUtility.SetDirty(controller);
            }
            if (GUILayout.Button("Apply to Fog Color"))
            {
                Undo.RecordObject(controller, "Modify Fog Gradient");
                controller.fogColor = InsertKeyIntoGradient(controller.fogColor, t, sampledColor);
                EditorUtility.SetDirty(controller);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply to Ambient Sky"))
            {
                Undo.RecordObject(controller, "Modify Ambient Sky Gradient");
                controller.ambientSkyColor = InsertKeyIntoGradient(controller.ambientSkyColor, t, sampledColor);
                EditorUtility.SetDirty(controller);
            }
            if (GUILayout.Button("Apply to Ambient Equator"))
            {
                Undo.RecordObject(controller, "Modify Ambient Equator Gradient");
                controller.ambientEquatorColor = InsertKeyIntoGradient(controller.ambientEquatorColor, t, sampledColor);
                EditorUtility.SetDirty(controller);
            }
            GUILayout.EndHorizontal();
        }

        private Gradient InsertKeyIntoGradient(Gradient gradient, float time, Color color)
        {
            if (gradient == null) gradient = new Gradient();

            List<GradientColorKey> colorKeys = gradient.colorKeys.ToList();
            List<GradientAlphaKey> alphaKeys = gradient.alphaKeys.ToList();

            // Replace existing key if it's very close (within 1% time difference)
            bool replacedColor = false;
            for (int i = 0; i < colorKeys.Count; i++)
            {
                if (Mathf.Abs(colorKeys[i].time - time) < 0.01f)
                {
                    var k = colorKeys[i];
                    k.color = color;
                    colorKeys[i] = k;
                    replacedColor = true;
                    break;
                }
            }

            if (!replacedColor)
            {
                if (colorKeys.Count >= 8)
                {
                    Debug.LogWarning("Gradient already has 8 color keys (Unity's maximum). Cannot add more.");
                }
                else
                {
                    colorKeys.Add(new GradientColorKey(color, time));
                }
            }

            bool replacedAlpha = false;
            for (int i = 0; i < alphaKeys.Count; i++)
            {
                if (Mathf.Abs(alphaKeys[i].time - time) < 0.01f)
                {
                    var k = alphaKeys[i];
                    k.alpha = color.a;
                    alphaKeys[i] = k;
                    replacedAlpha = true;
                    break;
                }
            }

            if (!replacedAlpha)
            {
                if (alphaKeys.Count < 8)
                {
                    alphaKeys.Add(new GradientAlphaKey(color.a, time));
                }
            }

            gradient.SetKeys(colorKeys.ToArray(), alphaKeys.ToArray());
            return gradient;
        }
    }
}
#endif
