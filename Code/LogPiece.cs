namespace TreeChopping;

public sealed class LogPiece : Component, IChoppable
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public Color TrunkTint { get; set; } = new( 0.46f, 0.32f, 0.22f, 1f );
	[Property] public int ChopsRemaining { get; set; } = 1;

	private bool _broken;

	bool IChoppable.IsValid() => !_broken && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Axe;

	public void Chop( Vector3 direction )
	{
		if ( _broken ) return;
		ChopsRemaining--;

		var hitPoint = WorldPosition + Vector3.Up * 10f;
		var dirFlat = direction.WithZ( 0f );
		dirFlat = dirFlat.LengthSquared > 0.0001f ? dirFlat.Normal : Vector3.Forward;
		var count = ChopsRemaining > 0 ? Tunables.ChipBurstCountWood : Tunables.ChipBurstCountWoodHeavy;
		var speed = ChopsRemaining > 0 ? Tunables.ChipSpeedWood : Tunables.ChipSpeedWoodHeavy;
		ChopParticles.Burst( Scene, hitPoint, dirFlat, TrunkTint, count, speed );
		AudioBank.PlayChopWood( Scene, hitPoint );

		if ( ChopsRemaining > 0 )
		{
			if ( Body.IsValid() )
			{
				Body.ApplyImpulseAt( hitPoint, direction.WithZ( 0.3f ).Normal * 30f );
			}
			return;
		}
		BreakIntoChunks( direction );
	}

	private void BreakIntoChunks( Vector3 direction )
	{
		_broken = true;
		AudioBank.PlayLogBreak( Scene, WorldPosition );
		// Splinter burst visuel pour marquer le shatter — distinct du chunk
		// spawn (qui est gameplay/pickup), c'est purement particle effect.
		ChopParticles.SplinterBurst( Scene, WorldPosition, direction.WithZ( 0.4f ).Normal, TrunkTint, 10, 280f );
		ComboTracker.Get( Scene )?.AddTrauma( 0.10f );
		for ( int i = 0; i < Tunables.ChunksPerLogPiece; i++ )
		{
			SpawnChunk( direction );
		}
		GameObject.Destroy();
	}

	private void SpawnChunk( Vector3 direction )
	{
		var go = Scene.CreateObject();
		go.Name = "WoodChunk";
		go.WorldPosition = WorldPosition + Vector3.Random * 20f + Vector3.Up * 10f;
		// Size jitter ±35% pour chaque chunk — flat slab uniform devient une
		// scatter de morceaux de tailles différentes, lit comme "bois éclaté".
		float chunkJitter = Game.Random.Float( 0.65f, 1.35f );
		float zJitter = Game.Random.Float( 0.80f, 1.45f );
		go.WorldScale = new Vector3( Tunables.ChunkRadius * 2f * chunkJitter, Tunables.ChunkRadius * 2f * chunkJitter, Tunables.ChunkHeight * zJitter ) / Tunables.CubeBase;
		go.WorldRotation = Rotation.Random;
		go.Tags.Add( "wood_chunk" );

		// Chunks stay as Model.Cube — they're small enough that the Kenney log
		// silhouette would be visual overkill, and the tinted cube reads
		// clearly as "wood chip" against the ground.
		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = TrunkTint;

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.ChunkMass;
		rb.LinearDamping = 0.6f;
		rb.AngularDamping = 1.2f;
		rb.ApplyImpulse( (direction + Vector3.Up * 0.6f + Vector3.Random * 0.3f).Normal * Tunables.ChunkMass * 4f );
		rb.ApplyTorque( Vector3.Random * 120f );

		go.AddComponent<WoodChunk>();
	}
}
