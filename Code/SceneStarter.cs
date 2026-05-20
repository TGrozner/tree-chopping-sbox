namespace TreeChopping;

public sealed class SceneStarter : Component
{
	[Property] public int TreeCount { get; set; } = 1000;
	// MinSpacing 48 (était 35) : avec scaleJitter adaptive 0.7×–1.4× donne
	// un spacing réel 33.6–67.2u. Tree collider half-width = 16u → centers à
	// 32u au minimum, donc 33.6u laisse 1.6u de marge. Avant 35×0.7=24.5
	// → overlap visible des colliders kinematic.
	[Property] public float MinSpacing { get; set; } = 48f;
	[Property] public int Seed { get; set; } = 0xCA5C;
	// Spawn uphill (-X = uphill side of the +Y-axis 12° slope) so the player
	// looks downhill +X with the forest descending in front of them — bowling
	// framing. Z = ground at this X (+320u from slope) + small drop so rigidbody
	// settles on contact. ArenaCenterKeepout used to gate trees around origin ;
	// la pad keepout est désormais autour de BeaverSpawn (SpawnPadRadius).
	[Property] public Vector3 BeaverSpawn { get; set; } = new( -1500f, 0f, 380f );
	// Rayon autour de BeaverSpawn où aucun arbre ne spawn — laisse le joueur
	// faire 1-2 pas avant le 1er tronc, et garde la vue dégagée à l'avant.
	[Property] public float SpawnPadRadius { get; set; } = 180f;

	// If you want to ship a daily-challenge arena: leave Seed at 51804 (the
	// scene-default sentinel) and SceneStarter.OnStart will substitute today's
	// date hash so every player gets the same arena on the same calendar day.
	// Override Seed to any other value to pin a specific layout for testing.
	public const int SeedDailySentinel = 51804;

	protected override void OnStart()
	{
		try
		{
			if ( Seed == SeedDailySentinel )
			{
				Seed = DailySeed();
				Log.Info( $"[SceneStarter] Daily seed activated: {Seed} for {DateTime.UtcNow:yyyy-MM-dd}" );
			}
			EnsureInventory();
			EnsureCombo();
			EnsureWeather();
			EnsureBiomes();
			EnsureDayNight();
			EnsureCompass();
			EnsureAmbientLeaves();
			EnsurePauseMenu();
			EnsureTitleScreen();
			EnsureRunManager();
			EnsureAimIndicator();
			EnsureSelfTest();
			EnsureTestSuite();
			EnsureAutoSpin();
			var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
			var beaver = SpawnBeaver( camera );
			EnsureHud();
			SpawnForest();
			int grassTufts = SpawnGrassTufts();
			int rocks = SpawnRocks();

			var beaverMr = beaver?.Components.Get<ModelRenderer>();
			var anyTree = Scene.GetAllComponents<Tree>().FirstOrDefault();
			var treeMr = anyTree?.Components.Get<ModelRenderer>();
			Log.Info( $"[SceneStarter] Bootstrap OK — beaver pos={beaver?.WorldPosition} bounds={beaverMr?.Bounds}, sample tree pos={anyTree?.WorldPosition} bounds={treeMr?.Bounds}, trees={Scene.GetAllComponents<Tree>().Count()}, grassTufts={grassTufts}, rocks={rocks}, cam reused={camera.IsValid()}" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"[SceneStarter] Bootstrap failed: {ex}" );
		}
	}

	private void EnsureInventory()
	{
		var existing = Scene.GetAllComponents<WoodInventory>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "WoodInventory";
		go.AddComponent<WoodInventory>();
	}

	private void EnsureCombo()
	{
		var existing = Scene.GetAllComponents<ComboTracker>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "ComboTracker";
		go.AddComponent<ComboTracker>();
	}

	private void EnsureWeather()
	{
		var existing = Scene.GetAllComponents<Weather>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "Weather";
		go.AddComponent<Weather>();
	}

	private void EnsureBiomes()
	{
		var existing = Scene.GetAllComponents<BiomeManager>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "BiomeManager";
		go.AddComponent<BiomeManager>();
	}

	private void EnsureDayNight()
	{
		var existing = Scene.GetAllComponents<DayNightCycle>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "DayNightCycle";
		go.AddComponent<DayNightCycle>();
	}

	private void EnsureHud()
	{
		var existing = Scene.GetAllComponents<WoodHud>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "WoodHud";
		go.AddComponent<WoodHud>();
	}

	private void EnsureCompass()
	{
		var existing = Scene.GetAllComponents<HudCompass>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "HudCompass";
		go.AddComponent<HudCompass>();
	}

	private void EnsureAmbientLeaves()
	{
		var existing = Scene.GetAllComponents<AmbientLeaves>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "AmbientLeaves";
		go.AddComponent<AmbientLeaves>();
	}

