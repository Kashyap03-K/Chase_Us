using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative visual skin for a player. Sits on the networked player
/// prefab alongside <see cref="PlayerMovement"/>. Owner reports its
/// CharacterSelectUI choice at spawn; server writes the resolved roster index
/// into <see cref="characterIndex"/>; every peer (including the owner) reacts
/// to that NetworkVariable by parenting a fresh visual prefab under the
/// <see cref="modelRoot"/> transform.
///
/// Why a NetworkVariable and not just per-client instantiation:
///   * Everyone sees everyone. If Kai's client picked Nova, every OTHER client
///     needs the Nova mesh on Kai's body, not their own last-picked default.
///   * Late-joiners get the current value from the initial sync — no bespoke
///     "hello, what did you pick" RPC round-trip.
///   * Server has final say. Untrusted client input is clamped through the
///     ServerRpc, matching the host-authority stance elsewhere in the codebase.
///
/// Only the VISUAL is swapped — the NetworkObject, CharacterController,
/// colliders, PlayerMovement etc. all live on the root and stay identical for
/// every character. That's the whole point of Fixed rig humanoids: same
/// physical footprint, different skin. Animator Controller / real gameplay
/// clips are follow-up scope (currently the imported Meshy FBX ships with its
/// walk clip as the default state, which is enough to smoke-test that the
/// selection is propagating).
/// </summary>
[DisallowMultipleComponent]
public class PlayerCharacterVisual : NetworkBehaviour
{
    /// <summary>Sentinel meaning "no valid choice yet" — the initial NetworkVariable value.</summary>
    private const byte NoChoice = byte.MaxValue;

    [Header("Wiring")]
    [Tooltip("Empty child transform under which each character's visual FBX is instantiated. Required.")]
    [SerializeField] private Transform modelRoot;

    [Tooltip("Fallback roster index used when a client has never opened Character Select (fresh PlayerPrefs) or when the picked id isn't in the library. Default 0 = first entry (Vigo).")]
    [SerializeField, Range(0, 31)] private int fallbackIndex = 0;

    [Tooltip("Shared Humanoid Animator Controller applied to every instantiated character's Animator. Overrides whatever controller shipped with the FBX so Idle/Walk/Run/Jump/Hit/Punch play from one canonical clip set that retargets via Humanoid Avatar. Build via Tools → Chase Us → Build Player Animator Controller.")]
    [SerializeField] private RuntimeAnimatorController playerController;

