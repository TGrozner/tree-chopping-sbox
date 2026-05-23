namespace TreeChopping;

// Deterministic visual capture scenario — drives the player through a clean
// "idle → swing → tree fall → wood banner" sequence so an orchestrator
// (Claude session via the Sbox-Claude bridge) can grab 12-15 screenshots at
// fixed intervals and judge swing *feel* frame-by-frame.
//
// Activation paths :
//   A) Launch sbox-dev/sbox-server with +tc_filmstrip 1 → auto-active at boot.
//   B) During Play, an orchestrator flips FilmStrip.Active=true via the
//      bridge (`mcp__sbox__set_runtime_property name=Active`). Setting it
//      back to false (or letting Phase reach Done) ends the run.
//
// `Phase` is exposed as a [ReadOnly] runtime property so orchestrators poll
// for state transitions instead of relying on wall-clock estimates — which
// matters because tree fall+landing time depends on Kind (sapling vs veteran).
//
// Cookbook for future sessions in CLAUDE.md → "Visual cycle — filmstrip".
public sealed class FilmStrip : Component
{
	public enum FilmPhase { Init, Setup, Ready, Swinging, Falling, Landed, ChopTrunk, Pickup, Done }

	[ConVar( "tc_filmstrip", Help = "Auto-activate the FilmStrip director on bootstrap for visual capture." )]
	public static bool BootEnable { get; set; }

	// Used by GameState.Save to gate persistence so a filmstrip run never
	// clobbers the user's progress.json — checks both the boot ConVar AND
	// any FilmStrip instance flipped Active at runtime via the bridge.
	public static bool IsAnyActive( Scene scene )
	{
		if ( BootEnable ) return true;
		if ( scene is null ) return false;
		foreach ( var fs in scene.GetAllComponents<FilmStrip>() )
			if ( fs.IsValid() && fs.Active ) return true;
		return false;
	}

	[Property] public new bool Active { get; set; }
	[Property, ReadOnly] public FilmPhase Phase { get; set; } = FilmPhase.Init;
	[Property, ReadOnly] public float Elapsed { get; set; }
	[Property, ReadOnly] public int WoodAtFinish { get; set; }
	[Property, ReadOnly] public int SwingsFired { get; set; }
	[Property, ReadOnly] public int HitsConfirmed { get; set; }
	[Property, ReadOnly] public int MissedSwings { get; set; }
	[Property, ReadOnly] public int PhysicsAnomalies { get; set; }
	[Property, ReadOnly] public float WorstLogPenetration { get; set; }
	[Property, ReadOnly] public float WorstLogFloat { get; set; }
	[Property, ReadOnly] public float WorstLogVerticalUpDot { get; set; }
	[Property, ReadOnly] public float MaxLogSpeed { get; set; }
	[Property, ReadOnly] public float MaxLogAngularSpeed { get; set; }
	[Property, ReadOnly] public string WorstPhysicsIssue { get; set; } = "";
	// ResetState wipes Wood/tiers so the captured swing reads as "tier-0 starter
	// at a sapling" — the cleanest first impression. Save() is gated on
	// IsActiveRequest so this doesn't touch the user's persisted progress.
	[Property] public bool ResetState { get; set; } = true;
	// Kind ciblé pour ce cycle de capture. Setup phase pick le Tree le plus proche
	// matching ce kind ; fallback au plus proche standing si aucun match. Permet
	// d'orchestrer multi-scénario (Sapling/Normal/Veteran/Brittle) en flippant
	// cette property entre 2 cycles Active=false→true.
	[Property] public TreeKind TargetKind { get; set; } = TreeKind.Sapling;
	// Review capture should be repeatable and readable: by default each run
	// spawns a fresh target instead of reusing whatever crowded tree is nearest.
	[Property] public bool SpawnFreshTarget { get; set; } = true;
	[Property] public bool ApproachPreview { get; set; } = true;
	[Property] public bool ForceTooHard { get; set; }
	// Audio audit hook : quand true, chaque Sfx.Play écrit dans FileSystem.Data/audio_log.txt.
	// Permet d'examiner Bash-side les events audio firés pendant ce cycle.
	[Property] public bool AudioLog { get; set; }