	private void EnsureRunManager()
	{
		var existing = Scene.GetAllComponents<RunManager>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "RunManager";
		go.AddComponent<RunManager>();
	}

	// Restart hook for RunManager — re-runs the procedural spawn with a fresh
	// seed so each run reads as a new arena layout. Beaver / managers /
	// ground stay; RunManager is responsible for destroying the previous
	// run's trees + log pieces + chunks + stumps before calling us.
	public void RegenerateForest()
	{
		Seed = unchecked( Seed * 1664525 + 1013904223 );
		SpawnForest();
		Log.Info( $"[SceneStarter] Forest regenerated with seed={Seed}, trees={Scene.GetAllComponents<Tree>().Count()}" );
	}

	public static int DailySeed()
	{
		// UtcNow.Date hashed to keep the seed stable for everyone in the same
		// calendar day. Fold to a positive int.
		var d = DateTime.UtcNow.Date;
		int h = d.Year * 10000 + d.Month * 100 + d.Day;
		// Mix a constant prime so the seed doesn't trivially shift by 1 each day.
		return (int)(unchecked( (uint)h * 2654435761u ) & 0x7FFFFFFFu);
	}

	private void EnsureAimIndicator()
	{
		var existing = Scene.GetAllComponents<AimIndicator>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "AimIndicator";
		go.AddComponent<AimIndicator>();
	}

	private void EnsureAutoSpin()
	{
		if ( !AutoSpin.IsActiveRequest() ) return;
		var existing = Scene.GetAllComponents<AutoSpin>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "AutoSpin";
		go.AddComponent<AutoSpin>();
		Log.Info( "[AutoSpin] spawned by SceneStarter" );
	}

	private void EnsurePauseMenu()
	{
		var existing = Scene.GetAllComponents<PauseMenu>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "PauseMenu";
		go.AddComponent<PauseMenu>();
	}

	private void EnsureTitleScreen()
	{
		var existing = Scene.GetAllComponents<TitleScreen>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "TitleScreen";
		go.AddComponent<TitleScreen>();
	}

	private void EnsureTestSuite()
	{
		if ( !TestSuite.IsActiveRequest() ) return;
		var existing = Scene.GetAllComponents<TestSuite>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "TestSuite";
		go.AddComponent<TestSuite>();
		Log.Info( "[TC_TEST] TestSuite spawned by SceneStarter" );
	}

	private void EnsureSelfTest()
	{
		// Gated by env var / CLI flag (see SelfTest.IsActiveRequest) so normal
		// editor Play sessions never see this. The headless harness in tools/
		// flips TREE_CHOPPING_SELFTEST=1 to enable.
		if ( !SelfTest.IsActiveRequest() ) return;
		var existing = Scene.GetAllComponents<SelfTest>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "SelfTest";
		go.AddComponent<SelfTest>();
		Log.Info( "[TC_TEST] SelfTest spawned by SceneStarter" );
	}

	private BeaverController SpawnBeaver( CameraComponent existingCamera )
	{
		var go = Scene.CreateObject();
		go.Name = "Beaver";
		go.WorldPosition = BeaverSpawn;
		go.Tags.Add( "player" );

		go.WorldScale = new Vector3( 32f, 32f, 72f ) / Tunables.CubeBase;

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = new Color( 0.40f, 0.27f, 0.18f, 1f );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 70f;
		rb.LinearDamping = 1.2f;
		rb.AngularDamping = 6f;
		// Lock pitch + roll so a falling trunk (mass 240) can't tip the beaver
		// onto its side. Yaw stays free — BeaverController drives WorldRotation
		// via slerp from camera yaw, and a Yaw lock would prevent that write.
		// Without these locks the player often gets stuck after the first chop:
		// the falling tree's contact bounces them sideways, their 32×32×72
		// collider ends up horizontal, and the now-wider footprint wedges
		// between the static stump and the landed log.
		rb.Locking = new PhysicsLock { Pitch = true, Roll = true };

		var beaver = go.AddComponent<BeaverController>();

		var cam = existingCamera;
		if ( !cam.IsValid() )
		{
			var camGo = Scene.CreateObject();
			camGo.Name = "Camera";
			cam = camGo.AddComponent<CameraComponent>();
			cam.IsMainCamera = true;
			cam.ZNear = 4f;
			cam.ZFar = 8000f;
			cam.FieldOfView = 75f;
			cam.BackgroundColor = new Color( 0.40f, 0.55f, 0.65f, 1f );
		}
		beaver.Camera = cam;
		beaver.CameraRoot = cam.GameObject;

		return beaver;
	}

