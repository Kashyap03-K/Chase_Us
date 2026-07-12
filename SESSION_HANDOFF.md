# Chase Us — Session Handoff

**Last updated:** 2026-07-09
**Purpose:** Living document so a fresh AI chat session (or a returning teammate) can pick up work without re-litigating decisions or re-exploring the repo. Not committed to git — this is a working scratchpad, not a team artifact. Consider adding to `.gitignore` if it starts feeling like clutter.

**To resume a new chat:** paste the contents of this file, or say "Read `D:\Free Time\SESSION_HANDOFF.md` and continue where we left off."

---

## 1. Project at a glance

| Field | Value |
|---|---|
| **Name** | Chase Us (aka Hide & Seek MVP) |
| **Repo** | https://github.com/Kashyap03-K/Chase_Us |
| **Project path** | `D:\Free Time\` |
| **Current branch** | `Lobby` (feature branch — PRs target `staging`, then `main` per `CONTRIBUTING.md`) |
| **Editor** | Unity 6 LTS · `6000.5.2f1` |
| **Team** | Kashyap (owner), Tarang, Jaivik |
| **Linear workspace** | `linear.app/kashyap-kamalia` |
| **Linear project** | `Hide & Seek MVP` |

**Game concept:** LAN + Relay multiplayer chain-tag party game. One primary Hunter, tagged Runners join a growing chain until only one Runner remains ("The Chain wins" if everyone gets caught). Party-game scale — 4 players Sprint 2 target, scales to 10.

**Stack:**
- Netcode for GameObjects (NGO) `2.13.0`
- Unity Multiplayer Services `2.2.4` (provides Relay + Lobby APIs — do **not** install standalone `com.unity.services.relay` / `com.unity.services.lobby`, they're deprecated for this editor version)
- Unity Authentication `3.7.2` (anonymous sign-in)
- Unity Transport `6.5.0` (transitive via NGO — never pin manually)
- ParrelSync (multi-editor testing)
- URP for rendering

---

## 2. Sprint 2 status snapshot

### Kashyap's tickets — all Done ✅
- **KAS-19** · D3 · Lobby Flow Design → artifact locked, dark direction
- **KAS-20** · D4 · HUD & State Machine Diagram → artifact locked
- **KAS-22** · E1 · Mode Selection UI → shipped
- **KAS-25** · E4 · Player List / Ready-Up UI → shipped (was Jaivik's ticket, taken to unblock joiner path)

### Open Sprint 2 (not Kashyap's)
- **KAS-21** · D5 · Design Doc Handoff (unassigned)
- **KAS-23** · E2 · LAN Connection Mode (Tarang)
- **KAS-24** · E3 · Relay Connection Mode extend (Jaivik)
- **KAS-26** · E5 · Cleanup / remove NetworkDebugUI (Tarang)
- **KAS-32** · D6 · UI/Art Components — character design + trees (Jaivik)

### Backlog (Sprint 3+)
- **KAS-10** · Sprint 3 · Game loop v1 → this is where **Start Game actually starts a round** (currently just logs). Round state machine (Waiting → Assignment → InProgress → Over → restart) already designed in KAS-20 artifact.
- **KAS-27..31** · F1–F5 · Player prefab, chain formation, catch detection, integration test.
- **KAS-11..13** · Sprints 4–6 (playtest, polish, mobile).

---

## 3. Design direction — locked calls (do not re-litigate)

### UI direction: dark theme
Locked 2026-07-08 in KAS-19. Palette lives in `Assets/_Scripts/UI/UITheme.cs`:

| Token | Hex | Role |
|---|---|---|
| Ground | `#0F0B1F` | Midnight indigo — full-screen backdrop |
| Surface | `#1A1530` | Panel backgrounds |
| SurfaceHi | `#241B3D` | Lifted panels, button interiors |
| Stroke / StrokeHi | `#2E2450` / `#453770` | Borders |
| Text | `#F5F1E8` | Warm off-white — primary text |
| TextMuted / TextDim | `#9A8EB8` / `#6A5F85` | Secondary / tertiary |
| Accent | `#FF3D7A` | Hot pink — Hunter role, primary CTA |
| Accent2 | `#4EEBD9` | Electric cyan — Runner role, ready states |
| Chain | `#B47CFF` | Purple — In-Chain role |
| Warn | `#FFB84E` | Amber — connecting / caution |
| Danger | `#FF6B6B` | Soft red — errors |

