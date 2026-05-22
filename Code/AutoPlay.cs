namespace TreeChopping;

// Headless-friendly autoplay driver. Toggle Active=true to make the player
// walk to the nearest standing tree, split the landed log, collect, sell,
// upgrade, then repeat.
// Used both by gameplay validation runs (bridge sets Active=true, frames
// captured by screenshot loop) and as a stress harness for cascade physics.
public sealed class AutoPlay : Component
{
	[Property] public new bool Active { get; set; }
	[Property] public bool LookBack { get; set; }
	[Property, ReadOnly] public string CurrentAction { get; set; } = "idle";
	[Property, ReadOnly] public int TreesFelled { get; set; }

	public static bool IsAnyActive( Scene scene )
		=> scene?.GetAllComponents<AutoPlay>().Any( a => a.IsValid() && a.Active ) == true;

	private AxeController _axe;
	private Tree _target;
	private Vector3 _targetPos;
	private bool _countedTargetFell;
	private TimeSince _sinceLastSwing = 999f;
	private TimeSince _sinceLastPickupMove = 999f;
	private TimeSince _sinceShopArrived = 999f;
	private bool _wasActive;
	private int _step;
	private const int StepPickTarget = 0;
	private const int StepApproach = 1;
	private const int StepSwing = 2;
	private const int StepCollectDrops = 3;
	private const int StepGoShop = 4;
	private const int StepBuyShop = 5;

	protected override void OnUpdate()
	{
		_axe ??= Scene?.GetAllComponents<AxeController>().FirstOrDefault();

		if ( LookBack && _axe.IsValid() )
		{
			// One-shot : yaw the player in place to face the shop spawn point.
			// No teleport, no vertical movement — just rotation so the camera
			// (third-person, follows player yaw) frames the totem behind.
			var spawn = Scene.GetAllComponents<SceneStarter>().FirstOrDefault()?.ResolvedPlayerSpawn ?? Vector3.Zero;
			var dir = (spawn - _axe.WorldPosition).WithZ( 0f );
			if ( dir.LengthSquared > 1f )
			{
				float yaw = Rotation.LookAt( dir.Normal ).Yaw();
				_axe.TeleportTo( _axe.WorldPosition, yaw );
				CurrentAction = "looking back at shop";
			}
			LookBack = false;
		}

		if ( !Active )
		{
			_wasActive = false;
			if ( CurrentAction != "looking back at shop" ) CurrentAction = "idle";
			return;
		}
		if ( !_wasActive )
		{
			_wasActive = true;
			_target = null;
			_step = StepPickTarget;
		}

		if ( !_axe.IsValid() ) { CurrentAction = "no player"; return; }

		switch ( _step )
		{
			case StepPickTarget: TickPickTarget(); break;
			case StepApproach: TickApproach(); break;
			case StepSwing: TickSwing(); break;
			case StepCollectDrops: TickCollectDrops(); break;
			case StepGoShop: TickGoShop(); break;
			case StepBuyShop: TickBuyShop(); break;
		}
	}

	private void TickPickTarget()
	{
		// Full-loop driver : if prestige is available, detour to the shop and
		// take it (resets tiers but +1%/spirit wood is the long-term win).
		// Otherwise, detour to buy the cheapest affordable upgrade.
		var gs = GameState.Get( Scene );
		if ( gs.IsValid() && gs.CanPrestige() )
		{
			CurrentAction = $"prestige ready ({gs.SpiritsFromPrestige - gs.Spirits} new spirits) — heading to shop";
			_step = StepGoShop;
			return;
		}
		if ( gs.IsValid() && gs.BackpackFull )
		{
			CurrentAction = $"backpack full {gs.BackpackTotal}/{gs.BackpackCapacity} - heading to shop";
			_step = StepGoShop;
			return;
		}
		if ( gs.IsValid() && gs.BackpackTotal > 0 && AnyUpgradeAffordableAfterSell( gs ) )
		{
			CurrentAction = $"cargo funds upgrade {gs.BackpackTotal}/{gs.BackpackCapacity} - heading to shop";
			_step = StepGoShop;
			return;
		}
		if ( gs.IsValid() && AnyUpgradeAffordable( gs ) )
		{
			CurrentAction = $"have {gs.Wood} wood — heading to shop";
			_step = StepGoShop;
			return;
		}
		_target = PickNearestStandingTree();
		if ( !_target.IsValid() ) { CurrentAction = "no standing trees in range"; return; }
		_targetPos = _target.WorldPosition;
		_countedTargetFell = false;
		CurrentAction = $"targeted tree at {_target.WorldPosition}";
		_step = StepApproach;
	}

