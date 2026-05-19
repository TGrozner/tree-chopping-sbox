namespace TreeChopping;

// Cheap chop-impact burst: spawns N tinted cubes with random impulses biased
// along `forward` + a small upward kick. Ports the Godot proto's `_emit_chips`
// / `_spawn_chip_burst` (CPUParticles3D BoxMesh 0.07m, amount 18, lifetime 0.7s,
// gravity -12). No collider on chips so they don't interfere with player or
// each other — purely visual confetti.
public static class ChopParticles
{
	public static void Burst( Scene scene, Vector3 worldPos, Vector3 forward, Color tint, int count, float speed )
	{
		if ( scene is null ) return;
		var dir = forward.WithZ( 0f );
		dir = dir.LengthSquared > 0.0001f ? dir.Normal : Vector3.Forward;

		for ( int i = 0; i < count; i++ )
		{
			SpawnChip( scene, worldPos, dir, tint, speed );
		}
	}

	private static void SpawnChip( Scene scene, Vector3 worldPos, Vector3 forward, Color tint, float speed )
	{
		var go = scene.CreateObject();
		go.Name = "ChopChip";
		// Slight spawn jitter so chips don't pop out of one point.
		go.WorldPosition = worldPos + Vector3.Random * 2f;
		go.WorldRotation = Rotation.Random;

		var size = Game.Random.Float( Tunables.ChipSizeMin, Tunables.ChipSizeMax );
		go.WorldScale = new Vector3( size ) / Tunables.CubeBase;

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		// Slight per-chip brightness variation so the burst doesn't read as a flat slab of one color.
		var jitter = Game.Random.Float( 0.85f, 1.1f );
		mr.Tint = new Color( (tint.r * jitter).Clamp( 0f, 1f ), (tint.g * jitter).Clamp( 0f, 1f ), (tint.b * jitter).Clamp( 0f, 1f ), tint.a );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 0.05f;
		rb.LinearDamping = 0.4f;
		rb.AngularDamping = 0.4f;
		rb.Gravity = true;

		// Bias along forward + upward kick (Godot used dir = up + world_dir * 0.5).
		var kick = (Vector3.Up + forward * 0.5f + Vector3.Random * 0.6f).Normal;
		var spd = speed * Game.Random.Float( 0.7f, 1.3f );
		rb.ApplyImpulse( kick * spd * rb.PhysicsBody.Mass );
		rb.ApplyTorque( Vector3.Random * 90f );

		var life = go.AddComponent<ChopChipLifetime>();
		life.Lifetime = Game.Random.Float( Tunables.ChipLifetimeMin, Tunables.ChipLifetimeMax );
	}
}

// Self-destruct timer so chips don't leak. No collider on the GO means it
// doesn't interact with the player or other physics bodies — Rigidbody still
// falls under gravity, but with nothing to collide with it just arcs and dies.
public sealed class ChopChipLifetime : Component
{
	[Property] public float Lifetime { get; set; } = 0.8f;

	private TimeSince _born;

	protected override void OnStart() => _born = 0f;

	protected override void OnUpdate()
	{
		if ( _born >= Lifetime ) GameObject.Destroy();
	}
}
