# CONTINUITY.md

Compact briefing for future Codex sessions. Durable facts only; no transcript.

## Snapshot
- Goal: mow-the-lawn-like progression where chopping trees feels as close as possible to Valheim.
- Process source of truth: `AGENTS.md`, `Code/AGENTS.md`, this file.
- Validation default: `tools/check.ps1`; current contract is build clean + selftest with at least 52 PASS markers per seed.
- Visual/audio/feel changes need filmstrip evidence via `tools/capture-feel.ps1` or `tools/feel-suite.ps1`.
- Valheim reference corpus: `_valheim-decompile/INDEX.md`, `_valheim-decompile/TREE-PIPELINE.md`, `TreeBase`, `TreeLog`, `ImpactEffect`, `HitData`, `EffectList`.
- Valheim test assemblies are not shipped; use decompiled runtime code + our selftests/filmstrips as the regression oracle.

## Decisions
- Valheim first for tree/log physics; any s&box compensation must be named in a selftest or `_valheim-decompile/TREE-PIPELINE.md`.
- Standing trees stay kinematic until chopped; cascade is impact damage, not physical pushing of standing trees.
- Falling logs follow Valheim `TreeBase.SpawnLog`: `ResetInertiaTensor` + one off-center impulse (`hitDir * 0.2m/s * mass` at `+4m * scale.y`), no scripted torque seed.
- `HitData.ChopPower` and `HitData.PushForce` stay split like Valheim damage vs push; final combo boosts push only where Valheim does.
- Smaller logs are still `FallenLog` and must behave like TreeLog: half mass, same first-frame damage grace, same kick/impact/damage path.
- Landed logs keep gravity enabled like Valheim TreeLog. Ground-contact limits apply only near/recent terrain contact; airborne landed logs fall back under gravity instead of being snapped down.

## Current State
- `ValheimImpact` centralizes verified Valheim `Utils.LerpStep` impact scaling for Tree/FallenLog.
- Audio capture distinguishes local player SFX from positional/world SFX; selftest covers swing, hit, chop, miss.
- `tools/selftest.ps1` supports parallel seeds, retrying only the known local compile race where `SceneStarter` briefly misses after compile.
- `tools/check.ps1` exposes `-Seeds`, `-MaxParallel`, `-TimeoutSeconds`, `-MinPassMarkers`; default marker floor is 52.
- `SelfTest.TestLandedLogSupport` guards horizontal, terrain-supported landed logs.
- `SelfTest.TestLandedLogGravity` guards gravity-on landed logs: an airborne landed log must fall under gravity and must not be snap-dropped.
- `SelfTest.TestPlayerBumpDamping` guards landed logs keeping weight when the player bumps into them.
- `SelfTest.TestSplitLogSpawn` guards smaller-log spawn pose, Valheim half mass, first-frame damage grace, and TreeLog-like push response.
- `SelfTest.TestCascadeCollision` guards both falling-log cascade and already-landed-log cascade into a sapling.
- `SelfTest.TestValheimLogLaunch` guards the actual Valheim spawn impulse direction and compressed `TreeKind -> m_logSpawnPoint` mapping for all four tree kinds; body velocity after terrain contact is logged separately because slope/contact can redirect the visible log.
- Full Valheim `TreeBase.m_logSpawnPoint` local transforms are extracted in `_valheim-decompile/CONSTANTS.md` and wired through `FallenLog.SpawnFromTree`.
- Legacy single-object Tree-as-log fallback is disabled; if `FallenLog.SpawnFromTree` fails, the code logs an error instead of silently using the old s&box compensation path.
- Cascade broad-sweep remains a collision reliability compensation, but its interval/min speed/damage multiplier now match Valheim `ImpactEffect` exactly.
- FilmStrip now fails on log-physics absurdities, not just sequence misses: settled logs are audited for penetration, floating clearance, vertical pose, upward pop, high speed, and high spin; `tools/feel-suite.ps1` reads `TC_FEEL_SUMMARY`.
- FilmStrip also asserts Valheim standing HP per kind, so GUI captures fail if sbox-dev runs a stale/partial hotload with old resistance values.
- Resistance ratios are now compressed from extracted Valheim prefabs: axe chop power /10, standing tree HP /10, parent/smaller log HP /10, and impact max damage=3 because blunt is immune. Visual scale no longer changes tree HP.
- Ground-contact limits run in fixed and visible update for landed logs; gravity stays enabled, but Source 2 post-physics spin/pops are capped before they become visible filmstrip anomalies.
- Backpack-full pickup now rejects nearby WoodItems before magnetizing; items stay physical/gravity-on and are nudged out instead of sticking to the player/collider.

## Incidents
- Logs/sublogs previously failed by landing too early, floating, entering terrain, or turning vertical; keep those cases guarded.
- sbox-dev can partial-hotload changed classes and leave unchanged `Tree/Tunables` stale in Play. If FilmStrip reports `wrong_hp`, restart sbox-dev before trusting visual captures.
- Silent visual/physics fallbacks are dangerous here; prefer failing selftests or visible warnings.
- Visual review previously misread stump vs split log; use filmstrip sequence/events, not one still image, when judging feel.

