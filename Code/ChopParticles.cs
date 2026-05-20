namespace TreeChopping;

// Cheap chop-impact burst: spawns N tinted cubes with random impulses biased
// along `forward` + a small upward kick. Ports the Godot proto's `_emit_chips`
// / `_spawn_chip_burst` (CPUParticles3D BoxMesh 0.07m, amount 18, lifetime 0.7s,
// gravity -12). No collider on chips so they don't interfere with player or
// each other — purely visual confetti.
//
// Three variants:
// - `Burst` — square cubes, "chips/leaves" feel, balanced kick.
// - `SplinterBurst` — long thin bars, "bark shards" feel, faster + flatter trajectory.
// - `LeafTrail` — tiny soft scatter, "leaves shedding from the canopy" trail.
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

	// Long thin bark splinters — flatter, faster, less upward kick. Reads as
	// "the trunk cracked" vs Burst which reads as "wood pulp flew off".
	public static void SplinterBurst( Scene scene, Vector3 worldPos, Vector3 forward, Color tint, int count, float speed )
	{
		if ( scene is null ) return;
		var dir = forward.WithZ( 0f );
		dir = dir.LengthSquared > 0.0001f ? dir.Normal : Vector3.Forward;

		for ( int i = 0; i < count; i++ )
		{
			SpawnSplinter( scene, worldPos, dir, tint, speed );
		}
	}

	// Trail of small leaves drifting off a falling/rolling tree. Lighter,
	// slower, no gravity — they float and decay. Use sparingly per tick.
	public static void LeafTrail( Scene scene, Vector3 worldPos, Color tint, int count, float speed )
	{
		if ( scene is null ) return;
		for ( int i = 0; i < count; i++ )
		{
			SpawnLeafFloater( scene, worldPos, tint, speed );
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
		// Per-chip palette mixing — brightness + occasional accent variant
		// (green leaf, golden splinter, dark bark). Donne au burst un mix
		// "bois + feuilles + écorce" au lieu d'un flat slab uniform.
		Color baseColor;
		float roll = Game.Random.Float( 0f, 1f );
		if ( roll < 0.55f )
		{
			// 55% — chip color with brightness jitter (default wood).
			var jitter = Game.Random.Float( 0.80f, 1.15f );
			baseColor = new Color( tint.r * jitter, tint.g * jitter, tint.b * jitter, tint.a );
		}
		else if ( roll < 0.78f )
		{
			// 23% — bright golden splinter (catches light, reads as fresh wood pulp).
			baseColor = new Color( 0.85f, 0.70f, 0.42f, 1f );
		}
		else if ( roll < 0.92f )
		{
			// 14% — dark bark chunk for contrast.
			baseColor = new Color( 0.22f, 0.14f, 0.08f, 1f );
		}
		else
		{
			// 8% — green leaf accent (cascades drop a little canopy with each hit).
			baseColor = new Color( 0.38f, 0.62f, 0.22f, 1f );
		}
		mr.Tint = new Color( baseColor.r.Clamp(0f,1f), baseColor.g.Clamp(0f,1f), baseColor.b.Clamp(0f,1f), baseColor.a );

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

	private static void SpawnSplinter( Scene scene, Vector3 worldPos, Vector3 forward, Color tint, float speed )
	{
		var go = scene.CreateObject();
		go.Name = "BarkSplinter";
		go.WorldPosition = worldPos + Vector3.Random * 3f;
		go.WorldRotation = Rotation.Random;

		// Long thin rectangle : ~12u × 2.5u × 2.5u. Reads as a bark shard.
		float lenW = Game.Random.Float( 10f, 16f );
		go.WorldScale = new Vector3( lenW, 2.5f, 2.5f ) / Tunables.CubeBase;

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		var jitter = Game.Random.Float( 0.75f, 1.05f );
		mr.Tint = new Color( (tint.r * jitter).Clamp( 0f, 1f ), (tint.g * jitter).Clamp( 0f, 1f ), (tint.b * jitter).Clamp( 0f, 1f ), 1f );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 0.10f;
		rb.LinearDamping = 0.25f;
		rb.AngularDamping = 0.20f;
		rb.Gravity = true;

		// Flatter kick than Burst — splinters fly outward more, upward less.
		var kick = (forward * 0.85f + Vector3.Up * 0.25f + Vector3.Random * 0.5f).Normal;
		var spd = speed * Game.Random.Float( 0.85f, 1.4f );
		rb.ApplyImpulse( kick * spd * rb.PhysicsBody.Mass );
		// Strong tumble — splinters spin end-over-end visibly.
		rb.ApplyTorque( Vector3.Random * 180f );

		var life = go.AddComponent<ChopChipLifetime>();
		life.Lifetime = Game.Random.Float( Tunables.ChipLifetimeMin * 1.3f, Tunables.ChipLifetimeMax * 1.4f );
	}

	private static void SpawnLeafFloater( Scene scene, Vector3 worldPos, Color tint, float speed )
	{
		var go = scene.CreateObject();
		go.Name = "LeafFloater";
		go.WorldPosition = worldPos + Vector3.Random.WithZ( 0f ) * 6f;
		go.WorldRotation = Rotation.Random;

		float size = Game.Random.Float( 2.5f, 4.5f );
		// Slightly flat (thin Z) so it reads as a leaf instead of a cube.
		go.WorldScale = new Vector3( size, size, size * 0.35f ) / Tunables.CubeBase;

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		var jitter = Game.Random.Float( 0.85f, 1.1f );
		mr.Tint = new Color( (tint.r * jitter).Clamp( 0f, 1f ), (tint.g * jitter).Clamp( 0f, 1f ), (tint.b * jitter).Clamp( 0f, 1f ), 1f );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 0.02f;
		rb.LinearDamping = 1.8f;     // float-y, lots of drag
		rb.AngularDamping = 1.2f;
		rb.Gravity = true;
		rb.GravityScale = 0.25f;     // 1/4 gravity — they drift down lazily

		var kick = (Vector3.Up * 0.25f + Vector3.Random.WithZ( 0f ) * 0.7f).Normal;
		var spd = speed * Game.Random.Float( 0.6f, 1.1f );
		rb.ApplyImpulse( kick * spd * rb.PhysicsBody.Mass );
		rb.ApplyTorque( Vector3.Random * 30f );

		var life = go.AddComponent<ChopChipLifetime>();
		life.Lifetime = Game.Random.Float( Tunables.ChipLifetimeMax * 0.8f, Tunables.ChipLifetimeMax * 1.6f );
	}
}

// Self-destruct timer so chips don't leak. No collider on the GO means it
// doesn't interact with the player or other physics bodies — Rigidbody still
// falls under gravity, but with nothing to collide with it just arcs and dies.
public sealed class ChopChipLifetime : Component
{
	[Property] public float Lifetime { get; set; } = 0.8f;

	private TimeSince _born;
	private ModelRenderer _mr;
	private Color _baseTint;
	private bool _cached;

	protected override void OnStart() => _born = 0f;

	protected override void OnUpdate()
	{
		if ( _born >= Lifetime ) { GameObject.Destroy(); return; }

		// Fade out over the last 35% of lifetime — chips disparaissent gracieusement
		// au lieu de popper out d'un seul coup. Cache le renderer + tint pour pas
		// re-querier chaque frame (cheap mais N chips peut être 500+ in flight).
		float u = (float)_born / Lifetime;
		if ( u > 0.65f )
		{
			if ( !_cached )
			{
				_mr = GameObject.Components.Get<ModelRenderer>();
				if ( _mr.IsValid() ) _baseTint = _mr.Tint;
				_cached = true;
			}
			if ( _mr.IsValid() )
			{
				float fade = 1f - (u - 0.65f) / 0.35f;
				_mr.Tint = _baseTint.WithAlpha( _baseTint.a * fade );
			}
		}
	}
}
