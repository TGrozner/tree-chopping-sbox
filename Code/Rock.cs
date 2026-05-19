namespace TreeChopping;

public sealed class Rock : Component, IChoppable
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public Color RockTint { get; set; } = new( 0.55f, 0.55f, 0.58f, 1f );
	[Property] public int ChopsRemaining { get; set; } = Tunables.RockChops;

	private bool _broken;

	bool IChoppable.IsValid() => !_broken && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Pickaxe;

	public void Chop( Vector3 direction )
	{
		if ( _broken ) return;
		ChopsRemaining--;
		if ( ChopsRemaining > 0 )
		{
			if ( Body.IsValid() )
			{
				var hitPoint = WorldPosition + Vector3.Up * 8f;
				Body.ApplyImpulseAt( hitPoint, direction.WithZ( 0.1f ).Normal * 30f );
			}
			return;
		}
		ShatterIntoChunks( direction );
	}

	private void ShatterIntoChunks( Vector3 direction )
	{
		_broken = true;
		for ( int i = 0; i < Tunables.StonesPerRock; i++ )
		{
			SpawnStoneChunk( direction );
		}
		GameObject.Destroy();
	}

	private void SpawnStoneChunk( Vector3 direction )
	{
		var go = Scene.CreateObject();
		go.Name = "StoneChunk";
		go.WorldPosition = WorldPosition + Vector3.Random * 14f + Vector3.Up * 12f;
		go.WorldScale = new Vector3( Tunables.StoneChunkRadius * 2f, Tunables.StoneChunkRadius * 2f, Tunables.StoneChunkHeight ) / Tunables.CubeBase;
		go.Tags.Add( "stone_chunk" );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = RockTint;

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.StoneChunkMass;
		rb.LinearDamping = 0.6f;
		rb.AngularDamping = 1.2f;
		rb.ApplyImpulse( (direction + Vector3.Up * 0.7f + Vector3.Random * 0.3f).Normal * Tunables.StoneChunkMass * 4f );
		rb.ApplyTorque( Vector3.Random * 90f );

		go.AddComponent<StoneChunk>();
	}
}
