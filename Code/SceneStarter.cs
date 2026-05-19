namespace TreeChopping;

public sealed class SceneStarter : Component
{
	[Property] public int TreeCount { get; set; } = 8;
	[Property] public float ForestRadius { get; set; } = 600f;
	[Property] public float MinSpacing { get; set; } = 140f;
	[Property] public int Seed { get; set; } = 0xCA5C;
	[Property] public Vector3 BeaverSpawn { get; set; } = new( 0f, 0f, 48f );

	protected override void OnAwake()
	{
		EnsureInventory();
		var camera = Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		var beaver = SpawnBeaver( camera );
		EnsureHud();
		SpawnForest( beaver?.WorldPosition ?? Vector3.Zero );
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

		go.WorldScale = new Vector3( 32f, 32f, 72f );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = new Color( 0.40f, 0.27f, 0.18f, 1f );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = Vector3.One;

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

	private void SpawnForest( Vector3 around )
	{
		var rng = new Random( Seed );
		var placed = new List<Vector3>();
		int attempts = 0;
		while ( placed.Count < TreeCount && attempts < TreeCount * 30 )
		{
			attempts++;
			var theta = (float)(rng.NextDouble() * MathF.PI * 2f);
			var r = MathX.Lerp( 220f, ForestRadius, (float)rng.NextDouble() );
			var pos = around + new Vector3( MathF.Cos( theta ) * r, MathF.Sin( theta ) * r, 0f );
			if ( placed.Any( p => p.Distance( pos ) < MinSpacing ) ) continue;
			placed.Add( pos );
			SpawnTree( pos );
		}
	}

	private void SpawnTree( Vector3 footPosition )
	{
		var go = Scene.CreateObject();
		go.Name = "Tree";
		go.WorldPosition = footPosition + Vector3.Up * (Tunables.TreeHeight * 0.5f);
		go.Tags.Add( "tree" );

		go.WorldScale = new Vector3( Tunables.TreeRadius * 2f, Tunables.TreeRadius * 2f, Tunables.TreeHeight );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = new Color( 0.42f, 0.30f, 0.18f, 1f );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = Vector3.One;

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.TreeMass;
		rb.AngularDamping = 1.2f;
		rb.LinearDamping = 0.3f;
		rb.StartAsleep = true;

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
	}
}
