using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F4 round-result screen. Auto-shows when GameRoundManager's Phase flips to
/// Ended, renders the outcome (Hunter/chain wins, last runner wins, runners
/// win on the 5-minute expiry, or no contest) and offers Play Again — visible
/// on every client, clickable on the host only (the server validates the
/// sender regardless). Auto-hides when the phase leaves Ended (Play Again) or
/// the session stops.
/// </summary>
public class ResultScreenUI : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
    [SerializeField] private int canvasSortingOrder = 120;   // above the lobby (90) and entry menus (100)

    /// <summary>True while the result screen is up — OrbitCamera leaves the cursor free.</summary>
    public static bool IsVisible { get; private set; }

    private CanvasGroup canvasGroup;
    private TMP_Text eyebrowText;
    private TMP_Text titleText;
    private TMP_Text detailText;
    private GameObject playAgainButton;
    private GameRoundManager roundManager;
    private bool subscribed;

    private void Start()
    {
        BuildCanvas();
        Hide();
        // Result content comes from the RPC payload — never from reading
        // LastWinner inside a Phase change handler: NetworkVariable deltas
        // apply in declaration order on clients, so Phase flips to Ended
        // BEFORE LastWinner updates (clients briefly see the stale default,
        // which is how the "No contest on the client" bug happened).
        GameRoundManager.RoundResultReceived += HandleRoundResult;
        TryBind();
    }

    private void Update()
    {
        // The manager is an in-scene NetworkObject, but binding may race its
        // spawn — retry until subscribed.
        if (!subscribed)
        {
            TryBind();
        }
    }

    private void TryBind()
    {
        roundManager = GameRoundManager.Instance;
        if (roundManager == null)
        {
            return;
        }

        roundManager.Phase.OnValueChanged += HandlePhaseChanged;
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientStopped += HandleSessionStopped;
        }
        subscribed = true;

        // Late bind while a round is already over (e.g. domain reload quirks).
        // Reading the NetworkVariables is safe HERE — the deltas settled long
        // before this frame; it's only Phase change handlers that race them.
        if (roundManager.Phase.Value == GameRoundManager.RoundPhase.Ended)
        {
            ShowResult(roundManager.LastWinner.Value, roundManager.LastWinnerClientId.Value);
        }
    }

    private void OnDestroy()
    {
        GameRoundManager.RoundResultReceived -= HandleRoundResult;
        if (subscribed)
        {
            if (roundManager != null)
            {
                roundManager.Phase.OnValueChanged -= HandlePhaseChanged;
            }
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientStopped -= HandleSessionStopped;
            }
        }
        IsVisible = false;
    }

    // ---------- State ----------

    private void HandleRoundResult(GameRoundManager.RoundWinner winner, ulong winnerClientId)
    {
        ShowResult(winner, winnerClientId);
    }

    private void HandlePhaseChanged(GameRoundManager.RoundPhase previous, GameRoundManager.RoundPhase current)
    {
        // Showing is RPC-driven (see Start); this handler only hides once the
        // round state moves on (Play Again reset the round — back to waiting).
        if (previous == GameRoundManager.RoundPhase.Ended && current != GameRoundManager.RoundPhase.Ended)
        {
            Hide();
        }
    }

    private void HandleSessionStopped(bool wasHost)
    {
        Hide();
    }

    private void ShowResult(GameRoundManager.RoundWinner winner, ulong winnerClientId)
    {
        switch (winner)
        {
            case GameRoundManager.RoundWinner.Chain:
                eyebrowText.text = "ROUND OVER";
                titleText.text = "HUNTER WINS";
                titleText.color = UITheme.Accent;
                detailText.text = "Every runner was caught — the chain takes the round.";
                break;
            case GameRoundManager.RoundWinner.LastRunner:
                eyebrowText.text = "ROUND OVER";
                titleText.text = "LAST RUNNER WINS";
                titleText.color = UITheme.Accent2;
                detailText.text = $"Player {winnerClientId} survived the final 60 seconds.";
                break;
            case GameRoundManager.RoundWinner.Runners:
                eyebrowText.text = "TIME'S UP";
                titleText.text = "RUNNERS WIN";
                titleText.color = UITheme.Accent2;
                detailText.text = "The 5-minute round ended with two or more runners uncaught — the Hunter loses.";
                break;
            default:
                eyebrowText.text = "ROUND OVER";
                titleText.text = "NO CONTEST";
                titleText.color = UITheme.Warn;
                detailText.text = "The round could not be completed.";
                break;
        }

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        playAgainButton.SetActive(isHost);

        canvasGroup.alpha = 1f;
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;
        IsVisible = true;
    }

    private void Hide()
    {
        if (canvasGroup == null) return;
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        IsVisible = false;
    }

    private void OnPlayAgainClicked()
    {
        if (roundManager == null)
        {
            Debug.LogError("[ResultScreenUI] Play Again clicked but GameRoundManager is missing.");
            return;
        }
        Debug.Log("[ResultScreenUI] Play Again clicked — requesting round reset.");
        roundManager.RequestPlayAgainRpc();
    }

    // ---------- Canvas construction (house style — see HostJoinChoiceUI) ----------

    private void BuildCanvas()
    {
        GameObject canvasGO = new GameObject("ResultScreenCanvas", typeof(RectTransform));
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

        // Dim background over the arena — result overlays gameplay, not a full page.
        GameObject bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(canvasGO.transform, false);
        RectTransform bgRT = (RectTransform)bg.transform;
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        Image bgImg = bg.GetComponent<Image>();
        bgImg.color = new Color32(0x0F, 0x0B, 0x1F, 0xD9);   // UITheme.Ground @ ~85% alpha
        bgImg.raycastTarget = true;

        // Centre column.
        GameObject col = new GameObject("Column", typeof(RectTransform), typeof(VerticalLayoutGroup));
        col.transform.SetParent(canvasGO.transform, false);
        RectTransform colRT = (RectTransform)col.transform;
        colRT.anchorMin = new Vector2(0.5f, 0.5f);
        colRT.anchorMax = new Vector2(0.5f, 0.5f);
        colRT.pivot = new Vector2(0.5f, 0.5f);
        colRT.sizeDelta = new Vector2(760, 420);
        colRT.anchoredPosition = Vector2.zero;
        VerticalLayoutGroup vlg = col.GetComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.MiddleCenter;
        vlg.spacing = 24;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        eyebrowText = CreateText(col.transform, "Eyebrow", "ROUND OVER", 14, UITheme.TextMuted, letterSpacing: 12);
        titleText = CreateText(col.transform, "Title", "", 64, UITheme.Text, style: FontStyles.Bold, letterSpacing: 4, wrap: false);
        detailText = CreateText(col.transform, "Detail", "", 16, UITheme.TextMuted);

        playAgainButton = BuildPlayAgainButton(col.transform);
    }

    private GameObject BuildPlayAgainButton(Transform parent)
    {
        GameObject card = new GameObject("PlayAgain", typeof(RectTransform), typeof(Image), typeof(Button));
        card.transform.SetParent(parent, false);
        LayoutElement le = card.AddComponent<LayoutElement>();
        le.preferredHeight = 64;
        le.preferredWidth = 320;

        Image bg = card.GetComponent<Image>();
        bg.color = UITheme.Accent;

        Button button = card.GetComponent<Button>();
        ColorBlock cb = button.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1f, 1f, 1f, 0.92f);
        cb.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        button.colors = cb;
        button.onClick.AddListener(OnPlayAgainClicked);

        TMP_Text label = CreateText(card.transform, "Label", "PLAY AGAIN", 24, UITheme.Text, style: FontStyles.Bold, letterSpacing: 6, wrap: false);
        RectTransform labelRT = (RectTransform)label.transform;
        labelRT.anchorMin = Vector2.zero;
        labelRT.anchorMax = Vector2.one;
        labelRT.offsetMin = Vector2.zero;
        labelRT.offsetMax = Vector2.zero;

        return card;
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
