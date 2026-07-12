# Chase Us — Networking Setup (Sprint 1, Track A)

Foundation for all multiplayer work: Unity Netcode for GameObjects (NGO) over
Unity Relay, join-code based. Verified 2026-07-06 with two ParrelSync editor
instances connected over Relay (host + 1 client, confirmed via NGO connection
callbacks).

## Unity Cloud project

| | |
|---|---|
| Unity Editor | 6000.5.2f1 |
| Cloud project name | Free Time |
| Cloud project ID | `3474cbb8-508f-41bd-a3ce-1c890e67a5fd` |
| Organization | `kashyapkamalia1303` |

The project is already linked (Project Settings → Services shows "Linked").
Relay works with **anonymous** authentication — no per-developer account setup
is needed to run the test below.

### Teammate access

You only need Unity Cloud dashboard access if you have to view Relay usage or
change service settings — not to develop or test:

1. An org owner opens [cloud.unity.com](https://cloud.unity.com) →
   organization `kashyapkamalia1303` → **Members** → invite by email.
2. In the dashboard, the project appears as **Free Time**. Relay is under
   **Products → Multiplayer → Relay**.

If a teammate clones the repo fresh, the editor may show the project as
unlinked on their machine: Project Settings → Services → sign in → select the
existing project **Free Time** (do **not** create a new project — Relay
allocations are scoped to the project ID above).

## Packages (exact resolved versions)

| Package | Version | Notes |
|---|---|---|
| `com.unity.netcode.gameobjects` | 2.13.0 | NGO |
| `com.unity.services.multiplayer` | 2.2.4 | Provides Relay **and** Lobby APIs — the standalone `com.unity.services.relay` / `com.unity.services.lobby` packages are deprecated on this editor version and must **not** be installed |
| `com.unity.services.authentication` | 3.7.2 | Anonymous sign-in |
| `com.unity.services.core` | 1.18.0 | Pulled in transitively |
| `com.unity.transport` | 6.5.0 | Pulled in transitively by NGO — never pin it manually |
| ParrelSync | latest | Install via Package Manager → "+ → git URL": `https://github.com/VeriorPies/ParrelSync.git?path=/ParrelSync` |

The Relay API namespaces (`Unity.Services.Relay`, `RelayService.Instance`,
`CreateAllocationAsync`, `JoinAllocationAsync`, `ToRelayServerData`) come from
`com.unity.services.multiplayer` — same call signatures as the old standalone
Relay package.

## Scene / script layout

Everything lives in `Assets/_Scenes/Sakri.unity`:

- **`_Bootstrap`** (root GameObject, `DontDestroyOnLoad`):
  - `ServicesBootstrap` — initializes UGS + anonymous sign-in on Start; exposes
    `Instance.IsSignedIn` and `Instance.PlayerId`. Everything networking waits
    on this.
  - `NetworkBootstrap` — `StartHostAsync(maxConnections)` (Relay allocation →
    join code → `UnityTransport.SetRelayServerData` → `StartHost()`) and
    `StartClientAsync(joinCode)`. Logs every connect/disconnect with client ID
    and (on the host) `ConnectedClients.Count`. All failure paths log a
    distinct `[NetworkBootstrap] … FAILED` message — if you see silence, the
    code did not run.
- **`NetworkManager`** (root GameObject): NGO `NetworkManager` + `UnityTransport`
  components, all defaults. No player prefab yet (Sprint 2).

Player Settings: **Run In Background is enabled** — required so the unfocused
editor keeps pumping its connection during two-instance tests. Don't turn it
off.

## LAN mode (KAS-23 / E2)

**Play LAN** on Mode Select opens the LAN screen: one **Host LAN Game** button
plus a live list of games discovered on the local network. No Relay, no join
code, no Unity Services — works fully offline.

- Host: UnityTransport binds `0.0.0.0:7777` directly (`NetworkBootstrap.StartLanHost`).
- Discovery: the host broadcasts a UDP ping on port `47777` once per second
  (`LanDiscovery`); clients on the same network list every session they hear.
  Same-machine ParrelSync instances discover each other via broadcast loopback.
- Join: clicking a listed game calls `NetworkBootstrap.StartLanClient(ip, port)`.
  A 10-second watchdog surfaces unreachable hosts instead of letting UTP retry
  for its default full minute.
- Failure cases all render on-screen: port 7777 already bound (second host on
  one machine), discovery port unavailable, join timeout / host gone.
- **Windows Firewall**: the first LAN host/discovery run may prompt; Unity must
  be allowed on **private networks** or nothing is discovered across machines.

## The 2-instance Relay test

1. **Clone:** ParrelSync → Clones Manager → create/open a clone (first creation
   takes a few minutes). ParrelSync gives each editor its own UGS
   auth profile, so the instances sign in as different players automatically.
2. **Both editors:** open `Sakri.unity`, press Play, wait for the Mode Select
   screen (sign-in happens silently in the background — `ServicesBootstrap`
   logs `Signed in anonymously` in the Console once it's done). The two
   PlayerIds in the logs must differ.
3. **Original editor:** click **Play Online → Host**, pick a map, and confirm.
   Within ~2s the Lobby Room screen shows a 6-character join code.
   - ⚠️ Do **not** use the Start Host/Start Client buttons in the
     NetworkManager *inspector* — those bypass Relay (localhost only, no join
     code) and will look like a working host that clients can never reach.
4. **Clone:** click **Play Online → Join**, type the join code, confirm.

### Expected console output

Host:

```
[ServicesBootstrap] Signed in anonymously. PlayerId: <id A> (profile: editorXXXXXX)
[NetworkBootstrap] Creating Relay allocation (maxConnections=3)...
[NetworkBootstrap] Allocation created (region: <your region>). Requesting join code...
[NetworkBootstrap] CLIENT CONNECTED: clientId=0. ConnectedClients.Count=1   ← host's own client
[NetworkBootstrap] HOST STARTED. Relay join code: XXXXXX — share this with the joining client.
[NetworkBootstrap] CLIENT CONNECTED: clientId=1. ConnectedClients.Count=2   ← the joining client; this is the pass condition
```

Client:

```
[ServicesBootstrap] Signed in anonymously. PlayerId: <id B, different from A> (profile: editorYYYYYY)
[NetworkBootstrap] CLIENT STARTED with join code XXXXXX. Waiting for connection callback...
[NetworkBootstrap] CONNECTED TO HOST: clientId=1 (LocalClientId=1)
```

Pass = both of the marked lines appear with **zero errors**. Any failure logs
as `[NetworkBootstrap] … FAILED (<reason>)`.

### Known console noise (safe to ignore)

- `QosJob: …` logs during allocation — the Relay SDK measuring region latency.
- `Account API did not become accessible within 30 seconds` and
  `UserNotInOrganization … generators.ai.unity.com` — the `com.unity.ai.*`
  packages complaining about their own accounts; unrelated to networking.

### Troubleshooting

- **Clicking Host/Join does nothing, no console output at all** — something is
  eating the cursor. `OrbitCamera` locks the mouse for camera control; it is
  supposed to yield while any menu screen is visible (`ModeSelectUI`,
  `HostJoinChoiceUI`, `MapSelectUI`, `JoinScreenUI`, or `LobbyRoomUI` —
  each exposes a static `IsVisible`, checked in `OrbitCamera.MenuWantsCursor`).
  If you add a new cursor-locking script, give it the same guard.
- **Join fails with a Relay error mentioning the join code** — codes expire
  when the host allocation dies (host stopped Play, or ~stale allocation).
  Re-host, get a fresh code.
- **Client connects then times out after ~10s** — check Run In Background is
  still on, and that the host editor is not paused.
- **Client logs `[Netcode] NetworkPrefab hash was not found! In-Scene placed
  NetworkObject soft synchronization failure`** — the two editors are running
  different versions of the scene (or prefabs). Usually: a NetworkObject was
  added in one editor and the scene was never saved (Play-mode edits are
  discarded!), so the ParrelSync clone still loads the old scene. Fix: exit
  Play mode in the original, verify the object survived, **save the scene**,
  then let the clone reload it. This error also corrupts the rest of the
  client's spawn sync — treat any occurrence as a full test invalidator.
- **What is/isn't synced** — as of this sprint, *nothing* is replicated: no
  player prefab, no NetworkObjects. Each editor renders its own local scene;
  characters visible in both are scene props. Connection state is the only
  shared thing. Player spawning/sync is Sprint 2.
