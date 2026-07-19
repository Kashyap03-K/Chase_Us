using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Chase Us character picker. Sits between Mode Select and the connection
/// screens (LAN or Host/Join) so every player has locked in an avatar before
/// they touch networking. Per-player choice — persisted to PlayerPrefs and
/// exposed via <see cref="SelectedCharacterId"/> so the eventual player
/// prefab (KAS-27) can read it when the local client spawns.
///
/// Roster locked in SESSION_HANDOFF.md Section 3:
///   Vigo (green) · Mira (cyan) · Kai (pink) · Nova (purple) · Sunny (amber)
///
/// Live 3D preview:
///   Each character with a wired prefab is instantiated once into a hidden
///   preview stage far below the play area, framed by a dedicated Camera
///   that renders into a RenderTexture. The tile's RawImage displays that
///   texture. If no prefab is wired, the tile falls back to a colored
///   silhouette panel and is marked "COMING SOON" (mirrors MapSelectUI's
///   locked-tile treatment).
/// </summary>
public class CharacterSelectUI : MonoBehaviour
{
    public enum NextScreen { HostJoinChoice, LanConnect }

    [Serializable]
    public class CharacterDefinition
    {
        public string id = "character_id";
        public string displayName = "NAME";
        public string tagline = "TAGLINE";
        [Tooltip("Signature accent color. Used for the tile border when selected and the fallback silhouette when no prefab is wired.")]
        public Color signatureColor = Color.white;
        [Tooltip("Rigged humanoid FBX (or prefab wrapping it). If null, this tile is treated as locked (unless a portraitSprite is provided).")]
        public GameObject prefab;
        [Tooltip("Optional pre-rendered PNG portrait. When set, the tile displays this image instead of the real-time 3D preview — crisper and cheaper. Falls back to the 3D RenderTexture path when null.")]
        public Sprite portraitSprite;
        [Tooltip("Small nudge for the preview camera framing this character. Y is head-height, Z is camera distance.")]
        public Vector2 previewFramingYZ = new Vector2(0.85f, 3.2f);
        [Tooltip("Uniform scale applied to the instantiated preview. Meshy exports vary.")]
        public float previewScale = 1f;
    }

    [Header("Wiring (auto-found if left null)")]
    [SerializeField] private ModeSelectUI modeSelectUI;
    [SerializeField] private HostJoinChoiceUI hostJoinChoiceUI;
    [SerializeField] private LanConnectUI lanConnectUI;

    [Header("Roster (order matters — grid renders left→right)")]
    [SerializeField]
    private List<CharacterDefinition> roster = new List<CharacterDefinition>
    {
        new CharacterDefinition { id = "vigo",  displayName = "VIGO",  tagline = "SCOUT · GREEN",  signatureColor = new Color(0.36f, 0.73f, 0.47f, 1f) },
        new CharacterDefinition { id = "mira",  displayName = "MIRA",  tagline = "EXPLORER · CYAN",signatureColor = new Color(0.37f, 0.83f, 0.72f, 1f) },
        new CharacterDefinition { id = "kai",   displayName = "KAI",   tagline = "SPRINTER · PINK",signatureColor = new Color(1.00f, 0.24f, 0.48f, 1f) },
        new CharacterDefinition { id = "nova",  displayName = "NOVA",  tagline = "SNEAK · PURPLE", signatureColor = new Color(0.71f, 0.49f, 1.00f, 1f) },
        new CharacterDefinition { id = "sunny", displayName = "SUNNY", tagline = "SPARK · AMBER",  signatureColor = new Color(0.96f, 0.73f, 0.26f, 1f) }
    };

    [Header("Custom button art (optional — falls back to candy pills if null)")]
    [SerializeField] private Sprite confirmSprite;
    [SerializeField] private Sprite backSprite;

    [Header("Button sizes (used when the sprite variant is active)")]
    [SerializeField] private Vector2 confirmButtonSize = new Vector2(400, 100);
    [SerializeField] private Vector2 backButtonSize    = new Vector2(300, 100);

    [Header("Background")]
    [SerializeField, Tooltip("Optional. If null, uses a solid Ground-color background.")]
    private Sprite backgroundSprite;
    [SerializeField, Range(0f, 1f), Tooltip("Dark overlay opacity on top of the background image.")]
    private float backgroundOverlayAlpha = 0.35f;

    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 100;
    [SerializeField] private Vector2Int previewTextureSize = new Vector2Int(1024, 1280);
    [SerializeField, Range(1, 8), Tooltip("MSAA samples for the preview render textures. 1 = off (fastest, jaggy silhouettes). 4 or 8 = smooth edges. Cost is 5 RTs × sample count.")] private int previewMsaa = 8;

