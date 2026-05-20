namespace TreeChopping;

// Headless test of the mow-the-lawn loop. Phases :
//   Init           — locate beaver + tree + game state
//   Approach       — park beaver in front of a target tree
//   Swing          — call DebugSwingVerbose until target tree fells
//   Verify         — assert : GameState.Wood increased, target tree felled
//   TestStats      — assert : TryUpgradeSpeed pays wood + bumps tier
//   TestPrestige   — assert : prestige formula + tier reset + lifetime kept
//   TestGateBreak  — spawn a 1-chop gate, fell it, assert GatesBroken++
//   Done
//
// ConVar tc_selftest=1 to enable. PowerShell harness greps [TC_TEST] markers.
public sealed class SelfTest : Component
{
	enum Phase { Init, Approach, Swing, Verify, TestStats, TestPrestige, TestGateBreak, Done }

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
			case Phase.TestStats: TickTestStats(); break;
			case Phase.TestPrestige: TickTestPrestige(); break;
			case Phase.TestGateBreak: TickTestGateBreak(); break;
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

		// Throttle swings ~ once per 0.45s. DebugSwingVerbose bypasses the
		// swing state machine and calls Chop() direct, so the natural
		// WindUp+Recovery cooldown doesn't apply — this gap just gives the
		// trunk a tick to register the chop before we hit it again.
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
			Transition( Phase.TestStats );
		}
		else
		{
			Finish();
		}
	}

	private void TickTestStats()
	{
		// Pump wood to comfortably afford Speed T1 (cost 12) and Power T1
		// (cost 20). Both Save calls are no-ops during selftest so the disk
		// stays untouched. Then exercise both upgrade paths and assert the
		// tier + wood deltas.
		int speedCost = Tunables.SpeedCosts[1];
		int powerCost = Tunables.PowerCosts[1];
		int totalCost = speedCost + powerCost;
		_state.AddWood( totalCost );
		int woodBefore = _state.Wood;

		if ( !_state.TryUpgradeSpeed() || _state.SpeedTier != 1 || _state.Wood != woodBefore - speedCost )
		{
			Log.Error( $"[TC_TEST] FAIL: TryUpgradeSpeed didn't apply (tier={_state.SpeedTier} wood {woodBefore}→{_state.Wood} expected {woodBefore - speedCost})" );
			Finish();
			return;
		}
		int afterSpeed = _state.Wood;
		if ( !_state.TryUpgradePower() || _state.PowerTier != 1 || _state.Wood != afterSpeed - powerCost )
		{
			Log.Error( $"[TC_TEST] FAIL: TryUpgradePower didn't apply (tier={_state.PowerTier} wood {afterSpeed}→{_state.Wood})" );
			Finish();
			return;
		}
		// Sanity-check the derived multipliers wired into BeaverController +
		// Tree paths.
		if ( !(_state.SpeedMultiplier > 1f) )
		{
			Log.Error( $"[TC_TEST] FAIL: SpeedMultiplier didn't increase past 1.0 ({_state.SpeedMultiplier})" );
			Finish();
			return;
		}
		if ( _state.ChopPower <= Tunables.AxeTierChopPower[_state.AxeTier] )
		{
			Log.Error( $"[TC_TEST] FAIL: ChopPower didn't get +1 from Power T1 (got {_state.ChopPower})" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] STATS PASS  speed=T{_state.SpeedTier}×{_state.SpeedMultiplier:0.00}  power=T{_state.PowerTier}+{Tunables.PowerBonus[_state.PowerTier]}chop  wood={_state.Wood}" );
		Transition( Phase.TestPrestige );
	}

	private void TickTestPrestige()
	{
		// Pump TotalWoodEarned past the 500 threshold so CanPrestige flips
		// true, snapshot the lifetime + expected spirits, prestige, assert
		// reset + spirit gain + lifetime kept.
		int needed = Math.Max( 0, 500 - _state.TotalWoodEarned );
		if ( needed > 0 ) _state.AddWood( needed );
		if ( !_state.CanPrestige() )
		{
			Log.Error( $"[TC_TEST] FAIL: CanPrestige false despite TotalWood {_state.TotalWoodEarned} >= 500" );
			Finish();
			return;
		}
		int expectedSpirits = _state.SpiritsFromPrestige;
		int totalBefore = _state.TotalWoodEarned;
		int speedBefore = _state.SpeedTier;
		int powerBefore = _state.PowerTier;
		if ( !_state.TryPrestige() )
		{
			Log.Error( $"[TC_TEST] FAIL: TryPrestige returned false despite CanPrestige true" );
			Finish();
			return;
		}
		bool ok = true;
		if ( _state.Spirits != expectedSpirits )
		{
			Log.Error( $"[TC_TEST] FAIL: spirits {_state.Spirits} != expected {expectedSpirits}" );
			ok = false;
		}
		if ( _state.Wood != 0 || _state.AxeTier != 0 || _state.SpeedTier != 0 || _state.PowerTier != 0 || _state.PetTier != 0 || _state.GatesBroken != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL: tiers not reset (wood={_state.Wood} axe={_state.AxeTier} spd={_state.SpeedTier} pwr={_state.PowerTier} pet={_state.PetTier} gates={_state.GatesBroken})" );
			ok = false;
		}
		if ( _state.TotalWoodEarned != totalBefore )
		{
			Log.Error( $"[TC_TEST] FAIL: TotalWoodEarned not preserved across prestige ({totalBefore} → {_state.TotalWoodEarned})" );
			ok = false;
		}
		// Sanity : speed/power had been bumped in TestStats ; prestige
		// should have wiped them. The condition above covers that.
		if ( speedBefore == 0 || powerBefore == 0 )
		{
			Log.Error( $"[TC_TEST] FAIL: pre-prestige snapshot looked already-reset (speed={speedBefore} power={powerBefore}) — TestStats may not have run" );
			ok = false;
		}
		if ( ok )
		{
			Log.Info( $"[TC_TEST] PRESTIGE PASS  spirits 0→{_state.Spirits}  lifetime kept at {_state.TotalWoodEarned}" );
			Transition( Phase.TestGateBreak );
		}
		else
		{
			Finish();
		}
	}

	private Tree _testGate;
	private int _gatesBeforeBreak;
	private void TickTestGateBreak()
	{
		// First tick of the phase : spawn a 1-chop gate next to the beaver,
		// snapshot GatesBroken, kick its HP to 0 with a single Chop call.
		// Subsequent ticks wait for the gate to land (TickFall → upDot
		// threshold or 5s force-land), which triggers Tree.GiveWoodOnce
		// → SceneStarter.OnGateBroken → GatesBroken++.
		if ( !_testGate.IsValid() )
		{
			_gatesBeforeBreak = _state.GatesBroken;
			var pos = _beaver.WorldPosition + new Vector3( 80f, 0f, 0f );
			if ( TryGetGroundZ( pos.x, pos.y, out float z ) ) pos.z = z;
			_testGate = Tree.SpawnGate( Scene, pos, 1, 0f );
			Log.Info( $"[TC_TEST] GATE spawned at {pos}, GatesBroken={_state.GatesBroken}" );
			// Chop once with arbitrary forward — drops to 0 → StartFell.
			_testGate.Chop( Vector3.Forward, 1 );
			return;
		}
		// Wait for the gate's GiveWoodOnce path to run (BecomeLandedLog).
		// That bumps GatesBroken. Max 8s like Verify.
		bool gatesIncremented = _state.GatesBroken > _gatesBeforeBreak;
		if ( !gatesIncremented && (float)_phaseTime < 8f ) return;

		bool ok = true;
		if ( _state.GatesBroken != _gatesBeforeBreak + 1 )
		{
			Log.Error( $"[TC_TEST] FAIL: GatesBroken {_gatesBeforeBreak} → {_state.GatesBroken}, expected +1" );
			ok = false;
		}
		if ( _testGate.IsValid() && _testGate.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL: test gate still standing after Chop+8s" );
			ok = false;
		}
		if ( ok )
		{
			Log.Info( $"[TC_TEST] GATE PASS  GatesBroken {_gatesBeforeBreak}→{_state.GatesBroken}" );
			PhaseOk( Phase.TestGateBreak );
		}
		Finish();
	}

	private bool TryGetGroundZ( float x, float y, out float groundZ )
	{
		var top = new Vector3( x, y, 2000f );
		var bot = new Vector3( x, y, -2000f );
		var hit = Scene.Trace.Ray( top, bot ).WithAnyTags( "ground" ).Run();
		if ( !hit.Hit ) { groundZ = 0f; return false; }
		groundZ = hit.EndPosition.z;
		return true;
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
