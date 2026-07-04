# ChaseUs — Project Guidelines

Everything the team needs to get set up, follow the same conventions, and ship builds consistently.

---

## 1. Project Setup (Local, per-developer)

1. Install **Unity 6 LTS** via Unity Hub (do not use a non-LTS version — LTS gives us stability for the length of this project).
2. Install **Git** (v2.40+) and **Git LFS**:
   ```bash
   git lfs install
   ```
   (Only needs to be run once per machine, not per repo.)
3. Clone the repo:
   ```bash
   git clone https://github.com/<org>/ChaseUs.git
   cd ChaseUs
   git lfs pull
   ```
4. Open the project folder in Unity Hub → **Add project from disk** → select the cloned folder → open with Unity 6 LTS.
5. First time only: if the repo doesn't yet contain a generated Unity project, create a new **3D (URP)** project in Unity Hub *inside the cloned repo folder* so Git tracks it from the start, then commit.

---

## 2. Git LFS — What Gets Tracked

Git LFS is configured via `.gitattributes` at the repo root. The following are tracked as LFS (binary/large files):

```gitattributes
# Models
*.fbx filter=lfs diff=lfs merge=lfs -text
*.obj filter=lfs diff=lfs merge=lfs -text
*.blend filter=lfs diff=lfs merge=lfs -text

# Textures
*.png filter=lfs diff=lfs merge=lfs -text
*.jpg filter=lfs diff=lfs merge=lfs -text
*.tga filter=lfs diff=lfs merge=lfs -text
*.psd filter=lfs diff=lfs merge=lfs -text
*.exr filter=lfs diff=lfs merge=lfs -text

# Audio
*.wav filter=lfs diff=lfs merge=lfs -text
*.mp3 filter=lfs diff=lfs merge=lfs -text
*.ogg filter=lfs diff=lfs merge=lfs -text

# Video
*.mp4 filter=lfs diff=lfs merge=lfs -text
*.mov filter=lfs diff=lfs merge=lfs -text

# Unity-specific binary formats
*.unitypackage filter=lfs diff=lfs merge=lfs -text
*.anim filter=lfs diff=lfs merge=lfs -text
*.controller filter=lfs diff=lfs merge=lfs -text
```

**Rule of thumb:** if it's not plain text (code, `.unity` scene YAML, `.prefab` YAML, `.meta`, `.md`, `.json`), it probably belongs in LFS. Scenes and prefabs stay as regular Git-tracked text (Unity serializes them as YAML) — do **not** LFS-track `.unity`, `.prefab`, or `.mat` files, since diffing/merging text YAML is still possible and useful.

---

## 3. Unity `.gitignore`

Use Unity's standard `.gitignore` (generate via [gitignore.io](https://gitignore.io) or GitHub's Unity template). Key folders to ignore:

```
/[Ll]ibrary/
/[Tt]emp/
/[Oo]bj/
/[Bb]uild/
/[Bb]uilds/
/[Ll]ogs/
/[Mm]emoryCaptures/
*.csproj
*.sln
.vs/
```

Never commit `Library/`, `Temp/`, or build output — these regenerate locally and bloat the repo.

---

## 4. Folder Structure

