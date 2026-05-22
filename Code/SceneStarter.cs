namespace TreeChopping;

// Mow-the-lawn bootstrap : spawn player at the summit, place the four
// ShopStation rings on the plateau, scatter a forest with biome bias (close trees =
// saplings/easy ; far trees = veterans/hard). No daily seed -- random per
// boot, continuous play.
public sealed class SceneStarter : Component
{
	// Mow-like resource field: the hub stays open, but the first ring beyond
	// it needs enough saplings to read as "work to clear", not a sparse arena.
	[Property] public int TreeCount { get; set; } = 130;
	[Property] public float MinSpacing { get; set; } = 190f;
	[Property] public int Seed { get; set; } = 0xCA5C;
	[Property] public Vector3 PlayerSpawn { get; set; } = new( -1000f, 0f, 80f );
	// Pad needs > ShopStationArcRadius + station footprint AND room around
	// the player so the spawn feels respirable, but Mow-like resource starts
	// must still be visible from the hub.
	[Property] public float SpawnPadRadius { get; set; } = 930f;
	// 4 shop stations on a forward arc (+X), spread +/-67.5 deg. At 650u radius,
	// neighbor-to-neighbor distance is ~497u -- well separated (Thomas
	// 2026-05-21 : "elles sont trop proches").
	[Property] public float ShopStationArcRadius { get; set; } = 650f;

	[ConVar( "tc_selftest_seed", Help = "If >0, overrides SceneStarter.Seed before bootstrap." )]
	public static int SeedOverride { get; set; }

	public Vector3 ResolvedPlayerSpawn { get; private set; }
	private static readonly Vector3 AuthoredSpawn = new( -1000f, 0f, 80f );

	protected override void OnStart()
	{
		try
		{
			if ( SeedOverride > 0 )
			{
				Seed = SeedOverride;
				Log.Info( $"[SceneStarter] Seed override: {Seed}" );
			}

			EnsureGameState();
			EnsureHud();
			EnsureSingleton<AutoPlay>( "AutoPlay" );
			EnsureSingleton<PerfProbe>( "PerfProbe" );
			EnsureSingleton<FilmStrip>( "FilmStrip" );
			EnsureSelfTest();

			SetupLighting();
			DisableSceneGround();
			TerrainHeightmap.Spawn( Scene, Seed, PlayerSpawn );
			int borders = MapBorders.Spawn( Scene, Vector3.Zero );
			Log.Info( $"[SceneStarter] Placed {borders} border-mountain segments" );

			ResolvePlayerSpawnGround();

			var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
			var player = SpawnPlayerCharacter( camera );
			SpawnShop();
			HubProps.Spawn( Scene, ResolvedPlayerSpawn, ShopStationArcRadius );
			// HubAmphitheatre dropped 2026-05-21 : "vire tous les trucs en
			// pierre du spawn". Hub is now just terrain + wooden props + the
			// 4 invisible station zones with their worldspace labels.
			SpawnForest();
			SpawnStarterSapling();
			SpawnPet( player );

			Log.Info( $"[SceneStarter] Bootstrap OK -- player pos={player?.WorldPosition}, trees={Scene.GetAllComponents<Tree>().Count()}" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"[SceneStarter] Bootstrap failed: {ex}" );
		}
	}

	// Warm sun + brighter ambient SkyColor so shaded trunks read as brown
	// instead of silhouettes. The scene's original Sun was warm-orange but
	// dim, with a cool-blue SkyColor at 0.22/0.32/0.48 -- directional-strong
	// + fill-weak = sun-facing faces bright, away-faces black. Bump both.
	private void SetupLighting()
	{
		// Daylight palette pivot 2026-05-21 : the warm-sunset cast was
		// crushing the hub silhouettes (Mow-the-lawn comparison showed our
		// stations/amphi/perso all blending into the same orange tone).
		// Shift toward bright golden hour : warm-white sun x2.5 + bright
		// blue sky fill + much less aggressive fog. Skybox + fog overridden
		// here (not in main.scene) so the values land even if the editor
		// hasn't reloaded the scene file.
		var sun = Scene.GetAllComponents<DirectionalLight>().FirstOrDefault();
		if ( sun.IsValid() )
		{
			sun.LightColor = new Color( 1.00f, 0.94f, 0.82f, 1f ) * 2.5f;
			sun.SkyColor = new Color( 0.62f, 0.78f, 1.00f, 1f ) * 1.4f;
			sun.Shadows = true;
			sun.ShadowHardness = 0.45f;
			sun.FogMode = DirectionalLight.FogInfluence.Enabled;
			sun.FogStrength = 0.12f;
		}
		var sky = Scene.GetAllComponents<SkyBox2D>().FirstOrDefault();
		if ( sky.IsValid() ) sky.Tint = new Color( 1.00f, 0.96f, 0.88f, 1f );
		var fog = Scene.GetAllComponents<GradientFog>().FirstOrDefault();
		if ( fog.IsValid() )
		{
			fog.Color = new Color( 0.76f, 0.80f, 0.76f, 1f );
			fog.StartDistance = 3000f;
			fog.EndDistance = 7000f;
		}
		Log.Info( "[SceneStarter] Daylight palette applied (sun x2.5, sky-blue fill x1.4, fog 3000->7000u neutral)" );
	}

