namespace TreeChopping;

// Valheim wood types — Beech drops Wood, Oak drops Wood+Finewood, Pine drops
// Wood+Core Wood. Index pour Tunables.WoodTypeTints et WoodTypeNames.
public enum WoodType { Wood = 0, Finewood = 1, CoreWood = 2 }

// Pickable wood unit dropped when a landed trunk is fully chopped. Sits as a small
// physics cube on the ground ; flies toward the player on proximity (magnetic
// pickup, Valheim-feel) then absorbs into BackpackByType[Type].
public sealed class WoodItem : Component
{
	[Property] public Rigidbody Body { get; set; }
	// Valheim wood type — détermine couleur, nom du toast, et quel backpack
	// slot le pickup remplit.
	[Property] public WoodType Type { get; set; } = WoodType.Wood;

	private TimeSince _timeSinceSpawn;
	private AxeController _axe;
	private bool _magnetized;
	private TimeSince _timeSinceFullFeedback = 999f;
	private TimeSince _timeSinceFullReject = 999f;
	private bool _debugValheimDrop;
	private Vector3 _debugInitialPosition;
	private Vector3 _debugInitialVelocity;

	public static WoodItem SpawnAt( Scene scene, Vector3 pos, float scaleMul = 1f, WoodType type = WoodType.Wood )
	{
		return SpawnAt( scene, pos, scaleMul, type, Vector3.Zero );
	}

	public static WoodItem SpawnAt( Scene scene, Vector3 pos, float scaleMul, WoodType type, Vector3 burstDir )
	{
		return SpawnInternal( scene, pos, scaleMul, type, burstDir, valheimDrop: false );
	}

	public static WoodItem SpawnValheimDropAt( Scene scene, Vector3 pos, float scaleMul, WoodType type )
	{
		return SpawnInternal( scene, pos, scaleMul, type, Vector3.Zero, valheimDrop: true );
	}

	private static WoodItem SpawnInternal( Scene scene, Vector3 pos, float scaleMul, WoodType type, Vector3 burstDir, bool valheimDrop )
	{
		var go = scene.CreateObject();
		go.Name = "WoodItem";
		go.WorldPosition = valheimDrop ? pos : pos + Vector3.Up * Game.Random.Float( 2f, 5f );
		// Random yaw 0-360° — Valheim DropOnDestroyed.OnDestroyed pattern :
		// `Quaternion.Euler(0, Random.Range(0, 360), 0)` sur chaque drop.
		float yaw = Game.Random.Float( 0f, 360f );
		go.WorldRotation = Rotation.FromYaw( yaw );

		// Items scale par scaleMul (Valheim TreeBase.SpawnLog : `SetLocalScale(transform.localScale)`
		// → log/drops adoptent l'échelle du tree parent). Sapling = small items,
		// veteran = bigger items. Cap [0.5..2.0] pour ne pas casser magnet/pickup ranges.
		float visualScale = scaleMul.Clamp( 0.5f, 2.0f );
		const float size = 14f;
		// Tint depends on type (Wood/Finewood/CoreWood) — Valheim visual differentiation.
		var tint = Tunables.WoodTypeTints[(int)type];
		var mr = Mat.AddTintedCube( go, tint );
		go.WorldScale = new Vector3( size * 1.4f, size, size ) * visualScale / Tunables.CubeBase;

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );
		col.ColliderFlags |= ColliderFlags.IgnoreMass;

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 1.8f;
		rb.LinearDamping = 1.4f;
		rb.AngularDamping = 2.6f;
		rb.MotionEnabled = true;

