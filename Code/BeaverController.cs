namespace TreeChopping;

public sealed class BeaverController : Component
{
	[Property] public GameObject CameraRoot { get; set; }
	[Property] public CameraComponent Camera { get; set; }
	[Property] public float MoveSpeed { get; set; } = Tunables.BeaverMoveSpeed;
	[Property] public float JumpImpulse { get; set; } = Tunables.BeaverJumpImpulse;
	[Property] public float CameraDistance { get; set; } = Tunables.CameraDistance;
	[Property] public float MouseSensitivity { get; set; } = 0.12f;

	public ToolKind CurrentTool { get; private set; } = ToolKind.Axe;

	private Vector3 _wishVelocity;
	private Angles _cameraAngles;
	private float _swingCooldown;
	private Rigidbody _rb;

	protected override void OnAwake()
	{
		_rb = Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		_cameraAngles = WorldRotation.Angles();
		_cameraAngles.pitch = 12f;
	}

	protected override void OnUpdate()
	{
		UpdateLook();
		UpdateMovement();
		UpdateCamera();
		UpdateToolSwap();
		UpdateDebugSpawns();
		UpdateSwing();
	}

	private void UpdateDebugSpawns()
	{
		if ( Input.Pressed( "Slot1" ) ) SpawnTestBody( densityFactor: 0.4f, tint: new Color( 0.20f, 0.55f, 0.90f, 1f ) ); // floater
		if ( Input.Pressed( "Slot2" ) ) SpawnTestBody( densityFactor: 2.5f, tint: new Color( 0.85f, 0.20f, 0.20f, 1f ) ); // sinker
		if ( Input.Pressed( "Slot3" ) ) SpawnTestBody( densityFactor: 1.0f, tint: new Color( 0.90f, 0.85f, 0.40f, 1f ) ); // neutral
	}

	private void SpawnTestBody( float densityFactor, Color tint )
	{
		var go = Scene.CreateObject();
		go.Name = "TestBody";
		go.WorldPosition = WorldPosition + Vector3.Up * 120f;
		go.WorldScale = new Vector3( 40f ) / Tunables.CubeBase;
		go.Tags.Add( "test_body" );

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		mr.Tint = tint;

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 8f * densityFactor;
		rb.LinearDamping = 0.5f;
		rb.AngularDamping = 0.8f;
	}

	private void UpdateToolSwap()
	{
		if ( !Input.Pressed( "Use" ) ) return;
		CurrentTool = CurrentTool switch
		{
			ToolKind.Axe => ToolKind.Pickaxe,
			ToolKind.Pickaxe => ToolKind.Axe,
			_ => ToolKind.Axe,
		};
		Log.Info( $"[Beaver] tool → {CurrentTool}" );
	}

	private void UpdateLook()
	{
		var look = Input.AnalogLook;
		_cameraAngles.pitch += look.pitch;
		_cameraAngles.yaw += look.yaw;
		_cameraAngles.pitch = _cameraAngles.pitch.Clamp( Tunables.CameraMinPitch, Tunables.CameraMaxPitch );
		_cameraAngles.roll = 0f;
	}

	private void UpdateMovement()
	{
		var move = Input.AnalogMove;
		var yawOnly = new Angles( 0f, _cameraAngles.yaw, 0f ).ToRotation();
		var wish = yawOnly * new Vector3( move.x, move.y, 0f );
		var speed = MoveSpeed * (Input.Down( "Run" ) ? Tunables.BeaverSprintMultiplier : 1f);
		_wishVelocity = wish.Normal * speed;

		if ( _rb.IsValid() )
		{
			var v = _rb.Velocity;
			v.x = _wishVelocity.x;
			v.y = _wishVelocity.y;
			_rb.Velocity = v;

			if ( Input.Pressed( "Jump" ) && IsGrounded() )
			{
				_rb.ApplyImpulse( Vector3.Up * JumpImpulse * _rb.PhysicsBody.Mass );
			}
		}
		else
		{
			WorldPosition += _wishVelocity * Time.Delta;
		}

		var horiz = wish.Normal;
		if ( horiz.LengthSquared > 0.01f )
		{
			var targetYaw = Rotation.LookAt( horiz, Vector3.Up );
			WorldRotation = Rotation.Slerp( WorldRotation, targetYaw, Time.Delta * 12f );
		}
	}

	private bool IsGrounded()
	{
		var down = Scene.Trace
			.Ray( WorldPosition + Vector3.Up * 4f, WorldPosition + Vector3.Down * 8f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		return down.Hit;
	}

	private void UpdateCamera()
	{
		if ( !Camera.IsValid() ) return;
		var pivot = WorldPosition + Vector3.Up * Tunables.CameraHeightAboveBeaver;
		var rot = _cameraAngles.ToRotation();
		var desired = pivot - rot.Forward * CameraDistance;

		var trace = Scene.Trace
			.Ray( pivot, desired )
			.Radius( 10f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		var camPos = trace.Hit ? trace.EndPosition + trace.Normal * 4f : desired;

		// Squirrel Eiserloh trauma shake: amplitude = trauma^2, scaled to screen-space-ish.
		var combo = ComboTracker.Get( Scene );
		if ( combo.IsValid() && combo.TraumaAmount > 0.01f )
		{
			float t = combo.TraumaAmount * combo.TraumaAmount;
			var rng = (uint)(Time.Now * 1000.0);
			float jx = (HashFloat( rng, 0u ) - 0.5f) * 2f;
			float jy = (HashFloat( rng, 1u ) - 0.5f) * 2f;
			float jz = (HashFloat( rng, 2u ) - 0.5f) * 2f;
			camPos += new Vector3( jx, jy, jz ) * t * Tunables.CameraTraumaScale;
			rot *= Rotation.FromAxis( Vector3.Up, (HashFloat( rng, 3u ) - 0.5f) * 4f * t );
		}

		Camera.WorldPosition = camPos;
		Camera.WorldRotation = rot;
	}

	private static float HashFloat( uint a, uint b )
	{
		uint h = a * 374761393u + b * 668265263u;
		h = (h ^ (h >> 13)) * 1274126177u;
		h ^= h >> 16;
		return (h & 0xFFFFFF) / (float)0x1000000;
	}

	private void UpdateSwing()
	{
		_swingCooldown = MathF.Max( 0f, _swingCooldown - Time.Delta );
		if ( _swingCooldown > 0f ) return;
		if ( !Input.Pressed( "attack1" ) ) return;

		_swingCooldown = Tunables.SwingCooldown;
		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var forward = (_cameraAngles.WithPitch( 0f )).ToRotation().Forward;
		var hit = ChooseSwingTarget( origin, forward );
		if ( hit is null ) return;

		hit.Chop( forward );
		ComboTracker.Get( Scene )?.Beat();
	}

	private IChoppable ChooseSwingTarget( Vector3 origin, Vector3 forward )
	{
		var candidates = Scene.GetAllComponents<Component>()
			.OfType<IChoppable>()
			.Where( c => c.IsValid() )
			.ToList();

		IChoppable best = null;
		var bestScore = float.NegativeInfinity;
		foreach ( var c in candidates )
		{
			var to = c.WorldPosition - origin;
			to.z = 0f;
			var dist = to.Length;
			if ( dist > Tunables.SwingRange ) continue;
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) continue;
			var score = dot - dist * 0.005f;
			if ( score > bestScore )
			{
				bestScore = score;
				best = c;
			}
		}
		return best;
	}
}

public interface IChoppable
{
	Vector3 WorldPosition { get; }
	bool IsValid();
	void Chop( Vector3 direction );
}

public enum ToolKind
{
	Axe,
	Pickaxe,
}
