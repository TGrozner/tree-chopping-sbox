namespace TreeChopping;

// Mow-the-lawn loop : continuous play. Click = swing axe (per-tier ChopPower
// hits ChopsRemaining), tree fells when its HP runs out, drops wood into
// GameState. R = teleport back to spawn. No runs, no cascade scoring, no
// cinema cam — just chop, gather, upgrade.
public sealed class BeaverController : Component
{
	[Property] public PlayerController Player { get; set; }
	[Property] public CameraComponent Camera { get; set; }
	[Property] public SkinnedModelRenderer PlayerRenderer { get; set; }

	private enum SwingPhase { Idle, WindUp, Recovery }
	private SwingPhase _phase = SwingPhase.Idle;
	private float _phaseTime;
	private Vector3 _pendingForward;

	private float _baseFov = -1f;
	private float _fovOffset;
	private int _hitstopFramesLeft;
	private Tree _previewTree;
	private TimeSince _shakeStart = 999f;
	private float _shakeAmplitude;
	private int _shakeSeed;

	// Bridge / autopilot hooks.
	[Property] public bool DebugRequestSwing { get; set; }
	[Property] public Vector3 DebugTeleportTo { get; set; }
	[Property] public bool DebugApplyTeleport { get; set; }
	[Property] public float DebugTeleportYawDegrees { get; set; }
	// Top-down screenshot mode : when true, OnPreRender pins the runtime
	// camera at (-1000, 0, 4000) looking straight down. Player rigidbody
	// stays grounded — only the camera moves, so we don't violate the
	// "no flying" rule.
	[Property] public bool AerialView { get; set; }

	protected override void OnAwake()
	{
		Player ??= Components.Get<PlayerController>( FindMode.EverythingInSelfAndDescendants );
		Camera ??= Scene.GetAllComponents<CameraComponent>().FirstOrDefault();
		PlayerRenderer ??= Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
	}

	protected override void OnUpdate()
	{
		TickDebugHooks();
		TickHitstop();
		TickFov();
		TickAimPreview();
		TickTeleportHome();
		TickSpeedStat();
		switch ( _phase )
		{
			case SwingPhase.Idle: TickIdle(); break;
			case SwingPhase.WindUp: TickWindUp(); break;
			case SwingPhase.Recovery: TickRecovery(); break;
		}
	}

	protected override void OnPreRender()
	{
		if ( !Camera.IsValid() ) return;
		if ( _baseFov <= 0f ) _baseFov = Camera.FieldOfView;
		Camera.FieldOfView = _baseFov + _fovOffset;
		if ( AerialView )
		{
			var spawn = Scene.GetAllComponents<SceneStarter>().FirstOrDefault()?.ResolvedBeaverSpawn
				?? new Vector3( -1000f, 0f, 600f );
			Camera.WorldPosition = spawn + Vector3.Up * 3500f;
			Camera.WorldRotation = Rotation.LookAt( Vector3.Down );
			Camera.FieldOfView = 60f;
			return;
		}
		// Anti-clip : trace from the player head toward the third-person
		// camera position, pull the cam in if a tree trunk is between. Stops
		// the "camera inside a wooden cube" frames when the player teleports
		// (autoplay) or strafes next to a thick trunk.
		var head = WorldPosition + Vector3.Up * Tunables.BeaverEyeHeight;
		var camPos = Camera.WorldPosition;
		var toCam = camPos - head;
		if ( toCam.LengthSquared > 1f )
		{
			var trace = Scene.Trace
				.Sphere( 10f, head, camPos )
				.WithAnyTags( "tree" )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();
			if ( trace.Hit )
			{
				Camera.WorldPosition = trace.EndPosition + (head - camPos).Normal * 6f;
			}
		}
		ApplyCameraShake();
	}

