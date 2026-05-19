namespace TreeChopping;

public sealed class SceneStarter : Component
{
	[Property] public int TreeCount { get; set; } = 8;
	[Property] public int RockCount { get; set; } = 12;
	[Property] public float ForestRadius { get; set; } = 600f;
	[Property] public float MinSpacing { get; set; } = 140f;
	[Property] public int Seed { get; set; } = 0xCA5C;
	[Property] public Vector3 BeaverSpawn { get; set; } = new( 0f, 0f, 48f );

	// Drop a .sdfvol asset here in the editor to wire up Pickaxe voxel digging.
	// When null, Phase 2e wiring stays dormant and the existing rock/chop loop is untouched.
	[Property] public Sdf3DVolume RockVolume { get; set; }
	[Property] public Vector3 SdfWorldSize { get; set; } = new( 1200f, 6400f, 300f );
	[Property] public Vector3 SdfWorldOrigin { get; set; } = new( -600f, -3200f, -50f );

	protected override void OnStart()
	{
		try
		{
			EnsureInventory();
			EnsureStoneInventory();
			EnsureCombo();
			EnsureWeather();
			EnsureBiomes();
			EnsureDayNight();
			EnsureHints();
			EnsureCompass();
			EnsurePauseMenu();
			EnsureSelfTest();
			var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
			var beaver = SpawnBeaver( camera );
			EnsureHud();
			SpawnForest();
			SpawnRocks();
			int borderBoulders = SpawnBorderBoulders();
			int grassTufts = SpawnGrassTufts();
			EnsureSdfWorld();

			var beaverMr = beaver?.Components.Get<ModelRenderer>();
			var anyTree = Scene.GetAllComponents<Tree>().FirstOrDefault();
			var treeMr = anyTree?.Components.Get<ModelRenderer>();
			var anyRock = Scene.GetAllComponents<Rock>().FirstOrDefault();
			Log.Info( $"[SceneStarter] Bootstrap OK — beaver pos={beaver?.WorldPosition} bounds={beaverMr?.Bounds}, sample tree pos={anyTree?.WorldPosition} bounds={treeMr?.Bounds}, trees={Scene.GetAllComponents<Tree>().Count()}, rocks={Scene.GetAllComponents<Rock>().Count()}, borderBoulders={borderBoulders}, grassTufts={grassTufts}, sample rock pos={anyRock?.WorldPosition}, cam reused={camera.IsValid()}" );
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

	private void EnsureStoneInventory()
	{
		var existing = Scene.GetAllComponents<StoneInventory>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "StoneInventory";
		go.AddComponent<StoneInventory>();
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

	private void EnsureHints()
	{
		var existing = Scene.GetAllComponents<HintManager>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "HintManager";
		go.AddComponent<HintManager>();
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

	private void EnsurePauseMenu()
	{
		var existing = Scene.GetAllComponents<PauseMenu>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "PauseMenu";
		go.AddComponent<PauseMenu>();
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
			cam.FieldOfView = 70f;
			cam.BackgroundColor = new Color( 0.40f, 0.55f, 0.65f, 1f );
		}
		beaver.Camera = cam;
		beaver.CameraRoot = cam.GameObject;

		return beaver;
	}

	private void SpawnForest()
	{
		// 3 bands matching the Godot proto distribution. Banks span |X| ∈ [200, 1200]
		// in s&box units; trees plant on top of bank (Z = Tunables.BankTopZ).
		// Riverside (close to creek edge) — ~30% of trees.
		// Mid — ~40%.
		// Outer (toward map edge) — ~30%.
		var rng = new Random( Seed );
		var placed = new List<Vector3>();

		int riverside = MathX.CeilToInt( TreeCount * 0.30f );
		int mid = MathX.CeilToInt( TreeCount * 0.40f );
		int outer = TreeCount - riverside - mid;

		PlaceBand( rng, placed, riverside, Tunables.BankRiversideMinX, Tunables.BankRiversideMaxX );
		PlaceBand( rng, placed, mid, Tunables.BankMidMinX, Tunables.BankMidMaxX );
		PlaceBand( rng, placed, outer, Tunables.BankOuterMinX, Tunables.BankOuterMaxX );
	}

	private void PlaceBand( Random rng, List<Vector3> placed, int count, float minAbsX, float maxAbsX )
	{
		int attempts = 0;
		int spawned = 0;
		while ( spawned < count && attempts < count * 40 )
		{
			attempts++;
			float absX = MathX.Lerp( minAbsX, maxAbsX, (float)rng.NextDouble() );
			float sideSign = rng.NextDouble() < 0.5 ? -1f : 1f;
			float x = absX * sideSign;
			float y = MathX.Lerp( Tunables.MapZMinDownstream, Tunables.MapZMaxUpstream, (float)rng.NextDouble() );
			var pos = new Vector3( x, y, Tunables.BankTopZ + y * Tunables.SlopeRatio );
			if ( placed.Any( p => p.Distance( pos ) < MinSpacing ) ) continue;
			placed.Add( pos );
			SpawnTree( pos );
			spawned++;
		}
	}

	private void EnsureSdfWorld()
	{
		if ( RockVolume == null )
		{
			Log.Info( "[SceneStarter] SDF wiring skipped — drop a .sdfvol asset on SceneStarter.RockVolume to enable Pickaxe voxel dig." );
			return;
		}

		try
		{
			var go = Scene.CreateObject();
			go.Name = "SdfWorld";
			go.WorldPosition = SdfWorldOrigin;
			var world = go.AddComponent<Sdf3DWorld>();
			world.IsFinite = true;
			world.Size = SdfWorldSize;
			// Solid fill spanning the world's local bounds — Pickaxe carves into it.
			_ = world.AddAsync( new BoxSdf3D( Vector3.Zero, SdfWorldSize ), RockVolume );
			Log.Info( $"[SceneStarter] Sdf3DWorld ready: origin={SdfWorldOrigin} size={SdfWorldSize}, volume={RockVolume.ResourcePath}" );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[SceneStarter] SDF init failed: {ex.Message}" );
		}
	}

	private void SpawnRocks()
	{
		// Rocks share the riverside band with trees but use their own placement list
		// so they can sit between trees. A tighter min-spacing keeps them packed.
		var rng = new Random( Seed ^ 0x5101D );
		var placed = new List<Vector3>();
		int attempts = 0;
		int spawned = 0;
		float spacing = Tunables.RockRadius * 2.5f;
		while ( spawned < RockCount && attempts < RockCount * 50 )
		{
			attempts++;
			float absX = MathX.Lerp( Tunables.BankRiversideMinX, Tunables.BankRiversideMaxX, (float)rng.NextDouble() );
			float sideSign = rng.NextDouble() < 0.5 ? -1f : 1f;
			float x = absX * sideSign;
			float y = MathX.Lerp( Tunables.MapZMinDownstream, Tunables.MapZMaxUpstream, (float)rng.NextDouble() );
			var foot = new Vector3( x, y, Tunables.BankTopZ + y * Tunables.SlopeRatio );
			if ( placed.Any( p => p.Distance( foot ) < spacing ) ) continue;
			placed.Add( foot );
			SpawnRock( foot );
			spawned++;
		}
	}

	private int SpawnBorderBoulders()
	{
		// Visual fence at the upstream/downstream map ends. Static rigidbodies
		// (no Rock component) so the pickaxe ignores them — these read as
		// terrain, not loot. Spread X across the full playable width so the
		// row reads continuous from a distance, with small Y/X jitter to
		// avoid a perfect grid.
		const int Count = 30;
		const float MinSize = 80f;
		const float MaxSize = 140f;
		const float YJitter = 90f; // staggers the line so silhouettes overlap
		const float EndInset = 60f; // sit just inside the map limits, not exactly on them
		const int Seed = 0xB0DEF;

		var rng = new Random( Seed );
		int spawned = 0;
		int perEnd = Count / 2;

		for ( int end = 0; end < 2; end++ )
		{
			// end 0 = downstream (-Y), end 1 = upstream (+Y).
			float baseY = end == 0
				? Tunables.MapZMinDownstream + EndInset
				: Tunables.MapZMaxUpstream - EndInset;

			int rowCount = (end == 0) ? perEnd : (Count - perEnd);

			for ( int i = 0; i < rowCount; i++ )
			{
				// Evenly distribute X across the full bank width, side-to-side.
				float t = (i + 0.5f) / rowCount;
				float x = MathX.Lerp( -Tunables.BankOuterMaxX, Tunables.BankOuterMaxX, t );
				// Small jitter so the row doesn't look surveyed.
				x += MathX.Lerp( -40f, 40f, (float)rng.NextDouble() );
				float y = baseY + MathX.Lerp( -YJitter, YJitter, (float)rng.NextDouble() );

				float sx = MathX.Lerp( MinSize, MaxSize, (float)rng.NextDouble() );
				float sy = MathX.Lerp( MinSize, MaxSize, (float)rng.NextDouble() );
				float sz = MathX.Lerp( MinSize, MaxSize, (float)rng.NextDouble() );

				// Foot sits on the sloped bank surface; lift by half-height so
				// the box rests on top rather than half-sunk.
				var foot = new Vector3( x, y, Tunables.BankTopZ + y * Tunables.SlopeRatio );
				var pos = foot + Vector3.Up * (sz * 0.5f);

				// Random yaw + small tilt → varied silhouettes without
				// collider weirdness from extreme orientations.
				var rot = Rotation.FromYaw( (float)(rng.NextDouble() * 360.0) )
					* Rotation.FromPitch( MathX.Lerp( -15f, 15f, (float)rng.NextDouble() ) )
					* Rotation.FromRoll( MathX.Lerp( -15f, 15f, (float)rng.NextDouble() ) );

				SpawnBorderBoulder( pos, rot, new Vector3( sx, sy, sz ) );
				spawned++;
			}
		}

		return spawned;
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
			// Sample on the same |X|∈[BankRiversideMinX, BankOuterMaxX] band
			// used by trees/rocks, mirrored across the creek; Y across the
			// full upstream/downstream span. Z follows the slope plane so
			// tufts sit on the bank surface, not floating.
			float absX = MathX.Lerp( Tunables.BankRiversideMinX, Tunables.BankOuterMaxX, (float)rng.NextDouble() );
			float sideSign = rng.NextDouble() < 0.5 ? -1f : 1f;
			float x = absX * sideSign;
			float y = MathX.Lerp( Tunables.MapZMinDownstream, Tunables.MapZMaxUpstream, (float)rng.NextDouble() );

			float w = MathX.Lerp( MinWidth, MaxWidth, (float)rng.NextDouble() );
			float h = MathX.Lerp( MinHeight, MaxHeight, (float)rng.NextDouble() );
			float jitter = MathX.Lerp( ScaleJitterMin, ScaleJitterMax, (float)rng.NextDouble() );
			float sx = w * jitter;
			float sy = w * jitter;
			float sz = h * jitter;

			// Lift by half-height so the cube sits ON the slope, not buried.
			var foot = new Vector3( x, y, Tunables.BankTopZ + y * Tunables.SlopeRatio );
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
		model.Model = Model.Cube;
		model.Tint = tint;

		// Intentionally no collider / no rigidbody — purely decorative.
	}

	private void SpawnBorderBoulder( Vector3 position, Rotation rotation, Vector3 size )
	{
		var go = Scene.CreateObject();
		go.Name = "BorderBoulder";
		go.WorldPosition = position;
		go.WorldRotation = rotation;
		go.Tags.Add( "border_boulder" );

		go.WorldScale = size / Tunables.CubeBase;

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		// Desaturated grey-brown vs Rock's cooler grey (0.55,0.55,0.58) —
		// reads as background terrain, not as a minable target.
		model.Tint = new Color( 0.45f, 0.43f, 0.40f, 1f );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		// Static-feeling body: high mass, asleep on spawn so the physics engine
		// never wakes them. No Rock component → IChoppable picks ignore them.
		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 5000f;
		rb.LinearDamping = 4f;
		rb.AngularDamping = 8f;
		rb.StartAsleep = true;
	}

	private void SpawnRock( Vector3 footPosition )
	{
		var go = Scene.CreateObject();
		go.Name = "Rock";
		go.WorldPosition = footPosition + Vector3.Up * (Tunables.RockHeight * 0.5f);
		go.Tags.Add( "rock" );

		go.WorldScale = new Vector3( Tunables.RockRadius * 2f, Tunables.RockRadius * 2f, Tunables.RockHeight ) / Tunables.CubeBase;

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = new Color( 0.55f, 0.55f, 0.58f, 1f );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.RockMass;
		rb.AngularDamping = 2f;
		rb.LinearDamping = 0.6f;
		rb.StartAsleep = true;

		var rock = go.AddComponent<Rock>();
		rock.Body = rb;
	}

	private void SpawnTree( Vector3 footPosition )
	{
		var biome = BiomeManager.Get( Scene );
		var tint = biome.IsValid() ? biome.TrunkTintForNewTree() : new Color( 0.42f, 0.30f, 0.18f, 1f );
		Tree.SpawnAt( Scene, footPosition, tint );
	}
}