### Game mechanic: chain tag (not classic tag)
Locked 2026-07-09. One primary Hunter, tagged Runners become **In-Chain** helpers. Chain grows. Last Runner wins; "The Chain wins" if all caught. HUD needs 3 roles + chain-length indicator + remaining-Runners count. Server-authoritative.

**"For now" caveat:** user may revisit if they'd rather do Classic. If so: chain UI drops, role transfers on tag.

### 5-character roster (Pixar-style 3D)
| Name | Signature | Look |
|---|---|---|
| Riven | Green | Brown hair, tan pith helmet, green hoodie, olive shorts |
| Mira | Cyan | Girl with two braids, teal sweater, wide-brim explorer hat |
| Kai | Pink | Blond boy, cherry-red windbreaker, backwards red cap |
| Nova | Purple | Violet hair, lavender hoodie, purple beanie |
| Sunny | Amber | Red curly hair, yellow bandana, denim vest, mustard tee |

Roles are assigned dynamically per round — signature colors are for character identity, not role locking.

### Design artifacts (locked references, still live URLs)
- **Lobby flow** (Mode Select → Map Select → Host Lobby → Join → Waiting Room): https://claude.ai/code/artifact/f31fc77d-8675-4d35-85c3-b204b01b9df0
- **HUD + State Machine** (KAS-20): https://claude.ai/code/artifact/944280c5-e2c3-44a4-9376-dfcad81b10ea

---

## 4. Code built this sprint

All lives in `Assets/_Scripts/UI/` unless noted.

| File | Purpose |
|---|---|
| `UITheme.cs` | Palette + type constants |
| `ModeSelectUI.cs` | Entry screen · PLAY LAN / PLAY ONLINE buttons |
| `MapSelectUI.cs` | Arena picker (host-only) · Confirm calls `NetworkBootstrap.StartHostAsync` directly |
| `HostJoinChoiceUI.cs` | Sub-choice after Play Online · HOST A ROOM / JOIN A ROOM |
| `JoinScreenUI.cs` | Client's 6-char code entry (KAS-19 Screen 04) |
| `LobbyRoomUI.cs` | Room room + KAS-25 live player list · observes NGO connection state |
| `LoadingScreenUI.cs` | Splash / sign-in wait · `sortingOrder = 200`, hides on `ServicesBootstrap.SignedIn` |

**Modified networking:**
- `Networking/NetworkBootstrap.cs` — `JoinCode` now set BEFORE `StartHost()` (fixes race with `OnServerStarted`), and persisted on client after `StartClientAsync`.

### Flow diagram (as-built)

```
LoadingScreen (sortingOrder 200)
    │  fades on ServicesBootstrap.SignedIn
    ▼
ModeSelect ── PLAY LAN ── (notice: not implemented)
    │
    └── PLAY ONLINE ──────► HostJoinChoice
                                │
                    ┌───────────┴───────────┐
                    ▼                       ▼
                HOST A ROOM             JOIN A ROOM
                    │                       │
                    ▼                       ▼
                MapSelect ── Confirm ── StartHost
                    ↑                       ↓          JoinScreen ── Join ── StartClient
                    │ Back                                                       │
                    └────────────────────► LobbyRoom ◄──────────────────────────┘
                                          (auto-shows on NGO connect,
                                           player list live via
                                           ConnectedClientsIds)
                                                │
                                                ▼
                                          START GAME
                                          (currently logs only —
                                           real behavior in Sprint 3 / KAS-10)
```

### Every UI screen follows the same pattern
- Self-constructing UGUI Canvas at runtime (no Prefabs, no scene wiring needed)
- Drop the component onto any GameObject and it auto-finds its dependencies via `FindFirstObjectByType` on Awake
- Sorting orders: Loading = 200, entry screens = 100, LobbyRoom = 90

### Commits landed this sprint
Check `git log --oneline` on the `Lobby` branch. Recent milestone commits:
- `f4a2630` [FEAT]Added Lobby Room UI + Host/Join flow (KAS-25)
- Earlier commits for Mode Select, Map Select

---

## 5. Currently pending

### 🔴 BLOCKING — Unity Editor wiring (user must do manually)

1. **Save** the generated `lobby_background.png` to `Assets/UI/Backgrounds/` (create the folder if needed)
2. **Import settings:**
   - Texture Type: `Sprite (2D and UI)`
   - Mesh Type: `Full Rect`
   - Filter Mode: `Bilinear`
   - Compression: `High Quality`
   - Max Size: `4096` (or drop to `2048` if perf tight)