All game-specific content lives under `Assets/_ChaseUs/` (the underscore keeps our folder pinned to the top of Unity's file browser, above default/plugin folders).

```
Assets/
└── _ChaseUs/
    ├── Scenes/
    │   ├── MainMenu.unity
    │   ├── Lobby.unity
    │   └── Arena.unity
    │
    ├── Scripts/
    │   ├── Networking/
    │   ├── Player/
    │   ├── GameManager/
    │   └── UI/
    │
    ├── Prefabs/
    │   ├── Player/
    │   ├── Environment/
    │   └── UI/
    │
    ├── Materials/
    ├── Textures/
    ├── Audio/
    │   ├── SFX/
    │   └── Music/
    ├── Animations/
    ├── Models/
    └── ScriptableObjects/
```

**Do not** dump loose files into the root `Assets/` folder — everything belongs under `_ChaseUs/` in its category subfolder.

---

## 5. Naming Conventions

Prefix: **ChaseUs_** on cross-cutting/manager-level assets to avoid collisions with plugin assets; category folders make prefixing per-item optional but consistent casing is mandatory.

| Type | Convention | Example |
|---|---|---|
| C# Scripts | PascalCase, no spaces | `PlayerController.cs`, `RoundManager.cs` |
| Scenes | PascalCase | `MainMenu.unity`, `Arena.unity` |
| Prefabs | PascalCase, descriptive | `Player.prefab`, `SpawnPoint.prefab` |
| Materials | `Mat_` + PascalCase | `Mat_Chaser.mat`, `Mat_Runner.mat` |
| Textures | `Tex_` + descriptive + suffix | `Tex_GroundArena_Albedo.png`, `Tex_Player_Normal.png` |
| Audio (SFX) | `SFX_` + PascalCase | `SFX_TagHit.wav`, `SFX_RoundStart.wav` |
| Audio (Music) | `Music_` + PascalCase | `Music_LobbyTheme.mp3` |
| Animations | `Anim_` + PascalCase | `Anim_PlayerRun.anim` |
| ScriptableObjects | `SO_` + PascalCase | `SO_GameSettings.asset` |
| Variables (in code) | camelCase | `moveSpeed`, `isChaser` |
| Classes / methods (in code) | PascalCase | `PlayerController`, `AssignRole()` |
| Private fields (in code) | `_camelCase` | `_networkTransform` |

No spaces, no special characters, no version numbers in filenames (Git handles versioning — don't name things `Player_v2.prefab`).

---

## 6. Git Branching Strategy

```
main        ← always stable, always deployable/buildable
  └── staging   ← integration branch, QA/internal testing happens here
        ├── feature/<feature-name>
        ├── feature/<feature-name>
        └── feature/<feature-name>
```

- **`main`** — production-ready only. Nothing is merged here directly; only from `staging` after it's been tested. Every merge to `main` should be tagged with a version (e.g. `v0.1.0`).
- **`staging`** — where completed features land first. This is what gets built for internal playtests. Should always be in a "buildable" state, even if not feature-complete.
- **`feature/<name>`** — one branch per feature/task, branched off `staging`. Naming: `feature/tagging-system`, `feature/lan-discovery`, `feature/lobby-ui`.

### Workflow
1. Branch off `staging`:
   ```bash
   git checkout staging
   git pull
   git checkout -b feature/tagging-system
   ```
2. Commit regularly with clear messages (see below).
3. Push and open a Pull Request **into `staging`** (not `main`).
4. At least one teammate reviews before merging.
5. Once `staging` is tested and stable, open a PR from `staging` → `main`, tag the release.

### Commit Message Convention
```
<type>: <short description>

feat: add tag detection trigger to PlayerController
fix: resolve host disconnect crash on round end
chore: update .gitattributes LFS rules
docs: update README with build instructions
refactor: split RoundManager timer logic into separate method
```
Types: `feat`, `fix`, `chore`, `docs`, `refactor`, `test`.

---

## 7. Adding New Assets — Checklist

Before committing a new asset:
- [ ] Placed in the correct subfolder under `_ChaseUs/`
- [ ] Named per convention in Section 5
- [ ] Correct file type is covered by `.gitattributes` LFS rules (check before adding a new binary format)
- [ ] No loose files left in `Assets/` root
- [ ] Meta files (`.meta`) are committed alongside the asset (Unity auto-generates these — never delete or gitignore them)

---

## 8. Deployment Process

**Branches → Build Targets:**

| Branch | Purpose | Build Type |
|---|---|---|
| `feature/*` | Active development | Local editor testing only, no formal builds |
| `staging` | Integration + QA | Internal test builds (Development Build, LAN testing) |
| `main` | Stable releases | Tagged release builds |

**Process:**
1. Feature branches are developed and tested locally in the Unity Editor (use ParrelSync for multi-instance LAN testing before merging).
2. On merge to `staging`, a build is made from `staging` (Unity → Build Settings → Development Build checked, target platform: Windows/PC for LAN prototype) and shared with the team for internal playtesting.
3. Once `staging` passes internal testing with no blocking bugs, open a PR to `main`.
4. On merge to `main`, tag the commit (`git tag v0.1.0 && git push --tags`) and produce a clean, non-development build as the official Phase 1 build.
5. (Future phase) — set up CI (GitHub Actions + Unity Build Automation / Game CI) to automate builds on push to `staging`/`main` instead of manual builds.

**Build output** should never be committed to the repo — store builds separately (shared drive, GitHub Releases attached to the tag, etc.), since `Builds/` is gitignored.

---

## 9. Quick Reference — Getting Started (New Team Member)

```bash
git lfs install                      # once per machine
git clone https://github.com/<org>/ChaseUs.git
cd ChaseUs
git lfs pull
git checkout staging
git pull
git checkout -b feature/your-feature-name
```
Then open the project folder via Unity Hub with **Unity 6 LTS**.
