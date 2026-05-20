namespace TreeChopping;

// Headless end-to-end test for the bowling pivot. Drives one swing through the
// REAL player path (BeaverController.DebugSwing → ChooseSwingTarget → Chop),
// then waits for RunManager to settle. Pass condition: RunState reaches Scored
// with Score >= 1 (the target tree at minimum). Score >= 2 logs as "cascade
// chained" — informational, not a fail.
//
// Activation: launch with "+tc_selftest 1". The engine binds the ConVar before
// scenes load. Off by default so editor Play sessions are untouched.
//
// System.Environment.* is on s&box's whitelist deny-list, so env vars don't
// work for launch flags. ConVar is the sandbox-friendly equivalent.
//
// Output: structured log lines tagged "[TC_TEST]". A PowerShell harness greps
// these to decide pass/fail. Final line is "[TC_TEST] DONE" so the harness
// can stop the process cleanly.
//
// Scenario:
//   1. Wait a few ticks for SceneStarter + RunManager + forest to come online.
//   2. Locate Beaver, RunManager, nearest Tree to beaver.
//   3. Teleport beaver one swing-range away from the target tree + aim at it.
//   4. DetectTree: dump cone/range/tool diagnostics + DebugSwingVerbose once.
//      If null, fail with the candidate-drop counts.
//   5. Swing: call DebugSwing. RunManager.OnSwingFired transitions to Cascading.
//   6. WaitCascade: poll RunState until Scored, max 12s.
//   7. Verify: Score >= 1 = PASS. Score >= 2 logs cascade-chain bonus.
//   8. Run2Trigger: call RunManager.RegenerateForTest(), wait for repopulate.
//   9. Run2DetectTree / Run2WaitCascade / Run2Verify: re-exercise the swing
//      path on the regenerated arena to validate Regenerate() + biome rotation
//      + RegenerateForest() actually fire correctly.
public sealed class SelfTest : Component
{
	enum Phase
	{
		Init, BumpTree, DetectTree, WaitCascade, Verify,
		Run2Trigger, Run2DetectTree, Run2WaitCascade, Run2Verify,
		Done
	}

	private Phase _phase = Phase.Init;
	private TimeSince _phaseTime;
	private int _frame;
	private BeaverController _beaver;
	private RunManager _run;
	private Tree _targetTree;
	private Vector3 _targetTreePos;

	// Run2 bookkeeping
	private BiomeKind _biomeBeforeRegen;
	private int _treeCountBeforeRegen;
	private int _run2WaitFrames;
	private bool _run2RegenFired;

