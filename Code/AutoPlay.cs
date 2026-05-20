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
	private int _step;

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
			case 0: TickPickTarget(); break;
			case 1: TickApproach(); break;
			case 2: TickSwing(); break;
		}
	}

	private void TickPickTarget()
	{
		_target = PickNearestStandingTree();
		if ( !_target.IsValid() ) { CurrentAction = "no standing trees in range"; return; }
		CurrentAction = $"targeted tree at {_target.WorldPosition}";
		_step = 1;
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

	private Tree PickNearestStandingTree()
	{
		var beaverPos = _beaver.WorldPosition;
		return Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() && t.IsStanding )
			.OrderBy( t => beaverPos.Distance( t.WorldPosition ) )
			.FirstOrDefault();
	}
}