	private AxeController _axe;
	private GameState _state;
	private Tree _target;
	private FallenLog _targetLog;
	private Vector3 _targetPos;
	private Vector3 _targetDir = Vector3.Forward;
	private int _frame;
	private TimeSince _phaseTime;
	private TimeSince _totalTime;
	private TimeSince _readyStart = 999f;
	private TimeSince _lastReSwing = 999f;
	private TimeSince _lastTelemetry = 999f;
	private TimeSince _timeSinceSwingRequest = 999f;
	private int _hpAtLastSwing;
	private bool _awaitingHitConfirm;
	private bool _swingTargetWasLog;
	private bool _approachParkedClose;
	private bool _wasActive;
	private readonly HashSet<string> _physicsAnomalyKeys = new();

	protected override void OnAwake()
	{
		if ( BootEnable ) Active = true;
		Log.Info( $"[TC_FILM] FilmStrip awake (boot active={BootEnable})" );
	}

	protected override void OnFixedUpdate()
	{
		Elapsed = (float)_totalTime;
		if ( !Active )
		{
			if ( _wasActive ) ResetRunState();
			_wasActive = false;
			return;
		}

		// Audio audit : only the active filmstrip owns Sfx.DebugLog. Inactive
		// directors must not stomp SelfTest or other debug captures.
		Sfx.DebugLog = AudioLog;

		if ( !_wasActive )
		{
			ResetRunState();
			_wasActive = true;
		}
		_frame++;
		EmitTelemetry();

		switch ( Phase )
		{
			case FilmPhase.Init: TickInit(); break;
			case FilmPhase.Setup: TickSetup(); break;
			case FilmPhase.Ready: TickReady(); break;
			case FilmPhase.Swinging: TickSwinging(); break;
			case FilmPhase.Falling: TickFalling(); break;
			case FilmPhase.Landed: TickLanded(); break;
			case FilmPhase.ChopTrunk: TickChopTrunk(); break;
			case FilmPhase.Pickup: TickPickup(); break;
			case FilmPhase.Done: break;
		}
	}

	private void ResetRunState()
	{
		Phase = FilmPhase.Init;
		Elapsed = 0f;
		WoodAtFinish = 0;
		SwingsFired = 0;
		HitsConfirmed = 0;
		MissedSwings = 0;
		PhysicsAnomalies = 0;
		WorstLogPenetration = 0f;
		WorstLogFloat = 0f;
		WorstLogVerticalUpDot = 0f;
		MaxLogSpeed = 0f;
		MaxLogAngularSpeed = 0f;
		WorstPhysicsIssue = "";
		_physicsAnomalyKeys.Clear();
		_frame = 0;
		_phaseTime = 0f;
		_totalTime = 0f;
		_readyStart = 999f;
		_lastReSwing = 999f;
		_lastTelemetry = 999f;
		_timeSinceSwingRequest = 999f;
		_hpAtLastSwing = 0;
		_awaitingHitConfirm = false;
		_approachParkedClose = false;
		_target = null;
		_targetLog = null;
		_targetPos = Vector3.Zero;
		_targetDir = Vector3.Forward;
		Sfx.DebugLog = false;
	}

	private void Transition( FilmPhase next )
	{
		Log.Info( $"[TC_FILM] phase {Phase} -> {next} (t={(float)_totalTime:F2}s)" );
		Phase = next;
		_phaseTime = 0f;
	}

	private void TickInit()
	{
		// Wait a few ticks so SceneStarter has time to spawn player + forest.
		if ( _frame < 10 ) return;
		_axe = Scene.GetAllComponents<AxeController>().FirstOrDefault();
		_state = GameState.Get( Scene );
		if ( !_axe.IsValid() || !_state.IsValid() )
		{
			Log.Error( $"[TC_FILM] missing entities player={_axe.IsValid()} state={_state.IsValid()}" );
			Phase = FilmPhase.Done;
			return;
		}
		if ( ResetState ) _state.ResetForTest();
		_totalTime = 0f;
		SwingsFired = 0;
		// Clear le log audio au début pour un fichier propre par cycle.
		if ( AudioLog ) Sfx.ClearAudioLog();
		Transition( FilmPhase.Setup );
	}

