namespace TreeChopping;

// Headless test of the mow-the-lawn loop. Phases :
//   Init       — locate beaver + tree + game state
//   Approach   — park beaver in front of a target tree
//   Swing      — call DebugSwingVerbose until target tree fells
//   Verify     — assert : GameState.Wood increased, target tree is no longer standing
//
// ConVar tc_selftest=1 to enable. PowerShell harness greps [TC_TEST] markers.
public sealed class SelfTest : Component
{
	enum Phase { Init, Approach, Swing, Verify, Done }

	private Phase _phase = Phase.Init;
	private TimeSince _phaseTime;
	private int _frame;
	private BeaverController _beaver;
	private GameState _state;
	private Tree _targetTree;
	private Vector3 _targetTreePos;
	private int _woodBeforeSwings;

	private int _swingsFired;
	private TimeSince _lastSwingTime = 999f;

	[ConVar( "tc_selftest", Help = "Spawn TreeChopping.SelfTest on bootstrap to run the mow-the-lawn headless scenario." )]
	public static bool Enable { get; set; }

	public static bool IsActiveRequest() => Enable;

	protected override void OnAwake()
	{
		Log.Info( "[TC_TEST] SelfTest component awake" );
	}

	protected override void OnFixedUpdate()
	{
		_frame++;
		switch ( _phase )
		{
			case Phase.Init: TickInit(); break;
			case Phase.Approach: TickApproach(); break;
			case Phase.Swing: TickSwing(); break;
			case Phase.Verify: TickVerify(); break;
			case Phase.Done: break;
		}
	}

	private void Transition( Phase next )
	{
		PhaseOk( _phase );
		Log.Info( $"[TC_TEST] phase {_phase} -> {next} (t={_phaseTime:F2}s)" );
		_phase = next;
		_phaseTime = 0f;
	}

	private void PhaseOk( Phase phase )
	{
		int wood = _state.IsValid() ? _state.Wood : -1;
		int tier = _state.IsValid() ? _state.AxeTier : -1;
		int trees = Scene.GetAllComponents<Tree>().Count( t => t.IsValid() );
		Log.Info( $"[TC_TEST] PHASE_OK {phase} (wood={wood} tier={tier} trees={trees})" );
	}

	private void TickInit()
	{
		if ( _frame < 10 ) return;
		_beaver = Scene.GetAllComponents<BeaverController>().FirstOrDefault();
		_state = GameState.Get( Scene );

		// Reset state so we test from a known baseline regardless of prior
		// saves on disk.
		if ( _state.IsValid() ) _state.ResetForTest();

		// Pick a small tree (low ChopsRemaining) close to beaver for a quick
		// felling. Saplings (kind=1) have 1 chop ; preferred.
		_targetTree = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() && t.IsStanding )
			.OrderBy( t => (_beaver.IsValid() ? _beaver.WorldPosition : Vector3.Zero).Distance( t.WorldPosition ) )
			.FirstOrDefault();
		if ( !_beaver.IsValid() || !_state.IsValid() || !_targetTree.IsValid() )
		{
			Log.Error( $"[TC_TEST] FAIL: missing entities beaver={_beaver.IsValid()} state={_state.IsValid()} tree={_targetTree.IsValid()}" );
			Finish();
			return;
		}
		_targetTreePos = _targetTree.WorldPosition;
		_woodBeforeSwings = _state.Wood;
		Log.Info( $"[TC_TEST] INIT beaverPos={_beaver.WorldPosition} treePos={_targetTreePos} kind={_targetTree.Kind} chops={_targetTree.ChopsRemaining} wood={_state.Wood} tier={_state.AxeTier}" );
		Transition( Phase.Approach );
	}

	private void TickApproach()
	{
		ParkBeaverInFrontOfTarget();
		Log.Info( $"[TC_TEST] APPROACH parked beaverPos={_beaver.WorldPosition}" );
		Transition( Phase.Swing );
	}

	private void TickSwing()
	{
		if ( !_targetTree.IsValid() || !_targetTree.IsStanding )
		{
			Log.Info( $"[TC_TEST] target tree no longer standing (valid={_targetTree.IsValid()}) — proceeding to verify" );
			Transition( Phase.Verify );
			return;
		}

		// Throttle swings ~ once per 0.25s so the chop pipeline state machine
		// (WindUp 0.16s + Recovery 0.18s = 0.34s natural cooldown) has time
		// to cycle back to Idle.
		if ( (float)_lastSwingTime < 0.45f ) return;
		_lastSwingTime = 0f;

		ParkBeaverInFrontOfTarget();
		var hit = _beaver.DebugSwingVerbose();
		_swingsFired++;
		Log.Info( $"[TC_TEST] SWING #{_swingsFired} hit={(hit == null ? "null" : hit.GetType().Name)} treeStanding={_targetTree.IsStanding} chopsLeft={(_targetTree.IsValid() ? _targetTree.ChopsRemaining : -1)}" );

		if ( _swingsFired > 20 )
		{
			Log.Error( $"[TC_TEST] FAIL: 20 swings without felling — chops remaining={(_targetTree.IsValid() ? _targetTree.ChopsRemaining : -1)}" );
			Finish();
		}
	}

	private void TickVerify()
	{
		// Wait for the wood gain OR a hard 8s timeout. Time-based 3s was
		// flaky : Saplings topple < 1s but a heavy Normal can take 4-5s
		// to reach upDot < 0.6 (CLAUDE.md non-negotiable #9 — chase the
		// real condition, not a guessed wait).
		bool woodGained = _state.IsValid() && _state.Wood > _woodBeforeSwings;
		if ( !woodGained && (float)_phaseTime < 8f ) return;

		bool ok = true;
		if ( _state.Wood <= _woodBeforeSwings )
		{
			Log.Error( $"[TC_TEST] FAIL: wood didn't increase ({_woodBeforeSwings} → {_state.Wood})" );
			ok = false;
		}
		if ( _targetTree.IsValid() && _targetTree.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL: target tree still standing after {_swingsFired} swings" );
			ok = false;
		}

		if ( ok )
		{
			Log.Info( $"[TC_TEST] PASS  swings={_swingsFired}  wood {_woodBeforeSwings}→{_state.Wood}" );
			PhaseOk( Phase.Verify );
		}
		Finish();
	}

	private void ParkBeaverInFrontOfTarget()
	{
		var pos = _targetTreePos + new Vector3( 60f, 0f, 40f );
		_beaver.TeleportTo( pos, 180f ); // face -X (toward tree at -60 relative)
	}

	private void Finish()
	{
		Log.Info( "[TC_TEST] DONE" );
		_phase = Phase.Done;
	}
}
