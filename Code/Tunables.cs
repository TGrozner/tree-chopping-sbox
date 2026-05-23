namespace TreeChopping;

public static class Tunables
{
	// Model.Cube => models/dev/box.vmdl is a 50u native cube. Spawn helpers
	// must scale WorldScale = wantedSize / CubeBase, and BoxCollider.Scale =
	// (CubeBase, CubeBase, CubeBase).
	public const float CubeBase = 50f;
	public const float UnitsPerMeter = 39.37f;

	// Player / player.
	public const float PlayerEyeHeight = 56f;

	// Tree geometry. Per-spawn scale jitter hashed on foot XY gives the
	// forest natural variation — small saplings to towering veterans share
	// the same arena. Mass scales with volume so taller trees fall heavier
	// and chain harder.
	// 470u ~= 12m : Normal stays Beech/Oak sized; Veteran reaches tall Pine.
	public const float TreeHeight = 470f;
	public const float TreeRadius = 32f;
	public const float TreeMass = 240f;
	public const float TreeScaleMin = 0.82f;
	public const float TreeScaleMax = 1.22f;

	// Tree kinds : Normal (baseline), Sapling (thin+light), Veteran (big+heavy),
	// Brittle (splits early on impact). Indices match TreeKind enum order.
	// Distribution weights are sampled per tree at spawn by interpolating
	// between Easy (close to spawn) and Hard (far) — biome bias for the
	// mow-the-lawn loop : starts trivial near the shop, scales up outward.
	// Easy band ~ pure-sapling 2026-05-21 : Thomas wants the player to start
	// surrounded by fragile little trees they can knock down with bare hands.
	// Normal/Veteran/Brittle stay reachable in the hard band (far from spawn).
	public static readonly int[] TreeKindWeightsEasy  = {   5,   90,     0,     5 };
	public static readonly int[] TreeKindWeightsHard  = {  35,    5,    50,    10 };
	public static readonly float[] TreeKindScaleMul   = { 1.0f, 0.55f, 1.6f,    1.0f };
	public static readonly float[] TreeKindVisualTrunkWidthMul = { 0.78f, 0.88f, 0.62f, 0.72f };
	// Veteran mass capped at 2.2× — higher values send neighbors flying on
	// trunk-on-trunk impact (rigid-body impulse goes superlinear).
	public static readonly float[] TreeKindMassMul    = { 1.0f, 0.30f, 2.2f,    0.8f };
	public static readonly Color[] TreeKindTrunkTint  =
	{
		new( 0.58f, 0.40f, 0.26f, 1f ), // Normal — default brown
		new( 0.68f, 0.52f, 0.30f, 1f ), // Sapling — lighter, warmer
		new( 0.42f, 0.28f, 0.20f, 1f ), // Veteran — darker, ancient bark
		new( 0.74f, 0.62f, 0.36f, 1f ), // Brittle — pale yellow-brown, dried out
	};
	// Per-tree multiplicative jitter on RGB — each tree picks one of these in
	// a deterministic hash. Makes a forest of varied bark colours rather than
	// a uniform wall of the same brown.
	public static readonly float[] TreeTintJitter = { 0.82f, 0.90f, 1.00f, 1.08f, 1.18f };

	// Mythic trees : rare gold-tinted veterans worth a big bonus on fell.
	// 1 in MythicSpawnRatio trees becomes mythic. Visibly larger + warmer
	// tinted so players can plan a run to hit one inside a cluster.
	public const int MythicSpawnRatio = 120;
	public const float MythicScaleMul = 1.3f;