	private void TickSetup()
	{
		// Pick le Tree le plus proche matching TargetKind (Property runtime-settable
		// pour multi-scénario). Fallback : si aucun match, on spawn-en un fresh à
		// proximité du player pour garantir la capture (utile pour Veteran/Brittle
		// qui sont rares près du spawn).
		_target = null;
		if ( !SpawnFreshTarget )
		{
			_target = Scene.GetAllComponents<Tree>()
				.Where( t => t.IsValid() && t.IsStanding && t.Kind == TargetKind )
				.OrderBy( t => _axe.WorldPosition.Distance( t.WorldPosition ) )
				.FirstOrDefault();
		}
		if ( !_target.IsValid() )
		{
			// Spawn-on-demand : pose un tree du TargetKind dans le starter field.
			var starter = Scene.GetAllComponents<SceneStarter>().FirstOrDefault();
			var basePos = starter.IsValid() ? starter.ResolvedPlayerSpawn : _axe.WorldPosition;
			float forwardOffset = starter.IsValid() ? starter.SpawnPadRadius + 520f : 1450f;
			var spawnPos = basePos + Vector3.Forward * forwardOffset;
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out float groundZ ) )
			{
				spawnPos = spawnPos.WithZ( groundZ );
			}
			ClearCaptureLane( spawnPos, 900f );
			_target = Tree.SpawnAt( Scene, spawnPos, biomeDifficulty: 0.5f, forceKind: TargetKind );
			Log.Info( $"[TC_FILM] spawned-on-demand {TargetKind} at {spawnPos}" );
		}
		// Pump l'axe tier pour matcher MinAxeTier du target kind (sinon TooHard
		// gate bloque les chops et le fell n'arrive jamais). Skip si ResetState=false
		// ou si on a déjà le tier suffisant.
		int neededTier = Tunables.TreeKindMinAxeTier[(int)_target.Kind];
		if ( !ForceTooHard && _state.AxeTier < neededTier )
		{
			int wood = 0, fw = 0, cw = 0;
			for ( int i = _state.AxeTier + 1; i <= neededTier; i++ )
			{
				var r = Tunables.AxeTierCostsByType[i];
				wood += r[0]; fw += r[1]; cw += r[2];
			}
			_state.AddWood( wood );
			if ( fw > 0 ) { _state.AddBackpack( fw, WoodType.Finewood ); _state.TryDeposit(); }
			if ( cw > 0 ) { _state.AddBackpack( cw, WoodType.CoreWood ); _state.TryDeposit(); }
			while ( _state.AxeTier < neededTier && _state.TryUpgradeAxe() ) { }
			Log.Info( $"[TC_FILM] pumped AxeTier → {_state.AxeTier} pour {_target.Kind} (needed {neededTier})" );
		}
		if ( !_target.IsValid() )
		{
			Log.Error( "[TC_FILM] no standing tree" );
			Phase = FilmPhase.Done;
			return;
		}
		int expectedChops = ExpectedValheimStandingChops( _target.Kind );
		if ( _target.ChopsRemaining != expectedChops )
			RecordPhysicsAnomaly( _target.Kind, "wrong_hp", $"hp={_target.ChopsRemaining} expected={expectedChops}" );
		var dir = (_target.WorldPosition - _axe.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 1f ) dir = Vector3.Forward;
		dir = dir.Normal;
		_targetPos = _target.WorldPosition;
		_targetDir = dir;
		var side = Vector3.Cross( Vector3.Up, dir );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		side = side.Normal;
		float readyDistance = ChopSurfaceStandOff() + StandingHalfWidth( _target );
		if ( ApproachPreview ) readyDistance += 150f;
		var pos = _target.WorldPosition
			- dir * readyDistance
			+ side * ParkSideOffsetForKind( _target.Kind )
			+ Vector3.Up * 40f;
		var look = (_target.WorldPosition - pos).WithZ( 0f );
		if ( look.LengthSquared < 0.001f ) look = dir;
		float yaw = Rotation.LookAt( look.Normal ).Yaw();
		_axe.TeleportTo( pos, yaw );

		Log.Info( $"[TC_FILM] SETUP target={_target.WorldPosition} kind={_target.Kind} chops={_target.ChopsRemaining}" );
		_readyStart = 0f;
		Transition( FilmPhase.Ready );
	}

	private static int ExpectedValheimStandingChops( TreeKind kind )
	{
		return kind switch
		{
			TreeKind.Normal => 8,
			TreeKind.Sapling => 4,
			TreeKind.Veteran => 20,
			TreeKind.Brittle => 12,
			_ => 1
		};
	}

	private void TickReady()
	{
		// Linger 0.6s on the idle pose so the orchestrator captures a clean
		// "before" frame at the head of the strip. This is what makes the
		// first frame recognizable as the baseline.
		if ( ApproachPreview && !_approachParkedClose && (float)_readyStart > 0.32f )
		{
			ParkForStandingTarget( close: false );
			_approachParkedClose = true;
			Log.Info( "[TC_FILM] approach preview -> chop distance" );
		}
		if ( (float)_readyStart < (ApproachPreview ? 0.85f : 0.6f) ) return;
		RequestSwingAtTarget();
		Log.Info( "[TC_FILM] SWING #1 triggered" );
		Transition( FilmPhase.Swinging );
	}

	private void TickSwinging()
	{
		ConfirmPendingHit( allowMiss: ForceTooHard );

		if ( !_target.IsValid() )
		{
			_targetLog = FindNearestFallenLog( _targetPos, 900f, requireChoppable: false );
			if ( _targetLog.IsValid() )
			{
				Transition( FilmPhase.Falling );
				return;
			}
			Phase = FilmPhase.Done;
			return;
		}

		if ( _target.IsFalling || !_target.IsStanding )
		{
			_targetLog = _target.SpawnedLog;
			if ( !_targetLog.IsValid() )
				_targetLog = FindNearestFallenLog( _targetPos, 900f, requireChoppable: false );
			Transition( FilmPhase.Falling );
			return;
		}

		if ( ForceTooHard && SwingsFired >= 3 )
		{
			if ( (float)_lastReSwing < 0.85f ) return;
			Log.Info( $"[TC_FILM] TOOHARD COMPLETE kind={_target.Kind} hp={_target.ChopsRemaining} axeTier={_state.AxeTier}" );
			Phase = FilmPhase.Done;
			return;
		}

		// Multi-chop tree (Normal/Veteran/Brittle) : re-fire swings every 0.75s
		// (above WindUp+Recovery total) until ChopsRemaining hits 0.
		if ( _awaitingHitConfirm ) return;
		if ( (float)_lastReSwing < 0.75f ) return;
		if ( !_axe.IsSwingIdle ) return;
		if ( SwingsFired >= 42 )
		{
			Log.Warning( "[TC_FILM] gave up after 42 swings - chops left likely > expected" );
			Phase = FilmPhase.Done;
			return;
		}
		RequestSwingAtTarget();
		Log.Info( $"[TC_FILM] SWING #{SwingsFired} (chops left={_target.ChopsRemaining})" );
	}

	private void TickFalling()
	{
		ConfirmPendingHit( allowMiss: false );
		if ( !_targetLog.IsValid() )
			_targetLog = FindNearestFallenLog( _targetPos, 900f, requireChoppable: false );
		if ( _targetLog.IsValid() )
		{
			if ( _targetLog.IsFallenLog )
				Transition( FilmPhase.Landed );
			return;
		}
		if ( !_target.IsValid() || (!_target.IsStanding && !_target.IsFalling) )
		{
			Transition( FilmPhase.Landed );
			return;
		}
		if ( (float)_phaseTime > 6f )
		{
			Log.Warning( "[TC_FILM] falling phase stuck >6s — aborting" );
			Transition( FilmPhase.Landed );
		}
	}

	private void TickLanded()
	{
		// Linger so impact dust/chips have time to read.
		if ( (float)_phaseTime < 1.5f ) return;
		_lastReSwing = 0.999f;
		Log.Info( "[TC_FILM] LANDED → chopping trunk" );
		Transition( FilmPhase.ChopTrunk );
	}

	// Chop le landed trunk jusqu'à ce que SplitIntoLogs détruise la Tree :
	// le split donne soit des smaller landed logs, soit des items directs
	// (Valheim TreeLog.Destroy pattern). Pickup quand il ne reste plus de log.
	private void TickChopTrunk()
	{
		if ( !_targetLog.IsValid() || !_targetLog.IsFallenLog )
		{
			if ( _awaitingHitConfirm )
			{
				HitsConfirmed++;
				_awaitingHitConfirm = false;
			}
			var nextLog = FindNearestFallenLog( _targetPos, 900f, requireChoppable: true );
			if ( nextLog.IsValid() )
			{
				_targetLog = nextLog;
				_lastReSwing = 0f;
				Log.Info( "[TC_FILM] trunk split -> smaller-log phase" );
				return;
			}
			Log.Info( "[TC_FILM] trunk split → pickup phase" );
			_lastReSwing = 0.999f;
			Transition( FilmPhase.Pickup );
			return;
		}
		if ( (float)_phaseTime > 24f )
		{
			Log.Warning( "[TC_FILM] chop-trunk stuck >24s, aborting" );
			Phase = FilmPhase.Done;
			return;
		}
		if ( (float)_phaseTime < 0.12f )
		{
			ParkForLandedTrunk();
			return;
		}
		ConfirmPendingHit( allowMiss: false );
		if ( _awaitingHitConfirm ) return;
		if ( (float)_lastReSwing < 0.75f ) return;
		if ( !_axe.IsSwingIdle ) return;
		ParkForLandedTrunk();
		RequestSwingAtTarget();
		Log.Info( $"[TC_FILM] TRUNK SWING #{SwingsFired} (chops left={_targetLog.ChopsRemaining})" );
	}

	private void RequestSwingAtTarget()
	{
		if ( !_axe.IsValid() ) return;
		_swingTargetWasLog = _targetLog.IsValid();
		if ( _swingTargetWasLog ) _hpAtLastSwing = _targetLog.ChopsRemaining;
		else if ( _target.IsValid() ) _hpAtLastSwing = _target.ChopsRemaining;
		else return;
		_awaitingHitConfirm = true;
		_timeSinceSwingRequest = 0f;
		_axe.DebugRequestSwing = true;
		SwingsFired++;
		_lastReSwing = 0f;
	}

	private void ConfirmPendingHit( bool allowMiss )
	{
		if ( !_awaitingHitConfirm ) return;
		if ( !_axe.IsSwingIdle || (float)_timeSinceSwingRequest < 0.2f ) return;

		if ( _swingTargetWasLog && !_targetLog.IsValid() )
		{
			HitsConfirmed++;
			_awaitingHitConfirm = false;
			return;
		}

		if ( !_swingTargetWasLog && !_target.IsValid() )
		{
			HitsConfirmed++;
			_awaitingHitConfirm = false;
			return;
		}

		if ( _targetLog.IsValid() && _targetLog.ChopsRemaining < _hpAtLastSwing )
		{
			HitsConfirmed++;
			_awaitingHitConfirm = false;
			return;
		}

		if ( _target.IsValid() && _target.ChopsRemaining < _hpAtLastSwing )
		{
			HitsConfirmed++;
			_awaitingHitConfirm = false;
			return;
		}

		if ( allowMiss )
		{
			_awaitingHitConfirm = false;
			return;
		}

		MissedSwings++;
		Log.Warning( $"[TC_FILM] swing miss kind={TargetKind} hp={_hpAtLastSwing} phase={Phase}; reparking closer" );
		if ( _targetLog.IsValid() ) ParkForLandedTrunk();
		else ParkForStandingTarget( close: true );
		_awaitingHitConfirm = false;
		_lastReSwing = 0.999f;
	}

	private void TickPickup()
	{
		var items = Scene.GetAllComponents<WoodItem>().Where( i => i.IsValid() ).ToList();
		if ( items.Count == 0 )
		{
			WoodAtFinish = _state.IsValid()
				? _state.Wood + _state.Finewood + _state.CoreWood + _state.BackpackTotal
				: -1;
			Log.Info( $"[TC_FILM] PICKUP COMPLETE wood+bag={WoodAtFinish} swings={SwingsFired} elapsed={(float)_totalTime:F2}s" );
			Log.Info( $"[TC_FEEL_SUMMARY] kind={TargetKind} physAnom={PhysicsAnomalies} worstPen={WorstLogPenetration:F1} worstFloat={WorstLogFloat:F1} worstUpDot={WorstLogVerticalUpDot:F2} maxSpeed={MaxLogSpeed:F1} maxAng={MaxLogAngularSpeed:F2} issue={WorstPhysicsIssue}" );
			Phase = FilmPhase.Done;
			return;
		}
		if ( (float)_phaseTime > 8f )
		{
			Log.Warning( $"[TC_FILM] pickup stuck >8s, {items.Count} items still floating" );
			Phase = FilmPhase.Done;
			return;
		}
		// Walk-into : teleport player next to nearest item, let the proximity
		// magnet (55u radius) snap it. Repeat each tick on the new nearest
		// item until none remain.
		var item = items.OrderBy( i => _axe.WorldPosition.Distance( i.WorldPosition ) ).First();
		float d = _axe.WorldPosition.Distance( item.WorldPosition );
		if ( d > 40f )
		{
			var away = (_axe.WorldPosition - item.WorldPosition).WithZ( 0f );
			if ( away.LengthSquared < 0.001f ) away = -_targetDir.WithZ( 0f );
			if ( away.LengthSquared < 0.001f ) away = -Vector3.Forward;
			away = away.Normal;
			var pos = item.WorldPosition + away * 24f + Vector3.Up * 34f;
			if ( TryGetGroundZ( pos.x, pos.y, out float groundZ ) )
				pos = pos.WithZ( groundZ + 34f );
			_axe.TeleportTo( pos, Rotation.LookAt( -away ).Yaw() );
		}
	}

	private void EmitTelemetry()
	{
		if ( (float)_lastTelemetry < 0.02f ) return;
		_lastTelemetry = 0f;

		var logs = Scene.GetAllComponents<FallenLog>()
			.Where( l => l.IsValid() && (_targetPos.LengthSquared < 0.01f || l.LogCenter.Distance( _targetPos ) < 1400f) )
			.ToList();
		int fallingLogs = 0;
		int landedLogs = 0;
		int splitLogs = 0;
		float minClearance = 9999f;
		float maxClearance = -9999f;
		float worstUpDot = 0f;
		float maxSpeed = 0f;
		float maxAng = 0f;
		foreach ( var log in logs )
		{
			if ( log.IsFalling ) fallingLogs++;
			if ( log.IsFallenLog ) landedLogs++;
			if ( log.DebugSplitDepth > 0 ) splitLogs++;

			float clearance = log.DebugMinGroundClearance();
			float upDot = log.DebugAxisUpDot();
			float speed = log.Body.IsValid() ? log.Body.Velocity.Length : 0f;
			float verticalSpeed = log.Body.IsValid() ? log.Body.Velocity.z : 0f;
			float angular = log.Body.IsValid() ? log.Body.AngularVelocity.Length : 0f;
			minClearance = MathF.Min( minClearance, clearance );
			maxClearance = MathF.Max( maxClearance, clearance );
			worstUpDot = MathF.Max( worstUpDot, upDot );
			maxSpeed = MathF.Max( maxSpeed, speed );
			maxAng = MathF.Max( maxAng, angular );
			AuditFallingLogPhysics( log, clearance, speed, verticalSpeed, angular );
			AuditLogPhysics( log, clearance, upDot, speed, verticalSpeed, angular );
		}

		if ( !_target.IsValid() && !_targetLog.IsValid() )
		{
			Log.Info( $"[TC_FEEL] t={(float)_totalTime:F2} phase={Phase} target=none logs={logs.Count} landed={landedLogs} split={splitLogs} minClear={minClearance:F1} maxClear={maxClearance:F1} worstUpDot={worstUpDot:F2} maxSpeed={maxSpeed:F1} maxAng={maxAng:F2} physAnom={PhysicsAnomalies} swings={SwingsFired} hits={HitsConfirmed} misses={MissedSwings}" );
			return;
		}

		var rotation = _targetLog.IsValid() ? _targetLog.WorldRotation : _target.WorldRotation;
		var body = _targetLog.IsValid() ? _targetLog.Body : _target.Body;
		var kind = _targetLog.IsValid() ? _targetLog.Kind : _target.Kind;
		var hp = _targetLog.IsValid() ? _targetLog.ChopsRemaining : _target.ChopsRemaining;
		float targetUpDot = rotation.Up.Dot( Vector3.Up );
		float targetTiltDeg = MathF.Acos( targetUpDot.Clamp( -1f, 1f ) ) * 180f / MathF.PI;
		float targetSpeed = body.IsValid() ? body.Velocity.Length : 0f;
		float targetAng = body.IsValid() ? body.AngularVelocity.Length : 0f;
		Log.Info( $"[TC_FEEL] t={(float)_totalTime:F2} phase={Phase} kind={kind} hp={hp} tilt={targetTiltDeg:F1} speed={targetSpeed:F1} ang={targetAng:F2} logs={logs.Count} falling={fallingLogs} landed={landedLogs} split={splitLogs} minClear={minClearance:F1} maxClear={maxClearance:F1} worstUpDot={worstUpDot:F2} maxLogSpeed={maxSpeed:F1} maxLogAng={maxAng:F2} physAnom={PhysicsAnomalies} swings={SwingsFired} hits={HitsConfirmed} misses={MissedSwings}" );
	}

	private void AuditLogPhysics( FallenLog log, float clearance, float upDot, float speed, float verticalSpeed, float angular )
	{
		if ( !ShouldAuditLogPhysics( log ) ) return;

		bool split = log.DebugSplitDepth > 0;
		float age = log.DebugLandedAge;
		WorstLogPenetration = MathF.Max( WorstLogPenetration, MathF.Max( 0f, -clearance ) );
		WorstLogFloat = MathF.Max( WorstLogFloat, clearance );
		WorstLogVerticalUpDot = MathF.Max( WorstLogVerticalUpDot, upDot );
		MaxLogSpeed = MathF.Max( MaxLogSpeed, speed );
		MaxLogAngularSpeed = MathF.Max( MaxLogAngularSpeed, angular );
		float penetrationLimit = split ? Tunables.LogGroundSkin * 1.6f : Tunables.LogGroundSkin * 3.0f;
		float floatLimit = Tunables.TreeGroundedLandingClearance + (split ? 10f : 24f);
		float verticalLimit = split
			? (age < Tunables.SplitLogSpawnPoseSettleDuration + 0.15f ? 0.72f : Tunables.SplitLogMaxSpawnUpDot + 0.12f)
			: Tunables.TreeRestingTiltUpDotMax + 0.22f;

		if ( -clearance > penetrationLimit )
			RecordPhysicsAnomaly( log, "penetrating", $"clearance={clearance:F1} limit=-{penetrationLimit:F1} upDot={upDot:F2} speed={speed:F1} age={age:F2}" );
		if ( log.Body.IsValid() && !log.Body.Gravity )
			RecordPhysicsAnomaly( log, "gravity_off", $"clearance={clearance:F1} upDot={upDot:F2} speed={speed:F1} age={age:F2} split={split}" );
		if ( clearance > floatLimit )
			RecordPhysicsAnomaly( log, "floating", $"clearance={clearance:F1} limit={floatLimit:F1} upDot={upDot:F2} speed={speed:F1} age={age:F2}" );
		if ( upDot > verticalLimit )
			RecordPhysicsAnomaly( log, "vertical", $"upDot={upDot:F2} limit={verticalLimit:F2} clearance={clearance:F1} age={age:F2} split={split}" );
		if ( age > 0.6f && speed > Tunables.TreeLandedMaxSpeed * 1.2f )
			RecordPhysicsAnomaly( log, "too_fast", $"speed={speed:F1} limit={Tunables.TreeLandedMaxSpeed * 1.2f:F1} clearance={clearance:F1} age={age:F2}" );
		if ( age > 0.6f && verticalSpeed > Tunables.TreeLandedMaxVerticalSpeed * 1.2f )
			RecordPhysicsAnomaly( log, "upward_pop", $"vz={verticalSpeed:F1} limit={Tunables.TreeLandedMaxVerticalSpeed * 1.2f:F1} clearance={clearance:F1} age={age:F2}" );
		if ( age > 0.6f && angular > Tunables.TreeLandedMaxAngularSpeed * 1.35f )
			RecordPhysicsAnomaly( log, "too_spinny", $"ang={angular:F2} limit={Tunables.TreeLandedMaxAngularSpeed * 1.35f:F2} clearance={clearance:F1} age={age:F2}" );
	}

	private bool ShouldAuditLogPhysics( FallenLog log )
	{
		if ( !log.IsValid() || !log.IsFallenLog ) return false;
		if ( Phase != FilmPhase.Landed && Phase != FilmPhase.ChopTrunk && Phase != FilmPhase.Pickup && Phase != FilmPhase.Done ) return false;
		return log.DebugLandedAge >= 0.30f;
	}

	private void AuditFallingLogPhysics( FallenLog log, float clearance, float speed, float verticalSpeed, float angular )
	{
		if ( !log.IsValid() || !log.IsFalling ) return;
		if ( Phase != FilmPhase.Falling && Phase != FilmPhase.Landed && Phase != FilmPhase.ChopTrunk ) return;
		float age = log.DebugAge;
		if ( age < 1.2f ) return;
		if ( clearance > Tunables.TreeGroundedLandingClearance * 6f && speed < 18f && MathF.Abs( verticalSpeed ) < 8f )
			RecordPhysicsAnomaly( log, "falling_hover", $"clearance={clearance:F1} speed={speed:F1} vz={verticalSpeed:F1} ang={angular:F2} age={age:F2}" );
	}

	private void RecordPhysicsAnomaly( FallenLog log, string type, string details )
	{
		string key = $"{log.GetHashCode()}:{type}";
		if ( !_physicsAnomalyKeys.Add( key ) ) return;
		PhysicsAnomalies++;
		WorstPhysicsIssue = $"{type}:{details}";
		Log.Warning( $"[TC_FEEL_ANOMALY] type={type} phase={Phase} kind={log.Kind} splitDepth={log.DebugSplitDepth} {details}" );
	}

	private void RecordPhysicsAnomaly( TreeKind kind, string type, string details )
	{
		string key = $"{kind}:{type}";
		if ( !_physicsAnomalyKeys.Add( key ) ) return;
		PhysicsAnomalies++;
		WorstPhysicsIssue = $"{type}:{details}";
		Log.Warning( $"[TC_FEEL_ANOMALY] type={type} phase={Phase} kind={kind} {details}" );
	}

	private bool TryGetGroundZ( float x, float y, out float groundZ )
	{
		var top = new Vector3( x, y, 2000f );
		var bottom = new Vector3( x, y, -2000f );
		var hit = Scene.Trace.Ray( top, bottom ).WithAnyTags( "ground" ).Run();
		if ( !hit.Hit )
		{
			groundZ = 0f;
			return false;
		}

		groundZ = hit.EndPosition.z;
		return true;
	}

	private void ParkForLandedTrunk()
	{
		if ( !_axe.IsValid() || !_targetLog.IsValid() ) return;

		var hitPoint = _targetLog.GetChopPointFrom( _axe.WorldPosition );
		var dir = (hitPoint - _axe.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = (_targetLog.LogCenter - _axe.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = _targetDir.WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = Vector3.Forward;
		dir = dir.Normal;
		var pos = hitPoint - dir * ChopSurfaceStandOff() + Vector3.Up * 36f;
		if ( TryGetGroundZ( pos.x, pos.y, out float groundZ ) )
			pos = pos.WithZ( groundZ + 36f );
		hitPoint = _targetLog.GetChopPointFrom( pos );
		var look = (hitPoint - pos).WithZ( 0f );
		if ( look.LengthSquared < 0.001f ) look = dir;
		float yaw = Rotation.LookAt( look.Normal ).Yaw();
		_axe.TeleportTo( pos, yaw );
	}

	private void ParkForStandingTarget( bool close )
	{
		if ( !_axe.IsValid() || !_target.IsValid() ) return;

		var dir = _targetDir.WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = Vector3.Forward;
		dir = dir.Normal;
		var side = Vector3.Cross( Vector3.Up, dir );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		side = side.Normal;
		float distance = StandingHalfWidth( _target ) + ChopSurfaceStandOff();
		float sideOffset = close ? 0f : ParkSideOffsetForKind( _target.Kind ) * 0.25f;
		var pos = _target.WorldPosition
			- dir * distance
			+ side * sideOffset
			+ Vector3.Up * 40f;
		if ( TryGetGroundZ( pos.x, pos.y, out float groundZ ) )
			pos = pos.WithZ( groundZ + 40f );
		var hitPoint = _target.GetChopPointFrom( pos );
		var look = (hitPoint - pos).WithZ( 0f );
		if ( look.LengthSquared < 0.001f ) look = dir;
		_axe.TeleportTo( pos, Rotation.LookAt( look.Normal ).Yaw() );
	}

	private static float ChopSurfaceStandOff()
	{
		return MathF.Max( 18f, Tunables.SwingRange * 0.72f );
	}

	private static float StandingHalfWidth( Tree tree )
	{
		return tree.IsValid() ? MathF.Max( tree.TrunkWidth * 0.5f, Tunables.TreeRadius * 0.25f ) : Tunables.TreeRadius * 0.5f;
	}

	private static float LogHalfWidth( FallenLog log )
	{
		return log.IsValid() ? MathF.Max( log.TrunkWidth * 0.5f, Tunables.TreeRadius * 0.25f ) : Tunables.TreeRadius * 0.5f;
	}

	private static float ParkSideOffsetForKind( TreeKind kind )
	{
		return kind switch
		{
			TreeKind.Sapling => 20f,
			TreeKind.Veteran => 35f,
			_ => 30f
		};
	}

	private FallenLog FindNearestFallenLog( Vector3 pos, float radius, bool requireChoppable )
	{
		return Scene.GetAllComponents<FallenLog>()
			.Where( l => l.IsValid() && (!requireChoppable || l.IsFallenLog) && l.WorldPosition.Distance( pos ) < radius )
			.OrderBy( l => l.WorldPosition.Distance( pos ) )
			.FirstOrDefault();
	}

	private void ClearCaptureLane( Vector3 center, float radius )
	{
		var trees = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() && t.WorldPosition.WithZ( center.z ).Distance( center ) < radius )
			.ToList();

		foreach ( var tree in trees )
		{
			tree.GameObject?.Destroy();
		}

		if ( trees.Count > 0 )
		{
			Log.Info( $"[TC_FILM] cleared {trees.Count} nearby trees for capture lane" );
		}
	}
}