## Receipts
- 2026-05-23T04:00+02:00: post-fix feel-suite PASS; contact sheets `_captures/20260523-035851-787`, `035904-003`, `035920-625`, `035941-475` reviewed with no planted/floating split logs.
- 2026-05-23T04:09+02:00: `tools/check.ps1 -Seeds 4 -MaxParallel 2 -TimeoutSeconds 120` PASS after fixing exact-min ImpactEffect damage to 0 and logging launch impulse dir/bodyDir separately.
- 2026-05-23T04:17+02:00: `tools/check.ps1 -Seeds 8 -MaxParallel 4 -TimeoutSeconds 120` PASS (capped to x2); bodyDir remained seed/terrain-dependent, pointing to authored `m_logSpawnPoint` parity rather than damping.
- 2026-05-23T04:19+02:00: Added `bodyDot` to `VALHEIM_LOG_LAUNCH` diagnostics; `tools/check.ps1 -Seeds 1 -MaxParallel 1 -TimeoutSeconds 120` PASS.
- 2026-05-23T04:25+02:00: Added `C:\dev\_tools\extract_valheim_treebase_spawnpoints.py`; it extracts 24 `TreeBase.m_logSpawnPoint` rows from Valheim bundles. Normalized table is in `_valheim-decompile/CONSTANTS.md`.
- 2026-05-23T09:03+02:00: `TreeKindLogSpawnPoint` wired into `FallenLog.SpawnFromTree`; `tools/check.ps1 -Seeds 8 -MaxParallel 4 -TimeoutSeconds 170` PASS after correcting mythic TreeBase drop bounds.
- 2026-05-23T09:05+02:00: `tools/feel-suite.ps1 -AudioLog -DurationSeconds 24 -FrameIntervalMs 180` PASS; Sapling/Normal/Veteran/Brittle captures `_captures/20260523-090358-841`, `090411-201`, `090428-216`, `090448-600` reviewed with no planted/floating logs.
- 2026-05-23T09:19+02:00: FilmStrip physics anomaly audit added. `tools/check.ps1 -Seeds 1 -MaxParallel 1 -TimeoutSeconds 170` PASS and `tools/feel-suite.ps1 -AudioLog -DurationSeconds 24 -FrameIntervalMs 180` PASS with `physAnom=0`; latest captures `_captures/20260523-091737-635`, `091749-849`, `091806-809`, `091827-760`.
- 2026-05-23T09:34+02:00: Landed logs/smaller logs reverted to Valheim-style gravity-on; contact limits are near/recent terrain only. `tools/check.ps1 -Seeds 4 -MaxParallel 2 -TimeoutSeconds 170` PASS and `tools/feel-suite.ps1 -AudioLog -DurationSeconds 24 -FrameIntervalMs 180` PASS with captures `_captures/20260523-093100-485`, `093112-926`, `093130-320`, `093149-605`.
- 2026-05-23T09:56+02:00: Test strategy hardened: new landed-log airborne gravity probe, fresh split-log collision grace now covers physics contacts and broad sweep, FilmStrip flags gravity-off/falling-hover and uses 0-miss trunk targeting. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 180` PASS, final `tools/check.ps1 -Seeds 1 -MaxParallel 1 -TimeoutSeconds 180` PASS, `tools/feel-suite.ps1 -AudioLog -DurationSeconds 24 -FrameIntervalMs 180` PASS with captures `_captures/20260523-095407-776`, `095420-277`, `095437-644`, `095456-834`.
- 2026-05-23T10:08+02:00: s&box compensation audit pass: removed legacy Tree-as-log fallback, removed airborne snap-down during ground-contact limits, neutralized post-impact velocity damping (1.0/1.0), and made broad-sweep cascade use Valheim min speed/interval/damage exactly. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 180` PASS; `tools/feel-suite.ps1 -AudioLog -DurationSeconds 24 -FrameIntervalMs 180` PASS with captures `_captures/20260523-100735-915`, `100748-243`, `100805-581`, `100825-621`.
- 2026-05-23T11:03+02:00: resistance/weight/log-physics pass final: Valheim-prefab HP/chop ratios wired (`TreeKindChopsBase {8,4,20,12}`, fixed per kind not visual scale, `LogChopHP {6,3,16,6}`, smaller-log HP `{6,0,14,0}`, impact max=3). FilmStrip `wrong_hp` caught stale sbox-dev HP=12 for Veteran; editor restart restored HP=20. Added grounded axe-kick/post-physics contact caps for visible smaller-log spin. `tools/feel-suite.ps1 -AudioLog -DurationSeconds 64 -FrameIntervalMs 180` PASS with `physAnom=0` on `_captures/20260523-105947-307`, `110002-975`, `110027-235`, `110056-707`; `tools/check.ps1 -Seeds 4 -MaxParallel 2 -TimeoutSeconds 260` PASS.
- 2026-05-23T11:16+02:00: backpack-full pickup fix: `WoodItem` checks `GameState.BackpackFull` before magnetizing and selftest phase `TestBackpackFullPickup` guards no consume/no stick/gravity-on/outward reject. `tools/check.ps1 -Seeds 4 -MaxParallel 2 -TimeoutSeconds 260` PASS.

## Next
- Treat log physics as guarded for the current vertical slice. If Thomas sees a mismatch in Play, capture the exact scenario and add a selftest/filmstrip before retuning.
- Continue Valheim alignment where observable gaps remain: landed-log chop readability, impact/cascade readability, audio selection/levels, terrain/tree density feel.
