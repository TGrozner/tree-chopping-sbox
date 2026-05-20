namespace TreeChopping;

public static class Tunables
{
	// Model.Cube => models/dev/box.vmdl is a 50u native cube. Spawn helpers
	// must scale WorldScale = wantedSize / CubeBase, and BoxCollider.Scale =
	// (CubeBase, CubeBase, CubeBase).
	public const float CubeBase = 50f;

	// Beaver / player.
	public const float BeaverEyeHeight = 56f;

	// Tree geometry. Per-spawn scale jitter hashed on foot XY gives the
	// forest natural variation — small saplings to towering veterans share
	// the same arena. Mass scales with volume so taller trees fall heavier
	// and chain harder.
	public const float TreeHeight = 600f;
	public const float TreeRadius = 32f;
	public const float TreeMass = 240f;
	public const float TreeScaleMin = 0.7f;
	public const float TreeScaleMax = 1.45f;

	// Tree kinds : Normal (baseline), Sapling (thin+light), Veteran (big+heavy),
	// Brittle (splits early on impact). Indices match TreeKind enum order.
	// Distribution weights are sampled per tree at spawn by interpolating
	// between Easy (close to spawn) and Hard (far) — biome bias for the
	// mow-the-lawn loop : starts trivial near the shop, scales up outward.
	public static readonly int[] TreeKindWeightsEasy  = {  20,   65,     5,    10 };
	public static readonly int[] TreeKindWeightsHard  = {  35,    5,    50,    10 };
	public static readonly float[] TreeKindScaleMul   = { 1.0f, 0.55f, 1.6f,    1.0f };
	// Veteran mass dropped 4.0→2.2 — was generating ridiculous impulses on
	// contact that sent neighbors flying off the map.
	public static readonly float[] TreeKindMassMul    = { 1.0f, 0.30f, 2.2f,    0.8f };
	public static readonly Color[] TreeKindTrunkTint  =
	{
		new( 0.58f, 0.40f, 0.26f, 1f ), // Normal — default brown (brightened from 0.46/0.32/0.22)
		new( 0.68f, 0.52f, 0.30f, 1f ), // Sapling — lighter, warmer
		new( 0.42f, 0.28f, 0.20f, 1f ), // Veteran — darker, ancient bark (brightened from 0.32/0.22/0.16)
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

	// Axe tier ladder. Each upgrade reduces ChopsRemaining drop per swing
	// (ChopPower) and bumps the wood reward. Costs in wood, front-loaded so
	// T1 is reachable in a handful of small-tree fells.
	public const int MaxAxeTier = 3;
	public static readonly int[] AxeTierCosts = { 0, 8, 28, 80 };
	public static readonly int[] AxeTierChopPower = { 1, 2, 3, 5 };
	public static readonly float[] AxeTierWoodMul = { 1.0f, 1.3f, 1.7f, 2.2f };

	// Wood reward per tree kind (before tier multiplier). Veterans are
	// premium, saplings are practice.
	public static readonly int[] TreeKindWoodReward = { 3, 1, 8, 2 };
	public static readonly int MythicWoodBonus = 12;

	// Per-kind chops required at T0 — multiplied UP for tier-gating.
	public static readonly int[] TreeKindChopsBase = { 3, 1, 8, 2 };
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

	// Fell physics. Slow-tip ramp = first 0.32s of the topple, scaled torque
	// going from 5% to 42% of max so trees don't snap-flat instantly.
	// Tone-downed after "trees flying everywhere" feedback. Stronger
	// dissipation = cleaner chains, less rogue logs.
	public const float FellTorque = 110000f;
	public const float FellPush = 2400f;
	public const float SlowTipInitialFrac = 0.05f;
	public const float SlowTipRampFrac = 0.42f;
	public const float SlowTipDuration = 0.32f;
	public const float TreeAngularDampLanded = 1.0f;
	public const float TreeLinearDampLanded = 0.45f;
	// Tree is "landed" once its up-axis tilts past this dot threshold.
	public const float TreeFallenUpDotMax = 0.6f;

	// Swing range : axe-arm reach — 350u is roughly the closest forest ring
	// around the spawn peak's plateau. Adjacent trees can topple it further
	// via natural rigidbody bumps (no scripted cascade).
	public const float SwingRange = 350f;
	public const float SwingConeDot = 0.30f;
	public const float SwingAimSweepRadius = 14f;

	// Swing feel : click → WindUp (anticipation) → Impact (Chop + chips + cam
	// punch + hit-stop) → Recovery (input locked) → Idle. The wind-up is what
	// turns the swing from a toggle into a gesture ; the hit-stop sells weight.
	public const float SwingWindUpDuration = 0.45f;
	public const float SwingRecoveryDuration = 0.18f;
	public const float SwingFovPunch = 6f;
	public const float SwingFovDecayPerSec = 14f;
	// Hit-stop : pure freeze (TimeScale=0) for 4 frames @ 60fps (~67 ms wall).
	// Frame-counted so the duration isn't itself scaled by the freeze.
	public const float HitstopTimeScale = 0f;
	public const int HitstopFrames = 4;

	// Impact chips — small cubes spawned at the contact point. Custom physics
	// (no Rigidbody — see deleted ChopParticles for the perf history). The
	// burst spawns ChipBurstCount "main" chips that fly back-and-up toward the
	// swinger PLUS ChipSplinterCount thin slivers that scatter sideways.
	public const int ChipBurstCount = 14;
	public const int ChipSplinterCount = 6;
	public const float ChipSizeMin = 4f;
	public const float ChipSizeMax = 8f;
	public const float ChipSpeed = 280f;
	public const float ChipLifetime = 1.6f;
	public static readonly Color ChipTint = new( 0.55f, 0.38f, 0.22f, 1f );
	public static readonly Color ChipSplinterTint = new( 0.78f, 0.62f, 0.40f, 1f );

	// Arena disc + density noise.
	public const float ArenaRadius = 2500f;
	public const float GroundZ = 0f;
	public const float ArenaCenterKeepout = 120f;
	public const float ArenaNoiseScale = 400f;
	public const float ArenaDensityThreshold = 0.05f;

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