		var item = go.AddComponent<WoodItem>();
		item.Body = rb;
		item.Type = type;
		item._debugValheimDrop = valheimDrop;
		item._debugInitialPosition = go.WorldPosition;
		// TimeSince default value = Time.Now (le struct interne stocke t=0,
		// donc (float)t = Time.Now - 0 = un grand nombre). Reset explicit ici
		// pour que _timeSinceSpawn track le délai DEPUIS spawn comme attendu.
		item._timeSinceSpawn = 0f;
		if ( rb.PhysicsBody.IsValid() )
		{
			if ( valheimDrop )
			{
				rb.Velocity = Vector3.Zero;
				rb.AngularVelocity = Vector3.Zero;
			}
			else
			{
				var burst = new Vector3(
					Game.Random.Float( -35f, 35f ),
					Game.Random.Float( -35f, 35f ),
					Game.Random.Float( 55f, 115f ) );
				if ( burstDir.LengthSquared > 0.001f )
				{
					burst += burstDir.Normal * Game.Random.Float( 20f, 55f );
				}
				rb.Velocity = burst;
				rb.AngularVelocity = new Vector3(
					Game.Random.Float( -2.5f, 2.5f ),
					Game.Random.Float( -2.5f, 2.5f ),
					Game.Random.Float( -2.5f, 2.5f ) );
			}
			item._debugInitialVelocity = rb.Velocity;
		}
		Sfx.Play( "sounds/wood_drop.sound", go.WorldPosition, volume: 0.25f, pitchMin: 0.85f, pitchMax: 1.20f );
		return item;
	}

	internal bool DebugValheimDrop => _debugValheimDrop;
	internal Vector3 DebugInitialPosition => _debugInitialPosition;
	internal Vector3 DebugInitialVelocity => _debugInitialVelocity;
	internal bool DebugMagnetized => _magnetized;

	protected override void OnUpdate()
	{
		// Despawn timeout — wood the player leaves on the ground eventually
		// vanishes so the world doesn't drown in unclaimed items.
		if ( (float)_timeSinceSpawn > Tunables.WoodItemDespawnDelay )
		{
			GameObject?.Destroy();
			return;
		}

		// Valheim ItemDrop.CanPickup grace : auto-pickup reject 0.5s post-spawn
		// pour laisser le burst animation se voir avant le magnet snap. Pendant
		// la grace, gravity + damping naturels font tomber l'item au sol.
		if ( (float)_timeSinceSpawn < Tunables.WoodItemMagnetGrace ) return;

		_axe ??= Scene?.GetAllComponents<AxeController>().FirstOrDefault();
		if ( !_axe.IsValid() ) return;

		var pickupTarget = _axe.WorldPosition + Vector3.Up * (Tunables.PlayerEyeHeight * 0.4f);
		var toPlayer = pickupTarget - WorldPosition;
		float dist = toPlayer.Length;
		var gs = GameState.Get( Scene );
		var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();

		if ( gs.IsValid() && gs.BackpackFull )
		{
			if ( dist < Tunables.WoodItemMagnetRange )
			{
				if ( _magnetized || dist < Tunables.WoodItemPickupRange * 1.5f )
					ShowFullFeedback( hud );
				RejectFromFullBackpack( pickupTarget, toPlayer, dist );
			}
			return;
		}

		// Magnetic flight kicks in inside MagnetRange ; once magnetized stays
		// so until consumed (no flicker if player walks back and forth past
		// the edge of the radius).
		if ( !_magnetized && dist < Tunables.WoodItemMagnetRange )
		{
			_magnetized = true;
			Sfx.Play( "sounds/wood_magnet.sound", WorldPosition, volume: 0.20f, pitchMin: 1.18f, pitchMax: 1.42f );
			// Disable the rigidbody's gravity influence so the item flies in a
			// straight line toward the player instead of arcing down.
			if ( Body.IsValid() )
			{
				Body.Gravity = false;
				Body.LinearDamping = 6f;
				Body.AngularDamping = 6f;
				Body.AngularVelocity = new Vector3(
					Game.Random.Float( -14f, 14f ),
					Game.Random.Float( -14f, 14f ),
					Game.Random.Float( -14f, 14f ) );
			}
		}

		if ( _magnetized && Body.IsValid() )
		{
			if ( dist > 0.001f )
			{
				var dir = toPlayer.Normal;
				// Valheim moves ItemDrop.transform directly during AutoPickup.
				float step = MathF.Min( Tunables.WoodItemMagnetSpeed * Time.Delta, dist );
				WorldPosition += dir * step;
				toPlayer = pickupTarget - WorldPosition;
				dist = toPlayer.Length;
			}
			Body.Velocity = Vector3.Zero;
			if ( Body.PhysicsBody.IsValid() )
			{
				Body.PhysicsBody.Position = WorldPosition;
				Body.PhysicsBody.Velocity = Vector3.Zero;
			}
		}

		// Pickup : credit max(1, round(WoodMultiplier)) units + destroy. The
		// axe-tier WoodMul lives at PICKUP time so upgrading mid-flight gives
		// the new value (fine — small edge case).
		if ( dist < Tunables.WoodItemPickupRange )
		{
			if ( gs.IsValid() )
			{
				int worth = Math.Max( 1, (int)MathF.Round( gs.WoodMultiplier ) );
				// Valheim multi-type pickup — route vers le backpack correspondant
				// (Wood / Finewood / CoreWood). Cap globale partagée (BackpackCapacity).
				int banked = gs.AddBackpack( worth, Type );
				if ( banked == 0 )
				{
					// Backpack full — show the warning, bail without consuming
					// so the item lingers until the player flushes the bag.
					ShowFullFeedback( hud );
					RejectFromFullBackpack( pickupTarget, toPlayer, dist );
					return;
				}
				// Pass type pour que le toast affiche "Wood / Finewood / CoreWood".
				if ( hud.IsValid() ) hud.ShowWoodPickupToast( banked, Type );
			}
			// Tiny "blip" pitch on consume for the satisfaction-tick feel.
			Sfx.Play( "sounds/wood_pickup.sound", WorldPosition,
				volume: 0.34f, pitchMin: 1.85f, pitchMax: 2.15f );
			GameObject?.Destroy();
		}
	}

	private void ShowFullFeedback( WoodHud hud )
	{
		if ( (float)_timeSinceFullFeedback <= 0.7f ) return;
		_timeSinceFullFeedback = 0f;
		if ( hud.IsValid() ) hud.ShowBackpackFullHint();
		Sfx.Play( "sounds/backpack_full.sound", WorldPosition, volume: 0.60f, pitchMin: 0.65f, pitchMax: 0.85f );
	}

	private void RejectFromFullBackpack( Vector3 pickupTarget, Vector3 toPlayer, float dist )
	{
		if ( !_magnetized && dist > Tunables.WoodItemFullRejectDistance ) return;
		if ( (float)_timeSinceFullReject <= Tunables.WoodItemFullRejectCooldown ) return;
		_timeSinceFullReject = 0f;
		_magnetized = false;

		var away = (-toPlayer).WithZ( 0f );
		if ( away.LengthSquared < 0.001f )
			away = new Vector3( Game.Random.Float( -1f, 1f ), Game.Random.Float( -1f, 1f ), 0f );
		if ( away.LengthSquared < 0.001f ) away = Vector3.Forward;
		away = away.Normal;

		if ( dist < Tunables.WoodItemFullRejectDistance )
			WorldPosition = pickupTarget + away * Tunables.WoodItemFullRejectDistance - Vector3.Up * (Tunables.PlayerEyeHeight * 0.2f);

		if ( !Body.IsValid() ) return;
		Body.MotionEnabled = true;
		Body.Gravity = true;
		Body.LinearDamping = 1.4f;
		Body.AngularDamping = 2.6f;
		Body.Velocity = away * Tunables.WoodItemFullRejectSpeed + Vector3.Up * Tunables.WoodItemFullRejectUpSpeed;
		Body.AngularVelocity = new Vector3(
			Game.Random.Float( -3f, 3f ),
			Game.Random.Float( -3f, 3f ),
			Game.Random.Float( -3f, 3f ) );
		if ( Body.PhysicsBody.IsValid() )
		{
			Body.PhysicsBody.Position = WorldPosition;
			Body.PhysicsBody.Velocity = Body.Velocity;
			Body.PhysicsBody.AngularVelocity = Body.AngularVelocity;
		}
	}
}
