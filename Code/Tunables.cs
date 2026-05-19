namespace TreeChopping;

public static class Tunables
{
	public const float UnitsPerMeter = 39.37f;

	// Model.Cube => models/dev/box.vmdl is a 50u native cube. Spawn helpers must scale
	// WorldScale = wantedSize / CubeBase, and BoxCollider.Scale = (CubeBase, CubeBase, CubeBase).
	public const float CubeBase = 50f;

	public const float BeaverMoveSpeed = 240f;
	public const float BeaverSprintMultiplier = 1.75f;
	public const float BeaverJumpImpulse = 320f;
	public const float BeaverEyeHeight = 56f;
	public const float CameraDistance = 200f;
	public const float CameraHeightAboveBeaver = 90f;
	public const float CameraMinPitch = -75f;
	public const float CameraMaxPitch = 55f;

	public const float TreeHeight = 280f;
	public const float TreeRadius = 16f;
	public const float TreeMass = 240f;
	public const float TreeMaxChops = 3;
	public const float FellTorque = 90000f;
	public const float FellPush = 2400f;
	public const float SlowTipInitialFrac = 0.05f;
	public const float SlowTipRampFrac = 0.30f;
	public const float SlowTipDuration = 0.45f;
	public const float TreeAngularDampLanded = 1.5f;
	public const float TreeLinearDampLanded = 0.6f;

	public const float SwingRange = 90f;
	public const float SwingConeDot = 0.45f;
	public const float SwingCooldown = 0.33f;

	public const float LogBreakHits = 2;
	public const float LogPieceHeight = 120f;
	public const float LogPieceRadius = 14f;
	public const float LogPieceMass = 40f;
	public const float ChunkHeight = 18f;
	public const float ChunkRadius = 7f;
	public const float ChunkMass = 5f;
	public const int ChunksPerLogPiece = 4;

	public const float PickupRadius = 48f;
	public const float PickupLerpSpeed = 16f;

	public const float TreeFallenUpDotMax = 0.6f;

	// Map layout — matches main.scene CreekBed + LeftBank + RightBank dimensions.
	// Creek bed: |X| < 200u (5m wide). Banks span |X| ∈ [200, 1200] (25m wide each),
	// top surface at Z = 200u (5m above creek). Map length: Y ∈ [-3200, +3200] (160m).
	public const float CreekHalfWidth = 200f;
	public const float BankTopZ = 200f;
	public const float MapZMinDownstream = -3000f;
	public const float MapZMaxUpstream = 3000f;

	// 1% downstream slope: terrain tilts so upstream (+Y) is higher than
	// downstream (-Y). Applied via X-axis rotation on the bank/creek GOs in
	// main.scene, and used here to keep spawned content on the surface.
	// Angle = atan(0.01) ≈ 0.5729° ; quat (sin(θ/2),0,0,cos(θ/2)) ≈ (0.005,0,0,0.99999).
	public const float SlopeRatio = 0.01f;
	public const float BankRiversideMinX = 220f;
	public const float BankRiversideMaxX = 320f;
	public const float BankMidMinX = 360f;
	public const float BankMidMaxX = 600f;
	public const float BankOuterMinX = 640f;
	public const float BankOuterMaxX = 1100f;

	// Cascade — impulse transfer fraction from a falling tree's contact velocity
	// to a neighbor it slams into. 1.0 = full conservation; <1.0 = energy loss to
	// the strike (matches Godot CASCADE_IMPULSE_TRANSFER tuning).
	public const float CascadeImpulseTransfer = 0.55f;
	public const float CascadeMinContactSpeed = 80f;

	// Shatter: a landed log struck at high speed by another body skips its
	// remaining chop count and breaks into pieces immediately. Threshold is
	// the impacting body's velocity magnitude (u/s).
	public const float ShatterIncomingSpeed = 300f;
	public const float ShatterVerticalBump = 180f;

	// Rocks (Pickaxe-only). Squat cubes scattered on banks, break into stone chunks.
	public const float RockRadius = 24f;
	public const float RockHeight = 40f;
	public const float RockMass = 320f;
	public const int RockChops = 3;
	public const int StonesPerRock = 3;
	public const float StoneChunkRadius = 10f;
	public const float StoneChunkHeight = 12f;
	public const float StoneChunkMass = 6f;

	// Tree regrowth — a felled tree leaves a stump, which respawns a fresh
	// sapling after StumpDuration. Ported from Godot Tunables.TREE_REGROWTH_*.
	public const float TreeRegrowthStumpSeconds = 30f;
	public const float StumpHeight = 18f;
	public const float StumpRadius = 20f;