	private void SpawnForest()
	{
		// Chaotic forest: uniform disk sample + 2D value-noise density gate.
		// Where noise > threshold → cluster (denser spacing); where noise <
		// threshold → clearing (rejection). Strategy: the player picks the
		// fattest cluster to maximize cascade chain.
		var rng = new Random( Seed );
		var placed = new List<Vector3>();
		int attempts = 0;
		int spawned = 0;
		int maxAttempts = TreeCount * 80;

		// Spawn pad keepout autour du beaver — pas d'arbre planté sur sa tête.
		float padXY_X = BeaverSpawn.x;
		float padXY_Y = BeaverSpawn.y;
		float padRSq = SpawnPadRadius * SpawnPadRadius;

		while ( spawned < TreeCount && attempts < maxAttempts )
		{
			attempts++;
			// √u for uniform disc sampling — without it density biases toward center.
			float r = MathF.Sqrt( (float)rng.NextDouble() ) * Tunables.ArenaRadius;
			if ( r < Tunables.ArenaCenterKeepout ) continue;
			float angle = (float)(rng.NextDouble() * MathF.Tau);
			float x = MathF.Cos( angle ) * r;
			float y = MathF.Sin( angle ) * r;

			// Spawn pad : pas d'arbre dans SpawnPadRadius autour du beaver.
			float dxPad = x - padXY_X;
			float dyPad = y - padXY_Y;
			if ( dxPad * dxPad + dyPad * dyPad < padRSq ) continue;

			float density = ValueNoise2D( x / Tunables.ArenaNoiseScale, y / Tunables.ArenaNoiseScale, Seed );
			if ( density < Tunables.ArenaDensityThreshold ) continue;

			// Adaptive spacing: dense clusters can pack at 0.7× base, clearings
			// fall back to 1.4× base (forces the placement to spread out where
			// it does spawn). MinSpacing default 140u.
			float spacing = MathX.Lerp( MinSpacing * 1.4f, MinSpacing * 0.7f, density );
			// Slope: trees follow the +X downhill plane (beaver looks +X by
			// default, so the slope drops in front of them and is symmetric
			// left↔right). Silhouettes step downhill in the player's view.
			var pos = new Vector3( x, y, Tunables.GroundZ - x * Tunables.ArenaSlope );
			if ( placed.Any( p => p.Distance( pos ) < spacing ) ) continue;

			placed.Add( pos );
			SpawnTree( pos );
			spawned++;
		}
	}

	// Deterministic 2D value noise (smoothstep-blended bilinear of hash-per-corner).
	// Output is in [0, 1]; sample at (x/scale, y/scale) where scale controls cluster
	// size. Same seed → same forest, which makes the layout reproducible across runs.
	private static float ValueNoise2D( float x, float y, int seed )
	{
		int xi = (int)MathF.Floor( x );
		int yi = (int)MathF.Floor( y );
		float xf = x - xi;
		float yf = y - yi;
		float u = xf * xf * (3f - 2f * xf);
		float v = yf * yf * (3f - 2f * yf);
		float a = Hash2D( xi, yi, seed );
		float b = Hash2D( xi + 1, yi, seed );
		float c = Hash2D( xi, yi + 1, seed );
		float d = Hash2D( xi + 1, yi + 1, seed );
		return MathX.Lerp( MathX.Lerp( a, b, u ), MathX.Lerp( c, d, u ), v );
	}

	private static float Hash2D( int x, int y, int seed )
	{
		uint h = (uint)(x * 374761393 ^ y * 668265263 ^ seed * 1274126177);
		h = (h ^ (h >> 13)) * 1274126177u;
		h ^= h >> 16;
		return (h & 0xFFFFFF) / (float)0x1000000;
	}