	// Axe tier ladder, compressed from Valheim axe chop damage by /10:
	// Stone=20, Bronze=40, Iron=50, BlackMetal=60, Berserkr=80.
	// T0 Hands and T6 Chainsaw are our prototype endpoints around that scale.
	public const int MaxAxeTier = 6;
	public static readonly int[] AxeTierChopPower = {    1,   2,    4,    5,    6,    8,   10 };
	// Valheim 1:1 recipes — each axe tier requires multi-resource cost. Index
	// 0=Wood, 1=Finewood, 2=CoreWood. Stone/Bronze pure Wood, Iron+ requires
	// Finewood (Birch/Oak-tier resources), Steel+ requires CoreWood (Pine-tier).
	// Match l'arche progression de Valheim : Stone → Bronze → Iron → Black Metal.
	public static readonly int[][] AxeTierCostsByType =
	{
		new int[] {   0,   0,   0 }, // T0 Hands — free baseline
		new int[] {   8,   0,   0 }, // T1 Stone — pure Wood (Beech)
		new int[] {  28,   0,   0 }, // T2 Bronze — Wood (cuivre tabou ici → on stay Wood)
		new int[] {  60,  10,   0 }, // T3 Iron — Wood + Finewood (Oak resource)
		new int[] { 160,  30,  10 }, // T4 Steel — + CoreWood (Pine resin)
		new int[] { 400,  80,  30 }, // T5 Lumberjack
		new int[] { 950, 200,  80 }, // T6 Chainsaw
	};
	// Wood mul curve bumped 2026-05-21 so each tier upgrade feels worth it —
	// roughly doubles per tier instead of linear-ish. Pairs with the per-kind
	// AxeTier gate : buying Stone (T1) unlocks Normals (+3 wood each) and a
	// 1.5× stockpile multiplier on top → income goes from trickle to flow.
	public static readonly float[] AxeTierWoodMul = { 1.0f, 1.5f, 2.5f, 4.0f, 6.5f, 10.0f, 16.0f };
	public static readonly string[] AxeTierName =
	{
		"Hands", "Stone", "Bronze", "Iron", "Steel", "Lumberjack", "Chainsaw",
	};

	// Personal stats (Mow-The-Lawn-style separate progression axes) :
	//   Speed : walk speed multiplier (PlayerController WalkSpeed)
	//   Luck  : chance per chop to double the wood drop
	//   Power : extra ChopPower added on top of the axe tier's base
	// Each costs its own wood ladder ; player picks at shop via Slot1..Slot4.
	public const int MaxStatTier = 5;

	// Tree shake animation (Valheim TreeBase.ShakeAnimation coroutine lignes
	// 194-209). Verbatim freq + decay + amplitude :
	//   localRotation = Euler(Sin(t*40Hz) * cubicDecay * 1.5°, 0, Cos(t*36Hz) * cubicDecay * 1.5°)
	//   cubicDecay = (1 - elapsed/duration)³
	// Duration 1s, dual-axis (X-pitch + Z/Forward-roll dans notre Z-up). Replace
	// notre ancien KickWobble single-axis lean (9° exp 9Hz) qui était notre
	// déviation. Pattern Valheim = buzz vibrato "axe biting wood".
	public const float TreeShakeDuration = 1.0f;
	public const float TreeShakeFreqA = 40f;     // Sin axis (X / Right) in Hz — Valheim exact
	public const float TreeShakeFreqB = 36f;     // Cos axis (Forward) — Valheim 40 × 0.9
	public const float TreeShakeAmplitudeDeg = 1.5f;
	public const float TreeHitFlashDuration = 0.16f;

	// Whoosh SFX pendant la chute — fire ONCE quand WorldRotation.Up.Dot(Up)
	// passe sous ce threshold (= tree tilt past ~45°). Match Valheim trees qui
	// font un "whoosh" audible en plein air. cos(45°) = ~0.707.
	public const float TreeWhooshUpDotThreshold = 0.707f;
	public const float SfxMasterVolume = 0.85f;

	// Per-kind chop SFX pitch multipliers — Sapling = high pitched crackle
	// (light/thin wood), Veteran = deep thunk (dense/old wood), Brittle = dry
	// crack. Multiplies AxeController.ApplyImpactFeedback chop_wood pitch
	// range. Indices match TreeKind enum (Normal/Sapling/Veteran/Brittle).
	public static readonly float[] TreeKindChopPitchMul = { 1.00f, 1.25f, 0.75f, 1.10f };

	// ImpactEffect.m_interval on Valheim tree-log prefabs.
	public const float ImpactInterval = 0.25f;

	// Multi-wood types Valheim 1:1 — Beech/Birch drops "Wood" + chance "Finewood",
	// Oak drops "Wood" + "Finewood", Pine (Black Forest) drops "Wood" + "Core Wood".
	// Notre mapping kind → type distribution :
	//   Normal/Sapling : 100% Wood (Beech-equivalent)
	//   Veteran        : 50% Wood + 50% Finewood (Oak-equivalent)
	//   Brittle        : 60% Wood + 40% Core Wood (Pine-equivalent)
	// Index : 0=Wood, 1=Finewood, 2=CoreWood. % roll per item dropped.
	public static readonly float[][] TreeKindWoodTypeMix =
	{
		new float[] { 1.0f, 0.0f, 0.0f }, // Normal → pur Wood
		new float[] { 1.0f, 0.0f, 0.0f }, // Sapling → pur Wood
		new float[] { 0.5f, 0.5f, 0.0f }, // Veteran → Wood + Finewood
		new float[] { 0.6f, 0.0f, 0.4f }, // Brittle → Wood + CoreWood
	};

