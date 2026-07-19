using System.Collections;
using UnityEngine;

/// <summary>
/// G3 — lightweight global audio for Chase Us. A DontDestroyOnLoad singleton
/// (same Instance pattern as GameRoundManager) with two channels:
///   * one 2D one-shot SFX source (<see cref="PlaySfx"/> → PlayOneShot);
///   * a pair of music sources for a simple linear crossfade
///     (<see cref="PlayMusic"/> / <see cref="Fade"/> / <see cref="StopMusic"/>).
///
/// Every clip in the G3 manifest is a serialized field, assigned in the
/// Inspector on the _Bootstrap AudioManager. Callers reference them off the
/// singleton, e.g.:
///     var am = AudioManager.Instance;
///     if (am != null) am.PlaySfx(am.chainCatch);
/// The clip-level null check in PlaySfx/PlayMusic means an unassigned clip is a
/// silent no-op rather than an error.
///
/// FUTURE WORK (out of G3 scope): AudioMixer groups + per-bus volume sliders /
/// a settings menu. Deliberately not built yet — PlaySfx/PlayMusic would route
/// through mixer groups once those exist, no call-site changes needed.
/// </summary>
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("SFX clips")]
    public AudioClip chainCatch;
    public AudioClip jump;
    public AudioClip footstep;
    public AudioClip landing;
    public AudioClip respawn;        // reuse: same asset as `landing` (no separate clip supplied)
    public AudioClip chainRemove;    // reuse: same asset as `chainCatch` (no separate clip supplied)
    public AudioClip roundStart;
    public AudioClip roleReveal;     // one clip for both hunter + runner reveal
    public AudioClip endgameTrigger;
    public AudioClip timerLastSeconds;
    public AudioClip roundWin;
    public AudioClip roundLose;
    public AudioClip uiClick;

    [Header("Music clips")]
    public AudioClip musicMenu;
    public AudioClip musicEndgame;

    [Header("Levels")]
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;
    [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.6f;

    private AudioSource sfxSource;
    private AudioSource musicA;
    private AudioSource musicB;
    private AudioSource activeMusic;   // the source currently (or last) playing music
    private Coroutine musicFade;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        sfxSource = CreateSource("SFX", loop: false);
        musicA = CreateSource("MusicA", loop: true);
        musicB = CreateSource("MusicB", loop: true);
        activeMusic = musicA;
    }

    private AudioSource CreateSource(string name, bool loop)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        AudioSource source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 0f;   // 2D — UI/global audio, no positional falloff
        return source;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ---------- SFX ----------

    /// <summary>Fire-and-forget 2D one-shot. Null clip = silent no-op.</summary>
    public void PlaySfx(AudioClip clip)
    {
        if (clip == null || sfxSource == null) return;
        sfxSource.PlayOneShot(clip, sfxVolume);
    }

    // ---------- Music ----------

    /// <summary>
    /// Starts <paramref name="clip"/> immediately on the active music source
    /// (no fade). Use <see cref="Fade"/> for a crossfade transition.
    /// </summary>
    public void PlayMusic(AudioClip clip, bool loop = true)
    {
        if (clip == null || activeMusic == null) return;
        StopMusicFade();
        activeMusic.clip = clip;
        activeMusic.loop = loop;
        activeMusic.volume = musicVolume;
        activeMusic.Play();
    }

    /// <summary>
    /// Linear crossfade from whatever is playing to <paramref name="next"/> over
    /// <paramref name="duration"/> seconds. Swaps between the two music sources
    /// so the outgoing track fades while the incoming one rises.
    /// </summary>
    public void Fade(AudioClip next, float duration)
    {
        if (next == null) return;
        if (duration <= 0f)
        {
            PlayMusic(next);
            return;
        }

        StopMusicFade();

        AudioSource from = activeMusic;
        AudioSource to = activeMusic == musicA ? musicB : musicA;

        to.clip = next;
        to.loop = true;
        to.volume = 0f;
        to.Play();

        activeMusic = to;
        musicFade = StartCoroutine(CrossfadeRoutine(from, to, duration));
    }

    /// <summary>Fades the current music to silence over <paramref name="fadeOutSeconds"/> (0 = instant), then stops it.</summary>
    public void StopMusic(float fadeOutSeconds = 0f)
    {
        StopMusicFade();

        if (fadeOutSeconds <= 0f)
        {
            if (musicA != null) musicA.Stop();
            if (musicB != null) musicB.Stop();
            return;
        }

        musicFade = StartCoroutine(FadeOutRoutine(activeMusic, fadeOutSeconds));
    }

    private IEnumerator CrossfadeRoutine(AudioSource from, AudioSource to, float duration)
    {
        float t = 0f;
        float startFrom = from != null ? from.volume : 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            if (from != null) from.volume = Mathf.Lerp(startFrom, 0f, k);
            if (to != null) to.volume = Mathf.Lerp(0f, musicVolume, k);
            yield return null;
        }
        if (from != null)
        {
            from.Stop();
            from.volume = musicVolume;
        }
        if (to != null) to.volume = musicVolume;
        musicFade = null;
    }

    private IEnumerator FadeOutRoutine(AudioSource source, float duration)
    {
        if (source == null) yield break;
        float start = source.volume;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            source.volume = Mathf.Lerp(start, 0f, Mathf.Clamp01(t / duration));
            yield return null;
        }
        source.Stop();
        source.volume = musicVolume;
        musicFade = null;
    }

    private void StopMusicFade()
    {
        if (musicFade != null)
        {
            StopCoroutine(musicFade);
            musicFade = null;
        }
    }
}
