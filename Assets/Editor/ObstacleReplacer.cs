using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Editor tool: replace placeholder obstacle GameObjects (e.g. grey cubes under
/// an "_Obstacles" group) with real prop prefabs, preserving each placeholder's
/// position, rotation, scale, parent, and name.
///
/// Usage:
///   Window > Tools > Obstacle Replacer
///   1. Assign the "_Obstacles" group Transform.
///   2. Click "Scan" to list its direct children as replacement slots.
///   3. Drag the correct prop prefab into each slot.
///   4. Click "Replace All" (or replace individually) to swap.
///
/// The original placeholder is destroyed only after the new instance is
/// successfully created and transform-matched, and the swap is registered
/// with Undo so Ctrl+Z reverts a mistaken replacement.
/// </summary>
public class ObstacleReplacer : EditorWindow
{
    private Transform obstacleGroup;
    private readonly List<Transform> placeholders = new List<Transform>();
    private readonly Dictionary<Transform, GameObject> prefabAssignments = new Dictionary<Transform, GameObject>();
    private Vector2 scrollPos;

    [MenuItem("Window/Tools/Obstacle Replacer")]
    public static void ShowWindow()
    {
        GetWindow<ObstacleReplacer>("Obstacle Replacer");
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Obstacle Replacer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        obstacleGroup = (Transform)EditorGUILayout.ObjectField(
            "Obstacles Group", obstacleGroup, typeof(Transform), true);

        using (new EditorGUI.DisabledScope(obstacleGroup == null))
        {
            if (GUILayout.Button("Scan Children"))
            {
                ScanChildren();
            }
        }

        EditorGUILayout.Space();

        if (placeholders.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Assign the _Obstacles group and click Scan to list placeholders.",
                MessageType.Info);
            return;
        }

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        foreach (var placeholder in placeholders)
        {
            if (placeholder == null) continue; // may have been replaced already

            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField(placeholder.name, GUILayout.Width(160));

            GameObject current = prefabAssignments.ContainsKey(placeholder)
                ? prefabAssignments[placeholder]
                : null;

            GameObject assigned = (GameObject)EditorGUILayout.ObjectField(
                current, typeof(GameObject), false, GUILayout.Width(200));

            prefabAssignments[placeholder] = assigned;

            using (new EditorGUI.DisabledScope(assigned == null))
            {
                if (GUILayout.Button("Replace", GUILayout.Width(70)))
                {
                    ReplaceSingle(placeholder, assigned);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();
        if (GUILayout.Button("Replace All Assigned"))
        {
            // Copy keys since ReplaceSingle mutates placeholders during iteration
            var toProcess = new List<Transform>(prefabAssignments.Keys);
            foreach (var placeholder in toProcess)
            {
                if (placeholder == null) continue;
                if (prefabAssignments.TryGetValue(placeholder, out var prefab) && prefab != null)
                {
                    ReplaceSingle(placeholder, prefab);
                }
            }
        }
    }

    private void ScanChildren()
    {
        placeholders.Clear();
        prefabAssignments.Clear();

        foreach (Transform child in obstacleGroup)
        {
            placeholders.Add(child);
        }
    }

    private void ReplaceSingle(Transform placeholder, GameObject prefab)
    {
        if (placeholder == null || prefab == null) return;

        // Capture transform + hierarchy info before destroying the placeholder.
        string originalName = placeholder.name;
        Vector3 position = placeholder.position;
        Quaternion rotation = placeholder.rotation;
        Vector3 scale = placeholder.localScale;
        Transform parent = placeholder.parent;
        int siblingIndex = placeholder.GetSiblingIndex();
        int layer = placeholder.gameObject.layer;

        // Instantiate as a prefab connection so future prefab edits propagate.
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        Undo.RegisterCreatedObjectUndo(instance, "Replace Obstacle Placeholder");

        instance.transform.position = position;
        instance.transform.rotation = rotation;
        instance.transform.localScale = scale;
        instance.transform.SetSiblingIndex(siblingIndex);
        instance.gameObject.layer = layer;
        instance.name = originalName; // keep naming convention, e.g. "Obstacle_01"

        Undo.DestroyObjectImmediate(placeholder.gameObject);

        // Clean up bookkeeping so the UI reflects the swap.
        placeholders.Remove(placeholder);
        prefabAssignments.Remove(placeholder);

        EditorUtility.SetDirty(instance);
        Debug.Log($"[ObstacleReplacer] Replaced '{originalName}' with prefab '{prefab.name}'.");
    }
}
