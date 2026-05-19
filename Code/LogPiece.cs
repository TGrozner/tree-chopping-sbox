namespace TreeChopping;

public sealed class LogPiece : Component, IChoppable
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public Color TrunkTint { get; set; } = new( 0.46f, 0.32f, 0.22f, 1f );
	[Property] public int ChopsRemaining { get; set; } = 1;

	private bool _broken;

	bool IChoppable.IsValid() => !_broken && this.IsValid();

	public void Chop( Vector3 direction )
	{
		if ( _broken ) return;
		ChopsRemaining--;
		if ( ChopsRemaining > 0 )
		{
			if ( Body.IsValid() )
			{
				Body.ApplyImpulseAt( WorldPosition + Vector3.Up * 10f, direction.WithZ( 0.3f ).Normal * 30f );
			}
			return;
		}
		BreakIntoChunks( direction );
	}

	private void BreakIntoChunks( Vector3 direction )
	{
		_broken = true;
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
		go.WorldScale = new Vector3( Tunables.ChunkRadius * 2f, Tunables.ChunkRadius * 2f, Tunables.ChunkHeight );
		go.Tags.Add( "wood_chunk" );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = TrunkTint;

		var col = go.AddComponent<BoxCollider>();
		col.Scale = Vector3.One;

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.ChunkMass;
		rb.LinearDamping = 0.6f;
		rb.AngularDamping = 1.2f;
		rb.ApplyImpulse( (direction + Vector3.Up * 0.6f + Vector3.Random * 0.3f).Normal * Tunables.ChunkMass * 4f );
		rb.ApplyTorque( Vector3.Random * 120f );

		go.AddComponent<WoodChunk>();
	}
}
