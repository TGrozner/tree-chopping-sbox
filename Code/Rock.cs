namespace TreeChopping;

public sealed class Rock : Component, IChoppable
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public Color RockTint { get; set; } = new( 0.55f, 0.55f, 0.58f, 1f );
	[Property] public int ChopsRemaining { get; set; } = Tunables.RockChops;

	private bool _broken;

	bool IChoppable.IsValid() => !_broken && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Pickaxe;

	// Mirrors SceneStarter.SpawnRock so RockStump regrowth can recreate a rock
	// with the original tint. Duplication with SceneStarter is intentional for
	// this commit — a parallel agent owns SceneStarter.cs.
	// Visual scale for Kenney rock_small*.vmdl. The source meshes are authored
	// at ~1m so without a multiplier they read as pebbles next to the beaver.
	// 30x lands them in the same silhouette band as the old cube (RockRadius=24,
	// RockHeight=40) while keeping a natural rock proportion.
	public const float RockModelScale = 30f;

	public static Rock SpawnAt( Scene scene, Vector3 footPosition, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = "Rock";
		go.WorldPosition = footPosition + Vector3.Up * (Tunables.RockHeight * 0.5f);
		go.Tags.Add( "rock" );

		// Per-position deterministic variant pick — same world spot always picks
		// the same rock silhouette across hotloads / regrowth so the world reads
		// stable rather than randomly reshuffling on each respawn.
		var seed = footPosition.GetHashCode();
		go.WorldScale = new Vector3( RockModelScale );
		go.WorldRotation = Rotation.FromYaw( (seed & 0xFFFF) * 0.0055f );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Models.RockVariant( Math.Abs( seed ) );
		model.Tint = tint;

		// Collider footprint stays cube-shaped — Kenney rocks are visually
		// blobby but a box read is cheap and matches the old gameplay feel.
		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.RockRadius * 2f, Tunables.RockRadius * 2f, Tunables.RockHeight ) / RockModelScale;

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.RockMass;
		rb.AngularDamping = 2f;
		rb.LinearDamping = 0.6f;
		rb.StartAsleep = true;

		var rock = go.AddComponent<Rock>();
		rock.Body = rb;
		rock.RockTint = tint;
		return rock;
	}

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
		// Leave a stump that will regrow into a fresh rock. Rocks are static so
		// WorldPosition is still the spawn-time center — subtract half-height to
		// get the foot plane that SpawnAt expects.
		RockStump.SpawnAt( Scene, WorldPosition - Vector3.Up * (Tunables.RockHeight * 0.5f), RockTint );
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