    // Server-write, everyone-read. Init to NoChoice so we can tell "not chosen yet"
    // apart from "chose index 0". Server clamps every incoming choice to the
    // configured CharacterLibrary range before writing.
    private readonly NetworkVariable<byte> characterIndex = new NetworkVariable<byte>(
        NoChoice,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private GameObject currentVisual;
    private byte appliedIndex = NoChoice;

    private void Awake()
    {
        if (modelRoot == null)
        {
            // Auto-create a Model child so a fresh player prefab still works.
            Transform found = transform.Find("Model");
            if (found == null)
            {
                GameObject go = new GameObject("Model");
                go.transform.SetParent(transform, false);
                found = go.transform;
            }
            modelRoot = found;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        characterIndex.OnValueChanged += HandleIndexChanged;

        // Late-joiners see the current value on spawn without a change event
        // firing, so mirror the swap eagerly.
        if (characterIndex.Value != NoChoice)
        {
            ApplyVisual(characterIndex.Value);
        }

        if (IsOwner)
        {
            SubmitLocalChoice();
        }
    }

    public override void OnNetworkDespawn()
    {
        characterIndex.OnValueChanged -= HandleIndexChanged;
        base.OnNetworkDespawn();
    }

    /// <summary>Owner-only: read the local CharacterSelectUI pick and tell the server.</summary>
    private void SubmitLocalChoice()
    {
        CharacterLibrary lib = CharacterLibrary.Instance;
        if (lib == null)
        {
            Debug.LogError("[PlayerCharacterVisual] No CharacterLibrary in the scene — cannot resolve the picked character. Add CharacterLibrary to _Bootstrap and populate its Entries list.");
            return;
        }

        int idx = lib.IndexOf(CharacterSelectUI.SelectedCharacterId);
        if (idx < 0)
        {
            Debug.LogWarning($"[PlayerCharacterVisual] Selected id '{CharacterSelectUI.SelectedCharacterId ?? "(none)"}' not in CharacterLibrary — falling back to index {fallbackIndex}.");
            idx = Mathf.Clamp(fallbackIndex, 0, Mathf.Max(0, lib.Count - 1));
        }

        if (IsServer)
        {
            // Host shortcut — bypass RPC self-round-trip.
            ServerApplyChoice((byte)idx);
        }
        else
        {
            SubmitChoiceRpc((byte)idx);
        }
    }

    [Rpc(SendTo.Server)]
    private void SubmitChoiceRpc(byte requestedIndex)
    {
        ServerApplyChoice(requestedIndex);
    }

    private void ServerApplyChoice(byte requestedIndex)
    {
        CharacterLibrary lib = CharacterLibrary.Instance;
        int max = lib != null ? Mathf.Max(0, lib.Count - 1) : 0;
        byte clamped = (byte)Mathf.Clamp(requestedIndex, 0, max);
        if (lib == null || lib.GetVisualPrefab(clamped) == null)
        {
            // Library missing or the chosen slot has no visual — try the fallback
            // instead of leaving the character as a floating collider capsule.
            clamped = (byte)Mathf.Clamp(fallbackIndex, 0, max);
        }
        characterIndex.Value = clamped;
    }

    private void HandleIndexChanged(byte previous, byte current)
    {
        ApplyVisual(current);
    }

    private void ApplyVisual(byte index)
    {
        if (index == NoChoice) return;
        if (index == appliedIndex && currentVisual != null) return;

        CharacterLibrary lib = CharacterLibrary.Instance;
        GameObject prefab = lib != null ? lib.GetVisualPrefab(index) : null;
        if (prefab == null)
        {
            Debug.LogWarning($"[PlayerCharacterVisual] No visual prefab wired for index {index} ({(lib != null ? lib.GetId(index) : "?")}) — leaving current visual in place.");
            return;
        }

        if (currentVisual != null)
        {
            Destroy(currentVisual);
            currentVisual = null;
        }

        currentVisual = Instantiate(prefab, modelRoot);
        currentVisual.name = $"Visual_{(lib != null ? lib.GetId(index) : index.ToString())}";
        // Per-character offset + scale so a Meshy export whose root bone sits
        // above the CharacterController capsule can be tuned down (or up) in
        // the CharacterLibrary Inspector without touching the prefab.
        (float offsetY, float scale) = lib != null ? lib.GetVisualTransform(index) : (0f, 1f);
        currentVisual.transform.localPosition = new Vector3(0f, offsetY, 0f);
        currentVisual.transform.localRotation = Quaternion.identity;
        currentVisual.transform.localScale = Vector3.one * scale;

        // Meshy walking exports have root motion baked in — kill it so the
        // CharacterController stays in charge of position. If we ever swap in a
        // proper Animator Controller, this stays correct (root motion off is
        // right for a CC-driven player regardless).
        Animator anim = currentVisual.GetComponentInChildren<Animator>();
        if (anim != null)
        {
            anim.applyRootMotion = false;
            // Override the character's per-FBX Animator Controller with the
            // shared Humanoid one so all 5 characters play the same locomotion
            // blend tree + one-shot triggers. The character's own Humanoid
            // Avatar stays — that's how retargeting works.
            if (playerController != null)
            {
                anim.runtimeAnimatorController = playerController;
            }
        }

        // Hand the fresh Animator to PlayerMovement so its Update loop can drive
        // the speed blend on the new mesh. The Inspector-wired one now points at
        // a destroyed GameObject and would silently no-op.
        PlayerMovement movement = GetComponent<PlayerMovement>();
        if (movement != null) movement.RebindAnimator(anim);

        appliedIndex = index;

        if (lib != null)
        {
            Debug.Log($"[PlayerCharacterVisual] Visual swapped → '{lib.GetId(index)}' (index {index}) for client {OwnerClientId}.");
        }
    }
}
