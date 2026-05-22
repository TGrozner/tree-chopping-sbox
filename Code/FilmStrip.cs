namespace TreeChopping;

// Deterministic visual capture scenario — drives the beaver through a clean
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
	// Audio audit hook : quand true, chaque Sfx.Play écrit dans FileSystem.Data/audio_log.txt.
	// Permet d'examiner Bash-side les events audio firés pendant ce cycle.
	[Property] public bool AudioLog { get; set; }

	private BeaverController _beaver;
	private GameState _state;
	private Tree _target;
	private Vector3 _targetDir = Vector3.Forward;
	private int _frame;
	private TimeSince _phaseTime;
	private TimeSince _totalTime;
	private TimeSince _readyStart = 999f;
	private TimeSince _lastReSwing = 999f;
	private bool _wasActive;

	protected override void OnAwake()
	{
		if ( BootEnable ) Active = true;
		Log.Info( $"[TC_FILM] FilmStrip awake (boot active={BootEnable})" );
	}

	protected override void OnFixedUpdate()
	{
		Elapsed = (float)_totalTime;
		// Audio audit : pendant qu'AudioLog est true, Sfx.DebugLog est true →
		// chaque Sfx.Play écrit dans FileSystem.Data/audio_log.txt.
		Sfx.DebugLog = AudioLog && Active;

		if ( !Active )
		{
			if ( _wasActive ) ResetRunState();
			_wasActive = false;
			return;
		}

		if ( !_wasActive )
		{
			ResetRunState();
			_wasActive = true;
		}
		_frame++;

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
		_frame = 0;
		_phaseTime = 0f;
		_totalTime = 0f;
		_readyStart = 999f;
		_lastReSwing = 999f;
		_target = null;
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
		// Wait a few ticks so SceneStarter has time to spawn beaver + forest.
		if ( _frame < 10 ) return;
		_beaver = Scene.GetAllComponents<BeaverController>().FirstOrDefault();
		_state = GameState.Get( Scene );
		if ( !_beaver.IsValid() || !_state.IsValid() )
		{
			Log.Error( $"[TC_FILM] missing entities beaver={_beaver.IsValid()} state={_state.IsValid()}" );
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
		// proximité du beaver pour garantir la capture (utile pour Veteran/Brittle
		// qui sont rares près du spawn).
		_target = null;
		if ( !SpawnFreshTarget )
		{
			_target = Scene.GetAllComponents<Tree>()
				.Where( t => t.IsValid() && t.IsStanding && t.Kind == TargetKind )
				.OrderBy( t => _beaver.WorldPosition.Distance( t.WorldPosition ) )
				.FirstOrDefault();
		}
		if ( !_target.IsValid() )
		{
			// Spawn-on-demand : pose un tree du TargetKind à 200u devant le beaver.
			var starter = Scene.GetAllComponents<SceneStarter>().FirstOrDefault();
			var basePos = starter.IsValid() ? starter.ResolvedBeaverSpawn : _beaver.WorldPosition;
			var spawnPos = basePos + new Vector3( 0f, -1600f, 0f );
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
		if ( _state.AxeTier < neededTier )
		{
			int wood = 0, fw = 0, cw = 0;
			for ( int i = _state.AxeTier + 1; i <= neededTier; i++ )
			{
				var r = Tunables.AxeTierCostsByType[i];
				wood += r[0]; fw += r[1]; cw += r[2];
			}
			_state.AddWood( wood );
			if ( fw > 0 ) { _state.AddBackpack( fw, WoodType.Finewood ); _state.TrySell(); }
			if ( cw > 0 ) { _state.AddBackpack( cw, WoodType.CoreWood ); _state.TrySell(); }
			while ( _state.AxeTier < neededTier && _state.TryUpgradeAxe() ) { }
			Log.Info( $"[TC_FILM] pumped AxeTier → {_state.AxeTier} pour {_target.Kind} (needed {neededTier})" );
		}
		if ( !_target.IsValid() )
		{
			Log.Error( "[TC_FILM] no standing tree" );
			Phase = FilmPhase.Done;
			return;
		}
		var dir = (_target.WorldPosition - _beaver.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 1f ) dir = Vector3.Forward;
		dir = dir.Normal;
		_targetDir = dir;
		var pos = _target.WorldPosition - dir * 80f + Vector3.Up * 40f;
		float yaw = Rotation.LookAt( dir ).Yaw();
		_beaver.TeleportTo( pos, yaw );

		Log.Info( $"[TC_FILM] SETUP target={_target.WorldPosition} kind={_target.Kind} chops={_target.ChopsRemaining}" );
		_readyStart = 0f;
		Transition( FilmPhase.Ready );
	}

	private void TickReady()
	{
		// Linger 0.6s on the idle pose so the orchestrator captures a clean
		// "before" frame at the head of the strip. This is what makes the
		// first frame recognizable as the baseline.
		if ( (float)_readyStart < 0.6f ) return;
		_beaver.DebugRequestSwing = true;
		SwingsFired = 1;
		_lastReSwing = 0f;
		Log.Info( "[TC_FILM] SWING #1 triggered" );
		Transition( FilmPhase.Swinging );
	}

	private void TickSwinging()
	{
		if ( !_target.IsValid() ) { Phase = FilmPhase.Done; return; }

		if ( _target.IsFalling || !_target.IsStanding )
		{
			Transition( FilmPhase.Falling );
			return;
		}

		// Multi-chop tree (Normal/Veteran/Brittle) : re-fire swings every 0.75s
		// (above WindUp+Recovery total) until ChopsRemaining hits 0.
		if ( (float)_lastReSwing < 0.75f ) return;
		if ( SwingsFired >= 15 )
		{
			Log.Warning( "[TC_FILM] gave up after 15 swings — chops left likely > expected" );
			Phase = FilmPhase.Done;
			return;
		}
		_beaver.DebugRequestSwing = true;
		SwingsFired++;
		_lastReSwing = 0f;
		Log.Info( $"[TC_FILM] SWING #{SwingsFired} (chops left={_target.ChopsRemaining})" );
	}

	private void TickFalling()
	{
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
		// Linger so the cam shake / dust / snap have time to read.
		if ( (float)_phaseTime < 1.5f ) return;
		_lastReSwing = 0.999f;
		Log.Info( "[TC_FILM] LANDED → chopping trunk" );
		Transition( FilmPhase.ChopTrunk );
	}

	// Chop le landed trunk jusqu'à ce que SplitIntoLogs détruise la Tree —
	// les items spawnent direct (Valheim TreeLog.Destroy pattern), pas de
	// sub-log intermediate à chopper. Transition direct vers Pickup.
	private void TickChopTrunk()
	{
		if ( !_target.IsValid() )
		{
			Log.Info( "[TC_FILM] trunk split → pickup phase" );
			_lastReSwing = 0.999f;
			Transition( FilmPhase.Pickup );
			return;
		}
		if ( (float)_phaseTime > 8f )
		{
			Log.Warning( "[TC_FILM] chop-trunk stuck >8s, aborting" );
			Phase = FilmPhase.Done;
			return;
		}
		if ( (float)_lastReSwing < 0.75f ) return;
		ParkForLandedTrunk();
		_beaver.DebugRequestSwing = true;
		SwingsFired++;
		_lastReSwing = 0f;
		Log.Info( $"[TC_FILM] TRUNK SWING #{SwingsFired} (chops left={_target.ChopsRemaining})" );
	}

	private void TickPickup()
	{
		var items = Scene.GetAllComponents<WoodItem>().Where( i => i.IsValid() ).ToList();
		if ( items.Count == 0 )
		{
			WoodAtFinish = _state.IsValid() ? _state.Wood + _state.BackpackWood : -1;
			Log.Info( $"[TC_FILM] PICKUP COMPLETE wood+bag={WoodAtFinish} swings={SwingsFired} elapsed={(float)_totalTime:F2}s" );
			Phase = FilmPhase.Done;
			return;
		}
		if ( (float)_phaseTime > 8f )
		{
			Log.Warning( $"[TC_FILM] pickup stuck >8s, {items.Count} items still floating" );
			Phase = FilmPhase.Done;
			return;
		}
		// Walk-into : teleport beaver next to nearest item, let the proximity
		// magnet (55u radius) snap it. Repeat each tick on the new nearest
		// item until none remain.
		var item = items.OrderBy( i => _beaver.WorldPosition.Distance( i.WorldPosition ) ).First();
		float d = _beaver.WorldPosition.Distance( item.WorldPosition );
		if ( d > 40f )
		{
			_beaver.TeleportTo( item.WorldPosition + Vector3.Up * 30f, 0f );
		}
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
		if ( !_beaver.IsValid() || !_target.IsValid() ) return;

		var dir = _targetDir.WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = Vector3.Forward;
		dir = dir.Normal;
		var pos = _target.WorldPosition + dir * 80f + Vector3.Up * 40f;
		float yaw = Rotation.LookAt( -dir ).Yaw();
		_beaver.TeleportTo( pos, yaw );
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
