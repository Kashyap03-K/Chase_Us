using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sub-choice screen shown after Mode Select's "Play Online" — asks the user
/// whether they want to host a new room or join an existing one. Routes to
/// MapSelectUI (host path) or JoinScreenUI (join path).
///
/// Not in the KAS-19 v4 mockup as a distinct screen; the design assumed the
/// disambiguation would happen implicitly. Adding it here to unblock joiners
/// without inflating Mode Select with a third button.
/// </summary>
public class HostJoinChoiceUI : MonoBehaviour
{
    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private ModeSelectUI modeSelectUI;
    [SerializeField] private MapSelectUI mapSelectUI;
    [SerializeField] private JoinScreenUI joinScreenUI;

    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image.")]
    private float backgroundOverlayAlpha = 0.25f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;

    [Header("Buttons (position over the background art)")]
    [SerializeField] private Vector2 hostButtonAnchor     = new Vector2(0.20f, 0.30f);
    [SerializeField] private Vector2 selfRoomButtonAnchor = new Vector2(0.50f, 0.30f);
    [SerializeField] private Vector2 joinButtonAnchor     = new Vector2(0.80f, 0.30f);
    [SerializeField] private Vector2 buttonSize = new Vector2(420, 320);
    [SerializeField] private Vector2 backButtonAnchor = new Vector2(0.5f, 0.06f);
    [SerializeField] private Vector2 backButtonSize = new Vector2(240, 72);

    [Header("Custom button art (optional — falls back to candy pills if null)")]
    [Tooltip("Illustrated plaque sprite for the Host button. Whole plaque IS the button — labels/icons baked in.")]
    [SerializeField] private Sprite hostButtonSprite;
    [Tooltip("Illustrated plaque sprite for the Self Room button (solo wander, no networking).")]
    [SerializeField] private Sprite selfRoomButtonSprite;
    [Tooltip("Illustrated plaque sprite for the Join button.")]
    [SerializeField] private Sprite joinButtonSprite;
    [Tooltip("Illustrated sprite for the Back button.")]
    [SerializeField] private Sprite backButtonSprite;

    [Header("Self Room tuning (does NOT affect networked play)")]
    [Tooltip("Drop the Player_Network prefab here. In Self Room we instantiate it (without NetworkObject.Spawn) instead of touching the scene's Player_Character, so every knob — CC dims, movement fields, Model root, animator — matches networked play exactly. If null, we fall back to the scene's Player_Character.")]
    [SerializeField] private GameObject selfRoomPlayerPrefab;
    [Tooltip("Where to place the Self Room player. Matches PlayerSpawnManager.spawnPoints[0] by default.")]
    [SerializeField] private Vector3 selfRoomSpawnPoint = new Vector3(-8f, 0.2f, -7f);
    [Tooltip("Extra Y offset applied on top of CharacterLibrary.visualOffsetY when a character is spawned in Self Room. Tune per taste without touching networked play. Negative sinks the character; positive lifts it.")]
    [SerializeField] private float selfRoomVisualOffsetY = 0f;
    [Tooltip("Uniform scale multiplier applied on top of CharacterLibrary.visualScale in Self Room only. 1 = unchanged.")]
    [SerializeField] private float selfRoomVisualScaleMul = 1f;
    [Tooltip("Optional shared Animator Controller used in Self Room. Drop Assets/_Animations/PlayerAnimator.controller here — same controller PlayerCharacterVisual uses in networked play. If left null, the character's per-FBX controller (usually just the walk clip) is kept, and Idle/Walk/Run/Jump blending won't work.")]
    [SerializeField] private RuntimeAnimatorController selfRoomAnimatorController;

    private CanvasGroup canvasGroup;

