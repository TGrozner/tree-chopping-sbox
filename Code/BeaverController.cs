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

	// Tool tier — pay wood (axe) or stone (pickaxe) to upgrade, get faster
	// swings + more chops/hit. Public getters so the HUD can read without
	// exposing an upgrade path.
	public int AxeTier { get; private set; } = 0;
	public int PickaxeTier { get; private set; } = 0;

	private Vector3 _wishVelocity;
	private Angles _cameraAngles;
	private float _swingCooldown;
	private Rigidbody _rb;
	private float _baseFov;
	private float _fovPunch;
	private float _sprintWiden;

	// Walk-bob phase + smoothed amplitude scalar.
	// Phase is in radians; amp 0..1 lerps toward 1 while moving, toward 0 when stopped
	// so there's no visual snap on stop and the sine wave glides out at its current phase.
	private float _walkPhase;
	private float _walkBobAmp;

	// Cached refs to the swappable tool meshes parented under ToolPivot.
	// Both stay alive — we only toggle .Enabled so swapping is allocation-free.
	private GameObject _axeRoot;
	private GameObject _pickaxeRoot;

	protected override void OnAwake()
	{
		_rb = Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		_cameraAngles = WorldRotation.Angles();
		_cameraAngles.pitch = 12f;
	}

	protected override void OnStart()
	{
		// Capture user-tuned baseline AFTER SceneStarter set FieldOfView.
		if ( Camera.IsValid() ) _baseFov = Camera.FieldOfView;

		// Retroactively swap the cube body that SceneStarter spawned for the
		// Poly Pizza beaver mesh. Done here (not in OnAwake) so we're sure
		// SceneStarter.SpawnBeaver finished AddComponent<ModelRenderer>; the
		// non-uniform parent WorldScale (32,32,72)/CubeBase still applies, so
		// the mesh inherits a mild stretch — accepted vs touching SceneStarter.
		var bodyMr = GameObject.Components.Get<ModelRenderer>();
		if ( bodyMr.IsValid() )
		{
			bodyMr.Model = Models.Beaver;
			// Clear the cube tint so the model's own materials show through.
			bodyMr.Tint = Color.White;
		}

		BuildHeldTools();
		ApplyToolVisibility();
	}

	protected override void OnUpdate()
	{
		// Pause gate — all gameplay-affecting inputs (look, move, swing,
		// upgrade, debug spawns) skip while the menu is open. We don't trust
		// Scene.TimeScale=0 alone since OnUpdate keeps ticking on it; the
		// menu also repurposes WASD for slider nav, so reading Input here
		// while paused would steal those presses.
		if ( PauseMenu.Get( Scene )?.IsPaused == true ) return;

		UpdateLook();
		UpdateMovement();
		UpdateCamera();
		UpdateToolSwap();
		UpdateToolUpgrade();
		UpdateDebugSpawns();
		UpdateSwing();
	}

	// R key tries to upgrade the *active* tool one tier. Axe path debits wood
	// from WoodInventory; pickaxe path debits stone from StoneInventory. Each
	// tool tracks its own tier and reads its own cost ladder so tuning the two
	// economies stays independent.
	private void UpdateToolUpgrade()
	{
		if ( !Input.Pressed( "Reload" ) ) return;

		if ( CurrentTool == ToolKind.Axe )
		{
			if ( AxeTier >= Tunables.MaxAxeTier ) return;
			var nextTier = AxeTier + 1;
			var cost = Tunables.AxeTierCosts[nextTier];

			var inv = WoodInventory.Get( Scene );
			if ( !inv.IsValid() ) return;
			if ( !inv.TrySpend( cost ) ) return;

			AxeTier = nextTier;
			Log.Info( $"[Beaver] Axe upgraded → tier {AxeTier} (-{cost} wood)" );
		}
		else if ( CurrentTool == ToolKind.Pickaxe )
		{
			if ( PickaxeTier >= Tunables.MaxPickaxeTier ) return;
			var nextTier = PickaxeTier + 1;
			var cost = Tunables.PickaxeTierCosts[nextTier];

			var inv = StoneInventory.Get( Scene );
			if ( !inv.IsValid() ) return;
			if ( !inv.TrySpend( cost ) ) return;

			PickaxeTier = nextTier;
			Log.Info( $"[Beaver] Pickaxe upgraded → tier {PickaxeTier} (-{cost} stone)" );
		}
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
		ApplyToolVisibility();
	}

	// Spawns the ToolPivot + Axe + Pickaxe child hierarchy once on start.
	// Mirrors the Godot proto's AxePivot pattern: both meshes always exist,
	// visibility is toggled, swing rotation eventually animates the pivot.
	// NOTE: parent beaver has non-uniform WorldScale (32,32,72)/CubeBase — local
	// transforms inherit that stretch and we accept the visual squash for now.
	private void BuildHeldTools()
	{
		// Pivot offset in *local* units (parent's WorldScale stretches X/Y/Z 0.64/0.64/1.44).
		// Chest height + a touch forward so the tool reads as held in front of the beaver.
		const float PivotForwardLocal = 0.55f;     // ~28u forward after X-stretch
		const float PivotUpLocal = 0.25f;          // ~18u up after Z-stretch (mid-chest)

		var pivot = Scene.CreateObject();
		pivot.Name = "ToolPivot";
		pivot.Parent = GameObject;
		pivot.LocalPosition = new Vector3( PivotForwardLocal, 0f, PivotUpLocal );
		pivot.LocalScale = Vector3.One;

		_axeRoot = BuildAxe( pivot );
		_pickaxeRoot = BuildPickaxe( pivot );
	}

	// Kenney axe held in hand. LocalScale=One — the parent's non-uniform
	// (32,32,72)/CubeBase squash propagates and is accepted (mentioned in
	// commit body). Model.Tint stays untouched so the GLB's own materials
	// read; only fall back to a brown cube if Models.Axe failed to load.
	private GameObject BuildAxe( GameObject pivot )
	{
		var go = Scene.CreateObject();
		go.Name = "Axe";
		go.Parent = pivot;
		go.LocalPosition = new Vector3( 0f, 0f, 0f );
		go.LocalScale = Vector3.One;
		go.Tags.Add( "beaver_tool" );

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Models.Axe;
		return go;
	}

	// Creative Trio pickaxe held in hand. Single ModelRenderer with the
	// imported GLB replaces the prior procedural shaft+head cube pair. Same
	// scale-inheritance squash applies as for the axe.
	private GameObject BuildPickaxe( GameObject pivot )
	{
		var root = Scene.CreateObject();
		root.Name = "Pickaxe";
		root.Parent = pivot;
		root.LocalPosition = new Vector3( 0f, 0f, 0f );
		root.LocalScale = Vector3.One;
		root.Tags.Add( "beaver_tool" );

		var mr = root.AddComponent<ModelRenderer>();
		mr.Model = Models.Pickaxe;
		return root;
	}

	private void ApplyToolVisibility()
	{
		if ( _axeRoot.IsValid() ) _axeRoot.Enabled = CurrentTool == ToolKind.Axe;
		if ( _pickaxeRoot.IsValid() ) _pickaxeRoot.Enabled = CurrentTool == ToolKind.Pickaxe;
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

		// Walk-bob — ported from Godot proto's visual-mesh bob (beaver.gd `_animate_visual`
		// used sin(_move_phase) * 0.04 m vertical + sin(_move_phase*0.5) * 0.05 roll).
		// Here we apply it to the *camera position* instead of the beaver mesh: vertical
		// from sin(phase), lateral from cos(phase*0.5) along the camera's right vector so
		// it composes with the trauma jiggle that just wrote camPos. Adding AFTER the
		// shake means the shake jitters the bobbed position, not the other way around.
		const float BobFrequencyHz = 1.6f;        // ~steps per second at base walk
		const float BobAmplitudeUp = 2.4f;        // SU (~0.06 m, slightly tighter than Godot's 0.04 m on body)
		const float BobAmplitudeSide = 1.4f;      // SU sway peak
		const float BobSpeedThreshold = 30f;      // below this horiz speed we decay to neutral
		const float BobSprintFreqMul = 1.5f;      // sprint cadence bump (matches Godot ~1.5x boost feel)
		const float BobSprintAmpMul = 1.25f;
		const float BobDecayRate = 6f;            // amp lerp rate when stopped (and when ramping back up)
		const float Tau = MathF.PI * 2f;

		var horizSpeed = _rb.IsValid() ? _rb.Velocity.WithZ( 0f ).Length : _wishVelocity.WithZ( 0f ).Length;
		bool moving = horizSpeed > BobSpeedThreshold;
		bool sprintingBob = Input.Down( "Run" );

		// Phase advances proportional to speed so it doesn't tick while idle-creeping;
		// freq * Tau converts Hz → rad/s. Sprint bumps cadence to read as a faster gait.
		float speedFactor = MathX.Clamp( horizSpeed / (Tunables.BeaverMoveSpeed + 0.001f), 0f, 2f );
		float freq = BobFrequencyHz * (sprintingBob ? BobSprintFreqMul : 1f);
		_walkPhase += Time.Delta * speedFactor * freq * Tau;
		if ( _walkPhase > Tau * 1024f ) _walkPhase -= Tau * 1024f; // keep from blowing up over hours

		// Smooth amp toward 0 when stopped (no snap) or toward target when moving.
		float targetAmp = moving ? 1f : 0f;
		_walkBobAmp = MathX.Lerp( _walkBobAmp, targetAmp, MathX.Clamp( Time.Delta * BobDecayRate, 0f, 1f ) );

		float ampMul = (sprintingBob ? BobSprintAmpMul : 1f) * _walkBobAmp;
		float bobUp = MathF.Sin( _walkPhase ) * BobAmplitudeUp * ampMul;
		float bobSide = MathF.Cos( _walkPhase * 0.5f ) * BobAmplitudeSide * ampMul;
		Camera.WorldPosition += Vector3.Up * bobUp + rot.Right * bobSide;

		UpdateFov();
	}

	private void UpdateFov()
	{
		if ( _baseFov <= 0f ) _baseFov = Camera.FieldOfView;

		// Sprint widen only counts when actually moving — pure key-hold shouldn't drift FOV.
		bool sprinting = Input.Down( "Run" ) && _wishVelocity.WithZ( 0f ).LengthSquared > 1f;
		float target = sprinting ? Tunables.FovSprintWiden : 0f;
		_sprintWiden = MathX.Lerp( _sprintWiden, target, Time.Delta * Tunables.FovSprintLerpRate );

		_fovPunch = MathF.Max( 0f, _fovPunch - Time.Delta * Tunables.FovPunchDecay );

		Camera.FieldOfView = _baseFov + _sprintWiden + _fovPunch;
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

		// Tier-aware cooldown: higher tier = shorter cooldown = faster swings.
		// Active tool drives which ladder we read so the axe and pickaxe
		// economies can be tuned independently.
		var isPickaxe = CurrentTool == ToolKind.Pickaxe;
		var cooldownLadder = isPickaxe ? Tunables.PickaxeTierSwingCooldown : Tunables.AxeTierSwingCooldown;
		var multLadder = isPickaxe ? Tunables.PickaxeTierChopMultiplier : Tunables.AxeTierChopMultiplier;
		var activeTier = isPickaxe ? PickaxeTier : AxeTier;
		activeTier = Math.Clamp( activeTier, 0, cooldownLadder.Length - 1 );
		_swingCooldown = cooldownLadder[activeTier];

		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var forward = (_cameraAngles.WithPitch( 0f )).ToRotation().Forward;
		var hit = ChooseSwingTarget( origin, forward );
		if ( hit is null )
		{
			TrySdfDig( origin, forward );
			return;
		}

		// Chop multiplier: tier 3 lands 3 chops per swing, felling a default
		// 3-chop tree/rock in one click — matches the Godot proto's tier-damage
		// payoff curve. Looping Chop() rather than passing N keeps the
		// IChoppable API untouched (parallel agents own Tree/LogPiece/Rock).
		var chops = multLadder[Math.Clamp( activeTier, 0, multLadder.Length - 1 )];
		for ( int i = 0; i < chops; i++ )
		{
			if ( !hit.IsValid() ) break;
			hit.Chop( forward );
		}
		_fovPunch += Tunables.FovChopPunch;
		ComboTracker.Get( Scene )?.Beat();
	}

	private Sdf3DWorld _sdfWorld;

	private void TrySdfDig( Vector3 origin, Vector3 forward )
	{
		if ( CurrentTool != ToolKind.Pickaxe ) return;
		_sdfWorld ??= Scene?.GetAllComponents<Sdf3DWorld>().FirstOrDefault();
		if ( !_sdfWorld.IsValid() ) return;

		var pitchedForward = _cameraAngles.ToRotation().Forward;
		var trace = Scene.Trace
			.Ray( origin, origin + pitchedForward * Tunables.SwingRange )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		if ( !trace.Hit ) return;

		var digWorld = trace.EndPosition - pitchedForward * 8f;
		var digLocal = _sdfWorld.WorldTransform.PointToLocal( digWorld );
		_ = _sdfWorld.SubtractAsync( new SphereSdf3D( digLocal, 24f ) );
		ComboTracker.Get( Scene )?.Beat();
	}

	// Test hook for [[sbox-selftest-pattern]] — exercises the real UpdateSwing
	// chain (ChooseSwingTarget + Chop) without going through Input.Pressed or
	// the cooldown. Lets the headless SelfTest probe cone/range/tool gating so
	// a "Chop() direct works but swinging in-game doesn't" regression is caught.
	//
	// Returns the IChoppable that got hit, or null when nothing was in range.
	// Reads CurrentTool and the current yaw — set them via DebugSetYaw + the
	// existing Use input (or DebugSetTool) before calling.
	public IChoppable DebugSwing()
	{
		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var forward = (_cameraAngles.WithPitch( 0f )).ToRotation().Forward;
		var hit = ChooseSwingTarget( origin, forward );
		if ( hit is null ) return null;
		hit.Chop( forward );
		ComboTracker.Get( Scene )?.Beat();
		return hit;
	}

	// Verbose version for the headless test — same selection logic but logs
	// every candidate's filter result so a "tree should be hittable but isn't"
	// regression can be diagnosed straight from the harness output.
	public IChoppable DebugSwingVerbose()
	{
		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var forward = (_cameraAngles.WithPitch( 0f )).ToRotation().Forward;
		Log.Info( $"[TC_TEST] DebugSwingVerbose origin={origin} forward={forward} tool={CurrentTool}" );

		var all = Scene.GetAllComponents<IChoppable>().ToList();
		int considered = 0, droppedValid = 0, droppedTool = 0, droppedRange = 0, droppedCone = 0;
		IChoppable best = null;
		var bestScore = float.NegativeInfinity;
		foreach ( var c in all )
		{
			considered++;
			if ( !c.IsValid() ) { droppedValid++; continue; }
			if ( !c.AcceptsTool( CurrentTool ) ) { droppedTool++; continue; }
			var to = c.WorldPosition - origin;
			to.z = 0f;
			var dist = to.Length;
			if ( dist > Tunables.SwingRange ) { droppedRange++; continue; }
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) { droppedCone++; continue; }
			var score = dot - dist * 0.005f;
			Log.Info( $"[TC_TEST]   candidate {c.GetType().Name} pos={c.WorldPosition} dist={dist:F1} dot={dot:F2} score={score:F2}" );
			if ( score > bestScore )
			{
				bestScore = score;
				best = c;
			}
		}
		Log.Info( $"[TC_TEST] DebugSwingVerbose considered={considered} droppedValid={droppedValid} droppedTool={droppedTool} droppedRange={droppedRange} droppedCone={droppedCone} best={(best == null ? "null" : best.GetType().Name)}" );
		if ( best is null ) return null;
		best.Chop( forward );
		ComboTracker.Get( Scene )?.Beat();
		return best;
	}

	// Yaw setter for tests — the SelfTest can aim the beaver at a known target
	// (tree, rock) before calling DebugSwing.
	public void DebugSetYaw( float yawDegrees )
	{
		_cameraAngles.yaw = yawDegrees;
		_cameraAngles.pitch = 0f;
		_cameraAngles.roll = 0f;
	}

	public Angles DebugCameraAngles => _cameraAngles;

	public void DebugSetTool( ToolKind tool ) => CurrentTool = tool;

	private IChoppable ChooseSwingTarget( Vector3 origin, Vector3 forward )
	{
		// Scene.GetAllComponents<T> with the base Component type returns nothing —
		// the engine matches by exact T (or T = interface) and there are no raw
		// Component instances. Querying the interface directly is the supported
		// path (Sandbox.Engine.xml says: "This can include interfaces.").
		var candidates = Scene.GetAllComponents<IChoppable>()
			.Where( c => c.IsValid() && c.AcceptsTool( CurrentTool ) )
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
	bool AcceptsTool( ToolKind tool );
}

public enum ToolKind
{
	Axe,
	Pickaxe,
}