	[ConVar( "tc_selftest", Help = "Spawn TreeChopping.SelfTest on bootstrap to run the bowling headless scenario." )]
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
			case Phase.BumpTree: TickBumpTree(); break;
			case Phase.DetectTree: TickDetectTree(); break;
			case Phase.WaitCascade: TickWaitCascade(); break;
			case Phase.Verify: TickVerify(); break;
			case Phase.Run2Trigger: TickRun2Trigger(); break;
			case Phase.Run2DetectTree: TickRun2DetectTree(); break;
			case Phase.Run2WaitCascade: TickRun2WaitCascade(); break;
			case Phase.Run2Verify: TickRun2Verify(); break;
			case Phase.Done: break;
		}
	}

	private void Transition( Phase next )
	{
		Log.Info( $"[TC_TEST] phase {_phase} -> {next} (t={_phaseTime:F2}s)" );
		_phase = next;
		_phaseTime = 0f;
	}

	// Pick the densest cluster: rank each tree by how many neighbors it has
	// within a one-trunk-length radius, then pick the highest-density tree.
	// Shared between Init and Run2Trigger so both runs hit a chainable target.
	private Tree PickDensestTree()
	{
		var allTrees = Scene.GetAllComponents<Tree>().ToList();
		const float NeighborRadius = 120f;
		const float NeighborRadiusSq = NeighborRadius * NeighborRadius;
		return allTrees
			.OrderByDescending( t =>
				allTrees.Count( other =>
					other != t && (other.WorldPosition - t.WorldPosition).WithZ( 0f ).LengthSquared < NeighborRadiusSq ) )
			.ThenBy( t => t.WorldPosition.LengthSquared )
			.FirstOrDefault();
	}

	// Park the beaver one swing-range away from the target tree + aim its
	// yaw at the tree so DebugSwing's ChooseSwingTarget puts it in cone.
	// low_poly_tree pivot is at the foot — tree.WorldPosition == foot, no
	// half-height subtraction needed (would put the beaver under the ground).
	private void ParkBeaverInFrontOfTarget()
	{
		_beaver.WorldPosition = _targetTreePos + new Vector3( 60f, 0f, 40f );
		var rb = _beaver.Components.Get<Rigidbody>();
		if ( rb.IsValid() ) rb.Velocity = Vector3.Zero;

		_beaver.DebugSetTool( ToolKind.Axe );
		var aim = (_targetTreePos - _beaver.WorldPosition).WithZ( 0f );
		var yawDeg = aim.LengthSquared > 0.001f
			? MathF.Atan2( aim.y, aim.x ) * (180f / MathF.PI)
			: 0f;
		_beaver.DebugSetYaw( yawDeg );
	}

	private void TickInit()
	{
		if ( _frame < 10 ) return;

		// Dismiss the title overlay so any input-gated paths exercised below
		// (UpdateSwing, TickScored) behave as if the player had clicked Start.
		TitleScreen.Dismissed = true;

		_beaver = Scene.GetAllComponents<BeaverController>().FirstOrDefault();
		_run = RunManager.Get( Scene );

		_targetTree = PickDensestTree();

		if ( !_beaver.IsValid() || !_run.IsValid() || !_targetTree.IsValid() )
		{
			Log.Error( $"[TC_TEST] FAIL: missing entities beaver={_beaver.IsValid()} run={_run.IsValid()} tree={_targetTree.IsValid()}" );
			Finish();
			return;
		}

		_targetTreePos = _targetTree.WorldPosition;

		// Pin the target to a 1-chop fell so species variety (Beech 2 / Spruce 3
		// / Ironwood 4 / Crystal 5) doesn't desync the test from "one swing
		// fells, cascade kicks off". Gameplay still respects the species ladder.
		_targetTree.ChopsRemaining = 1;

		ParkBeaverInFrontOfTarget();

		Log.Info( $"[TC_TEST] INIT beaverPos={_beaver.WorldPosition} treePos={_targetTreePos} runState={_run.State} initialTrees={_run.InitialTreeCount}" );
		Transition( Phase.BumpTree );
	}

	private void TickBumpTree()
	{
		// Regression: sprint-into a standing tree must NOT fell it via contact.
		// Standing trees stay kinematic (MotionEnabled=false) — only Chop()
		// flips that. Without this regression the bowling pivot would let you
		// "knock down" trees just by walking, breaking the one-shot rule.
		if ( !_targetTree.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL: target tree gone before bump probe" );
			Finish();
			return;
		}

		const float BumpDuration = 1.2f;
		if ( (float)_phaseTime < BumpDuration )
		{
			var toward = (_targetTreePos - _beaver.WorldPosition).WithZ( 0f );
			if ( toward.LengthSquared > 0.001f )
			{
				var dir = toward.Normal;
				var rb = _beaver.Components.Get<Rigidbody>();
				if ( rb.IsValid() )
				{
					var v = rb.Velocity;
					v.x = dir.x * Tunables.BeaverMoveSpeed * Tunables.BeaverSprintMultiplier;
					v.y = dir.y * Tunables.BeaverMoveSpeed * Tunables.BeaverSprintMultiplier;
					rb.Velocity = v;
				}
			}

			if ( !_targetTree.IsStanding )
			{
				Log.Error( $"[TC_TEST] FAIL: tree fell from collision at t={(float)_phaseTime:F2}s — standing trees must be kinematic" );
				Finish();
				return;
			}
			return;
		}

		Log.Info( $"[TC_TEST] BUMP survived — tree still standing after {BumpDuration:F1}s of sprint-into contact" );

		ParkBeaverInFrontOfTarget();
		Transition( Phase.DetectTree );
	}

	private void TickDetectTree()
	{
		if ( !_targetTree.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL: target tree gone before detection probe" );
			Finish();
			return;
		}

		var origin = _beaver.WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var to = (_targetTree.WorldPosition - origin).WithZ( 0f );
		var dist = to.Length;
		var forward = _beaver.DebugCameraAngles.WithPitch( 0f ).ToRotation().Forward;
		var dot = dist > 0.001f ? forward.Dot( to.Normal ) : 0f;
		var inRange = dist <= Tunables.SwingRange;
		var inCone = dot >= Tunables.SwingConeDot;
		var acceptsTool = ((IChoppable)_targetTree).AcceptsTool( ToolKind.Axe );

		Log.Info( $"[TC_TEST] DETECT dist={dist:F1}/range={Tunables.SwingRange} dot={dot:F2}/cone={Tunables.SwingConeDot} inRange={inRange} inCone={inCone} acceptsTool={acceptsTool}" );

		var hit = _beaver.DebugSwingVerbose();
		if ( hit is null )
		{
			Log.Error( $"[TC_TEST] FAIL: ChooseSwingTarget returned null — inRange={inRange} inCone={inCone} acceptsTool={acceptsTool}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] DETECT swing landed on {hit.GetType().Name}" );

		// DebugSwingVerbose did the chop. Tree.StartFell already called
		// RunManager.OnTreeFell which auto-engages the cascade tracker (per
		// the OnTreeFell relaxation in RunManager). Make doubly sure by
		// firing OnSwingFired ourselves — no-op if state already moved on.
		_run.OnSwingFired();
		Transition( Phase.WaitCascade );
	}

	private void TickWaitCascade()
	{
		if ( !_run.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL: RunManager vanished" );
			Finish();
			return;
		}

		if ( _run.State == RunState.Scored )
		{
			Log.Info( $"[TC_TEST] cascade resolved at t={(float)_phaseTime:F2}s, score={_run.Score}" );
			Transition( Phase.Verify );
			return;
		}

		if ( (float)_phaseTime > 12f )
		{
			Log.Error( $"[TC_TEST] FAIL: cascade did not settle within 12s (state={_run.State} score={_run.Score} idle={_run.CascadeIdleSeconds:F1})" );
			Finish();
			return;
		}
	}

	private void TickVerify()
	{
		if ( _run.Score >= 1 )
		{
			var chainNote = _run.Score >= 2 ? $" (cascade chained +{_run.Score - 1})" : "";
			Log.Info( $"[TC_TEST] PASS score={_run.Score}/{_run.InitialTreeCount}{chainNote}" );
		}
		else
		{
			Log.Error( $"[TC_TEST] FAIL: score={_run.Score} — expected at least 1 from the target swing" );
			Finish();
			return;
		}

		// Snapshot pre-regen state so Run2Verify can confirm Regenerate() actually
		// rotated the biome + repopulated the forest.
		_biomeBeforeRegen = BiomeManager.Get( Scene )?.Current ?? BiomeKind.Forest;
		_treeCountBeforeRegen = Scene.GetAllComponents<Tree>().Count( t => t.IsValid() );
		Log.Info( $"[TC_TEST] Pre-regen snapshot: biome={_biomeBeforeRegen} trees={_treeCountBeforeRegen}" );

		Transition( Phase.Run2Trigger );
	}

	private void TickRun2Trigger()
	{
		// Boolean gate, not _phaseTime < 0.05f — cascade slowmo from milestone
		// triggers (TIMBER SHOCK at score 200+) scales scene time, and the first
		// post-Transition tick can land well past 0.05f, skipping the regen and
		// leaving runState=Scored for the entire Run2 phase.
		if ( !_run2RegenFired )
		{
			Log.Info( "[TC_TEST] RUN2 Triggering RegenerateForTest()" );
			_run.RegenerateForTest();
			_run2RegenFired = true;
			_run2WaitFrames = 0;
			return;
		}

		// Give the spawn loop ~30 fixed ticks to repopulate the forest before
		// re-locating the densest cluster.
		_run2WaitFrames++;
		if ( _run2WaitFrames < 30 ) return;

		_targetTree = PickDensestTree();
		if ( !_targetTree.IsValid() )
		{
			Log.Error( "[TC_TEST] RUN2_FAIL_NO_TREES — no trees after Regenerate" );
			Finish();
			return;
		}

		_targetTreePos = _targetTree.WorldPosition;
		_targetTree.ChopsRemaining = 1;
		ParkBeaverInFrontOfTarget();

		Log.Info( $"[TC_TEST] RUN2 beaverPos={_beaver.WorldPosition} treePos={_targetTreePos} runState={_run.State}" );
		Transition( Phase.Run2DetectTree );
	}

	private void TickRun2DetectTree()
	{
		if ( !_targetTree.IsValid() )
		{
			Log.Error( "[TC_TEST] RUN2_FAIL_TARGET_GONE before detection probe" );
			Finish();
			return;
		}

		var origin = _beaver.WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var to = (_targetTree.WorldPosition - origin).WithZ( 0f );
		var dist = to.Length;
		var forward = _beaver.DebugCameraAngles.WithPitch( 0f ).ToRotation().Forward;
		var dot = dist > 0.001f ? forward.Dot( to.Normal ) : 0f;
		var inRange = dist <= Tunables.SwingRange;
		var inCone = dot >= Tunables.SwingConeDot;
		var acceptsTool = ((IChoppable)_targetTree).AcceptsTool( ToolKind.Axe );

		Log.Info( $"[TC_TEST] RUN2 DETECT dist={dist:F1}/range={Tunables.SwingRange} dot={dot:F2}/cone={Tunables.SwingConeDot} inRange={inRange} inCone={inCone} acceptsTool={acceptsTool}" );

		var hit = _beaver.DebugSwingVerbose();
		if ( hit is null )
		{
			Log.Error( $"[TC_TEST] RUN2_FAIL_NULL_SWING — ChooseSwingTarget returned null — inRange={inRange} inCone={inCone} acceptsTool={acceptsTool}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] RUN2 DETECT swing landed on {hit.GetType().Name}" );

		_run.OnSwingFired();
		Transition( Phase.Run2WaitCascade );
	}

	private void TickRun2WaitCascade()
	{
		if ( !_run.IsValid() )
		{
			Log.Error( "[TC_TEST] RUN2_FAIL_RUNMANAGER_GONE" );
			Finish();
			return;
		}

		if ( _run.State == RunState.Scored )
		{
			Log.Info( $"[TC_TEST] RUN2 cascade resolved at t={(float)_phaseTime:F2}s, score={_run.Score}" );
			Transition( Phase.Run2Verify );
			return;
		}

		if ( (float)_phaseTime > 12f )
		{
			Log.Error( $"[TC_TEST] RUN2_FAIL_CASCADE_TIMEOUT (state={_run.State} score={_run.Score} idle={_run.CascadeIdleSeconds:F1})" );
			Finish();
			return;
		}
	}

	private void TickRun2Verify()
	{
		bool ok = true;

		// 1) Score check
		if ( _run.Score >= 1 )
		{
			var chainNote = _run.Score >= 2 ? $" (cascade chained +{_run.Score - 1})" : "";
			Log.Info( $"[TC_TEST] RUN2_PASS_SCORE score={_run.Score}/{_run.InitialTreeCount}{chainNote}" );
		}
		else
		{
			Log.Error( $"[TC_TEST] RUN2_FAIL_SCORE score={_run.Score} — expected at least 1" );
			ok = false;
		}

		// 2) Biome rotation check — Regenerate() calls BiomeManager.AdvanceBiome()
		// so Current must differ from the pre-regen snapshot.
		var biomeNow = BiomeManager.Get( Scene )?.Current ?? BiomeKind.Forest;
		if ( biomeNow != _biomeBeforeRegen )
		{
			Log.Info( $"[TC_TEST] RUN2_PASS_BIOME {_biomeBeforeRegen} -> {biomeNow}" );
		}
		else
		{
			Log.Error( $"[TC_TEST] RUN2_FAIL_BIOME stayed at {biomeNow} — AdvanceBiome did not fire" );
			ok = false;
		}

		// 3) Tree repopulation check — RegenerateForest spawns >500 trees in the
		// default config. Confirms Regenerate() actually called RegenerateForest().
		// We count via InitialTreeCount captured at the end of Regenerate.
		const int ExpectedMinTrees = 500;
		if ( _run.InitialTreeCount >= ExpectedMinTrees )
		{
			Log.Info( $"[TC_TEST] RUN2_PASS_TREECOUNT {_run.InitialTreeCount} >= {ExpectedMinTrees} (was {_treeCountBeforeRegen} before)" );
		}
		else
		{
			Log.Error( $"[TC_TEST] RUN2_FAIL_TREECOUNT {_run.InitialTreeCount} < {ExpectedMinTrees} (was {_treeCountBeforeRegen} before) — RegenerateForest may not have fired" );
			ok = false;
		}

		if ( ok )
		{
			Log.Info( "[TC_TEST] RUN2_PASS_ALL" );
		}

		Finish();
	}

	private void Finish()
	{
		Log.Info( "[TC_TEST] DONE" );
		_phase = Phase.Done;
	}
}
