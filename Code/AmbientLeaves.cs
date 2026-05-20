namespace TreeChopping;

// Ambient drifting leaves across the arena — pure transform-anim cubes, no
// physics. ~60 instances pooled (recycled on Z<10 respawn). Gives the
// otherwise-static arena a sense of wind + scale even when nothing's falling.
public sealed class AmbientLeaves : Component
{
	// Bump 60→100 leaves, lift MaxHeight 540→620 et HorizontalSpeed 30→38 pour
	// que la forêt feel "vivante en permanence". Light meshes (5x5x3 cubes
	// sans collider/rb) → cost négligeable.
	[Property] public int LeafCount { get; set; } = 100;
	[Property] public float MinHeight { get; set; } = 280f;
	[Property] public float MaxHeight { get; set; } = 620f;
	[Property] public float HorizontalSpeed { get; set; } = 38f;
	[Property] public float FallSpeed { get; set; } = 18f;
	[Property] public float WindDirChangeFreq { get; set; } = 0.04f; // Hz — full rotation period ~25s

	private GameObject[] _leaves;
	private Random _rng;

	public static AmbientLeaves Get( Scene scene )
		=> scene?.GetAllComponents<AmbientLeaves>().FirstOrDefault();

	protected override void OnStart()
	{
		_rng = new Random( 0x1EAF50 );
		_leaves = new GameObject[LeafCount];
		for ( int i = 0; i < LeafCount; i++ )
		{
			_leaves[i] = SpawnLeaf();
		}
	}

	protected override void OnUpdate()
	{
		// Global wind direction rotates slowly so the drift feels organic.
		float t = Time.Now * MathF.Tau * WindDirChangeFreq;
		var wind = new Vector3( MathF.Cos( t ), MathF.Sin( t ), 0f ) * HorizontalSpeed;

		for ( int i = 0; i < LeafCount; i++ )
		{
			var leaf = _leaves[i];
			if ( !leaf.IsValid() )
			{
				_leaves[i] = SpawnLeaf();
				continue;
			}
			var p = leaf.WorldPosition;
			p += wind * Time.Delta;
			p.z -= FallSpeed * Time.Delta;
			if ( p.z < 10f )
			{
				// Respawn at the top of the wind column at a fresh random XY.
				p = RandomDiscPos( MaxHeight );
				// Re-tint pour matcher le biome courant — la swarm transitionne
				// naturellement vers la nouvelle palette quand biome avance.
				RetintLeaf( leaf );
			}
			leaf.WorldPosition = p;
			// Gentle yaw drift so it doesn't read as a flat sheet.
			leaf.WorldRotation = leaf.WorldRotation * Rotation.FromYaw( 60f * Time.Delta );
		}
	}

	// Apply current biome tint to an existing leaf — called on recycle so the
	// drifting swarm slowly adopts the new biome palette after AdvanceBiome.
	private void RetintLeaf( GameObject leaf )
	{
		var mr = leaf.Components.Get<ModelRenderer>();
		if ( !mr.IsValid() ) return;
		float h = (float)_rng.NextDouble();
		var biome = BiomeManager.Get( Scene );
		Color paletteMin, paletteMax;
		switch ( biome?.Current )
		{
			case BiomeKind.Autumn:
				paletteMin = new Color( 0.82f, 0.45f, 0.10f, 1f );
				paletteMax = new Color( 0.95f, 0.65f, 0.25f, 1f );
				break;
			case BiomeKind.Frost:
				paletteMin = new Color( 0.78f, 0.88f, 0.95f, 1f );
				paletteMax = new Color( 0.95f, 0.97f, 1.0f, 1f );
				break;
			default:
				paletteMin = new Color( 0.55f, 0.60f, 0.18f, 1f );
				paletteMax = new Color( 0.78f, 0.82f, 0.32f, 1f );
				break;
		}
		mr.Tint = Color.Lerp( paletteMin, paletteMax, h );
	}

	private GameObject SpawnLeaf()
	{
		var go = Scene.CreateObject();
		go.Name = "AmbientLeaf";
		go.Tags.Add( "ambient_leaf" );
		go.WorldPosition = RandomDiscPos( MathX.Lerp( MinHeight, MaxHeight, (float)_rng.NextDouble() ) );
		go.WorldScale = new Vector3( 5f, 5f, 3f ) / Tunables.CubeBase;
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		// Tint suit le biome courant : Forest=vert, Autumn=orange-rouge, Frost=blanc-bleu.
		// Avec hue jitter ±20% pour que le swarm ne lise pas comme un slab uni.
		float h = (float)_rng.NextDouble();
		var biome = BiomeManager.Get( Scene );
		Color paletteMin, paletteMax;
		switch ( biome?.Current )
		{
			case BiomeKind.Autumn:
				paletteMin = new Color( 0.82f, 0.45f, 0.10f, 1f );  // orange brûlé
				paletteMax = new Color( 0.95f, 0.65f, 0.25f, 1f );  // jaune doré
				break;
			case BiomeKind.Frost:
				paletteMin = new Color( 0.78f, 0.88f, 0.95f, 1f );  // bleu glacé
				paletteMax = new Color( 0.95f, 0.97f, 1.0f, 1f );   // blanc
				break;
			default: // Forest (also fallback)
				paletteMin = new Color( 0.55f, 0.60f, 0.18f, 1f );  // vert sombre
				paletteMax = new Color( 0.78f, 0.82f, 0.32f, 1f );  // vert-jaune clair
				break;
		}
		mr.Tint = Color.Lerp( paletteMin, paletteMax, h );
		return go;
	}

	private Vector3 RandomDiscPos( float z )
	{
		// √u disc sample inside the arena.
		float r = MathF.Sqrt( (float)_rng.NextDouble() ) * Tunables.ArenaRadius * 0.95f;
		float angle = (float)(_rng.NextDouble() * MathF.Tau);
		return new Vector3( MathF.Cos( angle ) * r, MathF.Sin( angle ) * r, z );
	}
}
