using Unity.Netcode;
using UnityEngine;

/// <summary>
/// G3 music director — the single owner of "what music plays when". Lives on
/// _Bootstrap next to AudioManager.
///
/// PLACEMENT NOTE (flagged for Tarang): the G3 brief suggested booting menu
/// music from "the earliest UI script (likely ModeSelectUI)". It lives here in
/// a dedicated controller instead, because menu-boot music, the Endgame
/// crossfade and the round-end stop are all one concern (music state driven by
/// GameRoundManager.Phase) — scattering them across a UI screen and a gameplay
/// hook would split that logic. Easy to move into ModeSelectUI if preferred.
///
/// Behaviour:
///   * Boot / lobby (Idle)  → menu music on loop.
///   * Round starts (Active)→ fade to silence (no music_round clip supplied — open question).
///   * Endgame              → crossfade to musicEndgame.
///   * Round over (Ended)   → fade out.
/// </summary>
public class GameMusicController : MonoBehaviour
{
    [SerializeField, Tooltip("Crossfade / fade-out duration for music transitions (seconds).")]
    private float fadeSeconds = 1.25f;

    private GameRoundManager boundRoundManager;

    private void Start()
    {
        AudioManager am = AudioManager.Instance;
        if (am != null) am.PlayMusic(am.musicMenu);

        TryBind();
    }

    private void Update()
    {
        // GameRoundManager is a scene NetworkObject that may bind a frame or two
        // after us; keep trying until it's available (cheap, stops once bound).
        if (boundRoundManager == null) TryBind();
    }

    private void OnDestroy()
    {
        if (boundRoundManager != null)
        {
            boundRoundManager.Phase.OnValueChanged -= HandlePhaseChanged;
            boundRoundManager = null;
        }
    }

    private void TryBind()
    {
        if (boundRoundManager != null || GameRoundManager.Instance == null) return;
        boundRoundManager = GameRoundManager.Instance;
        boundRoundManager.Phase.OnValueChanged += HandlePhaseChanged;
    }

    private void HandlePhaseChanged(GameRoundManager.RoundPhase previous, GameRoundManager.RoundPhase current)
    {
        AudioManager am = AudioManager.Instance;
        if (am == null) return;

        switch (current)
        {
            case GameRoundManager.RoundPhase.Idle:
                // Play Again returned everyone to the lobby — bring menu music back.
                am.Fade(am.musicMenu, fadeSeconds);
                break;

            case GameRoundManager.RoundPhase.Active:
                // No round music bed for now (open question) — silence during play.
                am.StopMusic(fadeSeconds);
                break;

            case GameRoundManager.RoundPhase.Endgame:
                am.Fade(am.musicEndgame, fadeSeconds);
                break;

            case GameRoundManager.RoundPhase.Ended:
                am.StopMusic(fadeSeconds);
                break;
        }
    }
}