	// Tints per wood type — Valheim visual differentiation. Wood = warm brown,
	// Finewood = pale yellow-brown (= Valheim "Fine Wood" creamy tone), Core Wood
	// = darker red-brown (= pine resin tint).
	public static readonly Color[] WoodTypeTints =
	{
		new Color( 0.58f, 0.34f, 0.18f, 1f ),  // Wood — standard brown
		new Color( 0.88f, 0.74f, 0.48f, 1f ),  // Finewood — cream/pale
		new Color( 0.42f, 0.20f, 0.14f, 1f ),  // Core Wood — dark red-brown
	};

	// Display names per type — Valheim verbatim.
	public static readonly string[] WoodTypeNames = { "Wood", "Finewood", "Core Wood" };

	// Global wind — Valheim EnvMan.GetWindDir/Intensity. Notre simplification :
	// direction rotate slowly sur WindRotationCycle (full circle), intensity pulse
	// en gusts sur WindGustCycle. Tous les arbres consomment la même valeur pour
	// avoir un sway cohérent dans la même direction (Valheim look).
	public const float WindRotationCycle = 90f;  // 90s pour un tour complet de direction
	public const float WindGustCycle = 8f;       // 8s par cycle de gust (intensity sine)

	// Combo chain Valheim Attack.m_attackChainLevels — Stone axe = 3-hit combo,
	// last hit damage × m_lastChainDamageMultiplier (=2) + pushForce ×1.2.
	// Reset si timeSinceLastAttack > window OR chain at max. Pattern Attack.cs
	// lignes 395-410 + 1094-1098.
	// Note : Valheim utilise 0.2s window mais avec swing cycle ~0.3s. Nous on
	// a SwingWindUpDuration 0.55 + Recovery 0.4 = ~0.95s par swing donc 0.2s
	// rendrait le combo INACTIVABLE. 1.2s adapte la timing à notre cycle :
	// click ~0.25s après recovery end = combo chained. Mécanique identique,
	// constante ajustée. Mécanique = m_attackChainLevels + last×2.
	public const int ChopComboMaxLevels = 3;
	public const float ChopComboWindow = 1.2f;
	public const float ChopComboFinalDamageMul = 2.0f;
	public const float ChopComboFinalPushMul = 1.2f;
	public const float SwingMoveSpeedFactor = 0.20f;

	// Per-tool sub-stats (phase C) : level inside the Tools station to extend
	// the current axe's reach + speed up the swing recovery. These ladders
	// are PERSISTENT (don't reset when buying a new axe) — the axe tier is
	// flavor + base power, the sub-stats are the player's investment.
	public const int MaxToolStatTier = 5;
	public static readonly int[]   ToolRangeCosts = {    0,   20,    65,   180,   460,  1100 };
	public static readonly float[] ToolRangeMul   = { 1.0f, 1.15f, 1.30f, 1.50f, 1.75f, 2.10f };
	public static readonly int[]   ToolSpeedCosts = {    0,   25,    75,   220,   560,  1300 };
	// Multiplier on SwingRecoveryDuration → lower = faster swing.
	public static readonly float[] ToolSpeedMul   = { 1.0f, 0.92f, 0.82f, 0.70f, 0.55f, 0.40f };

	// Backpack capacity tier — gates how much wood you can carry before
	// returning to the depot station. Base cap is intentionally tight (50)
	// so the back-to-depot loop kicks in within a few chops at the start.
	// Scales steep so late-game lets you mow several trees before returning.
	public const int MaxBackpackTier = 5;
	public static readonly int[] BackpackCosts =     {    0,   18,    60,   180,   480,  1200 };
	public static readonly int[] BackpackCaps =      {   50,  120,   280,   600,  1300,  3000 };