    // The preview stage lives here so the main camera can't see it. Y = -1000
    // keeps it well below any arena floor; layer 30 gives us a clean cull mask.
    private const int PreviewLayer = 30;
    private static readonly Vector3 PreviewStageOrigin = new Vector3(1000f, -1000f, 1000f);
    private const float PreviewCharacterSpacingX = 4f;

    /// <summary>PlayerPrefs key holding the last-picked character id.</summary>
    public const string PlayerPrefsKey = "chase_us.selected_character";

    /// <summary>ID of the confirmed character. Populated from PlayerPrefs on Awake; overwritten on Confirm.</summary>
    public static string SelectedCharacterId { get; private set; }

    /// <summary>Fires on Confirm. Payload is the character id.</summary>
    public event Action<string> Confirmed;

    private CanvasGroup canvasGroup;
    private readonly List<Image> tileBorders = new List<Image>();
    private readonly List<GameObject> tileSelectedBadges = new List<GameObject>();
    private readonly List<RenderTexture> previewTextures = new List<RenderTexture>();
    private readonly List<GameObject> previewInstances = new List<GameObject>();
    private readonly List<Camera> previewCameras = new List<Camera>();
    private Transform previewStage;
    private string highlightedCharacterId;
    private NextScreen pendingNext = NextScreen.HostJoinChoice;

    /// <summary>True while this screen is shown — lets OrbitCamera yield the cursor.</summary>
    public static bool IsVisible { get; private set; }

    private void Awake()
    {
        if (modeSelectUI == null)     modeSelectUI     = FindFirstObjectByType<ModeSelectUI>();
        if (hostJoinChoiceUI == null) hostJoinChoiceUI = FindFirstObjectByType<HostJoinChoiceUI>();
        if (lanConnectUI == null)     lanConnectUI     = FindAnyObjectByType<LanConnectUI>();

        SelectedCharacterId = PlayerPrefs.GetString(PlayerPrefsKey, null);
        highlightedCharacterId = ResolveInitialHighlight();
    }

    private void Start()
    {
        BuildPreviewStage();
        BuildCanvas();
        UpdateTileVisuals();
        Hide();
    }

    private void OnDestroy()
    {
        foreach (RenderTexture rt in previewTextures)
        {
            if (rt != null) rt.Release();
        }
    }

    public void Show(NextScreen next)
    {
        pendingNext = next;
        Show();
    }

