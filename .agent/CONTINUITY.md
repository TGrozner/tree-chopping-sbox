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
- `FallenLog` is physics-owned like Valheim TreeLog: gravity, inertia, contacts, first-frame grace only, no landed physics gate, no ground-contact damping, no force-land, no lift/depenetration snap, no artificial roll coupling, no manual sleep.
- Effects route through `ValheimEffects` mini EffectLists now. This centralizes Valheim-style hit/destroy/impact/start routes, but is still code-defined rather than asset-authored.
- `HitData` is now a mini Valheim payload: chop/blunt channels, tool tier, push force, and tree/log modifiers. Current tree/log modifier breadth is chop normal + blunt immune.
- Parent log spawn keeps Valheim-authored horizontal/lateral `m_logSpawnPoint` parity, but runtime Z is grounded from procedural log length (`0.5 * length + 6u`) because Valheim prefab Y offsets assume real mesh pivots/heights that our generated trees do not share.
- Parent logs intentionally use near-trunk length ratios `{0.78,0.82,0.72,0.76}` (Normal/Sapling/Veteran/Brittle), guarded below for underwhelming logs and above at `0.88` to avoid reading as the whole tree/canopy.

## Current State
- `ValheimImpact` centralizes verified Valheim `Utils.LerpStep` impact scaling for Tree/FallenLog.
- Audio capture distinguishes local player SFX from positional/world SFX; selftest covers swing, hit, chop, miss.
- `tools/selftest.ps1` supports parallel seeds, retrying only the known local compile race where `SceneStarter` briefly misses after compile.
- `tools/check.ps1` exposes `-Seeds`, `-MaxParallel`, `-TimeoutSeconds`, `-MinPassMarkers`, `-PhysicsOnly`, and `-FullSelftest`; default marker floor is 52, physics-only lowers it to the focused 13-marker contract.
- `SelfTest.TestLandedLogSupport` now logs `LOG_PHYS` origin/center/axis/probe clearances and guards distributed support shapes plus terrain support.
- `SelfTest.TestLandedLogGravity` guards gravity-on logs: an airborne diagnostic log must fall under gravity and must not be snap-dropped.
- Player contact no longer has a bespoke log-velocity clamp; if player/log contact feels wrong, solve with collision shapes/mass/layers first, not runtime velocity edits.
- `SelfTest.TestSplitLogSpawn` guards smaller-log spawn pose, Valheim half mass, first-frame damage grace, and TreeLog-like push response.
- `SelfTest.TestSplitLogSpawn` now also guards the whole live-tree -> parent TreeLog -> smaller TreeLogs chain with a shared Rigidbody contract; children differ physically only by Valheim half mass.
- `SelfTest.TestCascadeCollision` guards both falling-log cascade and already-landed-log cascade into a sapling.
- `SelfTest.TestValheimLogLaunch` guards the actual Valheim spawn impulse immediately after `ApplyImpulseAt`, authored `TreeKind -> m_logSpawnPoint` reference offset, grounded runtime spawn center, bottom clearance, and parent log length for all four tree kinds; body velocity after terrain contact is logged separately because slope/contact can redirect the visible log.
- Full Valheim `TreeBase.m_logSpawnPoint` local transforms are extracted in `_valheim-decompile/CONSTANTS.md` and wired through `FallenLog.SpawnFromTree`.
- Legacy single-object Tree-as-log fallback is disabled; if `FallenLog.SpawnFromTree` fails, the code logs an error instead of silently using the old s&box compensation path.
- Cascade broad-sweep is removed; cascade damage now depends on real `FallenLog.OnCollisionStart` physics contacts only.
- FilmStrip now fails on log-physics absurdities, not just sequence misses: settled logs are audited for penetration, floating clearance, vertical pose, upward pop, high speed, and high spin; `tools/feel-suite.ps1` reads `TC_FEEL_SUMMARY`.
- FilmStrip also asserts Valheim standing HP per kind, so GUI captures fail if sbox-dev runs a stale/partial hotload with old resistance values.
- Resistance ratios are now compressed from extracted Valheim prefabs: axe chop power /10, standing tree HP /10, parent/smaller log HP /10, and impact max damage=3 because blunt is immune. Visual scale no longer changes tree HP.
- `FallenLog` HP is set at TreeLog spawn and is never reset by landing; `TestFallingImpactSplit` guards partial falling damage, player damage, no landed gate, and fresh smaller-log instantiation.
- `TestFallingImpactSplit` waits for a damaged TreeLog to land naturally; no selftest helper may force `BecomeLandedLog` for physics assertions.
- Tick-time ground-contact correction is removed from the log path. Sticky drag, scripted roll coupling, render-frame contact limits, depen caps, lift/snap, and manual sleep are deleted from runtime TreeLog motion.
- `Tree` is now standing-TreeBase only: no collision listener, no landed-log kick, no log physics writes after `StartFell`; `FallenLog` owns TreeLog collision/physics exclusively. `TestTunablesValheimSanity` guards against reintroducing Tree-as-log state.
- Backpack-full pickup now rejects nearby WoodItems before magnetizing; items stay physical/gravity-on and are nudged out instead of sticking to the player/collider.
- Smaller-log geometry is guarded: Valheim Beech/Fir/Pine/Birch/Oak all use 2 smaller-log points (`m_subLogPoints`); our Normal/Veteran spawn 2 smaller logs, with per-kind length 0.50 parent and visual width 0.62/0.64 parent. Spawn disables parent body/collider, uses only parent/authored axis plus side offset, and creates fresh falling TreeLogs; there is no terrain-sampled pose, lift, or runtime stabilization after spawn.
- `TestSplitLogSpawn` also guards the log-hit scar scale; `Model.Cube` children under custom-scaled log meshes must divide local scale by `Tunables.CubeBase` or they render enormous.
- `TestImpactNoSelfDamage` now also guards hard vertical ground impacts: a falling TreeLog must land horizontal-ish without self-splitting (`m_damageToSelf=false`), while normal falling-impact tests still require the fresh log to remain Falling until explicit damage.

