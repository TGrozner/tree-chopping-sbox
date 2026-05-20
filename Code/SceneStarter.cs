namespace TreeChopping;

// Mow-the-lawn bootstrap : spawn beaver at the summit, drop a ShopArea
// marker on the plateau, scatter a forest with biome bias (close trees =
// saplings/easy ; far trees = veterans/hard). No daily seed — random per
// boot, continuous play.
public sealed class SceneStarter : Component
{
	[Property] public int TreeCount { get; set; } = 400;
	[Property] public float MinSpacing { get; set; } = 180f;
	[Property] public int Seed { get; set; } = 0xCA5C;
	[Property] public Vector3 BeaverSpawn { get; set; } = new( -1000f, 0f, 80f );
	[Property] public float SpawnPadRadius { get; set; } = 180f;

	[ConVar( "tc_selftest_seed", Help = "If >0, overrides SceneStarter.Seed before bootstrap." )]
	public static int SeedOverride { get; set; }

	public Vector3 ResolvedBeaverSpawn { get; private set; }
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
			EnsureSelfTest();

			SetupLighting();
			DisableSceneGround();
			TerrainHeightmap.Spawn( Scene, Seed, BeaverSpawn );
			int borders = MapBorders.Spawn( Scene, Vector3.Zero );
			Log.Info( $"[SceneStarter] Placed {borders} border-mountain segments" );

			ResolveBeaverSpawnGround();

			var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
			var beaver = SpawnBeaver( camera );
			SpawnShop();
			SpawnForest();

