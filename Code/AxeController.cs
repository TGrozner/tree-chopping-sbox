namespace TreeChopping;

// Mow-the-lawn loop : continuous play. Click = swing axe (per-tier ChopPower
// hits ChopsRemaining), tree fells when its HP runs out, drops wood into
// GameState. R = teleport back to spawn. No runs, no cascade scoring, no
// cinema cam — just chop, gather, upgrade.
public sealed class AxeController : Component
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
	// Combo chain Valheim Attack.m_attackChainLevels — chain reset à 0 si gap
	// > ChopComboWindow ou level atteint max. Level final = chop damage ×
	// ChopComboFinalDamageMul + push × ChopComboFinalPushMul. Expose en
	// [Property, ReadOnly] pour HUD pip indicator + selftest visibility.
	[Property, ReadOnly] public int ChainLevel { get; private set; }
	private TimeSince _timeSinceLastSwing = 999f;
	private TimeSince _shakeStart = 999f;
	private float _shakeAmplitude;
	private int _shakeSeed;

	// Bridge / autopilot hooks.
	[Property] public bool DebugRequestSwing { get; set; }
	[Property] public Vector3 DebugTeleportTo { get; set; }
	[Property] public bool DebugApplyTeleport { get; set; }
	[Property] public float DebugTeleportYawDegrees { get; set; }
	public bool IsSwingIdle => _phase == SwingPhase.Idle;
	public bool IsSwinging => _phase != SwingPhase.Idle;
	public float SwingViewProgress
	{
		get
		{
			return _phase switch
			{
				SwingPhase.WindUp => (_phaseTime / Tunables.SwingWindUpDuration).Clamp( 0f, 1f ),
				SwingPhase.Recovery => 1f + (_phaseTime / MathF.Max( Tunables.SwingRecoveryDuration, 0.001f )).Clamp( 0f, 1f ),
				_ => 0f,
			};
		}
	}

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
		// Anti-clip : trace from the player head toward the third-person
		// camera position, pull the cam in if a tree trunk is between. Stops
		// the "camera inside a wooden cube" frames when the player teleports
		// (autoplay) or strafes next to a thick trunk.
		var head = WorldPosition + Vector3.Up * Tunables.PlayerEyeHeight;
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
		var home = starter.IsValid() ? starter.ResolvedPlayerSpawn : new Vector3( -1000f, 0f, 600f );
		TeleportTo( home, 0f );
		Log.Info( "[Player] Teleport home" );
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

		// Combo chain Valheim Attack.cs lignes 401-403 : si gap > 0.2s ou chain
		// déjà au max, reset à 0. Sinon increment au level suivant.
		if ( (float)_timeSinceLastSwing > Tunables.ChopComboWindow
			|| ChainLevel >= Tunables.ChopComboMaxLevels - 1 )
		{
			ChainLevel = 0;
		}
		else
		{
			ChainLevel++;
		}
		_timeSinceLastSwing = 0f;

		_pendingForward = EyeForwardFlat();
		_phase = SwingPhase.WindUp;
		_phaseTime = 0f;

		_fovOffset += Tunables.SwingFovPunch * 0.4f;
		// No chip burst at swing-start anymore (Thomas 2026-05-21 : "quand on
		// chop dans le vide on a quand même des wood chips"). Chips only fire
		// on actual impact, via ApplyImpactFeedback.
		TriggerAttackAnim();
		// Pitch swing sound varies par chain level pour audio feedback (level 0
		// "ah", level 1 "uhh", level 2 "HEH" — hint sonore que le combo monte).
		float swingPitchMul = 1f + 0.10f * ChainLevel;
		var swingPos = Camera.IsValid()
			? Camera.WorldPosition
			: WorldPosition + Vector3.Up * Tunables.PlayerEyeHeight;
		Sfx.Play( "sounds/swing.sound", swingPos,
			volume: 1.00f, pitchMin: 1.28f * swingPitchMul, pitchMax: 1.52f * swingPitchMul );
	}

	private void TickWindUp()
	{
		_phaseTime += Time.Delta;
		if ( _phaseTime < Tunables.SwingWindUpDuration ) return;

		var origin = WorldPosition + Vector3.Up * Tunables.PlayerEyeHeight;
		var forward = EyeForwardFlat();
		var hit = PickCameraAimTarget( out var impactPoint )
			?? ChooseSwingTarget( origin, forward, out impactPoint );

		if ( hit is not null && hit.IsValid() )
		{
			if ( impactPoint == default )
				impactPoint = hit.WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.3f);
			var hitDir = HitDirection( origin, forward, impactPoint );
			int basePower = GameState.Get( Scene )?.ChopPower ?? 1;
			// Final hit du combo (last level) = damage × FinalDamageMul.
			// Valheim Attack.cs ligne 1094-1098 : `hitData.m_damage.Modify(m_lastChainDamageMultiplier)`.
			bool isFinalHit = ChainLevel == Tunables.ChopComboMaxLevels - 1;
			int chopPower = isFinalHit
				? Math.Max( 1, (int)MathF.Ceiling( basePower * Tunables.ChopComboFinalDamageMul ) )
				: basePower;
			bool tooHard = IsTooHardTreeHit( hit );
			// Pass tree tint pour que les chips reflètent la couleur du bois
			// frappé (Valheim chips wood-type colored). Pitch SFX par kind :
			// Sapling = high crackle (×1.25), Veteran = deep thunk (×0.75),
			// Brittle = dry (×1.10), Normal = baseline.
			Color? chipTint = null;
			float chopPitchMul = 1f;
			bool willBreakTree = false;
			bool isLogHit = false;
			float damageFrac = 1f;
			if ( hit is Tree treeHit )
			{
				chipTint = treeHit.TrunkTint;
				chopPitchMul = Tunables.TreeKindChopPitchMul[(int)treeHit.Kind];
				isLogHit = treeHit.IsFallenLog;
				willBreakTree = !tooHard && treeHit.ChopsRemaining <= chopPower;
				damageFrac = !tooHard
					? (chopPower / MathF.Max( 1f, treeHit.ChopsRemaining )).Clamp( 0.2f, 1.5f )
					: 0.2f;
			}
			if ( tooHard )
			{
				if ( hit is Tree t ) t.Chop( hitDir, chopPower, impactPoint );
				else hit.Chop( hitDir, impactPoint );
				ApplyTooHardFeedback( impactPoint, hitDir );
			}
			else
			{
				ApplyImpactFeedback( impactPoint, hitDir, chipTint, chopPitchMul, willBreakTree, damageFrac, isLogHit );
				if ( hit is Tree t ) t.Chop( hitDir, chopPower, impactPoint );
				else hit.Chop( hitDir, impactPoint );
			}
		}
		else
		{
			_fovOffset += Tunables.SwingFovPunch * 0.25f;
			Sfx.Play( "sounds/swing.sound", origin + forward * 70f,
				volume: 0.38f, pitchMin: 1.55f, pitchMax: 1.85f );
		}

		_phase = SwingPhase.Recovery;
		_phaseTime = 0f;
	}

	private void TickRecovery()
	{
		_phaseTime += Time.Delta;
		float speedMul = GameState.Get( Scene )?.SwingSpeedMultiplier ?? 1f;
		if ( _phaseTime >= Tunables.SwingRecoveryDuration * speedMul ) _phase = SwingPhase.Idle;
	}

	private void ApplyImpactFeedback( Vector3 contactPoint, Vector3 forward, Color? chipTint = null, float chopPitchMul = 1f, bool willBreakTree = false, float damageFrac = 1f, bool isLogHit = false )
	{
		// Final hit du combo = burst plus dense + amplified hit feedback. Match
		// Valheim Attack.cs ligne 1097 où pushForce × 1.2 et damage × 2 — on
		// boost aussi la sensation visuelle.
		bool isFinalHit = ChainLevel == Tunables.ChopComboMaxLevels - 1;
		bool heavyHit = isFinalHit || willBreakTree;
		float damageFeel = damageFrac.Clamp( 0.35f, 1.5f );
		int chipCount = isFinalHit
			? (int)(Tunables.ChipBurstCount * 1.6f)
			: (int)(Tunables.ChipBurstCount * MathX.Lerp( 0.85f, 1.25f, (damageFeel - 0.35f) / 1.15f ));
		if ( willBreakTree ) chipCount = Math.Max( chipCount, (int)(Tunables.ChipBurstCount * 1.35f) );
		var chipDir = isLogHit ? (forward.WithZ( 0f ) + Vector3.Up * 0.18f).Normal : forward;
		ChipBurst.Spawn( Scene, contactPoint, chipDir, isLogHit ? (int)(chipCount * 1.15f) : chipCount, chipTint );
		_fovOffset += heavyHit
			? Tunables.SwingFovPunch * 1.5f
			: Tunables.SwingFovPunch * damageFeel;
		Scene.TimeScale = Tunables.HitstopTimeScale;
		_hitstopFramesLeft = heavyHit ? Tunables.HitstopFrames + 2 : Tunables.HitstopFrames;
		// Valheim split hitEffect/destroyedEffect: every valid chop gets a
		// sharp hit effect at hit.m_point; cracks stay reserved for breakage.
		float vol = heavyHit ? 1.10f : MathX.Lerp( 0.85f, 1.0f, damageFeel );
		float pitchShift = heavyHit ? 0.85f : 1.0f;
		// Per-kind pitch mul: Sapling high crackle, Veteran deep thunk.
		float kindPitch = pitchShift * chopPitchMul;
		float logPitch = isLogHit ? 0.82f : 1f;
		Sfx.Play( "sounds/axe_hit_wood.sound", contactPoint, volume: (isLogHit ? 1.35f : 1.20f) * vol, pitchMin: 0.88f * kindPitch * logPitch, pitchMax: 1.02f * kindPitch * logPitch );
		Sfx.Play( "sounds/chop_wood.sound", contactPoint, volume: (isLogHit ? 0.48f : 0.36f) * vol, pitchMin: 0.95f * kindPitch * logPitch, pitchMax: 1.18f * kindPitch * logPitch );
		if ( heavyHit )
		{
			Sfx.Play( "sounds/log_break.sound", contactPoint, volume: 0.35f * vol, pitchMin: 1.25f * kindPitch, pitchMax: 1.45f * kindPitch );
		}
		// Positional camera shake — kept very subtle after the Phase D revert.
		// Was 1.6 + power×0.2 (up to 3.2u) → felt "shake de fou" ; halved.
		// Final hit boost le shake aussi.
		int power = GameState.Get( Scene )?.ChopPower ?? 1;
		float shakeAmp = 0.7f + MathF.Min( power * 0.08f, 0.7f );
		shakeAmp *= MathX.Lerp( 0.85f, 1.25f, damageFeel );
		if ( heavyHit ) shakeAmp *= 1.4f;
		AddCameraShake( shakeAmp );
	}

	private void ApplyTooHardFeedback( Vector3 contactPoint, Vector3 forward )
	{
		ChipBurst.Spawn( Scene, contactPoint, forward, Tunables.ChipBurstCount / 2, new Color( 0.55f, 0.50f, 0.42f, 1f ) );
		_fovOffset += Tunables.SwingFovPunch * 0.45f;
		Scene.TimeScale = Tunables.HitstopTimeScale;
		_hitstopFramesLeft = 1;
		Sfx.Play( "sounds/axe_too_weak.sound", contactPoint, volume: 0.95f, pitchMin: 0.75f, pitchMax: 0.95f );
		Sfx.Play( "sounds/axe_hit_wood.sound", contactPoint, volume: 0.45f, pitchMin: 0.55f, pitchMax: 0.70f );
		AddCameraShake( 0.45f );
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
		var origin = WorldPosition + Vector3.Up * Tunables.PlayerEyeHeight;
		var forward = EyeForwardFlat();
		var hit = ChooseSwingTarget( origin, forward, out var impactPoint );
		if ( hit is null ) return null;
		int chopPower = GameState.Get( Scene )?.ChopPower ?? 1;
		var hitDir = HitDirection( origin, forward, impactPoint );
		if ( hit is Tree t ) t.Chop( hitDir, chopPower, impactPoint );
		else hit.Chop( hitDir, impactPoint );
		return hit;
	}

	public IChoppable DebugSwingVerbose()
	{
		var origin = WorldPosition + Vector3.Up * Tunables.PlayerEyeHeight;
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
			if ( dist > SwingRangeNow() ) { droppedRange++; continue; }
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) { droppedCone++; continue; }
			var score = dot - dist * 0.005f;
			if ( score > bestScore ) { bestScore = score; best = c; }
		}
		Log.Info( $"[TC_TEST] DebugSwingVerbose considered={considered} droppedValid={droppedValid} droppedTool={droppedTool} droppedRange={droppedRange} droppedCone={droppedCone} best={(best == null ? "null" : best.GetType().Name)}" );
		if ( best is null ) return null;
		int chopPower = GameState.Get( Scene )?.ChopPower ?? 1;
		var impactPoint = GetFallbackHitPoint( best, origin );
		var hitDir = HitDirection( origin, forward, impactPoint );
		if ( best is Tree t ) t.Chop( hitDir, chopPower, impactPoint );
		else best.Chop( hitDir, impactPoint );
		return best;
	}

	// Effective swing range — base × per-tool Range sub-stat multiplier.
	private float SwingRangeNow() => Tunables.SwingRange * (GameState.Get( Scene )?.SwingRangeMultiplier ?? 1f);

	private IChoppable ChooseSwingTarget( Vector3 origin, Vector3 forward, out Vector3 hitPoint )
	{
		hitPoint = default;
		IChoppable best = null;
		var bestScore = float.NegativeInfinity;
		float range = SwingRangeNow();
		foreach ( var c in Scene.GetAllComponents<IChoppable>() )
		{
			if ( !c.IsValid() || !c.AcceptsTool( ToolKind.Axe ) ) continue;
			var to = c.WorldPosition - origin;
			to.z = 0f;
			var dist = to.Length;
			if ( dist > range ) continue;
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) continue;
			var score = dot - dist * 0.005f;
			if ( score > bestScore ) { bestScore = score; best = c; }
		}
		if ( best is not null ) hitPoint = GetFallbackHitPoint( best, origin );
		return best;
	}

	private static Vector3 GetFallbackHitPoint( IChoppable target, Vector3 origin )
	{
		if ( target is Tree tree ) return tree.GetChopPointFrom( origin );
		return target.WorldPosition;
	}

	private static Vector3 HitDirection( Vector3 origin, Vector3 fallbackForward, Vector3 impactPoint )
	{
		var dir = (impactPoint - origin).WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = fallbackForward.WithZ( 0f );
		return dir.LengthSquared < 0.001f ? Vector3.Forward : dir.Normal;
	}

	private bool IsTooHardTreeHit( IChoppable target )
	{
		if ( target is not Tree tree || !tree.IsStanding ) return false;
		var state = GameState.Get( Scene );
		int axeTier = state.IsValid() ? state.AxeTier : 0;
		return axeTier < Tunables.TreeKindMinAxeTier[(int)tree.Kind];
	}

	// HUD hit-or-miss indicator : true when there's a chop target under the
	// reticle within melee range. Re-evaluated at ~20Hz by TickAimPreview,
	// so reading this from WoodHud each frame is cheap (no extra trace).
	public bool HasAimTarget => _previewTree.IsValid();
	public bool AimTargetIsLog => _previewTree.IsValid() && _previewTree.IsFallenLog;
	public bool AimTargetTooHard
	{
		get
		{
			if ( !_previewTree.IsValid() || !_previewTree.IsStanding ) return false;
			var state = GameState.Get( Scene );
			int axeTier = state.IsValid() ? state.AxeTier : 0;
			return axeTier < Tunables.TreeKindMinAxeTier[(int)_previewTree.Kind];
		}
	}
	public string AimTargetLabel
	{
		get
		{
			if ( !_previewTree.IsValid() ) return "";
			if ( _previewTree.IsFallenLog ) return $"CHOP LOG · {_previewTree.ChopsRemaining}";
			return AimTargetTooHard ? "AXE TOO WEAK" : $"CHOP {_previewTree.Kind.ToString().ToUpper()} · {_previewTree.ChopsRemaining}";
		}
	}

	public IChoppable PickCameraAimTarget( out Vector3 hitPos )
	{
		hitPos = default;
		if ( !Camera.IsValid() ) return null;
		var origin = WorldPosition + Vector3.Up * Tunables.PlayerEyeHeight;
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
		var playerToHit = (hp - origin).WithZ( 0f );
		if ( playerToHit.Length > SwingRangeNow() ) return null;

		hitPos = hp;
		return ic;
	}
}

public interface IChoppable
{
	Vector3 WorldPosition { get; }
	bool IsValid();
	// hitPoint = where the axe actually contacted, in world coords. Used for
	// impulse application matching Valheim's Destructible/TreeLog pattern
	// (hit.m_dir * hit.m_pushForce at hit.m_point). Callers can pass default
	// (Vector3.Zero) to fall back to WorldPosition + a fixed offset inside
	// the implementor.
	void Chop( Vector3 direction, Vector3 hitPoint );
	// Valheim IDestructible.Damage(HitData) — universal damage entrypoint.
	// Default implementation forwards to legacy Chop signature pour
	// rétrocompat. Components peuvent override pour utiliser HitData.ChopPower,
	// ToolTier, Skill, etc.
	void Damage( HitData hit )
	{
		Chop( hit.Direction, hit.HitPoint );
	}
	bool AcceptsTool( ToolKind tool );
}

public enum ToolKind { Axe }