	// Cosmetic pet — purely visual companion. Each tier swaps colour/size
	// so progression reads at a glance.
	public const int MaxPetTier = 5;
	public static readonly int[] PetCosts = { 0, 30, 100, 280, 720, 1800 };
	public static readonly Color[] PetTints =
	{
		new( 0.6f, 0.6f, 0.6f, 1f ),   // T0 unused
		new( 0.96f, 0.84f, 0.30f, 1f ), // T1 yellow finch
		new( 0.42f, 0.86f, 0.96f, 1f ), // T2 cyan moth
		new( 0.96f, 0.32f, 0.42f, 1f ), // T3 crimson sprite
		new( 0.78f, 0.40f, 1.00f, 1f ), // T4 violet wisp
		new( 1.00f, 0.78f, 0.18f, 1f ), // T5 mythic gold
	};
	public static readonly float[] PetSizes =     { 0f, 9f, 11f, 13f, 16f, 20f };
	public static readonly int[] SpeedCosts =     {    0,   12,    40,   120,   320,   800 };
	public static readonly float[] SpeedMul =     { 1.0f, 1.15f, 1.30f, 1.50f, 1.75f, 2.10f };
	public static readonly int[] LuckCosts =      {    0,   15,    50,   140,   380,  1000 };
	public static readonly float[] LuckChance =   { 0.00f, 0.05f, 0.12f, 0.20f, 0.30f, 0.45f };
	public static readonly int[] PowerCosts =     {    0,   20,    65,   180,   460,  1100 };
	public static readonly int[] PowerBonus =     {    0,    1,     2,     3,     5,     8 };

	// Wood reward per tree kind (before tier multiplier). Veterans are
	// premium, saplings are practice.
	public static readonly int[] TreeKindWoodReward = { 3, 1, 8, 2 };
	public static readonly int MythicWoodBonus = 12;

	// Standing tree HP, compressed from Valheim TreeBase health by /10.
	// Normal=Beech80, Veteran=Oak200, Brittle=Pine120; Sapling is starter-scale.
	public static readonly int[] TreeKindChopsBase = { 8, 4, 20, 12 };

	// Per-kind minimum AxeTier required to chop. Lower tiers just bounce off
	// (no ChopsRemaining decrement, HUD hint). Drives the "buy a better axe
	// to unlock the bigger trees that drop more wood" progression curve.
	// Index matches TreeKind enum order : Normal / Sapling / Veteran / Brittle.
	// Veteran unlocks at Bronze/T2 so Finewood is earnable before the Iron/T3
	// recipe asks for it, matching Valheim's Birch/Oak progression shape.
	public static readonly int[] TreeKindMinAxeTier = { 1, 0, 2, 1 };
	// Per-kind respawn delay : seconds the landed log lingers before the
	// position grows a new tree. Order matches TreeKind enum
	// {Normal, Sapling, Veteran, Brittle}. Forager/Mow-The-Lawn pattern :
	// chopping is sustainable, the forest regrows on a timer that scales
	// with how much wood the kind drops.
	public static readonly float[] TreeKindRespawnDelay = { 90f, 30f, 300f, 60f };
	// Mythic respawn is an extra-long timer on top of the kind base — a
	// mythic Veteran (300+600=900s = 15 min) is a "weekend visit" reward.
	public const float MythicRespawnExtra = 600f;
	// Valheim TreeBase -> TreeLog. Standing tree -> falls -> landed log
	// (chopable) -> smaller landed logs or items.

