# One Wood

*One club to rule them all.*

One Wood is a golf simulator set in Middle-earth. It is built in **Unity 6 (HDRP)**, runs on **Windows with an NVIDIA GPU**, and uses a **FlightScope Mevo+** as the launch monitor. You hit real shots into an impact screen. Each shot is rebuilt from the Mevo+ launch data and flown with our own physics model through Middle-earth courses.

> **Personal use only.** This project uses names, places and imagery from J.R.R. Tolkien's legendarium. Those are owned by the Tolkien Estate and Middle-earth Enterprises. The project is for private play on the author's own simulator and **must not be distributed, published, or sold**. Keep the repository private.

---

## Table of contents

1. [Goals and non-goals](#goals-and-non-goals)
2. [Hardware and software](#hardware-and-software)
3. [System architecture](#system-architecture)
4. [Mevo+ integration (GSPro Open Connect)](#mevo-integration-gspro-open-connect)
5. [Ball-flight and ground physics](#ball-flight-and-ground-physics)
6. [Game modes](#game-modes)
7. [Courses](#courses)
8. [Theming: hazards and ambient events](#theming-hazards-and-ambient-events)
9. [Display, camera and HUD](#display-camera-and-hud)
10. [Controls](#controls)
11. [Art and audio pipeline](#art-and-audio-pipeline)
12. [Project structure](#project-structure)
13. [Development setup](#development-setup)
14. [Testing](#testing)
15. [Roadmap](#roadmap)
16. [Open items and assumptions](#open-items-and-assumptions)

---

## Goals and non-goals

### Goals
- **Realistic golf.** Ball flight comes from measured launch data (ball speed, launch angles, spin). Physics is simulated, so wind, elevation and air density change the result.
- **Immersive Middle-earth.** Recognizable, atmospheric settings: the Shire, Rivendell, and Minas Tirith/Mordor.
- **Themed hazards with real golf rules.** Water, out of bounds, bunkers and penalty areas are dressed as Middle-earth features.
- **Built for an impact-screen simulator.** Projector-friendly resolution and aspect ratio, a HUD you can read from the hitting mat, and a controller-driven UI.
- **Local multiplayer.** Several golfers take turns on one simulator.

### Non-goals (for now)
- Online multiplayer or leaderboards.
- Commercial or public release.
- Arcade power-ups or fantasy mechanics that change ball flight.
- On-screen (simulated) putting. Every putt is a real stroke read by the Mevo+.
- Launch monitors other than the Mevo+. The shot source is behind an interface so others can be added later.

---

## Hardware and software

| Item | Requirement |
|---|---|
| Launch monitor | FlightScope Mevo+ (recent firmware) |
| Shot-data bridge | FlightScope **FS Golf PC** app (Windows), configured to send to a GSPro Open Connect endpoint |
| OS | Windows 10/11, 64-bit |
| GPU | NVIDIA RTX 20-series or newer (for DLSS). Target: RTX 3070 or better |
| Display | Projector + impact screen. Any resolution/aspect ratio (4:3, 16:10, 16:9 and custom are supported) |
| Input | Xbox-compatible gamepad and/or wireless keyboard and mouse |
| Engine | Unity 6 LTS, HDRP, C# |

---

## System architecture

```
┌──────────┐  Wi-Fi  ┌──────────────┐  TCP :921 / JSON   ┌───────────────────────────────────────────┐
│  Mevo+   │ ──────▶ │ FS Golf (PC) │ ─────────────────▶ │                One Wood (Unity)           │
└──────────┘         └──────────────┘ ◀───────────────── │                                           │
                                       player/club info  │  LaunchMonitor ─▶ ShotPipeline ─▶ Physics │
                                                         │       │                │            │     │
                                                         │       ▼                ▼            ▼     │
                                                         │  DeviceStatus     GameMode/Rules   Ball   │
                                                         │                       │          Camera   │
                                                         │                       ▼            │     │
                                                         │                  Scoring/Players ◀─┘     │
                                                         │                       │                  │
                                                         │                      HUD / Audio / FX     │
                                                         └───────────────────────────────────────────┘
```

### Core modules

| Module | Responsibility |
|---|---|
| **LaunchMonitor** | `IShotSource` interface. `OpenConnectServer` implementation (TCP listener, JSON parsing, heartbeats, ready/ball-detected state). `MockShotSource` for development without hardware. |
| **ShotPipeline** | Normalizes incoming data to SI units and validates/filters bad reads. Emits a `LaunchData` event. Decides whether the shot is a full swing or a putt based on game state. |
| **BallPhysics** | Deterministic flight integrator (aerodynamic drag, lift, spin decay, wind), then bounce, roll and putting on surfaces with slopes. Pure C#, no MonoBehaviour dependency, unit-testable. |
| **Course** | Hole definitions (tees, pins, par, hazards, surface map), course metadata, and ScriptableObject authoring. |
| **Rules** | Lie detection, penalty strokes, OB/stroke-and-distance, penalty-area drops, unplayable lies, and holing out. |
| **GameModes** | Driving Range, Stroke Play, Match Play. Turn order for multiplayer. |
| **Players** | Local profiles: name, handedness, tee preference, club bag, stats history. |
| **Camera** | Tee view, ball-follow flight cam, landing cam, green/putting view, flyover intros. |
| **HUD / UI** | Shot-data panel, hole info, scorecard, menus, device status. Scaled for projectors. |
| **Ambience** | Themed cosmetic events, weather, day/night lighting, audio. |
| **Settings** | Display/aspect, graphics quality, DLSS, units, physics tuning, network port. |
| **Persistence** | Local JSON saves under `%USERPROFILE%/AppData/LocalLow/OneWood/`: profiles, round history, range sessions. |

---

## Mevo+ integration (GSPro Open Connect)

The Mevo+ does not talk to third-party software directly. FlightScope's **FS Golf PC** app reads the device and can forward each shot to a simulator using the **GSPro Open Connect API v1**: line-delimited JSON over TCP. One Wood implements the *server* side of that protocol. To FS Golf, it looks like GSPro.

### Connection
- One Wood listens on **TCP port 921** by default (configurable).
- FS Golf connects as the client and sends a heartbeat and status messages, plus one message per shot.
- One Wood replies with a status code. It can also send the active player's info (handedness, club, distance to target) so FS Golf can show the right context.

### Inbound shot message (abridged)
```json
{
  "DeviceID": "FlightScope Mevo+",
  "Units": "Yards",
  "ShotNumber": 42,
  "APIversion": "1",
  "BallData": {
    "Speed": 148.2, "SpinAxis": -4.1, "TotalSpin": 2850,
    "BackSpin": 2843, "SideSpin": -203,
    "HLA": 1.2, "VLA": 12.4, "CarryDistance": 255.0
  },
  "ClubData": {
    "Speed": 101.3, "AngleOfAttack": -1.0, "FaceToTarget": 0.8,
    "Lie": 0, "Loft": 13.1, "Path": 2.0,
    "SpeedAtImpact": 101.3, "VerticalFaceImpact": 0, "HorizontalFaceImpact": 0,
    "ClosureRate": 0
  },
  "ShotDataOptions": {
    "ContainsBallData": true, "ContainsClubData": true,
    "LaunchMonitorIsReady": true, "LaunchMonitorBallDetected": true,
    "IsHeartBeat": false
  }
}
```

### Handling rules
- **Heartbeats / status** (`IsHeartBeat` or no ball data) update the on-screen device indicator (`Not connected`, `Waiting for ball`, `Ready`) and never trigger a shot.
- **Units.** Ball and club speed arrive in mph and angles in degrees. Everything is converted to SI (m/s, rad, rpm→rad/s) at the edge.
- **Ball data drives the flight.** `Speed`, `VLA`, `HLA`, `TotalSpin` and `SpinAxis` are the inputs to the physics model. `BackSpin`/`SideSpin` are a fallback if total spin or axis is missing. `CarryDistance` is kept for calibration and diagnostics only.
- **Club data is shown, not simulated.** It appears in the shot-data panel and is saved to stats.
- **Validation.** `ShotValidator` checks every message for missing fields, physically impossible values (e.g. ball speed ≤ 0 or > 250 mph, VLA outside −20°…80°, |HLA| > 45°, spin > 15,000 rpm), and internal consistency (TotalSpin/SpinAxis against BackSpin/SideSpin, including sign convention). Errors reject the shot with a "Misread – re-hit" prompt that costs no stroke. Warnings (unusual but playable shots, odd club data, duplicate ShotNumbers) are logged and the shot plays. Thresholds live in `ShotValidationLimits`.
- **Putting.** When the ball is on the green, the game expects a putt and runs the result through the putting/roll model. All putts are real strokes measured by the Mevo+. (FS Golf may need to be in a putting-capable mode; see [Open items](#open-items-and-assumptions).)
- **Threading.** The TCP server runs on a background thread and hands parsed shots to the Unity main thread through a thread-safe queue.

### Validating tracker feedback
Before the game consumes shots, check what FS Golf actually sends:

1. **Capture.** Either open `Assets/_Project/Scenes/Dev_ShotFeedback.unity` and press Play, or, without Unity, run `python Tools/open_connect.py capture`. Both listen on port 921, acknowledge shots like GSPro, and write every message to `OneWood/Logs/shots/shots_<timestamp>.jsonl`. The dev scene also shows connection and device-ready state, validates each message live, and has a button that sends player info with club `PT`/`DR` to test putting-mode switching. (`capture --club PT` does the same.)
2. **Validate.** Use **One Wood → Validate Shot Log...** in the editor, or batch mode:
   ```
   Unity -batchmode -projectPath OneWood -executeMethod OneWood.Editor.ShotLogValidation.RunFromCommandLine \
         -shotLog OneWood/Logs/shots/<file>.jsonl [-expectPutts] [-report report.txt]
   ```
   The report lists rejected shots and warnings, counts of each issue, **how often each Open Connect field was populated** (which answers "which ClubData does the Mevo+ fill in?"), and any fields outside the v1 schema.
3. **Keep good captures** in `Tools/ShotLogs/` as regression fixtures.

### Development without hardware
`python Tools/open_connect.py send <log.jsonl> [--framing newline|concat|split] [--delay s]` acts as FS Golf. It replays shot logs to the game over TCP and prints every response. `Tools/ShotLogs/sample_shots.jsonl` has a status message, a heartbeat, a driver, a 7-iron, a wedge and a putt. `problem_shots.jsonl` has one message for each failure the validator should catch. Planned: a `MockShotSource` with in-game F1–F5 preset shots.

---

## Ball-flight and ground physics

The ball is flown by our own model rather than animated to match the Mevo+'s reported carry. That lets wind, temperature, altitude and terrain affect the shot.

### Flight
- **State:** position, velocity, spin vector (ω).
- **Forces:** gravity, aerodynamic drag `F_d = ½ρC_dAv²`, Magnus lift `F_l = ½ρC_lAv²` perpendicular to velocity and spin axis.
- **Coefficients:** `C_d` and `C_l` are functions of Reynolds number and spin factor `S = rω/v`, tabulated from published golf-ball wind-tunnel data and tuned against Mevo+ carry numbers.
- **Spin decay:** exponential decay over flight time.
- **Environment:** air density from per-course altitude, temperature and humidity. The wind field has steady and gusting parts, defined per hole.
- **Integrator:** fixed-timestep RK4 (e.g. 1 ms), deterministic, separate from the Unity frame rate.

### Ground interaction
- **Surfaces** come from a per-hole surface map (splat/texture mask or polygon zones): tee, fairway, first cut, rough, deep rough, bunker, green, fringe, cart path, plus themed surfaces such as cobblestone, volcanic ash and elven stone. Each surface defines restitution, friction, spin retention and a lie penalty.
- **Bounce:** impulse model with normal/tangential restitution. Spin can produce check or release on landing.
- **Roll:** rolling resistance per surface. Gravity on slopes uses the terrain normal.
- **Greens:** per-course stimpmeter speed. Slopes come from the terrain mesh, with optional higher-resolution green heightmaps. The putting model uses the same roll integrator with green friction.
- **Lie effects:** the next shot's launch data is adjusted for the current lie (e.g. rough cuts ball speed and spin, a bunker cuts ball speed), with configurable strength.

### Calibration
A **Physics Lab** scene (dev only) compares simulated carry, apex and descent angle against the `CarryDistance` reported for logged shots, then fits the drag and lift curves. Goal: simulated carry within ±3% of Mevo+ carry in calm, sea-level conditions.

---

## Game modes

### Driving Range: "The Green Dragon Range"
A practice range set in the Shire.
- Distance markers, target greens and flags.
- Full shot-data panel (ball and club data) and a trajectory tracer.
- Session stats: averages and dispersion per club, plus a shot list.
- Optional "condition" toggles: wind and altitude, to preview how course conditions change carry.

### Course Play
- **Formats:** Stroke Play and Match Play (Stableford and Scramble are on the roadmap).
- **Rounds:** play any single course (3 holes in the first version) or **The Journey**: all courses chained, Shire → Rivendell → Minas Tirith/Mordor.
- **Rules:** stroke-and-distance for OB, penalty-area drops, an optional mulligan count per round, and an optional max score per hole (e.g. double par) to keep play moving.
- **Tees:** multiple tee sets per hole, chosen per player.
- **Scorecard:** per hole and total, with stats (FIR, GIR, putts).

### Local Multiplayer
- 1–4 players on one simulator.
- Turn order follows golf convention (farthest from hole plays next; honors on the tee), with an option for strict rotation.
- Each player has their own profile, handedness, tees and ball marker colour. The active player is sent to FS Golf in the Open Connect response.
- Match Play supports 1v1 and 2v2 (better ball).

---

## Courses

The first version is a **3-hole vertical slice per course** (9 holes total), built so each course can grow to 9 or 18 holes later. Every hole is a ScriptableObject with its terrain, tee/pin positions, surface map, hazard volumes, wind profile and flyover camera path.

| Course | Setting | Character | Signature features |
|---|---|---|---|
| **The Shire** | Hobbiton, Bywater, the Party Field | Gentle, scoring-friendly parkland. The starter course. | Bag End above a green, the Party Tree, hedgerow OB, Bywater pond, the Green Dragon inn |
| **Rivendell** | The hidden valley of Imladris | Elevation changes and forced carries over ravines | Waterfalls, the river Bruinen, elven bridges, Last Homely House backdrop |
| **Minas Tirith & Mordor** | Pelennor Fields to the Black Gate | The final test: long, exposed, punishing | The White City's tiers, Pelennor grassland, lava and ash hazards, Mount Doom on the skyline, Barad-dûr's Eye |

### Initial hole concepts

**The Shire**
1. *Bagshot Row* (Par 4): downhill opener toward Bag End's round green door. Hedgerows run down the left as OB.
2. *The Party Field* (Par 3): short iron over wildflowers to a green beside the Party Tree.
3. *Bywater Road* (Par 5): reachable par 5 doglegging around Bywater Pool (water hazard), ending near the Green Dragon.

**Rivendell**
1. *The Ford of Bruinen* (Par 4): tee shot over the river. The fairway is split by rocky outcrops.
2. *The Falls* (Par 3): elevated tee across a ravine to a green framed by waterfalls. A forced carry; short is lost.
3. *Last Homely House* (Par 4): uphill approach to a terraced green below the house.

**Minas Tirith & Mordor**
1. *Pelennor Fields* (Par 5): wide open, strong crosswinds, battlefield ruins as obstacles.
2. *The Black Gate* (Par 3): long iron through the gate's gap. Ash bunkers surround the green.
3. *Mount Doom* (Par 4): finishing hole on volcanic rock. A lava chasm (penalty area) sits before an island-like green under the mountain.

---

## Theming: hazards and ambient events

### Themed hazards
Every hazard plays under normal golf rules. Only the presentation changes.

| Golf hazard | Shire | Rivendell | Minas Tirith / Mordor |
|---|---|---|---|
| Water / penalty area | Bywater Pool, the Brandywine | River Bruinen, waterfalls, ravines | Lava flows, Dead Marshes-style bogs |
| Out of bounds | Hedgerows, hobbit gardens ("Get off my land!") | Cliff edges | Orc encampments, chasms |
| Bunkers | Garden sand traps | White elven sand | Black volcanic ash |
| Trees / obstacles | Oaks, the Party Tree | Beeches and pines | Dead trees, ruins, siege towers |

### Ambient events (cosmetic only, never affect play)
Triggered occasionally and tied to shot outcomes or random timers. Each can be toggled in settings.
- **Great shot / birdie:** the Eagles fly overhead; fireworks over the Party Field (Shire).
- **Ball in water or OB:** themed audio sting ("Fool of a Took!"-style reaction). The Eye of Sauron turns toward the player in Mordor.
- **Hole-in-one:** a special celebration sequence.
- **Ambient life:** hobbits in the fields, elves on the bridges, Nazgûl circling in the distance over Mordor.
- **Weather/lighting:** each course has a lighting mood (golden Shire afternoon, misty Rivendell dawn, smoke-red Mordor sky).

> Audio lines and music must be original or used privately. See [Art and audio pipeline](#art-and-audio-pipeline).

---

## Display, camera and HUD

### Projector / impact screen
- Resolution and aspect ratio come from settings (4:3, 16:10, 16:9, or a custom value), with a **safe-area** adjustment so HUD elements stay off masked edges of the screen.
- **Horizon / tee-height calibration:** a slider sets the camera height and pitch so the projected target line matches the real line from the hitting mat.
- **Fullscreen exclusive** or borderless on a chosen monitor, so a separate operator monitor can show the menus.

### Cameras
- **Address view:** behind the ball, at eye height, aligned with the aim line.
- **Flight cam:** follows the ball and blends to a landing cam near the target.
- **Green view:** lower and closer, with an optional slope overlay (grid or arrows) for reading putts.
- **Hole flyover:** a scripted Cinemachine path shown when arriving at a new tee (skippable).

### HUD
- Large, high-contrast type readable from about 3–4 m away.
- Panels: hole/par/distance to pin, wind, current player and score, Mevo+ status light, last-shot data (carry, total, ball speed, launch, spin, club speed, smash), and a mini-map with the aim line.
- Themed styling (parchment, elven script borders, a Mordor variant), always prioritizing legibility.

### Rendering targets
- HDRP with **NVIDIA DLSS** (Unity's HDRP DLSS integration) and TAA as the fallback.
- Target **60 fps** at the projector's native resolution on the target GPU, with Low/Medium/High/Ultra presets.
- Terrain with GPU-instanced vegetation, volumetric fog and clouds, and physically based sky.

---

## Controls

Controls are needed between shots only. The swing itself is the real shot.

| Action | Gamepad | Keyboard / mouse |
|---|---|---|
| Aim left/right | Left stick | A / D or mouse drag |
| Fine aim | Bumpers | Shift + A / D |
| Change club (display / stats) | D-pad up/down | W / S or scroll |
| Toggle green slope overlay | Y | G |
| Mulligan | Hold X | M |
| Re-hit (misread, no penalty) | Hold B | R |
| Scorecard | View/Back | Tab |
| Pause / menu | Menu/Start | Esc |
| Skip flyover / camera | A | Space |
| Inject mock shot (dev builds only) | n/a | F1–F5 |

Built on Unity's **Input System** package, with rebindable actions.

---

## Art and audio pipeline

- **Environment:** Unity Terrain (HDRP) with heightmaps, painted terrain layers and procedural vegetation scattering.
- **Third-party assets:** **free** assets only, e.g. free Unity Asset Store HDRP packs, Poly Haven, ambientCG (CC0). Every third-party asset is recorded in `Assets/ThirdParty/CREDITS.md` with its source and license.
- **Landmarks:** built in-house in Blender and imported as FBX. These include Bag End and the hobbit-hole facades, the Party Tree, Rivendell's architecture and bridges, Minas Tirith's tiers (as a backdrop), the Black Gate, Barad-dûr and Mount Doom.
- **Backdrops:** distant landmarks such as Minas Tirith, Mount Doom and Barad-dûr can be low-detail meshes or matte skybox layers to save performance.
- **Audio:** ambient beds (wind, birds, waterfalls, rumbling), impact and roll sounds per surface, crowd/character reactions, and an original or royalty-free fantasy score. Film audio and music are not bundled in the repo.
- **Git:** use **Git LFS** for binaries (`.fbx`, `.png`, `.tif`, `.exr`, `.wav`, `.blend`, terrain data).

---

## Project structure

```
one_wood/
├── README.md
├── OneWood/                        # Unity project root
│   ├── Assets/
│   │   ├── _Project/
│   │   │   ├── Scripts/
│   │   │   │   ├── LaunchMonitor/     # Engine-free assembly (OneWood.LaunchMonitor)
│   │   │   │   │   ├── OpenConnect/   #   framing, parser, responses, TCP server
│   │   │   │   │   ├── Shots/         #   LaunchData, ShotValidator, limits, unit conversion
│   │   │   │   │   └── Diagnostics/   #   shot log read/write, replay report, formatting
│   │   │   │   ├── Dev/               # ShotFeedbackMonitor (live tracker validation overlay)
│   │   │   │   ├── Physics/           # Flight integrator, aero model, ground/bounce/roll, putting
│   │   │   │   ├── Course/            # HoleDefinition, CourseDefinition, surfaces, hazards
│   │   │   │   ├── Rules/             # Lies, penalties, drops, holing out
│   │   │   │   ├── GameModes/         # Range, StrokePlay, MatchPlay, turn order
│   │   │   │   ├── Players/           # Profiles, bags, stats
│   │   │   │   ├── Cameras/
│   │   │   │   ├── UI/
│   │   │   │   ├── Ambience/          # Themed events, weather, audio triggers
│   │   │   │   ├── Settings/
│   │   │   │   └── Persistence/
│   │   │   ├── Scenes/
│   │   │   │   ├── Boot.unity
│   │   │   │   ├── MainMenu.unity
│   │   │   │   ├── Range_GreenDragon.unity
│   │   │   │   ├── Course_Shire.unity
│   │   │   │   ├── Course_Rivendell.unity
│   │   │   │   ├── Course_Mordor.unity
│   │   │   │   ├── Dev_ShotFeedback.unity
│   │   │   │   └── Dev_PhysicsLab.unity
│   │   │   ├── Editor/                # Menu items: shot-log validation, dev scene builder
│   │   │   ├── Tests/EditMode/        # NUnit tests (OneWood.Tests.EditMode)
│   │   │   ├── Data/                  # ScriptableObjects: courses, holes, surfaces, clubs
│   │   │   ├── Art/                   # In-house models, materials, textures
│   │   │   ├── Audio/
│   │   │   └── Settings/              # HDRP assets, volume profiles, input actions
│   │   └── ThirdParty/                # Imported free packs + CREDITS.md
│   ├── Packages/
│   └── ProjectSettings/
├── Tools/
│   ├── open_connect.py             # Fake FS Golf client (send) / tracker capture server (capture)
│   ├── run_tests.sh, run_tests.ps1 # Batch-mode EditMode test runners
│   └── ShotLogs/                   # Sample and problem shot logs (test fixtures, replayable)
├── Blender/                        # Source .blend files for landmark pieces
└── docs/
    ├── open-connect-protocol.md
    ├── physics-model.md
    └── course-authoring.md
```

---

## Development setup

1. Install **Unity Hub** and **Unity 6 LTS** with the *Windows Build Support (IL2CPP)* module.
2. Install **Git LFS** and run `git lfs install` before cloning. `.gitattributes` sends textures, models, audio, video, fonts, native plugins and baked lighting/NavMesh data to LFS. Keep `TerrainData` assets in a folder named `TerrainData/` so they go to LFS too. Unity is set to **Force Text** serialization, so scenes and prefabs stay as mergeable YAML.
   - *Optional, Unity Smart Merge:* point git at Unity's YAML merge tool so scene and prefab conflicts merge semantically:
     ```
     git config merge.unityyamlmerge.name "Unity SmartMerge"
     git config merge.unityyamlmerge.driver "'C:/Program Files/Unity/Hub/Editor/<version>/Editor/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
     git config merge.unityyamlmerge.recursive binary
     ```
3. Open `OneWood/` in Unity Hub. The project uses HDRP, the Input System, Cinemachine, and the NVIDIA DLSS package.
4. Install **FS Golf PC** on the simulator PC, pair the Mevo+, and set the simulator connection to **GSPro / Open Connect** pointing at `127.0.0.1:921` (or the game PC's IP).
5. Allow One Wood through **Windows Firewall** on TCP 921 if FS Golf runs on another machine.
6. Without hardware: run `python Tools/open_connect.py send Tools/ShotLogs/sample_shots.jsonl` against the dev scene (see [Validating tracker feedback](#validating-tracker-feedback)).

Most code (protocol, physics, rules) is plain C# and can be developed and unit-tested on any OS. Rendering, DLSS and the final integration are tested on the Windows/NVIDIA machine.

---

## Testing

Run the EditMode suite with `Tools/run_tests.sh` (Linux/macOS) or `Tools\run_tests.ps1` (Windows) with the editor closed, or from **Window → General → Test Runner** in the editor. Results go to `OneWood/Logs/tests/`.

- **EditMode unit tests (current):** message framing (split, concatenated, escaped strings, junk, oversize), Open Connect parsing (malformed JSON, wrong types, unknown fields), validation rules and SI conversion, responses and club codes, shot-log round trips and reports, loopback TCP server tests (acks, 501 on bad input, player info, reconnects), and every line in `Tools/ShotLogs/`.
- **EditMode unit tests (planned):** physics (known launch → expected carry within tolerance), bounce and roll, rules (penalties, drops, turn order), scoring.
- **PlayMode tests:** a full hole played with recorded shots ends with the expected score and ball positions.
- **Protocol integration:** `Tools/open_connect.py send` against a running build: connect/disconnect/reconnect, heartbeats, bursts of shots, and all framing modes.
- **Physics calibration:** regression suite of logged real shots. Fails if mean carry error exceeds ±3%.
- **Performance:** frame-time captures per course at the projector resolution for each quality preset.

---

## Roadmap

| Milestone | Scope |
|---|---|
| **M0: Foundations** | Unity HDRP project, repo/LFS, folder layout, settings, input actions, boot/menu scenes |
| **M1: Shot in, ball out** | Open Connect server, mock shot source, shot logging, flight physics, a basic range on a flat plane with a tracer and shot-data HUD |
| **M2: Range** | Green Dragon Range scene, ground physics (bounce/roll), per-club session stats, physics calibration lab |
| **M3: First hole** | Course/hole data model, surfaces, hazards and rules, cameras, real putting on a sloped green, Shire hole 1 playable |
| **M4: Vertical slice** | All 3 Shire holes, stroke play, scorecard, local multiplayer (1–4), profiles |
| **M5: Middle-earth** | Rivendell and Minas Tirith/Mordor (3 holes each), The Journey round, match play |
| **M6: Polish** | Themed ambient events, audio, flyovers, HUD theming, DLSS and performance tuning, projector calibration |
| **Later** | Expand to 9/18 holes per course, Stableford/scramble/skins, more courses (Rohan/Edoras, Moria, Lothlórien, Isengard), weather variation, optional phone web-remote |

---

## Open items and assumptions

To verify early (M1):
- [ ] **FS Golf → Open Connect support.** Confirm the current FS Golf PC version can stream Mevo+ shots to a custom GSPro Open Connect endpoint, and whether any FlightScope license or subscription is required.
- [ ] **Exact Open Connect schema.** Check the field names, units and response codes against the current GSPro Open Connect v1 documentation, and against real messages captured from FS Golf. The validation report flags unknown fields and field coverage.
- [ ] **Responses to heartbeats.** The server acks shots with 200 but stays silent on heartbeats and status messages (`RespondToStatusMessages`, off by default). Check whether FS Golf expects a reply.
- [ ] **Sign conventions.** Confirm FS Golf's HLA, SpinAxis and SideSpin are positive to the right. The validator warns with `ball.spin.sign_mismatch` if SpinAxis and SideSpin disagree.
- [ ] **Metric units.** If FS Golf sends `"Units": "Metres"`, confirm whether speeds switch to m/s. They are currently assumed to stay in mph.
- [ ] **Putting through FS Golf.** Confirm FS Golf sends putts reliably over Open Connect (and whether a putting mode must be switched on, or switched by the game's response). Measure Mevo+ putt accuracy indoors at short distances.
- [ ] **Club data availability.** Which `ClubData` fields the Mevo+ fills in (some need the Pro Package or face-impact features).
- [ ] **Indoor setup.** Ball-to-device distance and indoor-mode settings in FS Golf for the impact-screen room.

Assumptions:
- The game and FS Golf run on the same Windows PC, connected over localhost.
- Distances are shown in **yards** by default, with metres as an option. Physics runs in SI internally.
- One projector output plus an optional operator monitor.
- Single-player and multiplayer data stays on the local machine only.
