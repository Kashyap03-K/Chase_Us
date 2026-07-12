using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// F3 chain mechanics — per-player chain state, replicated to every client.
///
/// Lives on the player prefab next to PlayerMovement. The server (via
/// <see cref="ChainManager"/>) flips <see cref="IsCaught"/> and points
/// <see cref="Ahead"/> at the chain member this player is jointed to; clients
/// only ever read these.
///
/// Also owns two client-visible concerns:
///   * The placeholder rope visual — a LineRenderer from this player to the
///     member ahead, drawn locally on every peer from replicated transforms.
///     (Sprint 3 replaces this with the hand-hold IK layer.)
///   * <see cref="SpeedMultiplier"/> — the hunter's chain-length speed penalty
///     factor, written by ChainManager, consumed by PlayerMovement on the
///     server. Defaults to 1 (no penalty) for everyone else.
///
/// The physical bodies (Rigidbody, CapsuleCollider, ConfigurableJoint) are a
/// server-only concern: the prefab ships with a kinematic Rigidbody and a
/// disabled CapsuleCollider, and only ChainManager (server) ever makes them
/// dynamic. Client-side rigidbodies stay kinematic forever, so the existing
/// NetworkTransform replication remains the single source of client motion.
/// </summary>
public class ChainLink : NetworkBehaviour
{
    [Header("Rope placeholder (Sprint 3: replaced by hand-hold IK)")]
    [SerializeField] private float lineWidth = 0.06f;
    [SerializeField] private Color lineColor = new Color(0.706f, 0.486f, 1f, 1f); // UITheme.Chain purple
    [SerializeField, Tooltip("Rope is drawn at roughly hand height, not at the feet pivot.")]
    private float lineHeightOffset = 1f;

    /// <summary>True from the moment this player is caught into the chain. Server-write.</summary>
    public NetworkVariable<bool> IsCaught = new NetworkVariable<bool>();

    /// <summary>The chain member this player is jointed to (valid while caught). Server-write.</summary>
    public NetworkVariable<NetworkObjectReference> Ahead = new NetworkVariable<NetworkObjectReference>();

    /// <summary>
    /// Hunter-only speed penalty factor (1 = no penalty). Written by ChainManager
    /// as the chain grows; PlayerMovement multiplies its speed by this on the server.
    /// </summary>
    public NetworkVariable<float> SpeedMultiplier = new NetworkVariable<float>(1f);

    /// <summary>Kinematic on the prefab; the server makes it dynamic on catch.</summary>
    public Rigidbody Body { get; private set; }

    /// <summary>Disabled on the prefab; the server enables it on catch (the CC turns off).</summary>
    public CapsuleCollider ChainCollider { get; private set; }

    /// <summary>Server-only: the joint connecting this player to <see cref="Ahead"/>.</summary>
    public ConfigurableJoint JointToAhead { get; set; }

    /// <summary>
    /// The clientId this player object belongs to, captured at spawn — the same
    /// stable key F4's CatchDetector uses (OwnerClientId reverts to the server
    /// when the owner disconnects, so it is NOT a stable identifier).
    /// </summary>
    public ulong PlayerClientId { get; private set; }

    /// <summary>Every spawned ChainLink — lets ChainManager resolve clientIds without scene scans.</summary>
    public static IReadOnlyList<ChainLink> All => all;
    private static readonly List<ChainLink> all = new List<ChainLink>();

    private LineRenderer line;

    private void Awake()
    {
        Body = GetComponent<Rigidbody>();
        ChainCollider = GetComponent<CapsuleCollider>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        PlayerClientId = OwnerClientId;
        if (!all.Contains(this)) all.Add(this);
    }

    public override void OnNetworkDespawn()
    {
        all.Remove(this);
        base.OnNetworkDespawn();
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsCaught.Value || !Ahead.Value.TryGet(out NetworkObject aheadObject))
        {
            if (line != null) line.enabled = false;
            return;
        }

        EnsureLine();
        line.enabled = true;
        line.SetPosition(0, transform.position + Vector3.up * lineHeightOffset);
        line.SetPosition(1, aheadObject.transform.position + Vector3.up * lineHeightOffset);
    }

    private void EnsureLine()
    {
        if (line != null) return;

        line = gameObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.startWidth = lineWidth;
        line.endWidth = lineWidth;
        line.useWorldSpace = true;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;

        // URP first; Sprites/Default keeps it visible if the pipeline ever changes.
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        Material mat = new Material(shader);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", lineColor);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", lineColor);
        line.material = mat;
        line.startColor = lineColor;
        line.endColor = lineColor;
    }
}
