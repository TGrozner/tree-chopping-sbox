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

		var hitPoint = WorldPosition + Vector3.Up * 8f;
		var dirFlat = direction.WithZ( 0f );
		dirFlat = dirFlat.LengthSquared > 0.0001f ? dirFlat.Normal : Vector3.Forward;
		// Stone dust = rock tint pushed brighter so it reads vs the rock body.
		var dust = new Color(
			MathF.Min( 1f, RockTint.r * 1.3f + 0.1f ),
			MathF.Min( 1f, RockTint.g * 1.3f + 0.1f ),
			MathF.Min( 1f, RockTint.b * 1.3f + 0.1f ),
			1f );
		ChopParticles.Burst( Scene, hitPoint, dirFlat, dust, Tunables.ChipBurstCountStone, Tunables.ChipSpeedStone );
		AudioBank.PlayChopStone( Scene, hitPoint );

		if ( ChopsRemaining > 0 )
		{
			if ( Body.IsValid() )
			{
				Body.ApplyImpulseAt( hitPoint, direction.WithZ( 0.1f ).Normal * 30f );
			}
			return;
		}
		ShatterIntoChunks( direction );
	}

	private void ShatterIntoChunks( Vector3 direction )
	{
		_broken = true;
		// Reuse log-break sound — a dedicated rock-shatter asset is future work.
		AudioBank.PlayLogBreak( Scene, WorldPosition );
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
