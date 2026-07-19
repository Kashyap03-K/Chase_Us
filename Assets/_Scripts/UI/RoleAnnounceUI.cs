using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F4 role banner — "YOU ARE THE HUNTER" / "YOU ARE A RUNNER", shown for a few
/// seconds on every peer whenever a round starts (first round and Play Again
/// restarts alike). Driven by <see cref="GameRoundManager.RoundStarted"/>,
/// whose RPC payload carries the Hunter's clientId — deliberately NOT read
/// from the HunterClientId NetworkVariable inside a phase-change handler,
/// which would race the variable's own delta on clients.
/// Purely passive: no raycasts, no cursor claim.
/// </summary>
public class RoleAnnounceUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 110;   // above lobby (90) and timer (80), below result (120)
    [SerializeField] private float displaySeconds = 3.5f;
    [SerializeField] private float fadeSeconds = 0.4f;

    private CanvasGroup canvasGroup;
    private TMP_Text eyebrowText;
    private TMP_Text titleText;
    private TMP_Text detailText;
    private Coroutine hideRoutine;

    private void Start()
    {
        BuildCanvas();
        canvasGroup.alpha = 0f;
        GameRoundManager.RoundStarted += HandleRoundStarted;
    }

    private void OnDestroy()
    {
        GameRoundManager.RoundStarted -= HandleRoundStarted;
    }

    private void HandleRoundStarted(ulong hunterClientId, int roundNumber)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || canvasGroup == null)
        {
            return;
        }

        bool isHunter = nm.LocalClientId == hunterClientId;
        eyebrowText.text = $"ROUND {roundNumber}";
        titleText.text = isHunter ? "YOU ARE THE HUNTER" : "YOU ARE A RUNNER";
        titleText.color = isHunter ? (Color)UITheme.Accent : (Color)UITheme.Accent2;
        detailText.text = isHunter
            ? "Catch every runner before the clock runs out."
            : "Stay away from the Hunter. Survive.";

        // G3 audio — role reveal sting (same clip for hunter + runner, no distinct
        // variants supplied). This handler runs on every peer via RoundStarted.
        AudioManager am = AudioManager.Instance;
        if (am != null) am.PlaySfx(am.roleReveal);

        canvasGroup.alpha = 1f;
        if (hideRoutine != null)
        {
            StopCoroutine(hideRoutine);
        }
        hideRoutine = StartCoroutine(HideAfterDelay());
    }

    private IEnumerator HideAfterDelay()
    {
        yield return new WaitForSeconds(displaySeconds);
        for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
        {
            canvasGroup.alpha = 1f - (t / fadeSeconds);
            yield return null;
        }
        canvasGroup.alpha = 0f;
        hideRoutine = null;
    }

    // ---------- Canvas construction (house style) ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("RoleAnnounceCanvas", typeof(RectTransform));
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

        // Banner strip in the upper third — visible without hiding the arena.
        GameObject panel = new GameObject("Banner", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(canvasGO.transform, false);
        RectTransform panelRT = (RectTransform)panel.transform;
        panelRT.anchorMin = new Vector2(0.5f, 1f);
        panelRT.anchorMax = new Vector2(0.5f, 1f);
        panelRT.pivot = new Vector2(0.5f, 1f);
        panelRT.anchoredPosition = new Vector2(0f, -200f);
        panelRT.sizeDelta = new Vector2(820, 170);
        Image bg = panel.GetComponent<Image>();
        bg.color = new Color32(0x0F, 0x0B, 0x1F, 0xD9);   // UITheme.Ground @ ~85% alpha
        bg.raycastTarget = false;
        VerticalLayoutGroup vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 6;
        vlg.padding = new RectOffset(24, 24, 18, 18);
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        eyebrowText = CreateText(panel.transform, "Eyebrow", "ROUND 1", 14, UITheme.TextMuted, letterSpacing: 10);
        titleText = CreateText(panel.transform, "Title", "", 52, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        detailText = CreateText(panel.transform, "Detail", "", 15, UITheme.TextMuted);
    }

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
        t.characterSpacing = letterSpacing;
        t.enableWordWrapping = wrap;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}
