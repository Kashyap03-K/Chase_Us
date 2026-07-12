using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F4 round countdown — small top-centre HUD visible on every client while a
/// round runs. The server writes a single deadline (NGO ServerTime) per phase
/// into GameRoundManager.PhaseEndsAtServerTime; each client renders the
/// remaining mm:ss locally against the synced clock, so the display costs no
/// replication traffic and stays in sync within clock-sync error.
///
/// ROUND (cyan) = the 5-minute overall timer; LAST RUNNER (amber) = the 60s
/// endgame that replaces it. Purely passive — no raycasts, no cursor claim.
/// </summary>
public class RoundTimerHUD : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 80;   // below every menu screen

    private CanvasGroup canvasGroup;
    private TMP_Text phaseLabel;
    private TMP_Text timeText;
    private bool shown;

    private void Start()
    {
        BuildCanvas();
        SetShown(false);
    }

    private void Update()
    {
        GameRoundManager manager = GameRoundManager.Instance;
        NetworkManager nm = NetworkManager.Singleton;

        bool roundRunning = manager != null && nm != null && nm.IsListening &&
                            (manager.Phase.Value == GameRoundManager.RoundPhase.Active ||
                             manager.Phase.Value == GameRoundManager.RoundPhase.Endgame) &&
                            manager.PhaseEndsAtServerTime.Value > 0d;

        SetShown(roundRunning);
        if (!roundRunning)
        {
            return;
        }

        double remaining = manager.PhaseEndsAtServerTime.Value - nm.ServerTime.Time;
        if (remaining < 0d)
        {
            remaining = 0d;   // client clock slightly ahead of the server's end event
        }

        int minutes = (int)(remaining / 60d);
        int seconds = (int)(remaining % 60d);
        timeText.text = $"{minutes}:{seconds:00}";

        bool endgame = manager.Phase.Value == GameRoundManager.RoundPhase.Endgame;
        phaseLabel.text = endgame ? "LAST RUNNER" : "ROUND";
        phaseLabel.color = endgame ? (Color)UITheme.Warn : (Color)UITheme.Accent2;
        timeText.color = endgame ? (Color)UITheme.Warn : (Color)UITheme.Text;
    }

    private void SetShown(bool value)
    {
        if (canvasGroup == null || shown == value)
        {
            return;
        }
        shown = value;
        canvasGroup.alpha = value ? 1f : 0f;
    }

    // ---------- Canvas construction (house style) ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("RoundTimerCanvas", typeof(RectTransform));
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

        canvasGroup = canvasGO.AddComponent<CanvasGroup>();
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;

        GameObject panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRT = (RectTransform)panel.transform;
        panelRT.anchorMin = new Vector2(0.5f, 1f);
        panelRT.anchorMax = new Vector2(0.5f, 1f);
        panelRT.pivot = new Vector2(0.5f, 1f);
        panelRT.anchoredPosition = new Vector2(0f, -18f);
        panelRT.sizeDelta = new Vector2(220, 92);
        Image panelBg = panel.GetComponent<Image>();
        panelBg.color = new Color32(0x1A, 0x15, 0x30, 0xCC);   // UITheme.Surface @ 80% alpha
        panelBg.raycastTarget = false;
        VerticalLayoutGroup vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 2;
        vlg.padding = new RectOffset(16, 16, 10, 10);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        phaseLabel = CreateText(panel.transform, "PhaseLabel", "ROUND", 13, UITheme.Accent2, letterSpacing: 8);
        timeText = CreateText(panel.transform, "Time", "5:00", 40, UITheme.Text, style: FontStyles.Bold, letterSpacing: 2);
    }

    private TMP_Text CreateText(Transform parent, string name, string content, int fontSize, Color color,
                                FontStyles style = FontStyles.Normal, float letterSpacing = 0f)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.text = content;
        t.fontSize = fontSize;
        t.color = color;
        t.fontStyle = style;
        t.characterSpacing = letterSpacing;
        t.enableWordWrapping = false;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}