			Log.Info( $"[SceneStarter] Bootstrap OK — beaver pos={beaver?.WorldPosition}, trees={Scene.GetAllComponents<Tree>().Count()}" );
		}
		catch ( System.Exception ex )
		{
			Log.Error( $"[SceneStarter] Bootstrap failed: {ex}" );
		}
	}

	// Warm sun + brighter ambient SkyColor so shaded trunks read as brown
	// instead of silhouettes. The scene's original Sun was warm-orange but
	// dim, with a cool-blue SkyColor at 0.22/0.32/0.48 — directional-strong
	// + fill-weak = sun-facing faces bright, away-faces black. Bump both.
	private void SetupLighting()
	{
		var sun = Scene.GetAllComponents<DirectionalLight>().FirstOrDefault();
		if ( sun.IsValid() )
		{
			// Keep the scene's warm sunset cast — bump intensity (×1.7) without
			// neutralising the orange. Sky cool-blue lifted ×2 from the scene's
			// 0.22/0.32/0.48 so shaded trunks gain a fill instead of clipping
			// to black.
			sun.LightColor = new Color( 1.00f, 0.78f, 0.48f, 1f ) * 1.7f;
			sun.SkyColor = new Color( 0.44f, 0.58f, 0.78f, 1f );
			sun.Shadows = true;
			// Softer shadow edges = less crisp banding on big trees casting on
			// terrain. Default ~1.0, drop to 0.5 for ~PCF-ish softness.
			sun.ShadowHardness = 0.5f;
			sun.FogMode = DirectionalLight.FogInfluence.Enabled;
			sun.FogStrength = 1.1f;
			Log.Info( "[SceneStarter] Sun warm sunset intensity ×1.7, sky fill ×2, soft shadows" );
		}
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

	private void ResolveBeaverSpawnGround()
	{
		const float clearance = 80f;
		const float driftLimit = 200f;
		var spawn = BeaverSpawn;
		var drift = new Vector2( spawn.x - AuthoredSpawn.x, spawn.y - AuthoredSpawn.y );
		if ( drift.Length > driftLimit )
		{
			Log.Warning( $"[SceneStarter] BeaverSpawn drift ({spawn}) — falling back to authored {AuthoredSpawn}" );
			spawn = AuthoredSpawn;
		}
		float z = spawn.z;
		if ( TryGetGroundZ( spawn.x, spawn.y, out float groundZ ) )
			z = groundZ + clearance;
		ResolvedBeaverSpawn = new Vector3( spawn.x, spawn.y, z );
	}

	private BeaverController SpawnBeaver( CameraComponent existingCamera )
	{
		var go = Scene.CreateObject();
		go.Name = "Player";
		go.WorldPosition = ResolvedBeaverSpawn;
		go.Tags.Add( "player" );

		var modelGo = Scene.CreateObject();
		modelGo.Name = "PlayerModel";
		modelGo.SetParent( go );
		modelGo.LocalPosition = Vector3.Zero;
		var renderer = modelGo.AddComponent<SkinnedModelRenderer>();
		renderer.Model = Model.Load( "models/citizen/citizen.vmdl" );
		renderer.CreateBoneObjects = true;

		var animHelper = modelGo.AddComponent<Sandbox.Citizen.CitizenAnimationHelper>();
		animHelper.Target = renderer;
		animHelper.HoldType = Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Swing;

		var player = go.AddComponent<PlayerController>();
		player.Renderer = renderer;
		player.ThirdPerson = true;
		player.UseAnimatorControls = true;
		player.UseCameraControls = true;
		player.UseInputControls = true;
		player.UseLookControls = true;

		var beaver = go.AddComponent<BeaverController>();
		beaver.Player = player;
		beaver.PlayerRenderer = renderer;

		var cam = existingCamera;
		if ( cam.IsValid() ) beaver.Camera = cam;

		AttachAxe( renderer, animHelper );
		return beaver;
	}

	private void AttachAxe( SkinnedModelRenderer renderer, Sandbox.Citizen.CitizenAnimationHelper animHelper )
	{
		if ( !renderer.IsValid() ) return;
		var hand = renderer.GetBoneObject( "hand_R" );
		if ( !hand.IsValid() ) hand = renderer.GetBoneObject( "hand_right" );
		if ( !hand.IsValid() ) return;

		string[] candidates = {
			"models/props/trim_sheets/tools/woodaxe.vmdl",
			"models/woodaxe.vmdl",
		};
		Model axe = null;
		string usedPath = null;
		foreach ( var path in candidates )
		{
			try
			{
				var m = Model.Load( path );
				if ( m is not null ) { axe = m; usedPath = path; break; }
			}
			catch { }
		}
		if ( axe is null )
		{
			Log.Info( "[SceneStarter] woodaxe asset not mounted — install facepunch/woodaxe" );
			return;
		}

		var axeGo = Scene.CreateObject();
		axeGo.Name = "Axe";
		axeGo.SetParent( hand );
		axeGo.LocalPosition = Vector3.Zero;
		axeGo.LocalRotation = Rotation.Identity;
		axeGo.LocalScale = Vector3.One;
		var mr = axeGo.AddComponent<ModelRenderer>();
		mr.Model = axe;
		if ( animHelper.IsValid() ) animHelper.IkLeftHand = null;
		Log.Info( $"[SceneStarter] Axe asset mounted on hand_R from '{usedPath}' (1H grip)" );
	}

	private void SpawnShop()
	{
		var go = Scene.CreateObject();
		go.Name = "ShopArea";
		go.WorldPosition = ResolvedBeaverSpawn;
		go.AddComponent<ShopArea>();
		// Compact wooden disk under the shop — 120u radius (was 250u and
		// dominated the camera foreground, hiding the green terrain).
		var disk = Scene.CreateObject();
		disk.Name = "ShopDisk";
		disk.SetParent( go );
		disk.LocalPosition = new Vector3( 0f, 0f, -65f );
		disk.LocalScale = new Vector3( 120f, 120f, 14f ) / Tunables.CubeBase;
		Mat.AddTintedCube( disk, new Color( 0.62f, 0.42f, 0.18f, 1f ) );
	}

	public void RegenerateForest()
	{
		// Drop existing trees + re-spawn with the same seed (mow-the-lawn
		// doesn't normally regen — but tools/selftest still calls this).
		foreach ( var t in Scene.GetAllComponents<Tree>().ToList() )
		{
			if ( !t.IsValid() ) continue;
			var go = t.GameObject;
			if ( go.IsValid() ) { go.Enabled = false; go.Destroy(); }
		}
		ResolveBeaverSpawnGround();
		SpawnForest();
		Log.Info( $"[SceneStarter] Forest regenerated, trees={Scene.GetAllComponents<Tree>().Count()}" );
	}

	private void SpawnForest()
	{
		var rng = new Random( Seed );
		var placed = new List<Vector3>();
		int attempts = 0;
		int spawned = 0;
		int maxAttempts = TreeCount * 80;

		float padXY_X = ResolvedBeaverSpawn.x;
		float padXY_Y = ResolvedBeaverSpawn.y;
		float padRSq = SpawnPadRadius * SpawnPadRadius;

		while ( spawned < TreeCount && attempts < maxAttempts )
		{
			attempts++;
			float r = MathF.Sqrt( (float)rng.NextDouble() ) * Tunables.ArenaRadius;
			if ( r < Tunables.ArenaCenterKeepout ) continue;
			float angle = (float)(rng.NextDouble() * MathF.Tau);
			float x = MathF.Cos( angle ) * r;
			float y = MathF.Sin( angle ) * r;

			float dxPad = x - padXY_X;
			float dyPad = y - padXY_Y;
			if ( dxPad * dxPad + dyPad * dyPad < padRSq ) continue;

			float density = ValueNoise2D( x / Tunables.ArenaNoiseScale, y / Tunables.ArenaNoiseScale, Seed );
			if ( density < Tunables.ArenaDensityThreshold ) continue;

			if ( !TryGetGroundZ( x, y, out float groundZ ) ) continue;

			float spacing = MathX.Lerp( MinSpacing * 1.4f, MinSpacing * 0.7f, density );
			var pos = new Vector3( x, y, groundZ );
			if ( placed.Any( p => p.Distance( pos ) < spacing ) ) continue;

			placed.Add( pos );
			float dxBeav = x - ResolvedBeaverSpawn.x;
			float dyBeav = y - ResolvedBeaverSpawn.y;
			float distBeav = MathF.Sqrt( dxBeav * dxBeav + dyBeav * dyBeav );
			// Biome difficulty 0..1 by distance to spawn. SpawnPadRadius..Arena
			// covers the playable annulus ; clamp to that band.
			float diff = ((distBeav - SpawnPadRadius) / (Tunables.ArenaRadius - SpawnPadRadius)).Clamp( 0f, 1f );
			Tree.SpawnAt( Scene, pos, diff );
			spawned++;
		}
		if ( spawned < TreeCount )
			Log.Warning( $"[SceneStarter] Forest shortfall : {spawned}/{TreeCount} trees ({attempts} attempts). Density threshold or MinSpacing too strict ?" );
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