	// LogChopHP = HP du landed log avant split-logs/items. Compressed /10 from
	// Valheim TreeLog health: Beech/Fir/Pine=60, Oak=160, Oak half=140.
	// TreeKindLandedDropCount = nombre d'items lâchés au split (= total wood
	// kind, modulé par luck + mythic à runtime).
	public static readonly int[] LogChopHP                  = { 6, 3, 16, 6 };
	public static readonly int[] TreeKindLandedDropCount    = { 4, 1, 9, 2 };
	public static readonly int[] TreeKindSplitLogCount      = { 2, 0, 3, 0 };
	public static readonly int[] TreeKindSplitLogHP         = { 6, 0, 14, 0 };
	public static readonly float[] TreeKindLogLengthMul     = { 0.34f, 0.42f, 0.30f, 0.34f };
	public static readonly float[] TreeKindLogWidthMul      = { 0.58f, 0.64f, 0.52f, 0.55f };
	public const float WoodItemPickupRange = 0.3f * UnitsPerMeter;
	public const float WoodItemMagnetRange = 2f * UnitsPerMeter;
	public const float WoodItemMagnetSpeed = 15f * UnitsPerMeter;
	// Grace post-spawn avant que le magnet engage — Valheim ItemDrop.CanPickup
	// reject auto-pickup si `Time.time - m_spawnTime < 0.5`. Laisse le burst
	// se voir avant que le snap commence.
	public const float WoodItemMagnetGrace = 0.5f;
	public const float WoodItemFullRejectCooldown = 0.35f;
	public const float WoodItemFullRejectDistance = 0.65f * UnitsPerMeter;
	public const float WoodItemFullRejectSpeed = 1.4f * UnitsPerMeter;
	public const float WoodItemFullRejectUpSpeed = 0.35f * UnitsPerMeter;
	public const float WoodItemDespawnDelay = 60f;
	public const float WoodItemHintRange = 140f;
	public static readonly Color WoodLogTint = new( 0.55f, 0.38f, 0.22f, 1f );
	public static readonly Color WoodItemTint = new( 0.70f, 0.50f, 0.28f, 1f );
	public static readonly Color MythicTrunkTint = new( 0.78f, 0.55f, 0.18f, 1f );
	public static readonly Color MythicCanopyTint = new( 1.00f, 0.78f, 0.22f, 1f );

	// Canopy : tinted cube parented above the trunk so the forest reads as
	// trees, not poles. Width relative to trunk radius, height relative to
	// trunk height. No collider — visual only ; cascade hits go through the
	// canopy and hit the trunk box (intended : you chop the trunk).
	// Forest greens — picked per tree from the palette via hash. Range from
	// muted blue-green to warm autumn yellow-brown for visual variety.
	public static readonly Color[] CanopyTints =
	{
		new( 0.34f, 0.50f, 0.26f, 1f ), // moss green (brightened 0.26/0.38/0.20)
		new( 0.42f, 0.58f, 0.28f, 1f ), // bright pine
		new( 0.30f, 0.46f, 0.38f, 1f ), // teal pine
		new( 0.52f, 0.54f, 0.22f, 1f ), // olive
		new( 0.62f, 0.46f, 0.20f, 1f ), // autumn ochre
	};

	// Fell physics. Valheim TreeBase.SpawnLog spawns a physical TreeLog,
	// resets its inertia tensor, then applies one off-center impulse. No
	// continuous torque ramp and no explicit angular velocity seed.
	public const float FellTorque = 0f;
	public const float FellPush = 0f;
	public const float SlowTipInitialFrac = 0f;
	public const float SlowTipRampFrac = 0f;
	public const float SlowTipDuration = 0f;
	public const float InitialFellOmega = 0f;
	// Valheim TreeBase.SpawnLog : AddForceAtPosition(hitDir * 0.2 * mass, pos + up * 4m * scale.y).
	public const float ValheimSpawnLogImpulseMetersPerSecond = 0.2f;
	public const float ValheimSpawnLogImpulseHeightMeters = 4f;
	public const float InitialFellTopImpulseSpeed = ValheimSpawnLogImpulseMetersPerSecond * UnitsPerMeter;
	public const float ValheimSpawnLogImpulseHeight = ValheimSpawnLogImpulseHeightMeters * UnitsPerMeter;
	public const float ValheimTreeBaseDropRadius = 0.5f * UnitsPerMeter;
	public const float ValheimTreeBaseDropYOffsetLow = 0.5f * UnitsPerMeter;
	public const float ValheimTreeBaseDropYOffsetHigh = 4f * UnitsPerMeter;
	public const float ValheimTreeBaseDropYStep = 0.3f * UnitsPerMeter;
	public static readonly Vector3[] TreeKindLogSpawnPoint =
	{
		new( 0f, 0f, 9.20f * UnitsPerMeter ), // Normal -> Beech1 log_spawnp
		new( 0f, 0f, 4.20f * UnitsPerMeter ), // Sapling -> FirTree log_spawnp
		new( -0.10f * UnitsPerMeter, 0.06f * UnitsPerMeter, 7.16f * UnitsPerMeter ), // Veteran -> Oak1 log_spawnp
		new( 0f, 0f, 4.20f * UnitsPerMeter ), // Brittle -> FirTree log_spawnp
	};
	public static readonly float[] TreeKindBaseDropYOffset =
	{
		ValheimTreeBaseDropYOffsetHigh,
		ValheimTreeBaseDropYOffsetLow,
		ValheimTreeBaseDropYOffsetLow,
		ValheimTreeBaseDropYOffsetHigh,
	};
	public const float ValheimTreeLogSpawnDistance = 2f * UnitsPerMeter;
	public const float ValheimTreeLogFullMass = 100f;
	public const float ValheimTreeLogHalfMass = 50f;
	public const float ValheimTreeLogLinearDamping = 0.1f;
	public const float ValheimTreeLogAngularDamping = 0.2f;
	public const float LogGroundSkin = 5f;
	public const float SplitLogAxisSpawnFrac = 0.34f;
	public const float SplitLogSideSpawnMin = 18f;
	public const float SplitLogSideSpawnMul = 0.75f;
	public const float ValheimTreeLogMaxDepenetrationVelocity = 1f * UnitsPerMeter;
	public const float SplitLogMaxSpawnValidationSpeed = 2f * UnitsPerMeter;
	public const float SplitLogSpawnPoseSettleDuration = 1.0f;
	public const float SplitLogMaxSpawnUpDot = 0.32f;