    public void Show()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        IsVisible = true;
        SetPreviewCamerasEnabled(true);
    }

    public void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        IsVisible = false;
        SetPreviewCamerasEnabled(false);
    }

    private void SetPreviewCamerasEnabled(bool on)
    {
        foreach (Camera cam in previewCameras)
        {
            if (cam != null) cam.enabled = on;
        }
    }

    private string ResolveInitialHighlight()
    {
        // Respect the last-picked character if it's still in the roster and unlocked.
        if (!string.IsNullOrEmpty(SelectedCharacterId))
        {
            foreach (CharacterDefinition c in roster)
            {
                if (c.id == SelectedCharacterId && c.prefab != null) return c.id;
            }
        }
        // Otherwise highlight the first unlocked entry.
        foreach (CharacterDefinition c in roster)
        {
            if (c.prefab != null) return c.id;
        }
        return null;
    }

    // ---------- Preview stage ----------

    private void BuildPreviewStage()
    {
        GameObject stage = new GameObject("CharacterSelectPreviewStage");
        stage.transform.position = PreviewStageOrigin;
        previewStage = stage.transform;

        // Ambient fill so the previews aren't lit only by whatever the arena scene provides.
        GameObject lightGO = new GameObject("PreviewFillLight");
        lightGO.transform.SetParent(previewStage, false);
        lightGO.transform.localPosition = new Vector3(1.5f, 3f, -2f);
        lightGO.transform.localRotation = Quaternion.Euler(35f, -30f, 0f);
        Light light = lightGO.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.6f;
        light.color = new Color(1f, 0.96f, 0.90f, 1f);
        light.cullingMask = 1 << PreviewLayer;

        for (int i = 0; i < roster.Count; i++)
        {
            CharacterDefinition def = roster[i];
            Vector3 slot = new Vector3(i * PreviewCharacterSpacingX, 0f, 0f);
            // Skip the 3D preview allocation entirely when the tile will render
            // from a portrait PNG — saves one RenderTexture + one Camera + one
            // instantiated humanoid per character that opts into the still image.
            if (def.prefab == null || def.portraitSprite != null)
            {
                previewInstances.Add(null);
                previewCameras.Add(null);
                previewTextures.Add(null);
                continue;
            }

            GameObject instance = Instantiate(def.prefab, previewStage);
            instance.name = $"Preview_{def.id}";
            instance.transform.localPosition = slot;
            instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // face the preview camera
            instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, def.previewScale);
            SetLayerRecursively(instance, PreviewLayer);
            NeutraliseRootMotion(instance);
            ForceUpdateSkinnedBounds(instance);
            previewInstances.Add(instance);

            RenderTexture rt = new RenderTexture(previewTextureSize.x, previewTextureSize.y, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = Mathf.Clamp(Mathf.ClosestPowerOfTwo(previewMsaa), 1, 8);
            rt.filterMode = FilterMode.Bilinear;
            rt.anisoLevel = 4;
            rt.useMipMap = false;
            rt.Create();
            previewTextures.Add(rt);

            GameObject camGO = new GameObject($"PreviewCam_{def.id}");
            camGO.transform.SetParent(previewStage, false);
            camGO.transform.localPosition = slot + new Vector3(0f, def.previewFramingYZ.x, -def.previewFramingYZ.y);
            // LookAt takes a WORLD position — the previous version passed `slot` (a
            // local offset) directly, so every camera aimed at world (0,1,0) instead
            // of at its character 1000 units away. Convert the local target through
            // the stage transform before handing it to LookAt.
            Vector3 lookTargetWorld = previewStage.TransformPoint(slot + new Vector3(0f, def.previewFramingYZ.x, 0f));
            camGO.transform.LookAt(lookTargetWorld);
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f); // transparent — the tile background shows through
            cam.cullingMask = 1 << PreviewLayer;
            cam.orthographic = false;
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 25f;
            cam.targetTexture = rt;
            cam.allowMSAA = true;
            cam.allowHDR = false; // preview is composited over an opaque UI backdrop — HDR gains nothing here
            cam.enabled = false; // Show() turns this on
            previewCameras.Add(cam);
        }

        // Make sure the main camera (if one exists) doesn't accidentally render the stage.
        if (Camera.main != null)
        {
            Camera.main.cullingMask &= ~(1 << PreviewLayer);
        }
    }

    private static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
        {
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
        }
    }

    private static void NeutraliseRootMotion(GameObject go)
    {
        // Meshy walk/run clips include forward translation. We're rendering an idle-in-place
        // preview, so kill root motion at the source and let the animator loop the clip.
        Animator anim = go.GetComponentInChildren<Animator>();
        if (anim != null) anim.applyRootMotion = false;
    }

    /// <summary>
    /// Meshy exports keep the SkinnedMeshRenderer's cached bounds pinned to the
    /// import-time skeleton pose, so when we park the instance at Y=-1000 on
    /// the preview stage the bounds still test as being near the world origin —
    /// the render camera frustum-culls them and the tile shows only the
    /// backdrop color. Turning on updateWhenOffscreen makes Unity recompute
    /// bounds each frame, which costs a bit but is trivial for 5 previews.
    /// </summary>
    private static void ForceUpdateSkinnedBounds(GameObject go)
    {
        SkinnedMeshRenderer[] renderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].updateWhenOffscreen = true;
        }
    }

    // ---------- Canvas construction ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("CharacterSelectCanvas", typeof(RectTransform));
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
        RectTransform root = BuildRoot(canvasGO.transform);
        BuildHeader(root);
        BuildRosterRow(root);
        BuildActions(root);
    }

    private void BuildBackground(Transform parent)
    {
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(parent, false);
        Stretch((RectTransform)bg.transform);
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
            Stretch((RectTransform)overlay.transform);
            Image overlayImg = overlay.GetComponent<Image>();
            Color32 ground = UITheme.Ground;
            overlayImg.color = new Color(ground.r / 255f, ground.g / 255f, ground.b / 255f, backgroundOverlayAlpha);
            overlayImg.raycastTarget = false;
        }
    }

    private RectTransform BuildRoot(Transform parent)
    {
        GameObject root = new GameObject("Root", typeof(RectTransform), typeof(VerticalLayoutGroup));
        root.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)root.transform;
        rt.anchorMin = new Vector2(0.05f, 0.06f);
        rt.anchorMax = new Vector2(0.95f, 0.94f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        VerticalLayoutGroup vlg = root.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 24;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        return rt;
    }

    private void BuildHeader(Transform parent)
    {
        GameObject header = new GameObject("Header", typeof(RectTransform), typeof(VerticalLayoutGroup));
        header.transform.SetParent(parent, false);

        VerticalLayoutGroup vlg = header.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 6;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        CreateText(header.transform, "Eyebrow", "PRE-GAME · YOUR AVATAR", 14, UITheme.TextMuted, letterSpacing: 12);
        CreateText(header.transform, "Title",   "PICK YOUR CHARACTER",   48, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        CreateText(header.transform, "Blurb",   "Everyone picks their own. Locked slots ship in a later sprint.", 15, UITheme.TextMuted);
    }

    private void BuildRosterRow(Transform parent)
    {
        GameObject row = new GameObject("RosterRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 14;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = true;

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 640;

        tileBorders.Clear();
        tileSelectedBadges.Clear();

        for (int i = 0; i < roster.Count; i++)
        {
            RenderTexture rt = i < previewTextures.Count ? previewTextures[i] : null;
            BuildRosterTile(row.transform, roster[i], rt);
        }
    }

    private void BuildRosterTile(Transform parent, CharacterDefinition def, RenderTexture previewTexture)
    {
        // A tile is "locked" only when there's NO way to show the character —
        // neither a prefab (for 3D preview) nor a portrait sprite (still image).
        bool locked = def.prefab == null && def.portraitSprite == null;

        GameObject card = new GameObject($"Tile_{def.id}", typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(parent, false);

        Image border = card.GetComponent<Image>();
        border.color = UITheme.StrokeHi;
        border.raycastTarget = true;
        tileBorders.Add(border);

        const int borderThickness = 6;
        GameObject inner = new GameObject("Inner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        inner.transform.SetParent(card.transform, false);
        RectTransform innerRT = (RectTransform)inner.transform;
        innerRT.anchorMin = Vector2.zero;
        innerRT.anchorMax = Vector2.one;
        innerRT.offsetMin = new Vector2(borderThickness, borderThickness);
        innerRT.offsetMax = new Vector2(-borderThickness, -borderThickness);

        Image innerFill = inner.GetComponent<Image>();
        innerFill.color = UITheme.SurfaceHi;
        innerFill.raycastTarget = false;

        VerticalLayoutGroup innerVlg = inner.GetComponent<VerticalLayoutGroup>();
        innerVlg.childAlignment = TextAnchor.UpperCenter;
        innerVlg.spacing = 0;
        innerVlg.padding = new RectOffset(0, 0, 0, 0);
        innerVlg.childControlWidth = true;
        innerVlg.childControlHeight = true;
        innerVlg.childForceExpandWidth = true;
        innerVlg.childForceExpandHeight = false;

        BuildTilePreview(inner.transform, def, previewTexture, locked);
        BuildTileInfo(inner.transform, def, locked);
        BuildTileSelectedBadge(card.transform);

        Button button = card.GetComponent<Button>();
        button.targetGraphic = innerFill;
        ColorBlock cb = button.colors;
        cb.normalColor = UITheme.SurfaceHi;
        cb.highlightedColor = UITheme.Ground2;
        cb.pressedColor = UITheme.Surface;
        cb.selectedColor = UITheme.Ground2;
        cb.disabledColor = UITheme.Surface;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        button.colors = cb;

        if (locked)
        {
            button.interactable = false;
            CanvasGroup dim = card.AddComponent<CanvasGroup>();
            dim.alpha = 0.55f;
            dim.interactable = false;
            dim.blocksRaycasts = true;
        }
        else
        {
            string capturedId = def.id;
            button.onClick.AddListener(() => OnTileClicked(capturedId));
        }
    }

    private void BuildTilePreview(Transform parent, CharacterDefinition def, RenderTexture previewTexture, bool locked)
    {
        GameObject preview = new GameObject("Preview", typeof(RectTransform), typeof(Image));
        preview.transform.SetParent(parent, false);

        // The tile-preview slot always has a solid signature-color backdrop so the
        // transparent-cleared render texture composites onto something character-appropriate.
        Image backdrop = preview.GetComponent<Image>();
        Color faded = def.signatureColor;
        faded.a = locked ? 0.35f : 0.85f;
        backdrop.color = faded;
        backdrop.raycastTarget = false;

        LayoutElement le = preview.AddComponent<LayoutElement>();
        le.preferredHeight = 520;

        if (def.portraitSprite != null)
        {
            // Portrait path — the source PNGs come out of image generators at 16:9
            // with lots of empty background around the character, and our tiles are
            // ~4:5 (taller than wide). A plain Image with preserveAspect fits the
            // WHOLE canvas inside the tile so the character shrinks and empty PNG
            // background floods the top/bottom.
            //
            // Fix: clip the preview slot with RectMask2D and render the portrait
            // with AspectRatioFitter in EnvelopeParent mode — the image scales to
            // COVER the tile height (character grows to fill vertically), overflow
            // gets clipped horizontally, empty PNG background around the character
            // never reaches the tile edge because it's outside the mask.
            preview.AddComponent<RectMask2D>();

            GameObject portraitGO = new GameObject("Portrait", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            portraitGO.transform.SetParent(preview.transform, false);

            RectTransform portRT = (RectTransform)portraitGO.transform;
            portRT.anchorMin = new Vector2(0.5f, 0.5f);
            portRT.anchorMax = new Vector2(0.5f, 0.5f);
            portRT.pivot = new Vector2(0.5f, 0.5f);
            portRT.anchoredPosition = Vector2.zero;

            Image portrait = portraitGO.GetComponent<Image>();
            portrait.sprite = def.portraitSprite;
            portrait.preserveAspect = false; // AspectRatioFitter handles ratio now
            portrait.raycastTarget = false;

            AspectRatioFitter arf = portraitGO.GetComponent<AspectRatioFitter>();
            arf.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            arf.aspectRatio = def.portraitSprite.rect.width / Mathf.Max(1f, def.portraitSprite.rect.height);

            BuildBottomFade(preview.transform, def.signatureColor);
        }
        else if (previewTexture != null)
        {
            GameObject rawGO = new GameObject("RenderPreview", typeof(RectTransform), typeof(RawImage));
            rawGO.transform.SetParent(preview.transform, false);
            Stretch((RectTransform)rawGO.transform);
            RawImage raw = rawGO.GetComponent<RawImage>();
            raw.texture = previewTexture;
            raw.raycastTarget = false;

            // Soft dark gradient in the lower ~40% of the preview slot so the
            // character's feet fade into the info block underneath instead of
            // hard-cutting on a flat signature color. Painted procedurally with
            // a stack of thin bands — no texture asset needed.
            BuildBottomFade(preview.transform, def.signatureColor);
        }
        else if (locked)
        {
            GameObject overlay = new GameObject("LockCover", typeof(RectTransform), typeof(Image));
            overlay.transform.SetParent(preview.transform, false);
            Stretch((RectTransform)overlay.transform);
            Image ovImg = overlay.GetComponent<Image>();
            ovImg.color = new Color(0.06f, 0.04f, 0.12f, 0.55f);
            ovImg.raycastTarget = false;

            TMP_Text lockText = CreateText(overlay.transform, "LockLabel", "COMING SOON", 14, UITheme.TextDim, letterSpacing: 8, wrap: false);
            Stretch(lockText.rectTransform);
            lockText.alignment = TextAlignmentOptions.Center;
        }
    }

    /// <summary>
    /// Stacks 8 thin semi-transparent bands at the bottom of the preview slot,
    /// each darker + more opaque than the last, to fake a soft vertical
    /// gradient without needing a gradient texture asset. Tint pulled from the
    /// signature color so each character's fade reads as their own accent.
    /// </summary>
    private void BuildBottomFade(Transform parent, Color tint)
    {
        const int bandCount = 8;
        const float fadeHeightPct = 0.45f;
        for (int i = 0; i < bandCount; i++)
        {
            GameObject band = new GameObject($"Fade_{i}", typeof(RectTransform), typeof(Image));
            band.transform.SetParent(parent, false);
            RectTransform rt = (RectTransform)band.transform;
            float t = i / (float)(bandCount - 1);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, fadeHeightPct * (1f - t));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            Image img = band.GetComponent<Image>();
            // Blend the signature tint toward the tile's dark plum surface
            // so the fade looks connected to the info block, not stamped on.
            Color darkPlum = UITheme.SurfaceHi;
            Color blended = Color.Lerp(tint * 0.35f, darkPlum, 0.75f);
            blended.a = 0.14f + (t * 0.10f);
            img.color = blended;
            img.raycastTarget = false;
        }
    }

    private void BuildTileInfo(Transform parent, CharacterDefinition def, bool locked)
    {
        GameObject info = new GameObject("Info", typeof(RectTransform), typeof(VerticalLayoutGroup));
        info.transform.SetParent(parent, false);

        VerticalLayoutGroup vlg = info.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.spacing = 4;
        vlg.padding = new RectOffset(12, 12, 14, 12);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        TMP_Text name = CreateText(info.transform, "Name", def.displayName, 34, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        name.alignment = TextAlignmentOptions.Center;

        Color metaColor = locked ? (Color)UITheme.TextDim : def.signatureColor;
        TMP_Text meta = CreateText(info.transform, "Meta", locked ? "LOCKED" : def.tagline, 13, metaColor, style: FontStyles.Bold, letterSpacing: 6);
        meta.alignment = TextAlignmentOptions.Center;
    }

    private void BuildTileSelectedBadge(Transform cardTransform)
    {
        GameObject badge = new GameObject("SelectedBadge", typeof(RectTransform), typeof(Image));
        badge.transform.SetParent(cardTransform, false);

        RectTransform badgeRT = (RectTransform)badge.transform;
        badgeRT.anchorMin = new Vector2(1, 1);
        badgeRT.anchorMax = new Vector2(1, 1);
        badgeRT.pivot = new Vector2(1, 1);
        badgeRT.anchoredPosition = new Vector2(-10, -10);
        badgeRT.sizeDelta = new Vector2(96, 22);

        Image bg = badge.GetComponent<Image>();
        bg.color = UITheme.Accent;
        bg.raycastTarget = false;

        TMP_Text txt = CreateText(badge.transform, "BadgeText", "SELECTED", 10, UITheme.White, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        Stretch(txt.rectTransform);
        txt.alignment = TextAlignmentOptions.Center;

        badge.SetActive(false);
        tileSelectedBadges.Add(badge);
    }

    private void BuildActions(Transform parent)
    {
        GameObject row = new GameObject("Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        row.transform.SetParent(parent, false);

        HorizontalLayoutGroup hlg = row.GetComponent<HorizontalLayoutGroup>();
        hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.spacing = 12;
        hlg.childControlWidth = false;
        hlg.childControlHeight = false;
        hlg.childForceExpandWidth = false;
        hlg.childForceExpandHeight = false;

        LayoutElement rowLE = row.AddComponent<LayoutElement>();
        rowLE.preferredHeight = 110;

        if (backSprite != null)
            BuildActionImageButton(row.transform, "BACK", backSprite, OnBackClicked, (int)backButtonSize.x, (int)backButtonSize.y);
        else
            BuildActionButton(row.transform, "BACK", ghost: true, onClick: OnBackClicked);

        if (confirmSprite != null)
            BuildActionImageButton(row.transform, "CONFIRM & CONTINUE", confirmSprite, OnConfirmClicked, (int)confirmButtonSize.x, (int)confirmButtonSize.y);
        else
            BuildActionButton(row.transform, "CONFIRM & CONTINUE", ghost: false, onClick: OnConfirmClicked);
    }

    private Button BuildActionImageButton(Transform parent, string name, Sprite sprite, Action onClick, int preferredWidth, int preferredHeight)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        // The parent HorizontalLayoutGroup has childControlWidth/Height=false so
        // Unity ignores LayoutElement.preferredWidth/Height and falls back to the
        // RectTransform's sizeDelta (default 100×100), which — combined with
        // preserveAspect on a wide button sprite — produced a tiny ~60×30 button.
        // Set sizeDelta directly so the button is the requested size regardless
        // of layout-group flags, and also add a LayoutElement so any future
        // layout-group config that DOES read LayoutElement still gets it right.
        RectTransform rt = (RectTransform)go.transform;
        rt.sizeDelta = new Vector2(preferredWidth, preferredHeight);

        LayoutElement le = go.AddComponent<LayoutElement>();
        le.preferredWidth = preferredWidth;
        le.preferredHeight = preferredHeight;
        le.minWidth = preferredWidth;
        le.minHeight = preferredHeight;

        Image img = go.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = true;

        Button btn = go.GetComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        return btn;
    }

    private void BuildActionButton(Transform parent, string label, bool ghost, Action onClick)
    {
        GameObject btn = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        btn.transform.SetParent(parent, false);

        LayoutElement le = btn.AddComponent<LayoutElement>();
        le.preferredWidth = ghost ? 180 : 320;
        le.preferredHeight = 56;

        Image strokeImg = btn.GetComponent<Image>();
        strokeImg.sprite = UITheme.PillSprite;
        strokeImg.type = Image.Type.Sliced;
        strokeImg.color = UITheme.ButtonBlueStroke;
        strokeImg.raycastTarget = true;

        Button button = btn.GetComponent<Button>();
        button.onClick.AddListener(() => onClick());

        Color32 fillColor  = ghost ? (Color32)UITheme.Ground   : UITheme.ButtonGreen;
        Color32 hoverColor = ghost ? (Color32)UITheme.Ground2  : UITheme.ButtonGreenHi;

        const int strokeThickness = 4;
        GameObject fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(btn.transform, false);
        RectTransform fillRT = (RectTransform)fillGO.transform;
        fillRT.anchorMin = Vector2.zero;
        fillRT.anchorMax = Vector2.one;
        fillRT.offsetMin = new Vector2(strokeThickness, strokeThickness);
        fillRT.offsetMax = new Vector2(-strokeThickness, -strokeThickness);
        Image fill = fillGO.GetComponent<Image>();
        fill.sprite = UITheme.PillSprite;
        fill.type = Image.Type.Sliced;
        fill.color = fillColor;
        fill.raycastTarget = false;

        button.targetGraphic = fill;
        ColorBlock cb = button.colors;
        cb.normalColor = fillColor;
        cb.highlightedColor = hoverColor;
        cb.pressedColor = fillColor;
        cb.selectedColor = hoverColor;
        cb.disabledColor = UITheme.Surface;
        cb.colorMultiplier = 1f;
        cb.fadeDuration = 0.12f;
        button.colors = cb;

        TMP_Text t = CreateText(fillGO.transform, "Label", label, 20, UITheme.White, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        Stretch(t.rectTransform);
        t.alignment = TextAlignmentOptions.Center;
        t.outlineColor = UITheme.ButtonBlueStroke;
        t.outlineWidth = 0.15f;
    }

    // ---------- Interactions ----------

    private void OnTileClicked(string id)
    {
        highlightedCharacterId = id;
        UpdateTileVisuals();
    }

    private void UpdateTileVisuals()
    {
        for (int i = 0; i < roster.Count; i++)
        {
            bool isSelected = roster[i].id == highlightedCharacterId;
            if (i < tileBorders.Count)
            {
                tileBorders[i].color = isSelected ? roster[i].signatureColor : (Color)UITheme.StrokeHi;
            }
            if (i < tileSelectedBadges.Count)
            {
                tileSelectedBadges[i].SetActive(isSelected);
            }
        }
    }

    private void OnBackClicked()
    {
        Hide();
        if (modeSelectUI != null) modeSelectUI.Show();
    }

    private void OnConfirmClicked()
    {
        if (string.IsNullOrEmpty(highlightedCharacterId))
        {
            Debug.LogWarning("[CharacterSelectUI] Confirm with no selection — ignoring.");
            return;
        }

        SelectedCharacterId = highlightedCharacterId;
        PlayerPrefs.SetString(PlayerPrefsKey, SelectedCharacterId);
        PlayerPrefs.Save();
        Debug.Log($"[CharacterSelectUI] Character confirmed: {SelectedCharacterId} → routing to {pendingNext}.");
        Hide();

        Confirmed?.Invoke(SelectedCharacterId);

        switch (pendingNext)
        {
            case NextScreen.LanConnect:
                if (lanConnectUI != null) lanConnectUI.Show();
                else Debug.LogWarning("[CharacterSelectUI] LanConnectUI not found — LAN flow can't proceed.");
                break;
            case NextScreen.HostJoinChoice:
            default:
                if (hostJoinChoiceUI != null) hostJoinChoiceUI.Show();
                else Debug.LogWarning("[CharacterSelectUI] HostJoinChoiceUI not found — Online flow can't proceed.");
                break;
        }
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

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
