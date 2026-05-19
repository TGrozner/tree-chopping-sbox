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
		go.WorldScale = new Vector3( Tunables.ChunkRadius * 2f, Tunables.ChunkRadius * 2f, Tunables.ChunkHeight ) / Tunables.CubeBase;
		go.Tags.Add( "wood_chunk" );

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