	// Per-kind multipliers. Launch physics stays Valheim-exact; variation
	// belongs to audio and break thresholds, not scripted fall torque.
	public static readonly float[] TreeKindInitialFellOmegaMul = { 1.0f, 1.0f, 1.0f, 1.0f };
	public static readonly float[] TreeKindFellTorqueMul       = { 1.0f, 1.0f, 1.0f, 1.0f };
	public static readonly float[] TreeKindGroanPitchMul       = { 1.0f, 1.30f, 0.65f, 1.35f };
	public static readonly float[] TreeKindSplitImpactMul      = { 1.0f, 1.0f, 1.0f, 0.45f };
	// Bonus items droppés instantanément au StartFell (Valheim TreeBase.RPC_Damage
	// drops m_dropWhenDestroyed en plus du log qui tombe). DropTable-style :
	// Min/Max pour la fourchette random, DropChance pour roll-skip (Valheim
	// utilise m_dropChance ∈ [0,1] avant de générer la liste — parfois rien).
	// Brittle drop bonus quasi-toujours (fragile = pleut des items au crack),
	// Veteran 2-4 (gros gain), Sapling 1 sometimes none.
	public static readonly int[] TreeKindFellBonusItemsMin = { 1, 0, 2, 1 };
	public static readonly int[] TreeKindFellBonusItemsMax = { 2, 1, 4, 2 };
	public static readonly float[] TreeKindFellBonusDropChance = { 0.85f, 0.55f, 1.00f, 0.95f };

	// Tree respawn grow animation - Valheim TreeBase.GrowAnimation anime
	// localScale × t/0.3f sur 0.3s avant d'activer le trunk définitif.
	// Notre TreeStump applique le même au moment du respawn (scale 0 → 1).
	public const float TreeGrowDuration = 0.3f;
	// WoodLog ignore les hits dans les 0.2s premières frames du spawn (Valheim
	// TreeLog.Damage : `if (!m_firstFrame)`). Évite les chops imprévus juste
	// après un split. Distinct de WoodLogPhysicsBreakGrace qui gate uniquement
	// les triggers physics OnCollisionStart.
	public const float WoodLogChopGrace = 0.2f;
	// Also gates cascade impact damage on fresh logs; Valheim's Damage()
	// early-return applies to every damage source while m_firstFrame is true.
	public const float TreeAngularDampLanded = ValheimTreeLogAngularDamping;
	public const float TreeLinearDampLanded = ValheimTreeLogLinearDamping;
	public const float TreeLogSleepThreshold = 0.05f;
	public const float TreeLandedManualSleepDelay = 3.0f;
	public const float TreeLandedManualSleepSpeed = 5.0f;
	public const float TreeLandedManualSleepAngularSpeed = 0.18f;
	public const float TreeLandedPostImpactLinearMul = 1.0f;
	public const float TreeLandedPostImpactAngularMul = 1.0f;
	public const float TreeLandedMaxSpeed = 160f;
	public const float TreeLandedMaxAngularSpeed = 2.8f;
	public const float TreeLandedMaxVerticalSpeed = 44f;
	public const float TreeGroundedLandingClearance = 14f;
	public const int LogGroundProbeCount = 7;
	public const float TreeGroundContactMemory = 0.45f;
	public const float TreeGroundContactHorizontalDrag = 0.965f;
	public const float TreeGroundContactAngularDrag = 0.970f;
	public const float TreeGroundContactStickyHorizontalDrag = 0.90f;
	public const float TreeGroundContactStickyAngularDrag = 0.92f;
	public const float TreeGroundContactMaxUpSpeed = 5f;
	public const float TreeGroundRollMinSpeed = 12f;
	public const float TreeGroundRollCoupling = 0.16f;
	public const float TreeGroundRollMaxAngularSpeed = 1.2f;
	public const float TreePlayerBumpHorizontalMul = 0.18f;
	public const float TreePlayerBumpAngularMul = 0.45f;
	public const float TreePlayerBumpMaxUpSpeed = 2f;
	public const float TreeFallbackLandingDelay = 6f;

