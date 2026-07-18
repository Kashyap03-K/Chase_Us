using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry mapping <see cref="CharacterSelectUI"/> roster ids to their visual
/// prefabs — the FBXs under Assets/_Import/Meshy/Character/&lt;Name&gt;/ dropped
/// into the Entries list on the Inspector. Sits on <c>_Bootstrap</c> as a plain
/// MonoBehaviour singleton so both the local CharacterSelect flow and the
/// server-side <see cref="PlayerCharacterVisual"/> spawn code can resolve
/// ids → prefabs without knowing about each other.
///
/// Order in the Inspector must mirror CharacterSelectUI.Roster's order, because
/// PlayerCharacterVisual replicates a byte index over the wire (id → index is
/// only cheap if both sides see the same list). The lookup is guarded by string
/// id anyway; index is just the transport format.
/// </summary>
public class CharacterLibrary : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        [Tooltip("Roster id — must match the CharacterSelectUI CharacterDefinition.id exactly (e.g. \"vigo\", \"kai\").")]
        public string id;

        [Tooltip("Visual-only prefab or FBX. Rigged mesh + Animator only — no NetworkObject, no CharacterController, no colliders. Gets instantiated as a child of the networked player's Model transform.")]
        public GameObject visualPrefab;

        [Tooltip("Local Y offset applied to the visual when instantiated. Nudge negative to sink a character whose feet float above the CharacterController capsule; positive to lift one whose feet clip through the floor. Meshy exports vary slightly per model.")]
        public float visualOffsetY = 0f;

        [Tooltip("Uniform scale applied to the visual. 1 = default. Leave at 1 unless a character imported at a wildly different scale than the roster average.")]
        public float visualScale = 1f;
    }

    public static CharacterLibrary Instance { get; private set; }

    [Header("Order MUST match CharacterSelectUI roster (vigo, mira, kai, nova, sunny).")]
    [SerializeField]
    private List<Entry> entries = new List<Entry>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Roster size — matches CharacterSelectUI.Roster.Count when configured correctly.</summary>
    public int Count => entries.Count;

    /// <summary>
    /// Roster index of <paramref name="id"/>, or -1 if unknown / empty / no visual
    /// wired. Callers should fall back to a sensible default (0 = first roster
    /// entry) when this returns -1 — e.g. a client that never opened
    /// CharacterSelect still needs a body.
    /// </summary>
    public int IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return -1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null && entries[i].id == id && entries[i].visualPrefab != null)
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>Visual prefab at the given index, or null if out of range / not wired.</summary>
    public GameObject GetVisualPrefab(int index)
    {
        if (index < 0 || index >= entries.Count) return null;
        Entry e = entries[index];
        return e != null ? e.visualPrefab : null;
    }

    /// <summary>
    /// Per-character transform tweaks to be applied to the instantiated visual.
    /// Returns (offsetY = 0, scale = 1) for out-of-range or unwired entries so
    /// callers never have to null-check.
    /// </summary>
    public (float offsetY, float scale) GetVisualTransform(int index)
    {
        if (index < 0 || index >= entries.Count) return (0f, 1f);
        Entry e = entries[index];
        if (e == null) return (0f, 1f);
        return (e.visualOffsetY, Mathf.Max(0.01f, e.visualScale));
    }

    /// <summary>Convenience — id at index, empty if not found. Used for logging.</summary>
    public string GetId(int index)
    {
        if (index < 0 || index >= entries.Count) return string.Empty;
        Entry e = entries[index];
        return e != null && e.id != null ? e.id : string.Empty;
    }
}