3. **Add `LoadingScreenUI` component** to the `_Bootstrap` GameObject (Add Component → Loading Screen UI)
4. **In the Inspector for both `LobbyRoomUI` and `LoadingScreenUI`**, drag `lobby_background` from Project onto the **Background Sprite** field
5. **Save the scene** (`Ctrl+S`)
6. **Test** the flow end-to-end in Play mode

### 🟡 Next up (design pending direction)
- **Character generation** — 5 character prompts are ready. User needs to run them through their image generator (same tool that made the Riven reference). Prompts are cleaned up for A-pose + no-accessories.
- **Meshy → Blender → Unity** pipeline for the 5 characters. Meshy is already installed and used for the arena assets. Meshy Rig Model → Humanoid button auto-rigs → export FBX.
- **Apply same background** to Mode Select, Map Select, Host/Join, Join Screen so the whole pre-game feels like one world. Code pattern is identical to LobbyRoomUI's — add `Sprite backgroundSprite` field + modify `BuildBackground()`. Say the word to do this pass.

### 🟢 Post-Sprint 2 / Sprint 3 setup
- **KAS-10** — round loop v1. `LobbyRoomUI.OnStartGameClicked` currently logs `[LobbyRoomUI] START GAME · N players. Round state machine wiring pending KAS-10.` — that's the swap-out point.
- **Player prefab (F2)** — nothing spawns in the arena yet. Sprint 2 explicitly does not include this.

---

## 6. Known scope calls / TODOs baked into code

Search codebase for `// TODO` to see them. Highlights:

| File | Line context | Note |
|---|---|---|
| `LobbyRoomUI.cs` | `OnStartGameClicked` | `TODO(Sprint 3 · KAS-10): trigger round state machine here` |
| `LobbyRoomUI.cs` | Player names | Currently `Player {clientId}`. Real display names need a NetworkVariable sync — out of KAS-25 scope. |
| `LobbyRoomUI.cs` | Empty slot border | Solid muted border for now; mockup showed dashed. Needs 9-slice sprite for real dashed effect. |
| `MapSelectUI.cs` | Preview tint | Currently solid green Color32 per map. Replace with real screenshot Sprites once arena is closer to final. |
| `JoinScreenUI.cs` | Code entry | Single TMP_InputField; mockup showed 6 discrete tiles. Polish for later. |
| `ModeSelectUI.cs` | PLAY LAN | Currently just shows a "coming soon" notice. `NetworkBootstrap` has no LAN transport path — waiting on Tarang's KAS-23. |

---

## 7. Team coordination notes

- **Kashyap took KAS-25** (Jaivik's) to unblock the joiner path. Worth pinging Jaivik so he pivots to **KAS-24** (Relay extension) + **KAS-32** (art) instead of duplicating.
- **HostJoinChoiceUI and JoinScreenUI** aren't dedicated Linear tickets — they were scaffolding built while KAS-25 was in flight because the flow had a Host/Join disambiguation gap. Could retroactively file as sub-tasks or leave as "part of KAS-25 delivery."
- **NetworkDebugUI is still in the scene** even though replaced. **KAS-26** (Tarang) is the cleanup ticket — do not delete it prematurely.

---

## 8. How to resume in a new chat

Copy-paste this whole file, or say:

> "Read `D:\Free Time\SESSION_HANDOFF.md` and continue where we left off."

Priority actions on resume:
1. Confirm the Unity Editor wiring from Section 5 has been done (or help with it)
2. Ask user what they want to work on next
3. Suggest the "apply background to all other screens" pass if not yet done — it's low-effort and unifies the pre-game feel

**Do not:**
- Re-propose changing the palette (locked)
- Re-propose classic tag over chain tag (locked)
- Re-explain the flow diagram from scratch (it's in Section 4)
- Rewire `NetworkDebugUI` back into the flow (deprecated, `KAS-26` will remove it)

---

## 9. Useful files to Read early in a new session

- `README.md` — game concept
- `CONTRIBUTING.md` — folder structure, naming conventions, Git workflow
- `NETWORKING_SETUP.md` — Relay setup, packages, ParrelSync test
- `Assets/_Scripts/UI/UITheme.cs` — palette source of truth
- `Assets/_Scripts/UI/LobbyRoomUI.cs` — biggest and most recent UI class
- `Assets/_Scripts/Networking/NetworkBootstrap.cs` — host/join API surface
- Memory files at `C:\Users\kashy\.claude\projects\D--Free-Time\memory\` (auto-loaded)
