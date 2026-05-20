namespace TreeChopping;

// Species port of the Godot proto's wood_type variants (tree.gd:_SPECIES_DENSITY,
// _SPECIES_COUNT). The Godot list was oak/birch/maple/pine; renamed here to
// Beech/Spruce/Ironwood/Crystal so the biome-themed flavor reads from the name
// alone — biome bias is decided in BiomeManager.SpeciesForNewTree, not here.
public enum TreeSpecies
{
	Beech,
	Spruce,
	Ironwood,
	Crystal,
}

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
	// Bowling-game framing: wide strategic third-person view so the player reads
	// the cluster they're aiming at (5-10 tree silhouettes in frame), not a close
	// gathering shot. Distance/height pushed out + up vs. the original tight cam.
	// Pitch caps trimmed so looking straight down doesn't lose the horizon and
	// looking up doesn't reveal the skybox edge.
	// Caméra ré-équilibrée : distance plus loin + height un poil moins haute +
	// pitch min réduit pour cadrer le sol près du beaver. Combinée à FOV 75
	// (au lieu de 90) ça lit comme une 3rd-person stratégique propre, pas un
	// fish-eye Noita-overstretched.
	// Cam tirée plus proche (240 was 360) — à 360 le beaver lit minuscule.
	// Pull tighter encore (240→170) pour matcher la ref Godot où le castor
	// remplit 30% écran height. Pitch min adouci -45→-30 pour cadrer le
	// sol près du beaver sans top-down. Height baissée 40→30 pour shoulder-cam.
	public const float CameraDistance = 170f;
	public const float CameraHeightAboveBeaver = 30f;
	public const float CameraMinPitch = -30f;
	public const float CameraMaxPitch = 35f;

	// Valheim grandeur : trees nettement plus hauts (vs 280) pour vraie verticalité.
	public const float TreeHeight = 380f;
	public const float TreeRadius = 18f;
	// Revert à 1.0 (uniform) : 1.35× X/Y déformait la canopée puisque le mesh
	// low_poly_tree.vmdl est designed pour uniform scale. Trunk wide + canopée
	// flat-wide = ugly et difforme. On accepte le ratio thin-asset 1:8.75
	// plutôt que distordre.
	public const float TreeTrunkWidthMul = 1.0f;
	public const float TreeMass = 240f;
	public const float TreeMaxChops = 3;
	// Cascade snappiness bump : torque 130000→158000, push 3200→3800. Trees
	// tippent faster + plus violent, chains se propagent plus rapidement et lit
	// plus "action movie" vs "doux glissé".
	public const float FellTorque = 158000f;
	public const float FellPush = 3800f;
	public const float SlowTipInitialFrac = 0.05f;
	public const float SlowTipRampFrac = 0.42f;
	public const float SlowTipDuration = 0.32f;
	// Damping landed — assez bas pour rouler sur la slope ET poke des standing
	// trees, mais assez haut pour settle dans les 6-8s afin que le run
	// resolve sans cascade infinite. Avant 0.25 linear = roll forever sur 12°.
	public const float TreeAngularDampLanded = 1.0f;
	public const float TreeLinearDampLanded = 0.45f;

	// SwingRange bumped 120→250 — ArenaCenterKeepout=120 mettait le 1er arbre
	// pile à la limite au spawn (rate quasi 100%). UpdateSwing utilise désormais
	// un sphere-sweep depuis la caméra (WYSIWYG aim), SwingRange est le cap de
	// distance beaver→cible. ConeDot reste utilisée par DebugSwing/selftest.
	public const float SwingRange = 250f;
	public const float SwingConeDot = 0.30f;
	public const float SwingCooldown = 0.33f;
	// Rayon du sphere-sweep camera→target — tolérance "tu vises à peu près"
	// pour catch un tronc même si le reticle n'est pas exactement dessus.
	public const float SwingAimSweepRadius = 14f;

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

	// Circular arena (bowling pivot). Trees scatter inside a disc of radius
	// ArenaRadius, with noise-driven density so the arena reads as clusters +
	// clearings instead of a uniform field. Ground plane sits at Z=0; the
	// beaver spawns at the disc center.
	public const float ArenaRadius = 2500f;
	public const float GroundZ = 0f;
	// Center keep-out so the beaver spawn doesn't overlap a trunk.
	public const float ArenaCenterKeepout = 120f;
	// Slope downhill along +X (beaver's default forward). 0.21 = ~12° grade.
	// Symmetric left↔right (Y) for the player; downhill straight ahead.
	// Felled trunks roll downhill toward the deeper clusters, cascades chain.
	public const float ArenaSlope = 0.21f;
	// Cell scale for the density noise — larger = bigger clusters/clearings.
	// 400u ≈ 12 noise cells across the arena diameter, gives 3-5 distinct
	// clusters per run with high-density spots inside each.
	public const float ArenaNoiseScale = 400f;
	// Density threshold: positions where noise < this are rejected → clearings.
	// 0.05 = ~5% rejection, ~95% du disque dispo pour arbres → quasi aucune
	// clairière, arène triple-A bourrée. Paired with TreeCount=800 + MinSpacing=38,
	// the canopy is packed tight enough that a single well-aimed swing can
	// chain through 50-150 trees instead of stalling in a clearing.
	public const float ArenaDensityThreshold = 0.05f;

	// Cascade triple-A — domino chain quasi sans perte d'énergie, topple
	// rapide, contacts lents propagent quand même.
	// 0.95 = pratiquement aucune perte sur le transfert d'impulsion : un arbre
	// qui tombe garde 95% de son momentum sur le suivant, la chaîne se
	// poursuit sur 50-150 arbres au lieu de s'éteindre.
	public const float CascadeImpulseTransfer = 0.97f;
	// Seuil très bas (25 u/s) pour que des nudges lents en bout de chaîne
	// déclenchent encore — la vague reste vivante très profond dans le cluster
	// même quand l'énergie résiduelle est faible.
	public const float CascadeMinContactSpeed = 25f;

	// Shatter: a landed log struck at high speed by another body skips its
	// remaining chop count and breaks into pieces immediately. Threshold is
	// the impacting body's velocity magnitude (u/s).
	// Shatter threshold abaissé de 300→180 u/s : un tronc qui en heurte un déjà
	// tombé à vitesse moyenne le splinter maintenant, au lieu d'avoir à le
	// re-chopper. Donne le feeling Noita "tout réagit physiquement".
	public const float ShatterIncomingSpeed = 180f;
	public const float ShatterVerticalBump = 180f;

	// Tree regrowth — a felled tree leaves a stump, which respawns a fresh
	// sapling after StumpDuration. Ported from Godot Tunables.TREE_REGROWTH_*.
	public const float TreeRegrowthStumpSeconds = 30f;
	public const float StumpHeight = 18f;
	public const float StumpRadius = 20f;

	// Day/night cycle — ported from Godot ambiance.gd (90s loop).
	// DayPhase starts at 0.78 (deeper late afternoon) so first launches read as golden hour.
	// 0.0 / 1.0 = dawn at horizon, 0.5 = noon overhead, 0.25 = late night.
	public const float DayLengthSeconds = 90f;
	// 0.55 = just past noon (cooler near-neutral light) instead of dusk gold.
	// Dusk-gold (0.78) crushed all teal canopies into olive-brown — Thomas saw
	// "tout est marron" because the warm sun mul'd the coolness out of the palette.
	public const float DayPhaseStart = 0.55f;
	public const float SunYawDegrees = 23f;
	// Sun max energy trim 1.85→1.45 — avec DayPhaseStart=0.55 (noon-ish) le
	// peak energy washait trop les couleurs. 1.45 garde luminosité satisfaisante
	// tout en preservant saturation palette.
	public const float SunMaxEnergyMul = 1.45f;
	public const float SunMinEnergyMul = 0.05f;

	// Chop impact chips — tiny tinted cubes spawned at each axe/pick hit.
	// Ported from Godot proto's _emit_chips (CPUParticles3D BoxMesh, amount=18,
	// lifetime=0.7s). Sizes in s&box inches (Godot 0.07m ≈ 2.76 u; we go a touch
	// bigger so they read at distance with Model.Cube).
	// Chop feedback bumped : user feedback "j'ai aucun indicateur visuel quand
	// je tape un arbre". Bigger bursts + faster chips + bigger chip cubes pour
	// que chaque hit lise comme un VRAI hit visuellement à distance caméra 170u.
	public const int ChipBurstCountWood = 26;
	public const int ChipBurstCountWoodHeavy = 36;
	public const int ChipBurstCountStone = 22;
	public const float ChipSpeedWood = 320f;
	public const float ChipSpeedWoodHeavy = 420f;
	public const float ChipSpeedStone = 360f;
	public const float ChipSizeMin = 6f;
	public const float ChipSizeMax = 10f;
	// Lifetime allongé 0.6-1.0 → 2.5-4.0 : les copeaux restent visibles sur le
	// sol après la cascade, donnent un sens de "ça a explosé pour de vrai" au
	// lieu de poof instantané. Coût frame négligeable (no collider, no rb-physx-tick).
	public const float ChipLifetimeMin = 2.5f;
	public const float ChipLifetimeMax = 4.0f;

	// Combo / juice tuning. A "beat" = a successful chop or pickup.
	public const float ComboIdleTimeout = 1.5f;
	public const int ComboSlowmoChain = 5;
	public const int ComboFlashChain = 8;
	public const float ComboSlowmoScale = 0.4f;
	public const float ComboSlowmoDuration = 1.0f;
	public const float ComboTraumaDecay = 1.4f;
	public const float CameraTraumaScale = 12f;

	// Score milestone tiers — each threshold the player passes during a cascade
	// triggers a popup banner + trauma spike. Tuned so the first 2-3 are easy to
	// hit and the high ones are achievement-y. Bowling-game-like "Strike! / Turkey
	// / Perfect Game" feel.
	public static readonly int[] ScoreMilestones = { 3, 8, 20, 50, 100, 200 };
	public static readonly string[] ScoreMilestoneNames = {
		"Spark", "Chain Reaction", "Lumberjack", "Domino King", "Forest Killer", "TIMBER SHOCK"
	};
	public const int ScoreGoodRunTarget = 50;
	public const int ScoreMasterTarget = 200;
	public const float FirstRunHintDuration = 5f;
	public static readonly Color[] ScoreMilestoneColors = {
		new Color( 1.0f, 0.85f, 0.40f, 1f ),  // Spark — gold
		new Color( 1.0f, 0.65f, 0.20f, 1f ),  // Chain Reaction — amber
		new Color( 1.0f, 0.40f, 0.15f, 1f ),  // Lumberjack — orange-red
		new Color( 1.0f, 0.20f, 0.30f, 1f ),  // Domino King — hot pink
		new Color( 0.85f, 0.30f, 1.0f, 1f ),  // Forest Killer — purple
		new Color( 0.40f, 0.95f, 1.0f, 1f ),  // TIMBER SHOCK — cyan
	};
	public const float MilestonePopupDuration = 1.8f;

	// Mythic trees — Noita-style "scolagi" gold targets. 1 in MythicSpawnRatio
	// trees becomes mythic: gold tint, +60% scale, on fell awards a +20 score
	// bonus + AOE trauma. Visible from far away so the player can plan a run
	// around hitting one inside a cluster.
	// Dial-back from +20 to +5 : 9 mythics × 5 = +45 max bonus per run, lit
	// enough pour récompenser le ciblage mais pas écraser le skill du chain.
	// Bumped 1/140 → 1/100 → ~10 mythics par run de 1000. Plus de prize trees
	// visibles = plus de tactical choices au moment du swing.
	public const int MythicSpawnRatio = 100;
	public const float MythicScaleMul = 1.45f;
	public const int MythicScoreBonus = 5;
	public static readonly Color MythicTint = new( 1.0f, 0.82f, 0.18f, 1f );

	// FOV juice — sprint widens, chop punches OUT (matches Godot _fov_punch additive).
	// Punch decay 14/s ports beaver.gd:981 (delta * 14.0); chop=4° vs Godot's
	// impact-scaled value (speed*0.4 clamped 5) — fixed-per-swing reads cleaner.
	// Sprint widen kept subtle (4°) because the bowling-cam is already zoomed out;
	// a stronger widen on top reads as fish-eye instead of speed.
	public const float FovSprintWiden = 4f;
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

	// Per-species visual + difficulty knobs. Indexed by (int)TreeSpecies so the
	// enum is the source of truth. Tints are picked to be visually distinct at
	// glance distance — warm brown beech, dusty green-brown spruce, dark
	// orange-red ironwood, light cyan-white crystal. Species tint wins over the
	// biome trunk tint (the biome bank tint still reads as the dominant cue).
	// Palette refresh — match the Godot proto ref. Values DEEPENED from initial
	// pastel coral (read as beige bone-white under directional light) to bold
	// saturated brand-colors that survive even at high directional light mul.
	// Tinted on the .vmdl mesh (Model.Cube ignores Tint, root cause found 2026-05-20).
	// These multiply the low_poly_tree.vmdl's textured material — values picked to
	// land on saturated coral/red/blue when multiplied with the .vmdl's brown base.
	// Per-species tint pushed harder pour matcher Godot palette : Beech vire vert
	// pop, Spruce mid-teal, Ironwood coral, Crystal cyan-bright. Tint × .vmdl
	// texture mults — donne nuances variées par species sur la scène.
	// Trunk tints Valheim-discrets : warm browns subtils + variations natural,
	// vs iter59 colors saturés. Plus naturaliste, moins stylized-Godot.
	public static readonly Color[] SpeciesTrunkTints =
	{
		new Color( 0.95f, 0.85f, 0.70f, 1f ), // Beech — warm brown
		new Color( 0.80f, 0.95f, 0.85f, 1f ), // Spruce — cool olive-pine
		new Color( 1.05f, 0.75f, 0.60f, 1f ), // Ironwood — copper-tan
		new Color( 0.85f, 0.95f, 1.05f, 1f ), // Crystal — pale blue-grey
	};

	// MAX saturation diagnostic — push to pure (0,1,1) cyan for Beech to
	// validate Model.Cube tint actually works. If trees go cyan: keep this
	// range. If grey: Model.Cube IS broken and we need MaterialOverride.
	public static readonly Color[] SpeciesCanopyTints =
	{
		new Color( 0.0f, 1.0f, 1.0f, 1f ),  // Beech — PURE CYAN diagnostic
		new Color( 0.0f, 0.85f, 0.6f, 1f ), // Spruce — pure teal
		new Color( 0.95f, 1.0f, 0.0f, 1f ), // Ironwood — pure yellow-lime
		new Color( 0.4f, 0.85f, 1.0f, 1f ), // Crystal — sky cyan
	};
	// Canopy mass on top of the trunk model — wider than the .vmdl canopy so
	// it visually dominates from distance. Width/height in u relative to the
	// trunk model's bounds.
	public const float CanopyWidth = 110f;
	public const float CanopyHeight = 130f;
	// Canopy sits with center at (TreeHeight * CanopyHeightFrac) above foot.
	public const float CanopyHeightFrac = 0.75f;
	// Chops to fell, mirrors Godot BASE_CHOPS_REQUIRED ladder (harder species
	// gate behind axe tier in the original; here we just up the chop count).
	public static readonly int[] SpeciesChopsRequired = { 2, 3, 4, 5 };
	// Scale variety so a forest of mixed species reads as a forest, not a
	// uniform grid. Wider spread than before (0.85..1.4): Beech is small/young,
	// Ironwood is hefty, and Crystal trunks tower at 1.4× — the BIG ones in a
	// cluster dominate the silhouette and read as "the prize tile" when lining
	// up a cascade. Multiplied into the trunk WorldScale at spawn.
	// Bump 0.85..1.4 → 1.3..1.95 pour que les canopées remplissent le frame
	// comme la ref Godot (où elles dominent 30-50% screen height). Avec
	// CameraDistance=170 les trees lisent maintenant comme une vraie masse.
	public static readonly float[] SpeciesScaleMul = { 1.30f, 1.55f, 1.80f, 1.95f };

	// Per-species silhouette stretch (XY width vs Z height ratio). Multiplied
	// into WorldScale on top of SpeciesScaleMul + jitter. Variety => visual
	// richness even with one .vmdl asset : Beech balanced, Spruce tall thin,
	// Ironwood wide squat broadleaf, Crystal towering obelisk spire.
	// X=Y always (radial), Z stretches independently.
	// Stretch ratios revert plus modéré — les iter4/25 squishes (0.55x XY) faisaient
	// que les troncs lisaient comme "anorexique / tordu" (user feedback iter64).
	// Stretch stays mostly uniform, juste légères variations pour silhouette variety.
	public static readonly Vector3[] SpeciesShapeStretch =
	{
		new Vector3( 1.05f, 1.05f, 1.00f ), // Beech — slight broadleaf
		new Vector3( 0.92f, 0.92f, 1.18f ), // Spruce — slightly tall narrow
		new Vector3( 1.15f, 1.15f, 0.90f ), // Ironwood — slightly squat
		new Vector3( 0.88f, 0.88f, 1.32f ), // Crystal — moderately tall narrow
	};
}