	private void DisableSceneGround()
	{
		var ground = Scene.Directory.FindByName( "Ground" ).FirstOrDefault();
		if ( ground.IsValid() ) ground.Enabled = false;
	}

	private void EnsureGameState() => EnsureSingleton<GameState>( "GameState" );
	private void EnsureHud() => EnsureSingleton<WoodHud>( "WoodHud" );

	private void EnsureSelfTest()
	{
		if ( !SelfTest.IsActiveRequest() ) return;
		EnsureSingleton<SelfTest>( "SelfTest" );
		Log.Info( "[TC_TEST] SelfTest spawned by SceneStarter" );
	}

	private T EnsureSingleton<T>( string name ) where T : Component, new()
	{
		var existing = Scene.GetAllComponents<T>().FirstOrDefault();
		if ( existing.IsValid() ) return existing;
		var go = Scene.CreateObject();
		go.Name = name;
		return go.AddComponent<T>();
	}

	private void ResolvePlayerSpawnGround()
	{
		const float clearance = 80f;
		const float driftLimit = 200f;
		var spawn = PlayerSpawn;
		var drift = new Vector2( spawn.x - AuthoredSpawn.x, spawn.y - AuthoredSpawn.y );
		if ( drift.Length > driftLimit )
		{
			Log.Warning( $"[SceneStarter] PlayerSpawn drift ({spawn}) -- falling back to authored {AuthoredSpawn}" );
			spawn = AuthoredSpawn;
		}
		float z = spawn.z;
		if ( TryGetGroundZ( spawn.x, spawn.y, out float groundZ ) )
			z = groundZ + clearance;
		ResolvedPlayerSpawn = new Vector3( spawn.x, spawn.y, z );
	}

	private AxeController SpawnPlayerCharacter( CameraComponent existingCamera )
	{
		var go = Scene.CreateObject();
		go.Name = "Player";
		go.WorldPosition = ResolvedPlayerSpawn;

		var modelGo = Scene.CreateObject();
		modelGo.Name = "PlayerModel";
		modelGo.SetParent( go );
		var renderer = modelGo.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );
		renderer.CreateBoneObjects = true;

		var animHelper = modelGo.AddComponent<Sandbox.Citizen.CitizenAnimationHelper>();
		animHelper.Target = renderer;
		animHelper.HoldType = Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Swing;

		var motor = go.AddComponent<PlayerController>();
		motor.Renderer = renderer;
		motor.ThirdPerson = true;
		motor.UseAnimatorControls = true;
		motor.UseCameraControls = true;
		motor.UseInputControls = true;
		motor.UseLookControls = true;

		var axe = go.AddComponent<AxeController>();
		axe.Player = motor;
		axe.PlayerRenderer = renderer;

		var axeView = modelGo.AddComponent<PlayerAxeView>();
		axeView.PlayerRenderer = renderer;

		var cam = existingCamera;
		if ( cam.IsValid() ) axe.Camera = cam;