## Incidents
- Logs/smaller logs previously failed by landing too early, floating, entering terrain, or turning vertical; keep those cases guarded.
- sbox-dev can partial-hotload changed classes and leave unchanged `Tree/Tunables` stale in Play. If FilmStrip reports `wrong_hp`, restart sbox-dev before trusting visual captures.
- Silent visual/physics fallbacks are dangerous here; prefer failing selftests or visible warnings.
- Visual review previously misread stump vs split log; use filmstrip sequence/events, not one still image, when judging feel.

## Receipts
- 2026-05-23T10:08+02:00: s&box compensation audit removed legacy Tree-as-log fallback, removed airborne snap-down, neutralized extra post-impact damping, and matched broad-sweep cascade to Valheim ImpactEffect. Check + feel-suite PASS.
- 2026-05-23T11:03+02:00: Valheim-prefab resistance/weight ratios wired: tree HP `{8,4,20,12}`, log HP `{6,3,16,6}`, smaller-log HP `{6,0,14,0}`, impact max=3. Check + long feel-suite PASS.
- 2026-05-23T11:16+02:00: Backpack-full pickup fix: items reject before magnetizing, no consume/no stick/gravity-on guarded by `TestBackpackFullPickup`. `tools/check.ps1 -Seeds 4 -MaxParallel 2 -TimeoutSeconds 260` PASS.
- 2026-05-23T11:27+02:00: Oversized smaller-log regression fixed: no width-derived minimum length, Veteran split count 2 from `m_subLogPoints`, child length max 50% parent guarded. Check + feel-suite PASS.
- 2026-05-23T12:04+02:00: Five-gap Valheim pass: `ValheimEffects`, mini `HitData.DamageTypes`, 0.2s combo, per-kind smaller-log points/lengths, scar scale guard. Check + feel-suite PASS on `_captures/20260523-120224-870`, `120239-287`, `120302-538`, `120325-987`.
- 2026-05-23T12:49+02:00: Parent logs enlarged to read as most of the original trunk: `TreeKindLogLengthMul {0.78,0.82,0.72,0.76}` with selftest lower bounds and max `0.88`. Runtime spawn still grounds center to `0.5 * length + 6u`, so tall trees do not spawn logs high above the stump. `dotnet build` PASS; `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 260` PASS. Filmstrip deliberately skipped per Thomas.
- 2026-05-23T13:14+02:00: Physics-sovereignty pass after Thomas spotted brutal fall slowdown. Falling logs never run ground-contact limits; hard end-first ground hits no longer force `BecomeLandedLog`; sticky drag/roll/near-ground damping are neutralized. Only landed deep-penetration correction remains. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 260` PASS (seeds 51800..51803). Filmstrip deliberately skipped per Thomas.
- 2026-05-23T13:40+02:00: Gravity-first rustine purge: removed manual sleep, render-frame contact limits, roll/drag helpers, runtime split-log stabilization, and angular depen cap. First check caught vertical split logs on seeds 51800/51801; final fix disables parent physics before child spawn and spawns smaller logs on a terrain-tangent axis. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 260` PASS (seeds 51800..51803). Filmstrip skipped.
- 2026-05-23T14:08+02:00: TreeBase/TreeLog ownership cleanup: removed the remaining dead `Tree.ApplyLandedKick` path and stale Tree-as-log physics writes; selftest now asserts `Tree` is not `ICollisionListener`, `StartFell` creates a collision-owning `FallenLog`, and legacy Tree-as-log count stays zero. `tools/check.ps1 -Seeds 4 -MaxParallel 2 -TimeoutSeconds 260` PASS (seeds 51800..51803, 54 PASS markers).
- 2026-05-23T14:48+02:00: Compensation polish pass: player-bump cap narrowed and normal rolling preserved; depen cap now clamps positive Z only; logs are wider/12-sided with inset colliders; split-log side-tangent fallback fixed seed 51803 vertical planting; `log_impact/log_landing/log_split` sound assets added; starter/depot rhythm tightened. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 260` PASS. Filmstrip skipped per Thomas.
- 2026-05-23T15:31+02:00: Valheim TreeLog damage tightened: falling logs are choppable/damageable after first-frame grace, partial impact/player hits no longer force landing, `OnCollisionUpdate` no longer reapplies ImpactEffect damage, smaller logs spawn as fresh physics-owned TreeLogs with side offset/lift, and vertical-pose selftests are terrain-aware. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 260` PASS (seeds 51800..51803). Filmstrip skipped.
- 2026-05-23T15:43+02:00: Removed final HP reset fallback at TreeLog landing and guarded HP preservation from falling damage through landing. `TestValheimLogLaunch` now asserts immediate post-impulse velocity/angular velocity and treats post-contact velocity as diagnostic only. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 260` PASS (seeds 51800..51803). Filmstrip skipped.
- 2026-05-23T17:47+02:00: Valheim TreeLog state audit: runtime no longer treats landed as a physics/damage gate, smaller logs instantiate fresh without inherited velocity, tick-time depen/lift correction removed, and logs use capsule + 5 support spheres for distributed terrain contact. `LOG_PHYS` traces log/ground coordinates in selftest. `tools/check.ps1 -Seeds 4 -MaxParallel 1 -TimeoutSeconds 260` PASS (54 markers). Filmstrip skipped because editor/game not running.
- 2026-05-23T18:37+02:00: Hard physics-rustine sanitize: removed player-bump damping, landed post-impact velocity/damping writes, dead snap-to-ground helper, depen references, landed damping aliases, broad-sweep cascade, terrain-sampled child-log pose/lift, plus dead physics knobs/branches (`FellTorque`, `InitialFellOmega`, per-kind torque/omega, `ImpactDamageSelf`, split-impact break thresholds). Added `tools/check.ps1 -PhysicsOnly`; validation PASS: full quick `tools/check.ps1 -Seeds 1 -MaxParallel 1 -TimeoutSeconds 130` (53 markers) and physics fuzz `tools/check.ps1 -PhysicsOnly -Seeds 4 -MaxParallel 2 -TimeoutSeconds 100` (seeds 51800..51803, 13 markers/seed, 54s wall).
- 2026-05-23T18:52+02:00: Sublog parity guard: parent logs and smaller logs now share `ConfigureTreeLogBody`; `LIVE_TREE_TO_SUBLOGS` asserts same Rigidbody/collider contract from live tree -> log -> smaller logs, mass 100/50 only. Physics profile skips forest/hub props and validated `tools/check.ps1 -PhysicsOnly -Seeds 4 -MaxParallel 2 -TimeoutSeconds 100` PASS (44s wall); full quick PASS (53 markers, 31s).
- 2026-05-23T19:13+02:00: Test-rustine purge: removed `DebugBecomeLandedForTest`; `TestFallingImpactSplit` now applies falling damage, waits for real contact-driven landing, then checks HP preservation. Validation PASS: physics fuzz `tools/check.ps1 -PhysicsOnly -Seeds 4 -MaxParallel 2 -TimeoutSeconds 120` (seeds 51800..51803, 13 markers/seed, 56s wall), post-guard physics smoke (13 markers, 24s), and final quick `tools/check.ps1 -Seeds 1 -MaxParallel 1 -TimeoutSeconds 150` (53 markers, 35s).

## Next
- Treat log physics as guarded for the current vertical slice. If Thomas sees a mismatch in Play, capture the exact scenario and add a selftest/filmstrip before retuning.
- Continue Valheim alignment where observable gaps remain: TreeLog chop readability, impact/cascade readability, audio selection/levels, terrain/tree density feel.