	private void TickApproach()
	{
		// Park player ~60u from target on the line player→target, facing target.
		var dir = (_target.WorldPosition - _axe.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 1f ) dir = Vector3.Forward;
		dir = dir.Normal;
		var pos = _target.WorldPosition - dir * 60f + Vector3.Up * 40f;
		float yaw = Rotation.LookAt( dir ).Yaw();
		_axe.TeleportTo( pos, yaw );
		_sinceLastSwing = 999f;
		CurrentAction = "approached";
		_step = 2;
	}

	private void TickSwing()
	{
		// Tree is "felled" once it's neither standing nor falling — i.e. it
		// has landed and (probably) paid wood. Counting on !IsStanding alone
		// over-counts during the fall animation.
		if ( _target.IsValid() && !_countedTargetFell && !_target.IsStanding )
		{
			_countedTargetFell = true;
			TreesFelled++;
			CurrentAction = $"tree fell — total {TreesFelled}";
		}
		if ( !_target.IsValid() )
		{
			CurrentAction = "log split - collecting drops";
			_sinceLastPickupMove = 999f;
			_step = StepCollectDrops;
			return;
		}
		if ( _target.IsFalling )
		{
			CurrentAction = $"tree falling - total {TreesFelled}";
			return;
		}
		if ( !CanChopNow( _target ) )
		{
			CurrentAction = $"{_target.Kind} too hard - retargeting";
			_step = StepPickTarget;
			return;
		}
		if ( (float)_sinceLastSwing < 0.45f ) return;
		ParkFacing( _target.LogCenter );
		var hit = _axe.DebugSwing();
		if ( hit is null )
		{
			CurrentAction = "swing miss - repositioning";
			_step = StepApproach;
			return;
		}
		if ( hit is Tree hitTree && hitTree.IsValid() && hitTree != _target )
		{
			if ( !CanChopNow( hitTree ) )
			{
				CurrentAction = $"{hitTree.Kind} too hard - retargeting";
				_step = StepPickTarget;
				return;
			}
			_target = hitTree;
			_targetPos = hitTree.WorldPosition;
			_countedTargetFell = !hitTree.IsStanding;
		}
		_sinceLastSwing = 0f;
		CurrentAction = $"swing — chops left {_target.ChopsRemaining}";
	}

	private void TickCollectDrops()
	{
		var gs = GameState.Get( Scene );
		var items = Scene.GetAllComponents<WoodItem>()
			.Where( w => w.IsValid() && w.WorldPosition.Distance( _targetPos ) < 900f )
			.OrderBy( w => _axe.WorldPosition.Distance( w.WorldPosition ) )
			.ToList();
		if ( items.Count == 0 || (gs.IsValid() && gs.BackpackFull) )
		{
			CurrentAction = gs.IsValid()
				? $"collected bag {gs.BackpackTotal}/{gs.BackpackCapacity}"
				: "collected";
			_step = StepPickTarget;
			return;
		}

		if ( (float)_sinceLastPickupMove < 0.20f ) return;
		var item = items[0];
		var dir = (item.WorldPosition - _axe.WorldPosition).WithZ( 0f );
		float yaw = dir.LengthSquared > 1f ? Rotation.LookAt( dir.Normal ).Yaw() : 0f;
		_axe.TeleportTo( item.WorldPosition + Vector3.Up * 35f, yaw );
		_sinceLastPickupMove = 0f;
		CurrentAction = $"collecting drops {items.Count} left";
	}