	// Tree is 'landed' once its up-axis tilts past this dot threshold.
	public const float TreeFallenUpDotMax = 0.28f;
	public const float TreeRestingTiltUpDotMax = 0.35f;
	public const float TreeRestingLandingDelay = 1.4f;
	public const float TreeRestingLandingSpeed = 90f;
	public const float TreeRestingLandingAngularSpeed = 0.22f;

	// Valheim Attack defaults : m_attackRange=1.5m and m_attackAngle=90°.
	// Range is measured to the surface hit point, not trunk center.
	public const float SwingAttackHeight = 0.6f * UnitsPerMeter;
	public const float SwingRange = 1.5f * UnitsPerMeter;
	public const float SwingConeDot = 0.70710678f;
	public const float SwingAimSweepRadius = 14f;

	// Valheim TreeLog.RPC_Damage : AddForceAtPosition(hit.m_dir * hit.m_pushForce * 2f, hit.m_point).
	public const float LandedLogKickImpulse = 3f;
	public const float ValheimTreeLogHitPushMul = 2f;
	public const float LandedLogKickTorque = 0f;
	public const float LandedLogHitPointTorqueMul = 0f;
	// Valheim TreeLog.Destroy uses m_spawnDistance=2m along the log up-axis.
	public const float LogDropAxisSpreadMax = ValheimTreeLogSpawnDistance;
	public const float LogDropSideSpread = 26f;

	// Valheim tree-log ImpactEffect prefab values: min=1m/s, max=5m/s,
	// damageToSelf=false. Payload is chop=30 + blunt=50; tree/log modifiers
	// make blunt immune, so effective damage is chop=30 -> /10 = 3.
	public const int ValheimImpactToolTier = 2;
	public const float ValheimImpactMinVelocity = 1f * UnitsPerMeter;
	public const float ValheimImpactMaxVelocity = 5f * UnitsPerMeter;
	public const float ImpactMinSpeed = ValheimImpactMinVelocity;
	public const float ImpactMaxSpeed = ValheimImpactMaxVelocity;
	public const float ImpactSoftMinSpeed = ValheimImpactMinVelocity;
	public const float ImpactHardScale = 0.35f;
	public const float ImpactViolentScale = 0.62f;
	public const int ImpactBaseDamage = 3;
	public const float CascadeSweepInterval = ImpactInterval;
	public const float CascadeSweepMinSpeed = ImpactMinSpeed;
	public const float CascadeSweepRadius = 46f;
	public const float CascadeSweepDamageMul = 1.0f;
	// A log can damage another tree/log, but hitting the ground does not damage itself.
	public static readonly bool ImpactDamageSelf = false;
	// Legacy : on garde TreeSplitImpactSpeed × TreeKindSplitImpactMul comme
	// fallback boolean (en plus du damage scalé) pour les kinds Brittle qui
	// doivent split à seuil bas même si damage incrémental est lent.
	public const float TreeSplitImpactSpeed = 700f;
	// WoodLog : un impact au-dessus de ce seuil → BreakIntoWoodItems direct.
	// 900 = bien au-dessus d'un atterrissage normal observé (~600-750 u/s),
	// donc le log peut rester posé en biais et chopable. Seul un coup franc
	// (cascade violente, tronc qui tombe dessus, kick cumulé extrême) burst.
	public const float WoodLogBreakImpactSpeed = 900f;
	// Grace period au spawn d'un WoodLog : ignore les triggers physics les
	// premières frames pour éviter un auto-break sur le rebond initial.
	public const float WoodLogPhysicsBreakGrace = 0.5f;



