using UnityEngine;
using UnityEditor;

public class BoundaryScatterTool : EditorWindow
{
    private GameObject prefabToScatter;
    private float boundaryHalfSize = 25f; // half of 50x50
    private float spacing = 5f;
    private float positionJitter = 1f;
    private float minScale = 0.9f;
    private float maxScale = 1.15f;
    private Transform parentContainer;

    [MenuItem("Tools/Boundary Scatter Tool")]
    public static void ShowWindow()
    {
        GetWindow<BoundaryScatterTool>("Boundary Scatter");
    }

    private void OnGUI()
    {
        GUILayout.Label("Scatter Prefabs Along Boundary", EditorStyles.boldLabel);

        prefabToScatter = (GameObject)EditorGUILayout.ObjectField(
            "Prefab", prefabToScatter, typeof(GameObject), false);

        parentContainer = (Transform)EditorGUILayout.ObjectField(
            "Parent (optional)", parentContainer, typeof(Transform), true);

        EditorGUILayout.Space();
        boundaryHalfSize = EditorGUILayout.FloatField("Boundary Half Size", boundaryHalfSize);
        spacing = EditorGUILayout.FloatField("Spacing (meters)", spacing);
        positionJitter = EditorGUILayout.FloatField("Position Jitter", positionJitter);

        EditorGUILayout.Space();
        minScale = EditorGUILayout.FloatField("Min Scale", minScale);
        maxScale = EditorGUILayout.FloatField("Max Scale", maxScale);

        EditorGUILayout.Space();

        if (GUILayout.Button("Scatter Along Boundary"))
        {
            if (prefabToScatter == null)
            {
                EditorUtility.DisplayDialog("Missing Prefab", "Assign a prefab first.", "OK");
                return;
            }
            ScatterAlongBoundary();
        }

        EditorGUILayout.HelpBox(
            "Places prefab copies along all 4 edges of a square boundary centered at origin, " +
            "with randomized Y rotation, scale, and position jitter. Undo-able (Ctrl+Z).",
            MessageType.Info);
    }

    private void ScatterAlongBoundary()
    {
        GameObject root = new GameObject(prefabToScatter.name + "_Scatter_Group");
        Undo.RegisterCreatedObjectUndo(root, "Scatter Boundary Props");

        if (parentContainer != null)
        {
            root.transform.SetParent(parentContainer);
        }

        int count = 0;
        float h = boundaryHalfSize;

        // North and South edges (varying X, fixed Z)
        for (float x = -h; x <= h; x += spacing)
        {
            PlaceOne(x, h, root.transform);   // North edge
            PlaceOne(x, -h, root.transform);  // South edge
            count += 2;
        }

        // East and West edges (fixed X, varying Z) - skip corners to avoid doubling up
        for (float z = -h + spacing; z <= h - spacing; z += spacing)
        {
            PlaceOne(h, z, root.transform);   // East edge
            PlaceOne(-h, z, root.transform);  // West edge
            count += 2;
        }

        Debug.Log($"Scattered {count} instances of {prefabToScatter.name} along the boundary.");
    }

    private void PlaceOne(float x, float z, Transform parent)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabToScatter);
        Undo.RegisterCreatedObjectUndo(instance, "Scatter Boundary Props");

        float jitterX = Random.Range(-positionJitter, positionJitter);
        float jitterZ = Random.Range(-positionJitter, positionJitter);

        instance.transform.position = new Vector3(x + jitterX, 0f, z + jitterZ);

        // Preserve the prefab's own baked rotation (e.g. axis correction),
        // and only add a random extra spin around world Y on top of it.
        float extraYRotation = Random.Range(0f, 360f);
        instance.transform.rotation = prefabToScatter.transform.rotation * Quaternion.Euler(0f, extraYRotation, 0f);

        // Preserve the prefab's own baked scale, and multiply by a random factor
        // instead of replacing it outright.
        float scaleMultiplier = Random.Range(minScale, maxScale);
        instance.transform.localScale = prefabToScatter.transform.localScale * scaleMultiplier;

        instance.transform.SetParent(parent);
    }
}