	// Per-hit positional camera shake. Sin-based decaying jitter, applied
	// AFTER the anti-clip pullback so the shake doesn't push the camera back
	// inside a trunk we just dodged.
	private void ApplyCameraShake()
	{
		float t = (float)_shakeStart;
		const float duration = 0.18f;
		if ( t >= duration || _shakeAmplitude < 0.05f ) return;
		float decay = 1f - (t / duration);
		float amp = _shakeAmplitude * decay * decay;
		// Two out-of-phase sin axes for a quick "rattle" rather than a smooth
		// orbit. Seed shifts the phase so back-to-back hits don't beat
		// constructively.
		float phase = _shakeSeed * 0.31f;
		float fx = MathF.Sin( (t * 47f) + phase );
		float fy = MathF.Sin( (t * 53f) + phase * 1.4f + 1.2f );
		var right = Camera.WorldRotation.Right;
		var up = Camera.WorldRotation.Up;
		Camera.WorldPosition += right * (fx * amp) + up * (fy * amp);
	}

	public void AddCameraShake( float amplitudeUnits )
	{
		_shakeStart = 0f;
		_shakeAmplitude = MathF.Max( _shakeAmplitude, amplitudeUnits );
		_shakeSeed = (_shakeSeed + 1) & 0xFF;
	}

	private void TickHitstop()
	{
		if ( _hitstopFramesLeft <= 0 ) return;
		_hitstopFramesLeft--;
		if ( _hitstopFramesLeft == 0 ) Scene.TimeScale = 1f;
	}

	private float _baseWalkSpeed = -1f;
	private float _baseRunSpeed = -1f;
	private void TickSpeedStat()
	{
		if ( !Player.IsValid() ) return;
		if ( _baseWalkSpeed <= 0f ) { _baseWalkSpeed = Player.WalkSpeed; _baseRunSpeed = Player.RunSpeed; }
		var gs = GameState.Get( Scene );
		float mul = gs.IsValid() ? gs.SpeedMultiplier : 1f;
		Player.WalkSpeed = _baseWalkSpeed * mul;
		Player.RunSpeed = _baseRunSpeed * mul;
	}

	private void TickFov()
	{
		if ( _fovOffset <= 0.01f ) { _fovOffset = 0f; return; }
		_fovOffset = MathX.Lerp( _fovOffset, 0f, Tunables.SwingFovDecayPerSec * Time.Delta );
	}

	private void TickDebugHooks()
	{
		if ( DebugApplyTeleport )
		{
			DebugApplyTeleport = false;
			TeleportTo( DebugTeleportTo, DebugTeleportYawDegrees );
		}
	}

	private int _aimPreviewFrame;
	private void TickAimPreview()
	{
		bool canHighlight = _phase == SwingPhase.Idle;
		Tree target = _previewTree;
		// Re-pick the target every 3 frames (~20Hz) instead of every frame.
		// Aim highlight feels identical to a human at this rate, the 60Hz
		// sphere+ray traces against 400 trees burned cycles for nothing.
		_aimPreviewFrame++;
		if ( canHighlight && _aimPreviewFrame % 3 == 0 )
		{
			var hit = PickCameraAimTarget( out _ );
			target = hit as Tree;
		}
		else if ( !canHighlight )
		{
			target = null;
		}
		if ( target != _previewTree )
		{
			if ( _previewTree.IsValid() ) _previewTree.SetAimHighlight( false );
			_previewTree = target;
			if ( _previewTree.IsValid() ) _previewTree.SetAimHighlight( true );
		}
	}

	private void TickTeleportHome()
	{
		if ( !Input.Pressed( "Reload" ) ) return;
		var starter = Scene.GetAllComponents<SceneStarter>().FirstOrDefault();
		var home = starter.IsValid() ? starter.ResolvedBeaverSpawn : new Vector3( -1000f, 0f, 600f );
		TeleportTo( home, 0f );
		Log.Info( "[Beaver] Teleport home" );
	}

	public void TeleportTo( Vector3 pos, float yawDeg )
	{
		var rb = Components.Get<Rigidbody>();
		if ( rb.IsValid() && rb.PhysicsBody.IsValid() )
		{
			rb.PhysicsBody.Position = pos;
			rb.Velocity = Vector3.Zero;
			rb.AngularVelocity = Vector3.Zero;
		}
		else WorldPosition = pos;
		if ( Player.IsValid() )
		{
			var ang = Player.EyeAngles;
			ang.yaw = yawDeg;
			ang.pitch = 0f;
			ang.roll = 0f;
			Player.EyeAngles = ang;
		}
	}