	private int SpawnGrassTufts()
	{
		// Cosmetic-only scatter across both banks. Pure ModelRenderer — no
		// collider/rigidbody so 200 instances stay cheap (transform + draw
		// only, no physics broadphase cost). Mirrors the Godot proto's
		// _build_grass_tuft scatter (cascade.gd L1660) but collapsed to a
		// single tinted cube per instance instead of 3-6 Kenney meshes.
		const int Count = 200;
		const float MinWidth = 6f;
		const float MaxWidth = 12f;
		const float MinHeight = 4f;
		const float MaxHeight = 8f;
		// Per-instance ±25% scale wobble so identical width/height rolls
		// still produce visibly distinct silhouettes side-by-side.
		const float ScaleJitterMin = 0.75f;
		const float ScaleJitterMax = 1.25f;
		const int GrassSeed = 0x9EA55;

		var rng = new Random( GrassSeed );
		int spawned = 0;

		for ( int i = 0; i < Count; i++ )
		{
			// Uniform disc sample inside the arena. √u correction so density
			// isn't biased toward the center, * 0.97 to keep tufts inside the
			// playable boundary instead of right on the edge.
			float r = MathF.Sqrt( (float)rng.NextDouble() ) * Tunables.ArenaRadius * 0.97f;
			float angle = (float)(rng.NextDouble() * MathF.Tau);
			float x = MathF.Cos( angle ) * r;
			float y = MathF.Sin( angle ) * r;

			float w = MathX.Lerp( MinWidth, MaxWidth, (float)rng.NextDouble() );
			float h = MathX.Lerp( MinHeight, MaxHeight, (float)rng.NextDouble() );
			float jitter = MathX.Lerp( ScaleJitterMin, ScaleJitterMax, (float)rng.NextDouble() );
			float sx = w * jitter;
			float sy = w * jitter;
			float sz = h * jitter;

			// Lift by half-height so the cube sits ON the ground, not buried.
			var foot = new Vector3( x, y, Tunables.GroundZ );
			var pos = foot + Vector3.Up * (sz * 0.5f);

			float yaw = (float)(rng.NextDouble() * 360.0);

			// Green-range tint mixing dark forest green to lighter yellow-green.
			float t = (float)rng.NextDouble();
			var tint = new Color(
				MathX.Lerp( 0.20f, 0.45f, t ),
				MathX.Lerp( 0.45f, 0.65f, t ),
				MathX.Lerp( 0.12f, 0.20f, t ),
				1f );

			SpawnGrassTuft( pos, Rotation.FromYaw( yaw ), new Vector3( sx, sy, sz ), tint );
			spawned++;
		}

		return spawned;
	}

	private void SpawnGrassTuft( Vector3 position, Rotation rotation, Vector3 size, Color tint )
	{
		var go = Scene.CreateObject();
		go.Name = "GrassTuft";
		go.WorldPosition = position;
		go.WorldRotation = rotation;
		go.Tags.Add( "grass_tuft" );

		go.WorldScale = size / Tunables.CubeBase;

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Models.GrassVariant( Math.Abs( position.GetHashCode() ) );
		model.Tint = tint;

		// Intentionally no collider / no rigidbody — purely decorative.
	}

	// Scatter low_poly_rock.vmdl props across the arena floor for visual variety.
	// Same cosmetic strategy as SpawnGrassTufts — no collider, no physics, just
	// drawn meshes. Tinted with neutral gray-brown jitter so they read as stones.
	private int SpawnRocks()
	{
		const int Count = 120;
		const float MinScale = 0.6f;
		const float MaxScale = 1.6f;
		const int RockSeed = 0x80CC5;
		var rng = new Random( RockSeed );
		int spawned = 0;
		for ( int i = 0; i < Count; i++ )
		{
			float r = MathF.Sqrt( (float)rng.NextDouble() ) * Tunables.ArenaRadius * 0.95f;
			float angle = (float)(rng.NextDouble() * MathF.Tau);
			float x = MathF.Cos( angle ) * r;
			float y = MathF.Sin( angle ) * r;
			float scale = MathX.Lerp( MinScale, MaxScale, (float)rng.NextDouble() );
			float yaw = (float)(rng.NextDouble() * 360.0);
			// Ground slope follows main.scene rotation (12° around X downhill +X).
			// Rest the rock visually on the inclined plane.
			float z = Tunables.GroundZ - x * Tunables.ArenaSlope;
			float t = (float)rng.NextDouble();
			var tint = new Color(
				MathX.Lerp( 0.55f, 0.78f, t ),
				MathX.Lerp( 0.50f, 0.72f, t ),
				MathX.Lerp( 0.45f, 0.66f, t ),
				1f );
			var go = Scene.CreateObject();
			go.Name = "Rock";
			go.WorldPosition = new Vector3( x, y, z );
			go.WorldRotation = Rotation.FromYaw( yaw );
			go.WorldScale = new Vector3( scale );
			go.Tags.Add( "rock_decor" );
			var mr = go.AddComponent<ModelRenderer>();
			mr.Model = Models.RockVariant( i );
			mr.Tint = tint;
			spawned++;
		}
		return spawned;
	}

	private void SpawnTree( Vector3 footPosition )
	{
		var biome = BiomeManager.Get( Scene );
		var tint = biome.IsValid() ? biome.TrunkTintForNewTree() : new Color( 0.42f, 0.30f, 0.18f, 1f );
		// Mix species per biome bias so the forest reads as varied silhouettes
		// rather than one trunk size + tint. Deterministic via Seed-derived RNG
		// so the same Seed reproduces the same forest layout AND distribution.
		_speciesRng ??= new Random( Seed ^ 0x57EC1E5 );
		var species = biome.IsValid() ? biome.SpeciesForNewTree( _speciesRng ) : TreeSpecies.Beech;
		// Mythic roll — Noita-style golden target. 1 in MythicSpawnRatio.
		bool mythic = _speciesRng.Next( Tunables.MythicSpawnRatio ) == 0;
		Tree.SpawnAt( Scene, footPosition, tint, species, mythic );
	}

	private Random _speciesRng;
}