	private void TickGoShop()
	{
		var spawn = Scene.GetAllComponents<SceneStarter>().FirstOrDefault()?.ResolvedPlayerSpawn ?? Vector3.Zero;
		_axe.TeleportTo( spawn, 0f );
		_sinceShopArrived = 0f;
		CurrentAction = "arrived at shop";
		_step = StepBuyShop;
	}

	private void TickBuyShop()
	{
		// Pause 1s on the shop disk so the upgrade transition reads in the
		// gameplay video — without the wait the screenshot capture would
		// catch the player still mid-teleport.
		if ( (float)_sinceShopArrived < 1f ) return;
		if ( ShopStation.TryBuyCheapestAcrossAll( Scene ) )
		{
			var gs = GameState.Get( Scene );
			CurrentAction = gs.IsValid()
				? $"bought : axe T{gs.AxeTier} spd T{gs.SpeedTier} luk T{gs.LuckTier} pwr T{gs.PowerTier}"
				: "bought upgrade";
		}
		_step = StepPickTarget;
	}

	private static bool AnyUpgradeAffordable( GameState gs )
	{
		if ( gs.CanAffordNextAxe() ) return true;
		if ( gs.SpeedTier < Tunables.MaxStatTier && gs.Wood >= Tunables.SpeedCosts[gs.SpeedTier + 1] ) return true;
		if ( gs.LuckTier  < Tunables.MaxStatTier && gs.Wood >= Tunables.LuckCosts[gs.LuckTier + 1] )   return true;
		if ( gs.PowerTier < Tunables.MaxStatTier && gs.Wood >= Tunables.PowerCosts[gs.PowerTier + 1] ) return true;
		if ( gs.PetTier   < Tunables.MaxPetTier  && gs.Wood >= Tunables.PetCosts[gs.PetTier + 1] )     return true;
		return false;
	}

	private static bool AnyUpgradeAffordableAfterSell( GameState gs )
	{
		int wood = gs.Wood + gs.BackpackWood;
		if ( gs.AxeTier < Tunables.MaxAxeTier )
		{
			var recipe = Tunables.AxeTierCostsByType[gs.AxeTier + 1];
			if ( wood >= recipe[0] && gs.Finewood + gs.BackpackFinewood >= recipe[1] && gs.CoreWood + gs.BackpackCoreWood >= recipe[2] )
				return true;
		}
		if ( gs.SpeedTier < Tunables.MaxStatTier && wood >= Tunables.SpeedCosts[gs.SpeedTier + 1] ) return true;
		if ( gs.LuckTier  < Tunables.MaxStatTier && wood >= Tunables.LuckCosts[gs.LuckTier + 1] )   return true;
		if ( gs.PowerTier < Tunables.MaxStatTier && wood >= Tunables.PowerCosts[gs.PowerTier + 1] ) return true;
		if ( gs.BackpackTier < Tunables.MaxBackpackTier && wood >= Tunables.BackpackCosts[gs.BackpackTier + 1] ) return true;
		if ( gs.PetTier < Tunables.MaxPetTier && wood >= Tunables.PetCosts[gs.PetTier + 1] ) return true;
		return false;
	}

	private bool CanChopNow( Tree tree )
	{
		if ( !tree.IsValid() || !tree.IsStanding ) return true;
		int axeTier = GameState.Get( Scene )?.AxeTier ?? 0;
		return Tunables.TreeKindMinAxeTier[(int)tree.Kind] <= axeTier;
	}

	private Tree PickNearestStandingTree()
	{
		var playerPos = _axe.WorldPosition;
		int axeTier = GameState.Get( Scene )?.AxeTier ?? 0;
		var standing = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() && t.IsStanding )
			.ToList();
		var choppable = standing
			.Where( t => Tunables.TreeKindMinAxeTier[(int)t.Kind] <= axeTier )
			.OrderBy( t => playerPos.Distance( t.WorldPosition ) )
			.FirstOrDefault();
		if ( choppable.IsValid() ) return choppable;
		return null;
	}

	private void ParkFacing( Vector3 targetPos )
	{
		var dir = (targetPos - _axe.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 1f ) dir = Vector3.Forward;
		float yaw = Rotation.LookAt( dir.Normal ).Yaw();
		_axe.TeleportTo( _axe.WorldPosition, yaw );
	}
}
