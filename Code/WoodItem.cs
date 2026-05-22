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

	public static WoodItem SpawnAt( Scene scene, Vector3 pos, float scaleMul = 1f, WoodType type = WoodType.Wood )
	{
		return SpawnAt( scene, pos, scaleMul, type, Vector3.Zero );
	}

	public static WoodItem SpawnAt( Scene scene, Vector3 pos, float scaleMul, WoodType type, Vector3 burstDir )
	{
		var go = scene.CreateObject();
		go.Name = "WoodItem";
		// Slight upward kick + horizontal jitter so the burst feels organic.
		go.WorldPosition = pos + Vector3.Up * Game.Random.Float( 6f, 14f );
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

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 0.5f;
		rb.LinearDamping = 0.4f;
		rb.AngularDamping = 1.0f;
		rb.MotionEnabled = true;

		var item = go.AddComponent<WoodItem>();
		item.Body = rb;
		item.Type = type;
		// TimeSince default value = Time.Now (le struct interne stocke t=0,
		// donc (float)t = Time.Now - 0 = un grand nombre). Reset explicit ici
		// pour que _timeSinceSpawn track le délai DEPUIS spawn comme attendu.
		item._timeSinceSpawn = 0f;
		// Initial upward + outward velocity so items burst from the log.
		if ( rb.PhysicsBody.IsValid() )
		{
			var burst = new Vector3(
				Game.Random.Float( -120f, 120f ),
				Game.Random.Float( -120f, 120f ),
				Game.Random.Float( 180f, 320f ) );
			if ( burstDir.LengthSquared > 0.001f )
			{
				burst += burstDir.Normal * Game.Random.Float( 90f, 180f );
			}
			rb.Velocity = burst;
			rb.AngularVelocity = new Vector3(
				Game.Random.Float( -8f, 8f ),
				Game.Random.Float( -8f, 8f ),
				Game.Random.Float( -8f, 8f ) );
		}
		Sfx.Play( "sounds/wood_drop.sound", go.WorldPosition, volume: 0.25f, pitchMin: 0.85f, pitchMax: 1.20f );
		return item;
	}

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

		var toPlayer = (_axe.WorldPosition + Vector3.Up * (Tunables.PlayerEyeHeight * 0.4f)) - WorldPosition;
		float dist = toPlayer.Length;

		// Magnetic flight kicks in inside MagnetRange ; once magnetized stays
		// so until consumed (no flicker if player walks back and forth past
		// the edge of the radius).
		if ( !_magnetized && dist < Tunables.WoodItemMagnetRange )
		{
			_magnetized = true;
			Sfx.Play( "sounds/wood_magnet.sound", WorldPosition, volume: 0.38f, pitchMin: 1.28f, pitchMax: 1.62f );
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
			var dir = toPlayer.Normal;
			Body.Velocity = dir * Tunables.WoodItemMagnetSpeed;
		}

		// Pickup : credit max(1, round(WoodMultiplier)) units + destroy. The
		// axe-tier WoodMul lives at PICKUP time so upgrading mid-flight gives
		// the new value (fine — small edge case).
		if ( dist < Tunables.WoodItemPickupRange )
		{
			var gs = GameState.Get( Scene );
			var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
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
					if ( (float)_timeSinceFullFeedback > 0.7f )
					{
						_timeSinceFullFeedback = 0f;
						if ( hud.IsValid() ) hud.ShowBackpackFullHint();
						Sfx.Play( "sounds/backpack_full.sound", WorldPosition, volume: 0.60f, pitchMin: 0.65f, pitchMax: 0.85f );
					}
					return;
				}
				// Pass type pour que le toast affiche "Wood / Finewood / CoreWood".
				if ( hud.IsValid() ) hud.ShowWoodPickupToast( banked, Type );
			}
			// Tiny "blip" pitch on consume for the satisfaction-tick feel.
			Sfx.Play( "sounds/wood_pickup.sound", WorldPosition,
				volume: 0.46f, pitchMin: 2.15f, pitchMax: 2.42f );
			GameObject?.Destroy();
		}
	}
}