	// Day/night cycle — ported from Godot ambiance.gd (90s loop).
	// DayPhase starts at 0.82 (late afternoon) so first launches read as golden hour.
	// 0.0 / 1.0 = dawn at horizon, 0.5 = noon overhead, 0.25 = late night.
	public const float DayLengthSeconds = 90f;
	public const float DayPhaseStart = 0.82f;
	public const float SunYawDegrees = 23f;
	public const float SunMaxEnergyMul = 1.6f;
	public const float SunMinEnergyMul = 0.05f;

	// Chop impact chips — tiny tinted cubes spawned at each axe/pick hit.
	// Ported from Godot proto's _emit_chips (CPUParticles3D BoxMesh, amount=18,
	// lifetime=0.7s). Sizes in s&box inches (Godot 0.07m ≈ 2.76 u; we go a touch
	// bigger so they read at distance with Model.Cube).
	public const int ChipBurstCountWood = 14;
	public const int ChipBurstCountWoodHeavy = 20;
	public const int ChipBurstCountStone = 16;
	public const float ChipSpeedWood = 220f;
	public const float ChipSpeedWoodHeavy = 300f;
	public const float ChipSpeedStone = 260f;
	public const float ChipSizeMin = 4f;
	public const float ChipSizeMax = 7f;
	public const float ChipLifetimeMin = 0.6f;
	public const float ChipLifetimeMax = 1.0f;

	// Combo / juice tuning. A "beat" = a successful chop or pickup.
	public const float ComboIdleTimeout = 1.5f;
	public const int ComboSlowmoChain = 5;
	public const int ComboFlashChain = 8;
	public const float ComboSlowmoScale = 0.4f;
	public const float ComboSlowmoDuration = 1.0f;
	public const float ComboTraumaDecay = 1.4f;
	public const float CameraTraumaScale = 12f;

	// FOV juice — sprint widens, chop punches OUT (matches Godot _fov_punch additive).
	// Punch decay 14/s ports beaver.gd:981 (delta * 14.0); chop=4° vs Godot's
	// impact-scaled value (speed*0.4 clamped 5) — fixed-per-swing reads cleaner.
	public const float FovSprintWiden = 6f;
	public const float FovSprintLerpRate = 8f;
	public const float FovChopPunch = 4f;
	public const float FovPunchDecay = 14f;

	// Per-resource backpack cap. Ports Godot Tunables.BACKPACK_CAP_BASE = 20.
	// Godot only caps wood; sbox port applies the same number independently to
	// stone so the second pickup loop reads symmetrically on the HUD.
	public const int BackpackCap = 20;

	// Axe / pickaxe tier ladder. Ported (simplified) from Godot beaver.gd's
	// AXE_TIER_RANGES/DAMAGE ladder — original had 5 form-factor tiers; here
	// we collapse to 4 stat tiers (0..3) since the wood-economy reward we care
	// about is "pay wood, chop faster, fell in fewer swings". Cost ladder is
	// front-loaded so tier 1 is cheap (early payoff) and tier 3 forces a real
	// grind. Tier 0 is the starting tool — free.
	public const int MaxAxeTier = 3;
	public static readonly int[] AxeTierCosts = { 0, 5, 12, 24 };
	// Per-tier swing cooldown (seconds). Tier 0 matches the legacy SwingCooldown
	// so unupgraded play is identical to pre-tier; each step shaves ~25% off.
	public static readonly float[] AxeTierSwingCooldown = { 0.33f, 0.25f, 0.18f, 0.12f };
	// Per-tier chop multiplier — how many Chop() calls land per swing. Tier 3
	// = 3 means a 3-chop tree falls in one swing, matching the Godot proto's
	// AXE_TIER_DAMAGE end-game payoff.
	public static readonly int[] AxeTierChopMultiplier = { 1, 1, 2, 3 };

	// Pickaxe tier ladder. Godot proto used 3 form-factor tiers tied to dig
	// radius (PICKAXE_TIER_RADII = 1.6 / 2.0 / 2.5); we mirror the axe's
	// 4-stat-tier structure here so HUD/code paths stay symmetric. Cost ladder
	// reuses the axe's front-loaded shape (5 / 12 / 24) — kept as its own
	// array so future tuning can diverge without touching the axe economy.
	public const int MaxPickaxeTier = 3;
	public static readonly int[] PickaxeTierCosts = { 0, 5, 12, 24 };
	// Same cadence as the axe — each tier shaves ~25% off the swing cooldown.
	public static readonly float[] PickaxeTierSwingCooldown = { 0.33f, 0.25f, 0.18f, 0.12f };
	// Multi-chop on rocks: tier 3 one-shots a default 3-chop rock.
	public static readonly int[] PickaxeTierChopMultiplier = { 1, 1, 2, 3 };
}

