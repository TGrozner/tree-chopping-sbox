namespace TreeChopping;

public sealed class SceneStarter : Component
{
	[Property] public int TreeCount { get; set; } = 8;
	[Property] public float ForestRadius { get; set; } = 600f;
	[Property] public float MinSpacing { get; set; } = 140f;
	[Property] public int Seed { get; set; } = 0xCA5C;
	[Property] public Vector3 BeaverSpawn { get; set; } = new( 0f, 0f, 48f );

	protected override void OnStart()
	{
		try
		{
			EnsureInventory();
			var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
			var beaver = SpawnBeaver( camera );
			EnsureHud();
			SpawnForest();

			var beaverMr = beaver?.Components.Get<ModelRenderer>();
			var anyTree = Scene.GetAllComponents<Tree>().FirstOrDefault();
			var treeMr = anyTree?.Components.Get<ModelRenderer>();
			Log.Info( $"[SceneStarter] Bootstrap OK — beaver pos={beaver?.WorldPosition} bounds={beaverMr?.Bounds}, sample tree pos={anyTree?.WorldPosition} bounds={treeMr?.Bounds}, trees={Scene.GetAllComponents<Tree>().Count()}, cam reused={camera.IsValid()}" );
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

	private void EnsureHud()
	{
		var existing = Scene.GetAllComponents<WoodHud>().FirstOrDefault();
		if ( existing.IsValid() ) return;
		var go = Scene.CreateObject();
		go.Name = "WoodHud";
		go.AddComponent<WoodHud>();
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
			var pos = new Vector3( x, y, Tunables.BankTopZ );
			if ( placed.Any( p => p.Distance( pos ) < MinSpacing ) ) continue;
			placed.Add( pos );
			SpawnTree( pos );
			spawned++;
		}
	}

	private void SpawnTree( Vector3 footPosition )
	{
		var go = Scene.CreateObject();
		go.Name = "Tree";
		go.WorldPosition = footPosition + Vector3.Up * (Tunables.TreeHeight * 0.5f);
		go.Tags.Add( "tree" );

		go.WorldScale = new Vector3( Tunables.TreeRadius * 2f, Tunables.TreeRadius * 2f, Tunables.TreeHeight ) / Tunables.CubeBase;

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = new Color( 0.42f, 0.30f, 0.18f, 1f );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.TreeMass;
		rb.AngularDamping = 1.2f;
		rb.LinearDamping = 0.3f;
		rb.StartAsleep = true;

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
	}
}