		return axe;
	}

	private void SpawnPet( AxeController player )
	{
		var go = Scene.CreateObject();
		go.Name = "Pet";
		var pet = go.AddComponent<Pet>();
		pet.FollowTarget = player;
	}

	private void SpawnShop()
	{
		// 4 stations on a forward arc (+X) so the player faces them on spawn.
		// Matches the Mow-the-lawn layout : Tools / Depot / Upgrades / Prestige
		// spread +/-67.5 deg from the spawn forward direction.
		// Totem dropped 2026-05-21 -- the tall flagpole made no sense vs the
		// physical stations which are the actual nav landmark.
		SpawnStationAt( -67.5f, StationKind.Tools );
		SpawnStationAt( -22.5f, StationKind.Deposit );
		SpawnStationAt(  22.5f, StationKind.Upgrades );
		SpawnStationAt(  67.5f, StationKind.Prestige );
	}

	private void SpawnStationAt( float arcAngleDeg, StationKind kind )
	{
		float rad = arcAngleDeg * MathF.PI / 180f;
		float dx = MathF.Cos( rad ) * ShopStationArcRadius;
		float dy = MathF.Sin( rad ) * ShopStationArcRadius;
		var pos = new Vector3( ResolvedPlayerSpawn.x + dx, ResolvedPlayerSpawn.y + dy, ResolvedPlayerSpawn.z );
		// Drop to the local ground at the station's XY so the disc sits flush
		// on the terrain instead of floating where the spawn plateau ends.
		if ( TryGetGroundZ( pos.x, pos.y, out float groundZ ) ) pos.z = groundZ + 60f;
		ShopStation.SpawnAt( Scene, pos, kind );
	}

	public void RegenerateForest()
	{
		// Drop existing trees + re-spawn with the same seed (mow-the-lawn
		// doesn't normally regen -- but tools/selftest still calls this).
		foreach ( var t in Scene.GetAllComponents<Tree>().ToList() )
		{
			if ( !t.IsValid() ) continue;
			var go = t.GameObject;
			if ( go.IsValid() ) { go.Enabled = false; go.Destroy(); }
		}
		ResolvePlayerSpawnGround();
		SpawnForest();
		Log.Info( $"[SceneStarter] Forest regenerated, trees={Scene.GetAllComponents<Tree>().Count()}" );
	}

	// Full arena spawned in one go. Progression comes from tool tiers, wood
	// types, backpack capacity, prestige, and the dense starter field around
	// the hub.
	// Gate-ring expansion was dropped.
	private void SpawnForest()
	{
		float innerR = SpawnPadRadius;
		float outerR = Tunables.ArenaRadius;
		SpawnStarterResourceField();
		int scripted = SpawnProgressionGroves();
		SpawnForestBand( innerR + 920f, outerR, Math.Max( 0, TreeCount - scripted ) );
	}

	private int SpawnProgressionGroves()
	{
		int spawned = 0;
		var placed = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() )
			.Select( t => t.WorldPosition )
			.ToList();

		spawned += SpawnProgressionGrove( placed, new Vector2( 1640f, -520f ), 15, 360f, 210f, TreeKind.Normal, TreeKind.Sapling, 0.62f );
		spawned += SpawnProgressionGrove( placed, new Vector2( 1840f,  520f ), 14, 360f, 220f, TreeKind.Normal, TreeKind.Brittle, 0.58f );
		spawned += SpawnProgressionGrove( placed, new Vector2( 2320f, -260f ), 12, 420f, 260f, TreeKind.Veteran, TreeKind.Normal, 0.68f );
		spawned += SpawnProgressionGrove( placed, new Vector2( 2460f,  420f ), 10, 360f, 240f, TreeKind.Veteran, TreeKind.Brittle, 0.64f );

		Log.Info( $"[SceneStarter] Progression groves spawned {spawned} scripted trees" );
		return spawned;
	}

	private int SpawnProgressionGrove( List<Vector3> placed, Vector2 centerOffset, int count, float radiusX, float radiusY, TreeKind primary, TreeKind support, float primaryChance )
	{
		var rng = new Random( Seed ^ centerOffset.GetHashCode() ^ ((int)primary << 12) ^ count );
		int spawned = 0;
		int attempts = 0;
		while ( spawned < count && attempts < count * 40 )
		{
			attempts++;
			float a = (float)(rng.NextDouble() * MathF.Tau);
			float r = MathF.Sqrt( (float)rng.NextDouble() );
			float x = ResolvedPlayerSpawn.x + centerOffset.x + MathF.Cos( a ) * radiusX * r;
			float y = ResolvedPlayerSpawn.y + centerOffset.y + MathF.Sin( a ) * radiusY * r;
			if ( !TryGetGroundZ( x, y, out float groundZ ) ) continue;

			var pos = new Vector3( x, y, groundZ );
			if ( placed.Any( p => p.Distance( pos ) < MinSpacing * 0.72f ) ) continue;
			placed.Add( pos );

			var kind = rng.NextDouble() < primaryChance ? primary : support;
			Tree.SpawnAt( Scene, pos, biomeDifficulty: primary == TreeKind.Veteran ? 0.86f : 0.45f, forceKind: kind );
			spawned++;
		}
		return spawned;
	}

	private void SpawnStarterResourceField()
	{
		const int StarterCount = 72;
		const float StartOffset = 80f;
		const float EndOffset = 760f;
		const float Spacing = 128f;
		const float FrontStep = 132f;
		var rng = new Random( Seed ^ 0x51A7E7 );
		var placed = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() )
			.Select( t => t.WorldPosition )
			.ToList();

		int spawned = 0;
		float[] laneOffsets = { -150f, 0f, 150f };
		for ( int row = 0; row < 5 && spawned < StarterCount; row++ )
		{
			float x = ResolvedPlayerSpawn.x + SpawnPadRadius + StartOffset + row * 145f;
			for ( int i = 0; i < laneOffsets.Length && spawned < StarterCount; i++ )
			{
				float y = ResolvedPlayerSpawn.y + laneOffsets[i] + (row % 2 == 0 ? 0f : 42f);
				if ( !TryGetGroundZ( x, y, out float groundZ ) ) continue;

				var pos = new Vector3( x, y, groundZ );
				if ( placed.Any( p => p.Distance( pos ) < Spacing ) ) continue;
				placed.Add( pos );

				var kind = spawned % 7 == 6 ? TreeKind.Normal : TreeKind.Sapling;
				Tree.SpawnAt( Scene, pos, biomeDifficulty: 0f, forceKind: kind );
				spawned++;
			}
		}

		for ( int row = 0; row < 5 && spawned < StarterCount; row++ )
		{
			float r = SpawnPadRadius + StartOffset + row * FrontStep;
			float[] angles = row % 2 == 0
				? new[] { -38f, -24f, -12f, 0f, 12f, 24f, 38f }
				: new[] { -31f, -18f, -6f, 6f, 18f, 31f };

			for ( int i = 0; i < angles.Length && spawned < StarterCount; i++ )
			{
				float angle = angles[i].DegreeToRadian();
				float x = ResolvedPlayerSpawn.x + MathF.Cos( angle ) * r;
				float y = ResolvedPlayerSpawn.y + MathF.Sin( angle ) * r;
				if ( !TryGetGroundZ( x, y, out float groundZ ) ) continue;

				var pos = new Vector3( x, y, groundZ );
				if ( placed.Any( p => p.Distance( pos ) < Spacing ) ) continue;
				placed.Add( pos );

				bool centerLane = MathF.Abs( angles[i] ) <= 12f;
				var kind = centerLane && row >= 2 && spawned % 4 == 3 ? TreeKind.Normal : TreeKind.Sapling;
				Tree.SpawnAt( Scene, pos, biomeDifficulty: 0f, forceKind: kind );
				spawned++;
			}
		}

		int attempts = 0;
		while ( spawned < StarterCount && attempts < StarterCount * 60 )
		{
			attempts++;
			float r = SpawnPadRadius + MathX.Lerp( StartOffset, EndOffset, (float)rng.NextDouble() );
			float angle = MathX.Lerp( -62f, 62f, (float)rng.NextDouble() ).DegreeToRadian();
			float x = ResolvedPlayerSpawn.x + MathF.Cos( angle ) * r;
			float y = ResolvedPlayerSpawn.y + MathF.Sin( angle ) * r;
			if ( !TryGetGroundZ( x, y, out float groundZ ) ) continue;

			var pos = new Vector3( x, y, groundZ );
			if ( placed.Any( p => p.Distance( pos ) < Spacing ) ) continue;
			placed.Add( pos );

			var kind = spawned % 9 == 8 ? TreeKind.Normal : TreeKind.Sapling;
			Tree.SpawnAt( Scene, pos, biomeDifficulty: 0f, forceKind: kind );
			spawned++;
		}

		Log.Info( $"[SceneStarter] Starter resource field spawned {spawned}/{StarterCount} trees" );
	}

	private void SpawnForestBand( float innerR, float outerR, int targetCount )
	{
		if ( outerR <= innerR || targetCount <= 0 ) return;
		var rng = new Random( Seed ^ (innerR.GetHashCode() * 131) );
		// Re-collect existing tree positions so the new band doesn't overlap.
		var placed = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() )
			.Select( t => t.WorldPosition )
			.ToList();
		int attempts = 0;
		int spawned = 0;
		int maxAttempts = targetCount * 80;
		float padRSq = SpawnPadRadius * SpawnPadRadius;
		float padXY_X = ResolvedPlayerSpawn.x;
		float padXY_Y = ResolvedPlayerSpawn.y;
		float bandWidth = outerR - innerR;

		while ( spawned < targetCount && attempts < maxAttempts )
		{
			attempts++;
			// Uniform area sampling in the band : r^2 uniform in [innerR^2, outerR^2].
			float r = MathF.Sqrt( innerR * innerR + (float)rng.NextDouble() * (outerR * outerR - innerR * innerR) );
			float angle = (float)(rng.NextDouble() * MathF.Tau);
			float x = MathF.Cos( angle ) * r;
			float y = MathF.Sin( angle ) * r;

			float dxPad = x - padXY_X;
			float dyPad = y - padXY_Y;
			if ( dxPad * dxPad + dyPad * dyPad < padRSq ) continue;

			float density = ValueNoise2D( x / Tunables.ArenaNoiseScale, y / Tunables.ArenaNoiseScale, Seed );
			if ( density < Tunables.ArenaDensityThreshold ) continue;
			float clearing = ValueNoise2D( x / Tunables.ForestClearingNoiseScale + 31.7f, y / Tunables.ForestClearingNoiseScale - 11.3f, Seed ^ 0x5A17 );
			if ( clearing < Tunables.ForestClearingThreshold && density < 0.72f ) continue;

			if ( !TryGetGroundZ( x, y, out float groundZ ) ) continue;

			float cluster = ((density - Tunables.ArenaDensityThreshold) / (1f - Tunables.ArenaDensityThreshold)).Clamp( 0f, 1f );
			if ( clearing > 0.76f ) cluster = MathF.Min( 1f, cluster + 0.18f );
			float spacing = MathX.Lerp( MinSpacing * 1.55f, MinSpacing * 0.58f, cluster );
			var pos = new Vector3( x, y, groundZ );
			if ( placed.Any( p => p.Distance( pos ) < spacing ) ) continue;

			placed.Add( pos );
			float distBeav = MathF.Sqrt( dxPad * dxPad + dyPad * dyPad );
			float diff = ComputeBiomeDifficulty( distBeav );
			Tree.SpawnAt( Scene, pos, diff );
			spawned++;
		}
		if ( spawned < targetCount )
			Log.Warning( $"[SceneStarter] Band [{innerR:0}..{outerR:0}] shortfall : {spawned}/{targetCount} trees" );
	}

	private float ComputeBiomeDifficulty( float distFromSpawn )
	{
		float t = ((distFromSpawn - SpawnPadRadius) / (Tunables.ArenaRadius - SpawnPadRadius)).Clamp( 0f, 1f );
		if ( t < 0.24f ) return t * 0.25f;
		if ( t < 0.58f ) return MathX.Lerp( 0.25f, 0.62f, (t - 0.24f) / 0.34f );
		return MathX.Lerp( 0.72f, 1.0f, (t - 0.58f) / 0.42f );
	}

	// Guaranteed weak sapling 120u ahead (+X) of the player on boot. This is
	// the first bite-sized chop before the denser starter lane.
	private void SpawnStarterSapling()
	{
		const float distAhead = 120f;
		float x = ResolvedPlayerSpawn.x + distAhead;
		float y = ResolvedPlayerSpawn.y;
		if ( !TryGetGroundZ( x, y, out float z ) ) z = ResolvedPlayerSpawn.z;
		var pos = new Vector3( x, y, z );
		Tree.SpawnAt( Scene, pos, biomeDifficulty: 0f, forceKind: TreeKind.Sapling );
		Log.Info( $"[SceneStarter] Starter sapling spawned at {pos}" );
	}

	private bool TryGetGroundZ( float x, float y, out float groundZ )
	{
		var top = new Vector3( x, y, 2000f );
		var bot = new Vector3( x, y, -2000f );
		var hit = Scene.Trace.Ray( top, bot ).WithAnyTags( "ground" ).Run();
		if ( !hit.Hit ) { groundZ = 0f; return false; }
		groundZ = hit.EndPosition.z;
		return true;
	}

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
}
