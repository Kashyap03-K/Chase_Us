using System;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;

/// <summary>
/// Initializes Unity Gaming Services and signs in anonymously on startup.
/// Other systems (e.g. NetworkBootstrap) must check IsSignedIn before using Relay.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class ServicesBootstrap : MonoBehaviour
{
    public static ServicesBootstrap Instance { get; private set; }

    /// <summary>Fired once anonymous sign-in has completed successfully.</summary>
    public event Action SignedIn;

    /// <summary>True only after UGS is initialized and anonymous sign-in succeeded.</summary>
    public bool IsSignedIn =>
        UnityServices.State == ServicesInitializationState.Initialized &&
        AuthenticationService.Instance.IsSignedIn;

    public string PlayerId => IsSignedIn ? AuthenticationService.Instance.PlayerId : null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private async void Start()
    {
        try
        {
            InitializationOptions options = new InitializationOptions();
#if UNITY_EDITOR
            // ParrelSync clones share PlayerPrefs with the source project, so without
            // separate auth profiles both editors would reuse the same cached anonymous
            // session and sign in with identical PlayerIds. The project path differs
            // between the source project and each clone, so it makes a stable per-editor
            // profile name.
            options.SetProfile($"editor{Mathf.Abs(Application.dataPath.GetHashCode() % 1000000)}");
#endif
            await UnityServices.InitializeAsync(options);
            Debug.Log("[ServicesBootstrap] UGS initialized.");
        }
        catch (Exception e)
        {
            Debug.LogError("[ServicesBootstrap] UGS initialization FAILED. Check internet " +
                           $"connectivity and that this project is linked to Unity Cloud. Details: {e}");
            return;
        }

        try
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            Debug.Log($"[ServicesBootstrap] Signed in anonymously. PlayerId: {AuthenticationService.Instance.PlayerId} " +
                      $"(profile: {AuthenticationService.Instance.Profile})");
            SignedIn?.Invoke();
        }
        catch (AuthenticationException e)
        {
            Debug.LogError($"[ServicesBootstrap] Anonymous sign-in FAILED (authentication error {e.ErrorCode}): {e.Message}");
        }
        catch (RequestFailedException e)
        {
            Debug.LogError($"[ServicesBootstrap] Anonymous sign-in FAILED (request/network error {e.ErrorCode}): {e.Message}");
        }
    }
}
