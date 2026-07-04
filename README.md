# Chase Us

A LAN multiplayer tag game — one Chaser, multiple Runners, tagged Runner becomes the new Chaser.

**Phase 1 goal:** a simple prototype — one map, LAN-only, 5–8 players — built in Unity 6 LTS with Netcode for GameObjects.

## Getting Started

See [CONTRIBUTING.md](./CONTRIBUTING.md) for full setup instructions, folder structure, naming conventions, Git workflow, and deployment process.

Quick start:
```bash
git lfs install
git clone https://github.com/tarang912/Chase-Us.git
cd Chase-Us
git lfs pull
git checkout staging
```
Then open the project folder in **Unity Hub** using **Unity 6 LTS**.

## Branching

- `main` — stable, tagged releases only
- `staging` — integration branch, internal test builds
- `feature/*` — individual feature branches, branched from `staging`

## Tech Stack

- Unity 6 LTS
- Netcode for GameObjects (NGO) + Unity Transport for LAN networking