	// Swing feel : click → WindUp (anticipation) → Impact (Chop + chips +
	// held-axe punch + hit-stop) → Recovery (input locked) → Idle. The wind-up is what
	// turns the swing from a toggle into a gesture ; the hit-stop conveys weight.
	// Cadence bumped 2026-05-21 to ~1s/chop (was ~0.63s) — matches the more
	// pondered Valheim axe rhythm observed in Thomas's gameplay capture.
	public const float SwingWindUpDuration = 0.55f;
	public const float SwingRecoveryDuration = 0.40f;
	// Valheim Attack.m_freezeFrameDuration = 0.15s. Frame-counted so the
	// duration isn't itself scaled by the freeze.
	public const float HitstopTimeScale = 0f;
	public const int HitstopFrames = 9;

	// Impact chips — small cubes spawned at the contact point. Custom physics
	// (no Rigidbody — see deleted ChopParticles for the perf history).
	// Phase G 2026-05-21 : retuned to spark/ember look (Valheim reference)
	// — smaller particles, gold tint, denser burst.
	public const int ChipBurstCount = 18;
	public const int ChipSplinterCount = 6;
	public const float ChipSizeMin = 1.5f;
	public const float ChipSizeMax = 3.5f;
	public const float ChipSpeed = 320f;
	public const float ChipLifetime = 0.9f;
	public static readonly Color ChipTint = new( 1.00f, 0.78f, 0.30f, 1f );
	public static readonly Color ChipSplinterTint = new( 1.00f, 0.92f, 0.55f, 1f );

	// Arena disc + density noise.
	public const float ArenaRadius = 2500f;
	public const float GroundZ = 0f;
	public const float ArenaCenterKeepout = 120f;
	public const float ArenaNoiseScale = 400f;
	public const float ArenaDensityThreshold = 0.05f;
	public const float ForestClearingNoiseScale = 760f;
	public const float ForestClearingThreshold = 0.18f;

	// Terrain : Sandbox.Terrain generated at bootstrap from a 3-octave FBM
	// value-noise heightmap. Smooth rolling hills + valleys ; tagged "ground"
	// so the existing tree-spawn raycast pipeline drops trees onto the
	// natural surface contour without modification.
	public const int TerrainResolution = 256;
	public const float TerrainSize = 6000f;
	// 520u peaks for "top of the world" feel — cascade unfolds visibly below
	// the spawn vantage. Pair with the narrow SpawnPeakPlateauRadius below
	// so the spawn reads as a true summit, not a mesa.
	public const float TerrainMaxHeight = 520f;
	public const float TerrainNoiseFreqLow = 2.5f;
	public const float TerrainNoiseFreqMid = 6.0f;
	public const float TerrainNoiseFreqHigh = 14.0f;
	// Mountain shape : the terrain is dominated by a radial cone centered on
	// the spawn — max height at the peak, decreasing to 0 at MountainBaseRadius
	// and beyond. FBM noise rides ON the cone as small variation, never
	// strong enough to flatten the slope. Result : steep mountain everywhere
	// around the player.
	public const float MountainBaseRadius = 2400f;  // distance where slope hits 0
	public const float MountainPlateauRadius = 70f; // flat top to stand on
	public const float MountainConeWeight = 0.85f;  // 0=pure noise, 1=pure cone
	public const float MountainNoiseWeight = 0.15f;

	// Mountain border : ring of tall static cubes at the edge of the
	// playable area. Visual = distant mountain wall ; gameplay = logs
	// bounce back instead of flying off into the void.
	// Border ring sits BEYOND the forest disc (ArenaRadius=2500) so it
	// frames the playable area without cutting through any trees.
	public const int BorderSegments = 40;
	public const float BorderRadius = 2750f;
	public const float BorderWallHeight = 900f;
	public const float BorderWallDepth = 220f;
	// Warm earth-tone mountain palette — was cool blue-gray which clashed
	// against the warm sun + copper fog. Brown silhouettes blend into the
	// sunset distance instead of standing out as a separate scene.
	public static readonly Color BorderTintLow = new( 0.42f, 0.36f, 0.30f, 1f );
	public static readonly Color BorderTintHigh = new( 0.58f, 0.48f, 0.36f, 1f );

	// Mountain shape variants — the boot Seed picks one deterministically.
	// Cone is the baseline radial peak ; Ridges is a long N-S spine ;
	// TwinPeaks has two summits the player can chain a cascade between ;
	// Plateau is a sharper drop-off mesa.
	public enum NoiseStyle { Cone, Ridges, TwinPeaks, Plateau }

}
