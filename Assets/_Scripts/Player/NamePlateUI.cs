using TMPro;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G1 — floating world-space nameplate above every networked player: character
/// name on top (the picked roster id, uppercased — there is no username system,
/// the character IS the identity signal), current role underneath (HUNTER /
/// RUNNER). Visible on every peer including the local player's own plate.
///
/// Data sources (no duplicated state):
///   * Name — <see cref="PlayerCharacterVisual.CharacterIndex"/> resolved
///     through <see cref="CharacterLibrary.GetId(int)"/>. Reacts to the
///     replicated index via PlayerCharacterVisual.CharacterIndexChanged and
///     mirrors the current value on spawn so late-joiners are correct
///     immediately (same pattern as PlayerCharacterVisual.OnNetworkSpawn).
///   * Role — <see cref="GameRoundManager.HunterClientId"/>. Every plate
///     subscribes to OnValueChanged and recomputes its OWN role from
///     OwnerClientId, so a hunter change (round start, Play Again, promotion
///     after a hunter disconnect) refreshes old and new hunter's plates alike.
///     Caught players deliberately still read RUNNER — no third state.
///
/// The canvas is built in code (house pattern — no scene wiring) as a child of
/// the player root, billboarded to the live camera every LateUpdate. Camera is
/// resolved the same way PlayerMovement finds the rig: the OrbitCamera that
/// actually sits on a Camera component.
/// </summary>
[DisallowMultipleComponent]
public class NamePlateUI : NetworkBehaviour
{
    [Header("Placement")]
    [Tooltip("World height of the plate above the player pivot (pivot = feet). Capsule is 2m tall; extra headroom clears every character's visualOffsetY. Confirm visually in the Editor per-character.")]
    [SerializeField] private float plateHeight = 2.45f;

    [Header("Look")]
    [SerializeField] private float nameFontSize = 34f;
    [SerializeField] private float roleFontSize = 22f;
    [SerializeField, Tooltip("Uniform world scale of the canvas — 0.01 makes a 200-wide canvas ~2m.")]
    private float canvasScale = 0.01f;

    private PlayerCharacterVisual characterVisual;
    private GameRoundManager boundRoundManager;
    private Camera billboardCamera;

    private Transform plateRoot;
    private TMP_Text nameText;
    private TMP_Text roleText;

    private void Awake()
    {
        characterVisual = GetComponent<PlayerCharacterVisual>();
        BuildPlate();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (characterVisual != null)
        {
            characterVisual.CharacterIndexChanged += HandleCharacterChanged;
            // Late-joiners get the already-replicated value with no change
            // event firing — mirror it eagerly.
            RefreshName(characterVisual.CharacterIndex);
        }

        TryBindRoundManager();
        RefreshRole();
    }

    public override void OnNetworkDespawn()
    {
        if (characterVisual != null)
        {
            characterVisual.CharacterIndexChanged -= HandleCharacterChanged;
        }
        UnbindRoundManager();
        base.OnNetworkDespawn();
    }

    // ---------- Data binding ----------

    private void HandleCharacterChanged(byte newIndex)
    {
        RefreshName(newIndex);
    }

    private void TryBindRoundManager()
    {
        if (boundRoundManager != null || GameRoundManager.Instance == null) return;
        boundRoundManager = GameRoundManager.Instance;
        boundRoundManager.HunterClientId.OnValueChanged += HandleHunterChanged;
    }

    private void UnbindRoundManager()
    {
        if (boundRoundManager == null) return;
        boundRoundManager.HunterClientId.OnValueChanged -= HandleHunterChanged;
        boundRoundManager = null;
    }

    private void HandleHunterChanged(ulong previous, ulong current)
    {
        // Each plate recomputes only its own role — with every plate
        // subscribed, the old hunter's and new hunter's plates both refresh.
        RefreshRole();
    }

    private void RefreshName(byte index)
    {
        if (nameText == null) return;

        if (index == PlayerCharacterVisual.NoChoiceIndex)
        {
            nameText.text = "…";
            return;
        }

        CharacterLibrary lib = CharacterLibrary.Instance;
        string id = lib != null ? lib.GetId(index) : string.Empty;
        nameText.text = string.IsNullOrEmpty(id) ? $"P{OwnerClientId}" : id.ToUpperInvariant();
    }

    private void RefreshRole()
    {
        if (roleText == null) return;

        bool isHunter = boundRoundManager != null &&
                        boundRoundManager.HunterClientId.Value == OwnerClientId;

        roleText.text = isHunter ? "HUNTER" : "RUNNER";
        roleText.color = isHunter ? (Color)UITheme.Hunter : (Color)UITheme.Accent2;
    }

    // ---------- Billboard ----------

    private void LateUpdate()
    {
        if (plateRoot == null) return;

        // GameRoundManager can spawn/bind after us (in-scene NetworkObject
        // sync order) — bind lazily until it exists.
        if (boundRoundManager == null && GameRoundManager.Instance != null)
        {
            TryBindRoundManager();
            RefreshRole();
        }

        if (billboardCamera == null)
        {
            billboardCamera = FindSceneCamera();
            if (billboardCamera == null) return;
        }

        // Match the camera's orientation — steadier than LookAt for a plate.
        plateRoot.rotation = billboardCamera.transform.rotation;
    }

    /// <summary>Same convention as PlayerMovement.FindSceneOrbitCamera: the OrbitCamera on an actual Camera.</summary>
    private static Camera FindSceneCamera()
    {
        foreach (OrbitCamera candidate in FindObjectsByType<OrbitCamera>(FindObjectsInactive.Exclude))
        {
            Camera cam = candidate.GetComponent<Camera>();
            if (cam != null) return cam;
        }
        return Camera.main;
    }

    // ---------- Canvas construction (house pattern — built in code) ----------

    private void BuildPlate()
    {
        GameObject rootGO = new GameObject("NamePlate", typeof(RectTransform));
        rootGO.transform.SetParent(transform, false);
        rootGO.transform.localPosition = new Vector3(0f, plateHeight, 0f);
        rootGO.layer = LayerMask.NameToLayer("UI");
        plateRoot = rootGO.transform;

        Canvas canvas = rootGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        RectTransform rt = (RectTransform)rootGO.transform;
        rt.sizeDelta = new Vector2(220f, 70f);
        rt.localScale = Vector3.one * canvasScale;

        nameText = CreateLine(rootGO.transform, "Name", nameFontSize, UITheme.Text, yMin: 0.42f, yMax: 1f);
        nameText.fontStyle = FontStyles.Bold;
        roleText = CreateLine(rootGO.transform, "Role", roleFontSize, UITheme.Accent2, yMin: 0f, yMax: 0.42f);
        roleText.characterSpacing = 6f;

        nameText.text = "…";
        roleText.text = "RUNNER";
    }

    private static TMP_Text CreateLine(Transform parent, string name, float fontSize, Color color, float yMin, float yMax)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");

        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0f, yMin);
        rt.anchorMax = new Vector2(1f, yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = fontSize;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.raycastTarget = false;
        // Soft outline so the plate reads against bright and dark map areas alike.
        t.outlineWidth = 0.2f;
        t.outlineColor = new Color32(0x0F, 0x0B, 0x1F, 0xFF); // UITheme.Ground
        return t;
    }
}
