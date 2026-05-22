namespace TreeChopping;

// Cheap impact chips — N tinted cubes with custom-integrated physics owned by
// Chip.OnUpdate. No Rigidbody / no broadphase / no PhysicsBody — at cascade
// peak the Rigidbody variant of this hit 9k+ active bodies and tanked FPS to
// 27/1. Custom transforms hold ≥30 FPS for the same visual.
public static class ChipBurst
{
	// Default tint = Tunables.ChipTint (oak-brown). Per-tree-kind overrides
	// passées par la Tree au moment du chop pour matcher la couleur du bois
	// chopped (Valheim chips reflect le wood type frappé).
	public static void Spawn( Scene scene, Vector3 worldPos, Vector3 awayDir, int count, Color? chipTint = null, Color? splinterTint = null )
	{
		if ( scene is null ) return;
		var dir = awayDir.WithZ( 0f );
		dir = dir.LengthSquared > 0.0001f ? dir.Normal : Vector3.Forward;
		// Sideways axis = perpendicular to dir in the horizontal plane. Used
		// for the splinter pattern that scatters left/right at impact.
		var side = Vector3.Cross( dir, Vector3.Up ).Normal;

		var actualChipTint = chipTint ?? Tunables.ChipTint;
		var actualSplinterTint = splinterTint ?? Tunables.ChipSplinterTint;
		for ( int i = 0; i < count; i++ ) SpawnChip( scene, worldPos, dir, actualChipTint );
		for ( int i = 0; i < Tunables.ChipSplinterCount; i++ )
			SpawnSplinter( scene, worldPos, dir, side, i % 2 == 0 ? 1f : -1f, actualSplinterTint );
	}

	// Leaf burst — small flat green flakes kicked upward, drifting down with
	// low gravity. Triggered when a tree starts falling so the canopy "sheds"
	// visibly. Different shape + lighter physics than wood chips.
	public static void SpawnLeaves( Scene scene, Vector3 worldPos, Vector3 awayDir, int count, Color tint )
	{
		if ( scene is null ) return;
		var dir = awayDir.WithZ( 0f );
		dir = dir.LengthSquared > 0.0001f ? dir.Normal : Vector3.Forward;
		for ( int i = 0; i < count; i++ )
			SpawnLeaf( scene, worldPos, dir, tint );
	}

	private static void SpawnLeaf( Scene scene, Vector3 pos, Vector3 dir, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = "Leaf";
		go.WorldPosition = pos + Vector3.Random * 8f;
		go.WorldRotation = Rotation.Random;
		float size = Game.Random.Float( 4f, 7f );
		go.WorldScale = new Vector3( size, size * 1.4f, size * 0.35f ) / Tunables.CubeBase;
		var jitter = Game.Random.Float( 0.85f, 1.15f );
		var t = new Color( (tint.r * jitter).Clamp( 0f, 1f ), (tint.g * jitter).Clamp( 0f, 1f ), (tint.b * jitter).Clamp( 0f, 1f ), 1f );
		Mat.AddTintedCube( go, t );

		var chip = go.AddComponent<Chip>();
		var kick = (Vector3.Up * 1.0f + dir * 0.35f + Vector3.Random * 0.5f).Normal;
		chip.Velocity = kick * Tunables.ChipSpeed * 0.55f * Game.Random.Float( 0.7f, 1.3f );
		chip.SpinAxis = Vector3.Random.Normal;
		chip.SpinSpeed = Game.Random.Float( -300f, 300f );
		chip.Lifetime = Game.Random.Float( 2.2f, 3.6f );
	}

	private static void SpawnChip( Scene scene, Vector3 pos, Vector3 dir, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = "Chip";
		go.WorldPosition = pos + Vector3.Random * 2f;
		go.WorldRotation = Rotation.Random;
		var size = Game.Random.Float( Tunables.ChipSizeMin, Tunables.ChipSizeMax );
		go.WorldScale = new Vector3( size ) / Tunables.CubeBase;
		Mat.AddTintedCube( go, tint );

		var chip = go.AddComponent<Chip>();
		var kick = (Vector3.Up + (-dir) * 0.6f + Vector3.Random * 0.5f).Normal;
		chip.Velocity = kick * Tunables.ChipSpeed * Game.Random.Float( 0.7f, 1.3f );
		chip.SpinAxis = Vector3.Random.Normal;
		chip.SpinSpeed = Game.Random.Float( -540f, 540f );
		chip.Lifetime = Game.Random.Float( Tunables.ChipLifetime * 0.7f, Tunables.ChipLifetime * 1.2f );
	}

	private static void SpawnSplinter( Scene scene, Vector3 pos, Vector3 dir, Vector3 side, float sideSign, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = "Splinter";
		go.WorldPosition = pos + Vector3.Random * 2f;
		go.WorldRotation = Rotation.Random;
		float len = Game.Random.Float( 12f, 18f );
		go.WorldScale = new Vector3( len, 2.5f, 2.5f ) / Tunables.CubeBase;
		Mat.AddTintedCube( go, tint );

		var chip = go.AddComponent<Chip>();
		var kick = (side * sideSign * 0.85f + Vector3.Up * 0.3f + (-dir) * 0.2f + Vector3.Random * 0.35f).Normal;
		chip.Velocity = kick * Tunables.ChipSpeed * Game.Random.Float( 0.9f, 1.4f );
		chip.SpinAxis = Vector3.Random.Normal;
		chip.SpinSpeed = Game.Random.Float( -1080f, 1080f );
		chip.Lifetime = Game.Random.Float( Tunables.ChipLifetime * 0.8f, Tunables.ChipLifetime * 1.3f );
	}
}

public sealed class Chip : Component
{
	public Vector3 Velocity;
	public Vector3 SpinAxis = Vector3.Up;
	public float SpinSpeed;
	public float Lifetime = 1.5f;

	private TimeSince _born;
	private bool _resting;
	private const float Gravity = 800f;

	protected override void OnStart() => _born = 0f;

	protected override void OnUpdate()
	{
		float t = _born;
		if ( t >= Lifetime ) { GameObject.Destroy(); return; }
		if ( _resting ) return;

		float dt = MathF.Min( Time.Delta, 0.05f );
		Velocity *= MathF.Exp( -0.5f * dt );
		SpinSpeed *= MathF.Exp( -0.5f * dt );
		Velocity = Velocity.WithZ( Velocity.z - Gravity * dt );

		var p = WorldPosition + Velocity * dt;
		float groundZ = FindGroundZ( p );
		if ( p.z <= groundZ )
		{
			p.z = groundZ;
			Velocity = Velocity.WithZ( 0f ) * 0.3f;
			SpinSpeed *= 0.4f;
			if ( Velocity.LengthSquared < 25f ) _resting = true;
		}
		WorldPosition = p;

		if ( SpinAxis.LengthSquared > 0.001f && MathF.Abs( SpinSpeed ) > 0.5f )
			WorldRotation = Rotation.FromAxis( SpinAxis, SpinSpeed * dt ) * WorldRotation;
	}

	private float FindGroundZ( Vector3 pos )
	{
		if ( Scene is null ) return Tunables.GroundZ;
		var hit = Scene.Trace.Ray( pos + Vector3.Up * 18f, pos - Vector3.Up * 120f )
			.WithAnyTags( "ground" )
			.Run();
		return hit.Hit ? hit.EndPosition.z : Tunables.GroundZ;
	}
}