    // Static handles so the static Self Room helpers can reach Inspector values without a scene lookup.
    private static float s_selfRoomOffsetY;
    private static float s_selfRoomScaleMul;
    private static RuntimeAnimatorController s_selfRoomController;
    private static Vector3 s_selfRoomSpawnPoint;
    private static GameObject s_selfRoomPlayerPrefab;
    private static GameObject s_selfRoomInstance; // The runtime clone, so we don't spawn a fresh one on every entry.

    private void Awake()
    {
        if (modeSelectUI == null)  modeSelectUI  = FindFirstObjectByType<ModeSelectUI>();
        if (mapSelectUI == null)   mapSelectUI   = FindFirstObjectByType<MapSelectUI>();
        if (joinScreenUI == null)  joinScreenUI  = FindFirstObjectByType<JoinScreenUI>();
    }

    private void Start()
    {
        BuildCanvas();
        Hide();
    }

    /// <summary>True while this screen is shown — lets OrbitCamera yield the cursor.</summary>
    public static bool IsVisible { get; private set; }

    public void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        IsVisible = true;
    }

    public void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        IsVisible = false;
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("HostJoinChoiceCanvas", typeof(RectTransform));
        canvasGO.transform.SetParent(transform, false);
        canvasGO.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        CanvasScaler scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = referenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        canvasGroup = canvasGO.AddComponent<CanvasGroup>();

        BuildBackground(canvasGO.transform);

        // Host + Join: use the illustrated plaque sprites if the user dropped them
        // into the Inspector; otherwise fall back to procedural candy pills.
        if (hostButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Host a Room", hostButtonAnchor, buttonSize, hostButtonSprite, OnHostClicked);
        else
            UIButton.BuildCandyPill (canvasGO.transform, "Host a Room", hostButtonAnchor, buttonSize, UITheme.ButtonGreen, UITheme.ButtonGreenHi, OnHostClicked);

        if (selfRoomButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Self Room", selfRoomButtonAnchor, buttonSize, selfRoomButtonSprite, OnSelfRoomClicked);
        else
            UIButton.BuildCandyPill (canvasGO.transform, "Self Room", selfRoomButtonAnchor, buttonSize, UITheme.Chain, UITheme.SurfaceHi, OnSelfRoomClicked);

        if (joinButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Join a Room", joinButtonAnchor, buttonSize, joinButtonSprite, OnJoinClicked);
        else
            UIButton.BuildCandyPill (canvasGO.transform, "Join a Room", joinButtonAnchor, buttonSize, UITheme.ButtonBlue,  UITheme.ButtonBlueHi,  OnJoinClicked);

        if (backButtonSprite != null)
            UIButton.BuildImageButton(canvasGO.transform, "Back", backButtonAnchor, backButtonSize, backButtonSprite, OnBackClicked);
        else
            UIButton.BuildCandyPill(canvasGO.transform, "Back", backButtonAnchor, backButtonSize, UITheme.Ground, UITheme.Ground2, OnBackClicked, fontSize: 24);
    }

    private void BuildBackground(Transform parent)
    {
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)bg.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        Image img = bg.GetComponent<Image>();
        img.raycastTarget = true;

        if (backgroundSprite != null)
        {
            img.sprite = backgroundSprite;
            img.color = Color.white;
            img.preserveAspect = false;
        }
        else
        {
            img.color = UITheme.Ground;
        }

        if (backgroundSprite != null && backgroundOverlayAlpha > 0f)
        {
            GameObject overlay = new GameObject("Overlay", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(parent, false);
            RectTransform overlayRT = (RectTransform)overlay.transform;
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;
            Image overlayImg = overlay.GetComponent<Image>();
            Color32 ground = UITheme.Ground;
            overlayImg.color = new Color(ground.r / 255f, ground.g / 255f, ground.b / 255f, backgroundOverlayAlpha);
            overlayImg.raycastTarget = false;
        }
    }

    // ---------- Interactions ----------

    private void OnHostClicked()
    {
        Debug.Log("[HostJoinChoiceUI] HOST → showing Map Select.");
        Hide();
        if (mapSelectUI != null) mapSelectUI.Show();
        else Debug.LogWarning("[HostJoinChoiceUI] MapSelectUI not found in scene — host flow can't proceed.");
    }

    private void OnSelfRoomClicked()
    {
        // Solo wander: no networking. Activate the offline Player_Character in
        // the scene, target the gameplay camera at it, and swap in the visual
        // matching the roster pick from CharacterSelect. Then drop the UI so
        // input reaches the player.
        Debug.Log("[HostJoinChoiceUI] SELF ROOM → entering solo wander (no networking).");
        Hide();
        // Publish Inspector-tuned values so the static helpers can read them.
        s_selfRoomOffsetY = selfRoomVisualOffsetY;
        s_selfRoomScaleMul = Mathf.Max(0.01f, selfRoomVisualScaleMul);
        s_selfRoomController = selfRoomAnimatorController;
        s_selfRoomSpawnPoint = selfRoomSpawnPoint;
        s_selfRoomPlayerPrefab = selfRoomPlayerPrefab;
        EnterSelfRoom();
    }

    /// <summary>
    /// Activates the scene's offline player, points the gameplay OrbitCamera at
    /// it, and instantiates the selected character's visual under a Model child
    /// so Self Room reflects the roster pick. Idempotent — calling twice just
    /// re-swaps the visual.
    /// </summary>
    private static void EnterSelfRoom()
    {
        PlayerMovement offline = ResolveOfflinePlayer();
        if (offline == null)
        {
            Debug.LogError("[HostJoinChoiceUI] Self Room: no player available. " +
                           "Either wire the Player_Network prefab into the 'Self Room Player Prefab' " +
                           "slot on Host Join Choice UI, or make sure the scene's Player_Character exists " +
                           "with a PlayerMovement component.");
            return;
        }

        if (!offline.gameObject.activeSelf) offline.gameObject.SetActive(true);

        TeleportOfflinePlayer(offline.transform, s_selfRoomSpawnPoint);

        // Camera — the one on an actual Camera component, not the inert copy on the player.
        foreach (OrbitCamera candidate in FindObjectsByType<OrbitCamera>(FindObjectsInactive.Exclude))
        {
            if (candidate.GetComponent<Camera>() != null)
            {
                candidate.SetTarget(offline.transform);
                break;
            }
        }

        SwapSoloVisual(offline.transform);
    }

    /// <summary>
    /// If the Inspector's Self Room Player Prefab is wired, instantiate it (or
    /// return the previously-instantiated clone) so Self Room uses an EXACT
    /// copy of the networked player — same CC, same PlayerMovement fields,
    /// same Model root, same visual pipeline. If unwired, fall back to the
    /// scene's Player_Character. Deactivates the scene player while the clone
    /// lives so both don't process input on top of each other.
    /// </summary>
    private static PlayerMovement ResolveOfflinePlayer()
    {
        if (s_selfRoomPlayerPrefab != null)
        {
            if (s_selfRoomInstance == null)
            {
                s_selfRoomInstance = Instantiate(s_selfRoomPlayerPrefab, s_selfRoomSpawnPoint, Quaternion.identity);
                s_selfRoomInstance.name = s_selfRoomPlayerPrefab.name + "_SelfRoom";
            }
            DeactivateSceneOfflinePlayerIfAny();
            return s_selfRoomInstance.GetComponent<PlayerMovement>();
        }

        // Fallback: scene player. "Offline" = no NetworkObject OR an unspawned one.
        foreach (PlayerMovement pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include))
        {
            Unity.Netcode.NetworkObject netObj = pm.GetComponentInParent<Unity.Netcode.NetworkObject>();
            if (netObj == null || !netObj.IsSpawned)
            {
                return pm;
            }
        }
        return null;
    }

    private static void DeactivateSceneOfflinePlayerIfAny()
    {
        foreach (PlayerMovement pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Include))
        {
            if (pm.gameObject == s_selfRoomInstance) continue;
            Unity.Netcode.NetworkObject netObj = pm.GetComponentInParent<Unity.Netcode.NetworkObject>();
            if ((netObj == null || !netObj.IsSpawned) && pm.gameObject.activeSelf)
            {
                pm.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Self Room only: teleport the offline player to the Inspector-configured
    /// spawn point (matches PlayerSpawnManager.spawnPoints[0] by default).
    /// CharacterController caches position, so it must be disabled across the
    /// teleport or it snaps the player back.
    /// </summary>
    private static void TeleportOfflinePlayer(Transform player, Vector3 point)
    {
        CharacterController cc = player.GetComponent<CharacterController>();
        bool ccWasEnabled = cc != null && cc.enabled;
        if (ccWasEnabled) cc.enabled = false;
        player.position = point;
        if (ccWasEnabled) cc.enabled = true;
    }

    private static void SwapSoloVisual(Transform player)
    {
        CharacterLibrary lib = CharacterLibrary.Instance;
        if (lib == null) return; // No library — the scene's placeholder mesh will do.

        int idx = lib.IndexOf(CharacterSelectUI.SelectedCharacterId);
        if (idx < 0) idx = 0;
        GameObject prefab = lib.GetVisualPrefab(idx);
        if (prefab == null) return;

        // Reuse the same "Model" convention PlayerCharacterVisual uses so
        // networked play and solo look identical.
        Transform modelRoot = player.Find("Model");
        if (modelRoot == null)
        {
            GameObject go = new GameObject("Model");
            go.transform.SetParent(player, false);
            modelRoot = go.transform;
        }

        for (int i = modelRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(modelRoot.GetChild(i).gameObject);
        }

        // Hide any renderer that's part of the offline Player_Character but NOT
        // under our Model root — e.g. the placeholder mesh baked directly into
        // the scene GameObject. Without this the old character stays visible
        // alongside the freshly-swapped one.
        foreach (Renderer r in player.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.transform.IsChildOf(modelRoot)) r.enabled = false;
        }

        GameObject visual = Instantiate(prefab, modelRoot);
        (float offsetY, float scale) = lib.GetVisualTransform(idx);
        visual.transform.localPosition = new Vector3(0f, offsetY + s_selfRoomOffsetY, 0f);
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * scale * s_selfRoomScaleMul;

        Animator anim = visual.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            // Match networked play: override the FBX's per-character controller
            // with the shared PlayerAnimator so Speed drives the Idle/Walk/Run
            // blend tree and Jump/Hit/Punch triggers fire correctly. Without
            // this the character just loops its FBX's default clip.
            if (s_selfRoomController != null)
            {
                anim.runtimeAnimatorController = s_selfRoomController;
            }
            if (!anim.enabled) anim.enabled = true;
            PlayerMovement pm = player.GetComponent<PlayerMovement>();
            if (pm != null) pm.RebindAnimator(anim);
        }
    }

    private void OnJoinClicked()
    {
        Debug.Log("[HostJoinChoiceUI] JOIN → showing Join Screen.");
        Hide();
        if (joinScreenUI != null) joinScreenUI.Show();
        else Debug.LogWarning("[HostJoinChoiceUI] JoinScreenUI not found in scene — join flow can't proceed.");
    }

    private void OnBackClicked()
    {
        Hide();
        if (modeSelectUI != null) modeSelectUI.Show();
    }

    // ---------- Shared helpers ----------

    private TMP_Text CreateText(Transform parent, string name, string content, int fontSize, Color color,
                                FontStyles style = FontStyles.Normal, float letterSpacing = 0f, bool wrap = true)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = fontSize;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.characterSpacing = letterSpacing;
        t.raycastTarget = false;
#pragma warning disable CS0618
        t.enableWordWrapping = wrap;
#pragma warning restore CS0618
        return t;
    }
}
