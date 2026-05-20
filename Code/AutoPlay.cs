namespace TreeChopping;

// Headless-friendly autoplay driver. Toggle Active=true to make the beaver
// walk to the nearest standing tree, swing until it fells, then repeat.
// Used both by gameplay validation runs (bridge sets Active=true, frames
// captured by screenshot loop) and as a stress harness for cascade physics.
public sealed class AutoPlay : Component
{
	[Property] public new bool Active { get; set; }
	[Property] public bool LookBack { get; set; }
	[Property, ReadOnly] public string CurrentAction { get; set; } = "idle";
	[Property, ReadOnly] public int TreesFelled { get; set; }

	private BeaverController _beaver;
	private Tree _target;
	private TimeSince _sinceLastSwing = 999f;
	private TimeSince _sinceShopArrived = 999f;
	private int _step;
	private const int StepPickTarget = 0;
	private const int StepApproach = 1;
	private const int StepSwing = 2;
	private const int StepGoShop = 3;
	private const int StepBuyShop = 4;

	protected override void OnUpdate()
	{
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();

		if ( LookBack && _beaver.IsValid() )
		{
			// One-shot : yaw the beaver in place to face the shop spawn point.
			// No teleport, no vertical movement — just rotation so the camera
			// (third-person, follows player yaw) frames the totem behind.
			var spawn = Scene.GetAllComponents<SceneStarter>().FirstOrDefault()?.ResolvedBeaverSpawn ?? Vector3.Zero;
			var dir = (spawn - _beaver.WorldPosition).WithZ( 0f );
			if ( dir.LengthSquared > 1f )
			{
				float yaw = Rotation.LookAt( dir.Normal ).Yaw();
				_beaver.TeleportTo( _beaver.WorldPosition, yaw );
				CurrentAction = "looking back at shop";
			}
			LookBack = false;
		}

		if ( !Active )
		{
			if ( CurrentAction != "looking back at shop" ) CurrentAction = "idle";
			return;
		}

		if ( !_beaver.IsValid() ) { CurrentAction = "no beaver"; return; }

		switch ( _step )
		{
			case StepPickTarget: TickPickTarget(); break;
			case StepApproach: TickApproach(); break;
			case StepSwing: TickSwing(); break;
			case StepGoShop: TickGoShop(); break;
			case StepBuyShop: TickBuyShop(); break;
		}
	}

	private void TickPickTarget()
	{
		// Full-loop driver : if we have enough wood for the next axe upgrade,
		// detour to the shop before continuing to chop.
		var gs = GameState.Get( Scene );
		if ( gs.IsValid() && gs.AxeTier < Tunables.MaxAxeTier
			&& gs.Wood >= Tunables.AxeTierCosts[gs.AxeTier + 1] )
		{
			CurrentAction = $"have {gs.Wood} wood — heading to shop for T{gs.AxeTier + 1}";
			_step = StepGoShop;
			return;
		}
		_target = PickNearestStandingTree();
		if ( !_target.IsValid() ) { CurrentAction = "no standing trees in range"; return; }
		CurrentAction = $"targeted tree at {_target.WorldPosition}";
		_step = StepApproach;
	}

	private void TickApproach()
	{
		// Park beaver ~60u from target on the line beaver→target, facing target.
		var dir = (_target.WorldPosition - _beaver.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 1f ) dir = Vector3.Forward;
		dir = dir.Normal;
		var pos = _target.WorldPosition - dir * 60f + Vector3.Up * 40f;
		float yaw = Rotation.LookAt( dir ).Yaw();
		_beaver.TeleportTo( pos, yaw );
		_sinceLastSwing = 999f;
		CurrentAction = "approached";
		_step = 2;
	}

	private void TickSwing()
	{
		// Tree is "felled" once it's neither standing nor falling — i.e. it
		// has landed and (probably) paid wood. Counting on !IsStanding alone
		// over-counts during the fall animation.
		if ( !_target.IsValid() || (!_target.IsStanding && !_target.IsFalling) )
		{
			TreesFelled++;
			CurrentAction = $"tree fell — total {TreesFelled}";
			_step = 0;
			return;
		}
		if ( (float)_sinceLastSwing < 0.45f ) return;
		_beaver.DebugSwing();
		_sinceLastSwing = 0f;
		CurrentAction = $"swing — chops left {_target.ChopsRemaining}";
	}

	private void TickGoShop()
	{
		var spawn = Scene.GetAllComponents<SceneStarter>().FirstOrDefault()?.ResolvedBeaverSpawn ?? Vector3.Zero;
		_beaver.TeleportTo( spawn, 0f );
		_sinceShopArrived = 0f;
		CurrentAction = "arrived at shop";
		_step = StepBuyShop;
	}

	private void TickBuyShop()
	{
		// Pause 1s on the shop disk so the upgrade transition reads in the
		// gameplay video — without the wait the screenshot capture would
		// catch the beaver still mid-teleport.
		if ( (float)_sinceShopArrived < 1f ) return;
		var gs = GameState.Get( Scene );
		if ( gs.IsValid() && gs.TryUpgradeAxe() )
			CurrentAction = $"upgraded to T{gs.AxeTier}";
		_step = StepPickTarget;
	}

	private Tree PickNearestStandingTree()
	{
		var beaverPos = _beaver.WorldPosition;
		return Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() && t.IsStanding )
			.OrderBy( t => beaverPos.Distance( t.WorldPosition ) )
			.FirstOrDefault();
	}
}