	private void TickIdle()
	{
		bool requested = Input.Pressed( "Attack1" ) || DebugRequestSwing;
		if ( DebugRequestSwing ) DebugRequestSwing = false;
		if ( !requested ) return;

		_pendingForward = EyeForwardFlat();
		_phase = SwingPhase.WindUp;
		_phaseTime = 0f;

		_fovOffset += Tunables.SwingFovPunch * 0.4f;
		var handPos = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.8f) + _pendingForward * 30f;
		ChipBurst.Spawn( Scene, handPos, _pendingForward, 4 );
		TriggerAttackAnim();
		Sfx.Play( "sounds/swing.sound", WorldPosition, volume: 0.80f, pitchMin: 1.40f, pitchMax: 1.65f );
	}

	private void TickWindUp()
	{
		_phaseTime += Time.Delta;
		if ( _phaseTime < Tunables.SwingWindUpDuration ) return;

		var origin = WorldPosition + Vector3.Up * Tunables.BeaverEyeHeight;
		var forward = EyeForwardFlat();
		var hit = PickCameraAimTarget( out var impactPoint )
			?? ChooseSwingTarget( origin, forward );

		if ( hit is not null && hit.IsValid() )
		{
			if ( impactPoint == default )
				impactPoint = hit.WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.3f);
			int chopPower = GameState.Get( Scene )?.ChopPower ?? 1;
			if ( hit is Tree t ) t.Chop( forward, chopPower );
			else hit.Chop( forward );
			ApplyImpactFeedback( impactPoint, forward );
		}
		else
		{
			_fovOffset += Tunables.SwingFovPunch * 0.25f;
		}

		_phase = SwingPhase.Recovery;
		_phaseTime = 0f;
	}

	private void TickRecovery()
	{
		_phaseTime += Time.Delta;
		if ( _phaseTime >= Tunables.SwingRecoveryDuration ) _phase = SwingPhase.Idle;
	}

	private void ApplyImpactFeedback( Vector3 contactPoint, Vector3 forward )
	{
		ChipBurst.Spawn( Scene, contactPoint, forward, Tunables.ChipBurstCount );
		_fovOffset += Tunables.SwingFovPunch;
		Scene.TimeScale = Tunables.HitstopTimeScale;
		_hitstopFramesLeft = Tunables.HitstopFrames;
		// Valheim-style audio layering : primary thunk + higher-pitched crack
		// stacked so the impact reads as a real wood-vs-axe hit instead of
		// one flat sample.
		Sfx.Play( "sounds/chop_wood.sound", contactPoint, volume: 0.95f, pitchMin: 0.85f, pitchMax: 1.15f );
		Sfx.Play( "sounds/log_break.sound", contactPoint, volume: 0.45f, pitchMin: 1.45f, pitchMax: 1.75f );
		// Positional camera shake — amplitude scales gently with chop power
		// so a Chainsaw hit feels heavier than a Hands hit.
		int power = GameState.Get( Scene )?.ChopPower ?? 1;
		AddCameraShake( 1.6f + MathF.Min( power * 0.20f, 1.6f ) );
	}

	private void TriggerAttackAnim()
	{
		if ( !PlayerRenderer.IsValid() ) return;
		try { PlayerRenderer.Set( "holdtype_attack", 1 ); } catch { }
		try { PlayerRenderer.Set( "b_attack", true ); } catch { }
	}

	private Vector3 EyeForwardFlat()
	{
		if ( Player.IsValid() )
		{
			var ang = Player.EyeAngles;
			ang.pitch = 0f;
			return ang.ToRotation().Forward;
		}
		return Vector3.Forward;
	}

	// ─── debug / test ────────────────────────────────────────────────────────

	// Silent variant used by AutoPlay. Same chop logic as DebugSwingVerbose
	// minus the [TC_TEST] log spam (~4 lines/sec at autoplay cadence).
	public IChoppable DebugSwing()
	{
		var origin = WorldPosition + Vector3.Up * Tunables.BeaverEyeHeight;
		var forward = EyeForwardFlat();
		var hit = ChooseSwingTarget( origin, forward );
		if ( hit is null ) return null;
		int chopPower = GameState.Get( Scene )?.ChopPower ?? 1;
		if ( hit is Tree t ) t.Chop( forward, chopPower );
		else hit.Chop( forward );
		return hit;
	}

	public IChoppable DebugSwingVerbose()
	{
		var origin = WorldPosition + Vector3.Up * Tunables.BeaverEyeHeight;
		var forward = EyeForwardFlat();
		Log.Info( $"[TC_TEST] DebugSwingVerbose origin={origin} forward={forward}" );

		IChoppable best = null;
		var bestScore = float.NegativeInfinity;
		int considered = 0, droppedValid = 0, droppedTool = 0, droppedRange = 0, droppedCone = 0;
		foreach ( var c in Scene.GetAllComponents<IChoppable>() )
		{
			considered++;
			if ( !c.IsValid() ) { droppedValid++; continue; }
			if ( !c.AcceptsTool( ToolKind.Axe ) ) { droppedTool++; continue; }
			var to = c.WorldPosition - origin;
			to.z = 0f;
			var dist = to.Length;
			if ( dist > Tunables.SwingRange ) { droppedRange++; continue; }
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) { droppedCone++; continue; }
			var score = dot - dist * 0.005f;
			if ( score > bestScore ) { bestScore = score; best = c; }
		}
		Log.Info( $"[TC_TEST] DebugSwingVerbose considered={considered} droppedValid={droppedValid} droppedTool={droppedTool} droppedRange={droppedRange} droppedCone={droppedCone} best={(best == null ? "null" : best.GetType().Name)}" );
		if ( best is null ) return null;
		int chopPower = GameState.Get( Scene )?.ChopPower ?? 1;
		if ( best is Tree t ) t.Chop( forward, chopPower );
		else best.Chop( forward );
		return best;
	}

	private IChoppable ChooseSwingTarget( Vector3 origin, Vector3 forward )
	{
		IChoppable best = null;
		var bestScore = float.NegativeInfinity;
		foreach ( var c in Scene.GetAllComponents<IChoppable>() )
		{
			if ( !c.IsValid() || !c.AcceptsTool( ToolKind.Axe ) ) continue;
			var to = c.WorldPosition - origin;
			to.z = 0f;
			var dist = to.Length;
			if ( dist > Tunables.SwingRange ) continue;
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) continue;
			var score = dot - dist * 0.005f;
			if ( score > bestScore ) { bestScore = score; best = c; }
		}
		return best;
	}

	public IChoppable PickCameraAimTarget( out Vector3 hitPos )
	{
		hitPos = default;
		if ( !Camera.IsValid() ) return null;
		var origin = WorldPosition + Vector3.Up * Tunables.BeaverEyeHeight;
		var ray = Camera.ScreenNormalToRay( new Vector3( 0.5f, 0.5f, 0f ) );
		float sweepLen = 2000f;
		var end = ray.Position + ray.Forward * sweepLen;

		var trace = Scene.Trace
			.Sphere( Tunables.SwingAimSweepRadius, ray.Position, end )
			.WithAnyTags( "tree" )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		if ( !trace.Hit )
		{
			trace = Scene.Trace
				.Ray( ray.Position, end )
				.WithAnyTags( "tree" )
				.IgnoreGameObjectHierarchy( GameObject )
				.Run();
		}
		if ( !trace.Hit ) return null;

		var go = trace.GameObject;
		if ( !go.IsValid() ) return null;

		var ic = (go.Components.Get<Tree>() as IChoppable)
			?? (go.Components.Get<Tree>( FindMode.InAncestors ) as IChoppable);
		if ( ic is null || !ic.IsValid() || !ic.AcceptsTool( ToolKind.Axe ) ) return null;

		var hp = trace.EndPosition;
		var beaverToHit = (hp - origin).WithZ( 0f );
		if ( beaverToHit.Length > Tunables.SwingRange ) return null;

		hitPos = hp;
		return ic;
	}
}

public interface IChoppable
{
	Vector3 WorldPosition { get; }
	bool IsValid();
	void Chop( Vector3 direction );
	bool AcceptsTool( ToolKind tool );
}

public enum ToolKind { Axe }
