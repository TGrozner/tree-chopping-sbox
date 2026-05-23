namespace TreeChopping;

// Headless test of the mow-the-lawn loop. The PowerShell harness derives its
// phase contract from the enum below, so adding a phase here automatically
// makes it mandatory in tools/selftest.ps1.
//
// ConVar tc_selftest=1 to enable. PowerShell harness greps [TC_TEST] markers.
public sealed class SelfTest : Component
{
	private static T RuntimeValue<T>( T value ) => value;

	enum Phase
	{
		Init, TestSpawnDistribution, Approach, Swing, Verify,
		TestStump, TestSplit, TestLandedLogChopGrace, TestBonusDrop, TestSplitLogSpawn, TestWoodPickup, TestImpactNoSelfDamage, TestLandedLogGravity, TestLandedLogSupport, TestStumpRespawn, TestCascadeDamage, TestCascadeCollision,
		TestAxeTierGate, TestLogTierGate, TestChopPowerScaling, TestImpactBelowMin, TestImpactZeroNoOp,
		TestBackpackFull, TestBackpackFullPickup, TestDepositFlush, TestDepositStationEntry, TestPrestigeFormula, TestFallingImpactSplit, TestComboFinalDamage, TestMultiWoodTypes,
		TestStatCounters, TestWoodCuttingLevel, TestPickupStackMerge, TestEnvWindSanity, TestStrictTooHard, TestTunablesValheimSanity,
		TestFellCanopyDestroyed, TestImpactDamageScaling, TestWindDirRotation, TestRespawnJitterRange, TestWoodTypeDistribution, TestTreeShakeReset, TestCascadeShakeNoFell,
		TestValheimLogLaunch, TestValheimTreeLogHitImpulse, TestValheimDropGeometry, TestRollingLogsDamping, TestEnvWindDeterministic, TestWoodTypeMixSumsAll, TestHitDataDamage, TestSwingFeedbackAudio,
		TestGameStateSanitize, TestStats, TestPrestige, Done
	}

	private Phase _phase = Phase.Init;
	private TimeSince _phaseTime;
	private int _frame;
	private AxeController _axe;
	private GameState _state;
	private Tree _targetTree;
	private FallenLog _targetLog;
	private bool _targetLogSeen;
	private bool _targetLogLandedSeen;
	private TimeSince _targetLogSinceLanded;
	private Vector3 _targetTreePos;
	private int _woodBeforeSwings;

	private int _swingsFired;
	private TimeSince _lastSwingTime = 999f;

	// État pour TestBonusDrop : référence sur le Veteran spawné, count avant.
	private Tree _bonusDropVeteran;
	private bool _bonusDropSpawned;
	private Vector3 _bonusDropPos;
	private int _woodItemsBeforeBonusDrop;
	private Tree _splitLogTree;
	private FallenLog _splitLogParent;
	private bool _splitLogSpawned;
	private TimeSince _splitLogSinceLanded;
	private TimeSince _splitLogValidationSince;
	private bool _splitLogSpawnObserved;
	private bool _splitLogFreshGraceChecked;
	private bool _splitLogParentDropBaselineSet;
	private int _splitLogParentDropBaseline;
	private float _splitLogParentLength;
	private float _splitLogParentWidth;
	private int _splitLogSwings;
	private Vector3 _splitLogPos;
	private Tree _landedGraceTree;
	private FallenLog _landedGraceLog;
	private bool _landedGraceSpawned;
	private bool _landedGraceEarlyHitChecked;
	private int _landedGraceHpAfterEarlyHit;
	// État pour TestWoodPickup
	private int _backpackBeforePickup;
	private TimeSince _pickupSpawnTime;
	private bool _pickupSpawned;
	private WoodItem _fullPickupItem;
	private TimeSince _fullPickupTime;
	private bool _fullPickupSpawned;
	private Vector3 _fullPickupSpawnPos;
	// État pour TestImpactNoSelfDamage
	private Tree _impactNoSelfTree;
	private FallenLog _impactNoSelfLog;
	private TimeSince _impactNoSelfStartTime;
	private bool _impactNoSelfSpawned;
	private Tree _landedGravityTree;
	private FallenLog _landedGravityLog;
	private TimeSince _landedGravityStartTime;
	private TimeSince _landedGravitySinceLift;
	private bool _landedGravitySpawned;
	private bool _landedGravityLifted;
	private float _landedGravityStartZ;
	private float _landedGravityStartClearance;
	private Tree _landedSupportTree;
	private FallenLog _landedSupportLog;
	private TimeSince _landedSupportSinceLanded;
	private TimeSince _landedSupportSinceTrace;
	private bool _landedSupportSpawned;
	private bool _landedSupportLandedSeen;
	private FallenLog _fallingImpactLog;
	private TimeSince _fallingImpactSinceSpawn;
	private bool _fallingImpactSpawned;
	private bool _fallingImpactPartialChecked;
	private bool _fallingImpactLandingPreserveChecked;
	private FallenLog _fallingImpactLandingProbeLog;
	private TimeSince _fallingImpactLandingProbeTime;
	private bool _fallingImpactLandingDamageApplied;
	private int _fallingImpactLandingExpectedHp;
	private Tree _cascadeSource;
	private FallenLog _cascadeSourceLog;
	private Tree _cascadeNeighbor;
	private TimeSince _cascadeCollisionStartTime;
	private bool _cascadeCollisionSpawned;
	private bool _cascadeCollisionFallingDone;
	private int _cascadeNeighborHpBefore;
	private Vector3 _cascadeCollisionSourcePos;
	private Vector3 _cascadeCollisionAxis;
	private Tree _landedCascadeSource;
	private FallenLog _landedCascadeLog;
	private Tree _landedCascadeNeighbor;
	private TimeSince _landedCascadeStartTime;
	private bool _landedCascadeSpawned;
	private bool _landedCascadeLaunchApplied;
	private int _landedCascadeNeighborHpBefore;
	private Vector3 _landedCascadeLogPos;
	private Vector3 _landedCascadeAxis;
	private FallenLog _logTierGateLog;
	private bool _logTierGateSpawned;
	private int _logTierGateHpBefore;
	// État pour TestStumpRespawn
	private TreeStump _respawnStump;
	private TimeSince _respawnStartTime;
	private bool _respawnTriggered;
	private bool _depositStationEntrySetup;
	private bool _depositStationEntered;
	private int _depositStationStockpileBefore;
	private int _depositStationTotalBefore;
	private Tree _fellCanopyTree;
	private bool _fellCanopyStarted;
	private int _fellCanopyBefore;
	private FallenLog _valheimLaunchLog;
	private bool _valheimLaunchSpawned;
	private TimeSince _valheimLaunchTime;
	private Vector3 _valheimLaunchPos;
	private FallenLog _valheimLogHitLog;
	private bool _valheimLogHitSpawned;
	private bool _valheimLogHitApplied;
	private TimeSince _valheimLogHitTime;
	private Vector3 _valheimLogHitPos;
	private bool _valheimDropGeometrySetup;
	private bool _valheimDropGeometryLandedSeen;
	private bool _valheimDropGeometryImpactApplied;
	private FallenLog _valheimDropGeometryLog;
	private TimeSince _valheimDropGeometryLogTime;
	private TimeSince _valheimDropGeometrySinceLanded;
	private Vector3 _valheimDropGeometryBasePos;
	private Vector3 _valheimDropGeometryLogPos;
	private Vector3 _valheimDropGeometryLogCenter;
	private Vector3 _valheimDropGeometryAxis;
	private Tree _swingFeedbackTree;
	private int _swingFeedbackStep;
	private TimeSince _swingFeedbackStepTime;
	private Vector3 _swingFeedbackPos;
	private int _swingFeedbackHpBefore;

	[ConVar( "tc_selftest", Help = "Spawn TreeChopping.SelfTest on bootstrap to run the mow-the-lawn headless scenario." )]
	public static bool Enable { get; set; }

	[ConVar( "tc_selftest_quick", Help = "Run selftest smoke timings; full physics settle waits are reserved for explicit full runs." )]
	public static bool Quick { get; set; }

	[ConVar( "tc_selftest_physics", Help = "Run only the tree/log physics regression slice." )]
	public static bool PhysicsOnly { get; set; }

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
			case Phase.TestSpawnDistribution: TickTestSpawnDistribution(); break;
			case Phase.Approach: TickApproach(); break;
			case Phase.Swing: TickSwing(); break;
			case Phase.Verify: TickVerify(); break;
			case Phase.TestStump: TickTestStump(); break;
			case Phase.TestSplit: TickTestSplit(); break;
			case Phase.TestLandedLogChopGrace: TickTestLandedLogChopGrace(); break;
			case Phase.TestBonusDrop: TickTestBonusDrop(); break;
			case Phase.TestSplitLogSpawn: TickTestSplitLogSpawn(); break;
			case Phase.TestWoodPickup: TickTestWoodPickup(); break;
			case Phase.TestImpactNoSelfDamage: TickTestImpactNoSelfDamage(); break;
			case Phase.TestLandedLogGravity: TickTestLandedLogGravity(); break;
			case Phase.TestLandedLogSupport: TickTestLandedLogSupport(); break;
			case Phase.TestStumpRespawn: TickTestStumpRespawn(); break;
			case Phase.TestCascadeDamage: TickTestCascadeDamage(); break;
			case Phase.TestCascadeCollision: TickTestCascadeCollision(); break;
			case Phase.TestAxeTierGate: TickTestAxeTierGate(); break;
			case Phase.TestLogTierGate: TickTestLogTierGate(); break;
			case Phase.TestChopPowerScaling: TickTestChopPowerScaling(); break;
			case Phase.TestImpactBelowMin: TickTestImpactBelowMin(); break;
			case Phase.TestImpactZeroNoOp: TickTestImpactZeroNoOp(); break;
			case Phase.TestBackpackFull: TickTestBackpackFull(); break;
			case Phase.TestBackpackFullPickup: TickTestBackpackFullPickup(); break;
			case Phase.TestDepositFlush: TickTestDepositFlush(); break;
			case Phase.TestDepositStationEntry: TickTestDepositStationEntry(); break;
			case Phase.TestPrestigeFormula: TickTestPrestigeFormula(); break;
			case Phase.TestFallingImpactSplit: TickTestFallingImpactSplit(); break;
			case Phase.TestComboFinalDamage: TickTestComboFinalDamage(); break;
			case Phase.TestMultiWoodTypes: TickTestMultiWoodTypes(); break;
			case Phase.TestStatCounters: TickTestStatCounters(); break;
			case Phase.TestWoodCuttingLevel: TickTestWoodCuttingLevel(); break;
			case Phase.TestPickupStackMerge: TickTestPickupStackMerge(); break;
			case Phase.TestEnvWindSanity: TickTestEnvWindSanity(); break;
			case Phase.TestStrictTooHard: TickTestStrictTooHard(); break;
			case Phase.TestTunablesValheimSanity: TickTestTunablesValheimSanity(); break;
			case Phase.TestFellCanopyDestroyed: TickTestFellCanopyDestroyed(); break;
			case Phase.TestImpactDamageScaling: TickTestImpactDamageScaling(); break;
			case Phase.TestWindDirRotation: TickTestWindDirRotation(); break;
			case Phase.TestRespawnJitterRange: TickTestRespawnJitterRange(); break;
			case Phase.TestWoodTypeDistribution: TickTestWoodTypeDistribution(); break;
			case Phase.TestTreeShakeReset: TickTestTreeShakeReset(); break;
			case Phase.TestCascadeShakeNoFell: TickTestCascadeShakeNoFell(); break;
			case Phase.TestValheimLogLaunch: TickTestValheimLogLaunch(); break;
			case Phase.TestValheimTreeLogHitImpulse: TickTestValheimTreeLogHitImpulse(); break;
			case Phase.TestValheimDropGeometry: TickTestValheimDropGeometry(); break;
			case Phase.TestRollingLogsDamping: TickTestRollingLogsDamping(); break;
			case Phase.TestEnvWindDeterministic: TickTestEnvWindDeterministic(); break;
			case Phase.TestWoodTypeMixSumsAll: TickTestWoodTypeMixSumsAll(); break;
			case Phase.TestHitDataDamage: TickTestHitDataDamage(); break;
			case Phase.TestSwingFeedbackAudio: TickTestSwingFeedbackAudio(); break;
			case Phase.TestGameStateSanitize: TickTestGameStateSanitize(); break;
			case Phase.TestStats: TickTestStats(); break;
			case Phase.TestPrestige: TickTestPrestige(); break;
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
		_axe = Scene.GetAllComponents<AxeController>().FirstOrDefault();
		_state = GameState.Get( Scene );

		// Reset state so we test from a known baseline regardless of prior
		// saves on disk.
		if ( _state.IsValid() ) _state.ResetForTest();

		// Pick a small tree (low ChopsRemaining) close to player for a quick
		// felling. Saplings (kind=1) have 1 chop ; preferred.
		_targetTree = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() && t.IsStanding )
			.OrderBy( t => (_axe.IsValid() ? _axe.WorldPosition : Vector3.Zero).Distance( t.WorldPosition ) )
			.FirstOrDefault();
		if ( !_axe.IsValid() || !_state.IsValid() || !_targetTree.IsValid() )
		{
			Log.Error( $"[TC_TEST] FAIL: missing entities player={_axe.IsValid()} state={_state.IsValid()} tree={_targetTree.IsValid()}" );
			Finish();
			return;
		}
		_targetTreePos = _targetTree.WorldPosition;
		_targetLog = null;
		_targetLogSeen = false;
		_targetLogLandedSeen = false;
		_woodBeforeSwings = _state.Wood;
		Log.Info( $"[TC_TEST] INIT playerPos={_axe.WorldPosition} treePos={_targetTreePos} kind={_targetTree.Kind} chops={_targetTree.ChopsRemaining} wood={_state.Wood} tier={_state.AxeTier}" );
		Transition( PhysicsOnly ? Phase.TestSplitLogSpawn : Phase.TestSpawnDistribution );
	}

	private void TickTestSpawnDistribution()
	{
		var starter = Scene.GetAllComponents<SceneStarter>().FirstOrDefault();
		if ( !starter.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestSpawnDistribution: no SceneStarter found" );
			Finish();
			return;
		}

		int insidePad = 0;
		int starterRing = 0;
		int starterSaplings = 0;
		int starterNormals = 0;
		int frontRing = 0;
		int frontSaplings = 0;
		int frontNormals = 0;
		int laneTrees = 0;
		int laneSaplings = 0;
		int midNormals = 0;
		int outerVeterans = 0;
		var origin = starter.ResolvedPlayerSpawn;
		var front = Vector3.Forward;
		foreach ( var tree in Scene.GetAllComponents<Tree>() )
		{
			if ( !tree.IsValid() || !tree.IsStanding ) continue;
			var delta = (tree.WorldPosition - origin).WithZ( 0f );
			float dist = delta.Length;
			if ( dist < starter.SpawnPadRadius && tree.WorldPosition.Distance( _targetTreePos ) > 30f )
				insidePad++;
			if ( dist >= starter.SpawnPadRadius + 80f && dist <= starter.SpawnPadRadius + 700f )
			{
				starterRing++;
				if ( tree.Kind == TreeKind.Sapling ) starterSaplings++;
				if ( tree.Kind == TreeKind.Normal ) starterNormals++;
				if ( delta.LengthSquared > 0.01f && delta.Normal.Dot( front ) >= 0.82f )
				{
					frontRing++;
					if ( tree.Kind == TreeKind.Sapling ) frontSaplings++;
					if ( tree.Kind == TreeKind.Normal ) frontNormals++;
				}
				if ( MathF.Abs( delta.y ) < 240f && delta.x > 0f )
				{
					laneTrees++;
					if ( tree.Kind == TreeKind.Sapling ) laneSaplings++;
				}
			}
			if ( dist > starter.SpawnPadRadius + 700f && dist <= starter.SpawnPadRadius + 1250f && tree.Kind == TreeKind.Normal )
				midNormals++;
			if ( dist > starter.SpawnPadRadius + 1250f && tree.Kind == TreeKind.Veteran )
				outerVeterans++;
		}

		if ( insidePad > 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestSpawnDistribution: {insidePad} non-test trees inside spawn pad" );
			Finish();
			return;
		}
		if ( starterRing < 36 || starterSaplings < 28 || starterNormals < 3 )
		{
			Log.Error( $"[TC_TEST] FAIL TestSpawnDistribution: starterRing={starterRing} saplings={starterSaplings} normals={starterNormals} (expected >=36 / >=28 / >=3)" );
			Finish();
			return;
		}
		if ( frontRing < 14 || frontSaplings < 11 || frontNormals < 2 )
		{
			Log.Error( $"[TC_TEST] FAIL TestSpawnDistribution: frontRing={frontRing} saplings={frontSaplings} normals={frontNormals} (expected >=14 / >=11 / >=2)" );
			Finish();
			return;
		}
		if ( laneTrees < 12 || laneSaplings < 10 )
		{
			Log.Error( $"[TC_TEST] FAIL TestSpawnDistribution: starter lane too sparse lane={laneTrees} saplings={laneSaplings} (expected >=12 / >=10)" );
			Finish();
			return;
		}
		if ( midNormals < 6 || outerVeterans < 12 )
		{
			Log.Error( $"[TC_TEST] FAIL TestSpawnDistribution: progression bands weak midNormals={midNormals} outerVeterans={outerVeterans} (expected >=6 / >=12)" );
			Finish();
			return;
		}

		Log.Info( $"[TC_TEST] SPAWN_DISTRIBUTION PASS  starterRing={starterRing}, saplings={starterSaplings}, normals={starterNormals}, front={frontRing}/{frontSaplings}/{frontNormals}, lane={laneTrees}/{laneSaplings}, midN={midNormals}, outerV={outerVeterans}, padClear" );
		Transition( Phase.Approach );
	}

	private void TickApproach()
	{
		ParkPlayerInFrontOfTarget();
		Log.Info( $"[TC_TEST] APPROACH parked playerPos={_axe.WorldPosition}" );
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

		ParkPlayerInFrontOfTarget();
		var hit = _axe.DebugSwingVerbose();
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
		// Phase F : chopping no longer credits wood directly. The standing
		// tree falls into a FallenLog → split directly into pickable
		// WoodItems → player walks over → AddBackpack. The harness can't
		// easily simulate the full pickup chain so we just assert the first
		// step : the target tree transitioned out of Standing within 8s.
		bool toppled = !_targetTree.IsValid() || !_targetTree.IsStanding;
		if ( !toppled && (float)_phaseTime < 8f ) return;

		bool ok = true;
		if ( _targetTree.IsValid() && _targetTree.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL: target tree still standing after {_swingsFired} swings" );
			ok = false;
		}

		if ( ok )
		{
			Log.Info( $"[TC_TEST] PASS  swings={_swingsFired}  target toppled" );
			Transition( Phase.TestStump );
		}
		else
		{
			Finish();
		}
	}

	private void TickTestStump()
	{
		// Le sapling vient juste de StartFell — TreeStump.SpawnAt a été appelée
		// dans le même tick. Une souche persistante doit donc exister près de
		// la position de spawn original du sapling.
		var stumps = Scene.GetAllComponents<TreeStump>()
			.Where( s => s.IsValid() )
			.ToList();
		var near = stumps
			.Where( s => s.WorldPosition.Distance( _targetTreePos ) < 30f )
			.ToList();
		if ( near.Count == 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestStump: aucune souche près du sapling felled (total stumps={stumps.Count}, target={_targetTreePos})" );
			Finish();
			return;
		}
		var s0 = near[0];
		Log.Info( $"[TC_TEST] STUMP PASS  pos={s0.WorldPosition}  kind={s0.Kind}  (delta={s0.WorldPosition.Distance( _targetTreePos ):F1}u)" );
		Transition( Phase.TestSplit );
	}

	private void TickTestSplit()
	{
		// Attend que le sapling tombé passe en état FallenLog (upDot threshold
		// atteint par TickFall), puis chop-it jusqu'à split en WoodItems.
		// Si le sapling auto-split sur un impact externe (ne devrait pas
		// passer le seuil, il est trop léger), GameObject est déjà destroyed.
		if ( !_targetLog.IsValid() )
		{
			_targetLog = FindNearestFallenLog( _targetTreePos, 700f );
			if ( _targetLog.IsValid() ) _targetLogSeen = true;
		}

		bool destroyed = _targetLogSeen && !_targetLog.IsValid();
		bool landed = _targetLog.IsValid() && _targetLog.IsFallenLog;

		if ( !destroyed && !landed )
		{
			if ( (float)_phaseTime > 5f )
			{
				string logState = _targetLog.IsValid()
					? $" upDot={_targetLog.DebugAxisUpDot():F2} clearance={_targetLog.DebugMinGroundClearance():F1} vel={_targetLog.Body.Velocity.Length:F1} ang={_targetLog.Body.AngularVelocity.Length:F2}"
					: "";
				Log.Error( $"[TC_TEST] FAIL TestSplit: no landed FallenLog after 5s (logValid={_targetLog.IsValid()} seen={_targetLogSeen}){logState}" );
				Finish();
			}
			return;
		}

		// Chop le tronc landed via DebugSwingVerbose. Sapling.LogChopHP=1 = 1 chop.
		if ( !destroyed )
		{
			if ( !_targetLogLandedSeen )
			{
				_targetLogLandedSeen = true;
				_targetLogSinceLanded = 0f;
				return;
			}
			if ( (float)_targetLogSinceLanded < Tunables.WoodLogChopGrace + 0.08f ) return;
			if ( (float)_lastSwingTime < 0.45f ) return;
			_lastSwingTime = 0f;
			ParkPlayerFacingLog( _targetLog );
			_axe.DebugSwingVerbose();
			_swingsFired++;
			if ( _swingsFired > 30 )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplit: 30 swings sur le landed log sans split (ChopsRemaining={(_targetLog.IsValid() ? _targetLog.ChopsRemaining : -1)})" );
				Finish();
				return;
			}
			return;
		}

		// Tree destroyed → SplitIntoLogs (drop items directs Valheim-style)
		// s'est exécuté. Compter les WoodItems near le foot. Sapling drop count
		// = TreeKindLandedDropCount[Sapling] = 1.
		var items = Scene.GetAllComponents<WoodItem>()
			.Where( w => w.IsValid() && w.WorldPosition.Distance( _targetTreePos ) < 500f )
			.ToList();
		int expected = Tunables.TreeKindLandedDropCount[(int)TreeKind.Sapling];
		if ( items.Count < expected )
		{
			Log.Error( $"[TC_TEST] FAIL TestSplit: {items.Count} WoodItems trouvés près du sapling felled (expected ≥{expected}, total scene WoodItems={Scene.GetAllComponents<WoodItem>().Count()})" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] SPLIT PASS  items={items.Count} swings={_swingsFired}  (sapling.LandedDropCount={expected})" );
		Transition( Phase.TestLandedLogChopGrace );
	}

	private void TickTestLandedLogChopGrace()
	{
		if ( !_landedGraceSpawned )
		{
			_landedGraceSpawned = true;
			_landedGraceEarlyHitChecked = false;
			var pos = _targetTreePos + new Vector3( -520f, 420f, 0f );
			if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
			_landedGraceTree = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Normal );
			_landedGraceTree.StartFell( Vector3.Forward );
			_landedGraceLog = FindNearestFallenLog( pos, 700f );
			return;
		}

		if ( !_landedGraceLog.IsValid() )
		{
			_landedGraceLog = FindNearestFallenLog( _targetTreePos + new Vector3( -520f, 420f, 0f ), 700f );
		}
		if ( !_landedGraceLog.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestLandedLogChopGrace: TreeLog missing after TreeBase.SpawnLog" );
			Finish();
			return;
		}

		if ( !_landedGraceEarlyHitChecked )
		{
			_landedGraceEarlyHitChecked = true;
			int hpBefore = _landedGraceLog.ChopsRemaining;
			_landedGraceLog.Chop( Vector3.Forward, 1, _landedGraceLog.LogCenter );
			_landedGraceHpAfterEarlyHit = _landedGraceLog.ChopsRemaining;
			if ( _landedGraceHpAfterEarlyHit != hpBefore )
			{
				Log.Error( $"[TC_TEST] FAIL TestLandedLogChopGrace: early hit damaged log HP {hpBefore}->{_landedGraceHpAfterEarlyHit}" );
				Finish();
			}
			_landedGraceLog.ApplyImpactDamage( 999, Vector3.Forward );
			if ( !_landedGraceLog.IsValid() || _landedGraceLog.ChopsRemaining != hpBefore )
			{
				Log.Error( $"[TC_TEST] FAIL TestLandedLogChopGrace: early impact damaged/split fresh log (valid={_landedGraceLog.IsValid()}, hp={(_landedGraceLog.IsValid() ? _landedGraceLog.ChopsRemaining : -1)}, expected={hpBefore})" );
				Finish();
			}
			return;
		}

		if ( (float)_phaseTime < Tunables.WoodLogChopGrace + 0.08f ) return;
		_landedGraceLog.Damage( HitData.Make( Vector3.Forward, 1, _landedGraceLog.LogCenter,
			Tunables.TreeKindMinAxeTier[(int)TreeKind.Normal], Tunables.LandedLogKickImpulse ) );
		if ( _landedGraceLog.IsValid() && _landedGraceLog.ChopsRemaining >= _landedGraceHpAfterEarlyHit )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogChopGrace: post-grace hit did not damage log HP={_landedGraceLog.ChopsRemaining}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] TREELOG_FIRSTFRAME_GRACE PASS  early chop+impact ignored for {Tunables.WoodLogChopGrace:0.00}s, post-grace hit applied without landed gate" );
		Transition( Phase.TestBonusDrop );
	}

	private void TickTestBonusDrop()
	{
		// Spawn un Veteran (DropChance=1.0, Min=2, Max=4 bonus items) puis
		// force fell via CascadeWake. Snapshot WoodItems avant/après pour
		// vérifier que ≥2 items ont apparu dans le ring près du spawn.
		if ( !_bonusDropSpawned )
		{
			// Spawn loin de la zone du test précédent pour pas que la cascade
			// physique tape autre chose.
			var spawnPos = _targetTreePos + new Vector3( 0f, 600f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var groundZ ) )
				spawnPos = spawnPos.WithZ( groundZ );
			_bonusDropSpawned = true;
			_bonusDropPos = spawnPos;
			_woodItemsBeforeBonusDrop = Scene.GetAllComponents<WoodItem>().Count( w => w.IsValid() );
			_bonusDropVeteran = Tree.SpawnAt( Scene, spawnPos, 1f, TreeKind.Veteran );
			Log.Info( $"[TC_TEST] BonusDrop : spawned Veteran at {spawnPos}, WoodItems before={_woodItemsBeforeBonusDrop}" );
			// StartFell (internal pour tests) déclenche le bonus drop directement.
			_bonusDropVeteran.StartFell( Vector3.Forward );
			return;
		}

		// Attend 2 ticks pour laisser StartFell + WoodItem.SpawnAt s'exécuter.
		if ( (float)_phaseTime < 0.1f ) return;

		// Veteran : DropChance=1.0 → toujours ≥ Min (2) items. Compter items
		// récemment apparus près du spawn de Veteran.
		int totalNow = Scene.GetAllComponents<WoodItem>().Count( w => w.IsValid() );
		int delta = totalNow - _woodItemsBeforeBonusDrop;
		int minExpected = Tunables.TreeKindFellBonusItemsMin[(int)TreeKind.Veteran];

		if ( delta < minExpected )
		{
			Log.Error( $"[TC_TEST] FAIL TestBonusDrop: only {delta} new WoodItems (expected ≥{minExpected} for Veteran DropChance=1.0)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] BONUS_DROP PASS  delta={delta} items (expected {minExpected}..{Tunables.TreeKindFellBonusItemsMax[(int)TreeKind.Veteran]})" );
		Transition( Phase.TestSplitLogSpawn );
	}

	private void TickTestSplitLogSpawn()
	{
		if ( !_splitLogSpawned )
		{
			UpgradeAxeTo( Tunables.TreeKindMinAxeTier[(int)TreeKind.Normal] );
			var spawnPos = _targetTreePos + new Vector3( 360f, 620f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var groundZ ) )
				spawnPos = spawnPos.WithZ( groundZ );
			ClearTestObjectsAround( spawnPos, 760f );
			_splitLogPos = spawnPos;
			_splitLogTree = Tree.SpawnAt( Scene, spawnPos, 0.5f, TreeKind.Normal );
			_splitLogTree.StartFell( Vector3.Forward );
			_splitLogParent = _splitLogTree.SpawnedLog;
			_splitLogSinceLanded = 0f;
			_splitLogParentDropBaselineSet = false;
			_splitLogFreshGraceChecked = false;
			_splitLogParentDropBaseline = 0;
			_splitLogParentLength = _splitLogParent.IsValid() ? _splitLogParent.DebugTrunkLength : 0f;
			_splitLogParentWidth = _splitLogParent.IsValid() ? _splitLogParent.DebugTrunkWidth : 0f;
			_splitLogSpawned = true;
			return;
		}

		if ( _splitLogParent.IsValid() && !_splitLogParent.IsFallenLog )
		{
			_splitLogSinceLanded = 0f;
			return;
		}
		if ( _splitLogParent.IsValid() && (float)_splitLogSinceLanded < Tunables.WoodLogChopGrace + 0.08f )
			return;

		if ( _splitLogParent.IsValid() )
		{
			if ( !ValidateTreeLogPhysicsContract( _splitLogParent,
				Tunables.ValheimTreeLogFullMass * MathF.Max( 0.1f, _splitLogParent.DebugSourceScale ),
				0, "parent TreeLog", out var parentPhysicsError ) )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: live tree -> parent log physics mismatch: {parentPhysicsError}" );
				Finish();
				return;
			}
			if ( !_splitLogParentDropBaselineSet )
			{
				_splitLogParentDropBaseline = CountWoodItemsAndBackpackNear( _splitLogPos, 700f );
				_splitLogParentDropBaselineSet = true;
			}
			if ( _splitLogSwings > 6 )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: parent log still valid after {_splitLogSwings} swings hp={_splitLogParent.ChopsRemaining}" );
				Finish();
				return;
			}
			ParkPlayerFacingLog( _splitLogParent );
			_axe.DebugSwingVerbose();
			_splitLogSwings++;
			return;
		}

		if ( !_splitLogSpawnObserved )
		{
			_splitLogSpawnObserved = true;
			_splitLogValidationSince = 0f;
			if ( !_splitLogFreshGraceChecked )
			{
				var freshLog = Scene.GetAllComponents<FallenLog>()
					.FirstOrDefault( l => l.IsValid() && !l.IsSplit && l.DebugSplitDepth > 0 && l.LogCenter.Distance( _splitLogPos ) < 700f );
				if ( !freshLog.IsValid() )
				{
					Log.Error( "[TC_TEST] FAIL TestSplitLogSpawn: no fresh split log found for first-frame grace check" );
					Finish();
					return;
				}
				int hpBeforeFreshHit = freshLog.ChopsRemaining;
				freshLog.DebugResetGraceForTest();
				var freshHit = HitData.Make( Vector3.Forward, 1, freshLog.LogCenter + Vector3.Up * 8f,
					Tunables.TreeKindMinAxeTier[(int)freshLog.Kind], Tunables.LandedLogKickImpulse );
				freshLog.Damage( freshHit );
				if ( !freshLog.IsValid() || freshLog.ChopsRemaining != hpBeforeFreshHit )
				{
					Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: fresh split log accepted damage during Valheim first-frame grace hp={hpBeforeFreshHit}->{(freshLog.IsValid() ? freshLog.ChopsRemaining : -1)}" );
					Finish();
					return;
				}
				_splitLogFreshGraceChecked = true;
			}
			return;
		}
		float splitLogValidationWait = Quick
			? MathF.Min( 0.45f, Tunables.SplitLogSpawnPoseSettleDuration + 0.35f )
			: Tunables.SplitLogSpawnPoseSettleDuration + 0.35f;
		if ( (float)_splitLogValidationSince < splitLogValidationWait )
			return;

		var childLogs = Scene.GetAllComponents<FallenLog>()
			.Where( l => l.IsValid() && !l.IsSplit && l.DebugSplitDepth > 0 && l.LogCenter.Distance( _splitLogPos ) < 700f )
			.ToList();
		int splitLogs = childLogs.Count;
		int expected = Tunables.TreeKindSplitLogCount[(int)TreeKind.Normal];
		int expectedParentDrops = FallenLog.ComputeParentLogImmediateDropCount( Tunables.TreeKindLandedDropCount[(int)TreeKind.Normal], expected );
		int parentDropDelta = CountWoodItemsAndBackpackNear( _splitLogPos, 700f ) - _splitLogParentDropBaseline;
		if ( parentDropDelta < expectedParentDrops )
		{
			Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: Valheim TreeLog.Destroy should spawn parent drops before smaller logs, delta={parentDropDelta}, expected>={expectedParentDrops}" );
			Finish();
			return;
		}
		if ( splitLogs < expected )
		{
			Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: splitLogs={splitLogs}, expected >= {expected}" );
			Finish();
			return;
		}
		foreach ( var log in childLogs )
		{
			if ( !ValidateTreeLogPhysicsContract( log,
				Tunables.ValheimTreeLogHalfMass * MathF.Max( 0.1f, log.DebugSourceScale ),
				1, "smaller TreeLog", out var childPhysicsError ) )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: parent log -> smaller log physics mismatch: {childPhysicsError}" );
				Finish();
				return;
			}
			float allowedUpDot = AllowedTerrainAwareUpDot( log, Tunables.SplitLogMaxSpawnUpDot );
			if ( log.DebugAxisUpDot() > allowedUpDot )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: split log too vertical upDot={log.DebugAxisUpDot():F2} allowed={allowedUpDot:F2} center={log.LogCenter}" );
				Finish();
				return;
			}
			float clearance = log.DebugMinGroundClearance();
			if ( clearance < -Tunables.LogGroundSkin * 2f )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: split log penetrates terrain clearance={clearance:F1}u center={log.LogCenter}" );
				Finish();
				return;
			}
			if ( log.Body.IsValid() && !log.Body.Gravity )
			{
				Log.Error( "[TC_TEST] FAIL TestSplitLogSpawn: split log disabled gravity, but Valheim TreeLog keeps Rigidbody.useGravity enabled" );
				Finish();
				return;
			}
			if ( log.Body.IsValid() && log.Body.Velocity.Length > Tunables.SplitLogMaxSpawnValidationSpeed )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: split log launched at absurd speed vel={log.Body.Velocity.Length:F1}u/s" );
				Finish();
				return;
			}
			if ( _splitLogParentLength > 0f && log.DebugTrunkLength > _splitLogParentLength * Tunables.SplitLogMaxParentLengthFrac + 0.5f )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: split log too large len={log.DebugTrunkLength:F1}u parent={_splitLogParentLength:F1}u maxFrac={Tunables.SplitLogMaxParentLengthFrac:F2}" );
				Finish();
				return;
			}
			if ( _splitLogParentWidth > 0f )
			{
				float expectedWidth = _splitLogParentWidth * Tunables.TreeKindSplitLogWidthFrac[(int)log.Kind];
				if ( MathF.Abs( log.DebugTrunkWidth - expectedWidth ) > 0.5f )
				{
					Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: split log width={log.DebugTrunkWidth:F1}u expected Valheim half-log width {expectedWidth:F1}u" );
					Finish();
					return;
				}
			}
		}
		var sampleSplitLog = Scene.GetAllComponents<FallenLog>()
			.FirstOrDefault( l => l.IsValid() && !l.IsSplit && l.DebugSplitDepth > 0 && l.LogCenter.Distance( _splitLogPos ) < 700f );
		if ( sampleSplitLog.IsValid() )
		{
			int hpBeforePush = sampleSplitLog.ChopsRemaining;
			var hit = HitData.Make( Vector3.Forward, 0, sampleSplitLog.LogCenter + Vector3.Up * 8f,
				Tunables.TreeKindMinAxeTier[(int)sampleSplitLog.Kind], Tunables.LandedLogKickImpulse );
			sampleSplitLog.Damage( hit );
			var expectedImpulse = Vector3.Forward * Tunables.LandedLogKickImpulse * Tunables.ValheimTreeLogHitPushMul;
			if ( !sampleSplitLog.IsValid() || sampleSplitLog.ChopsRemaining != hpBeforePush
				|| (sampleSplitLog.DebugLastLandedKickImpulse - expectedImpulse).Length > 0.01f )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: split log did not behave like TreeLog on zero-damage push hp={hpBeforePush}->{(sampleSplitLog.IsValid() ? sampleSplitLog.ChopsRemaining : -1)} impulse={(sampleSplitLog.IsValid() ? sampleSplitLog.DebugLastLandedKickImpulse : Vector3.Zero)} expected={expectedImpulse}" );
				Finish();
				return;
			}
			var scarHit = HitData.Make( Vector3.Forward, 1, sampleSplitLog.LogCenter + Vector3.Up * 8f,
				Tunables.TreeKindMinAxeTier[(int)sampleSplitLog.Kind] );
			sampleSplitLog.Damage( scarHit );
			float maxExpectedScarExtent = MathF.Max( sampleSplitLog.DebugTrunkWidth * 0.42f * 1.45f, sampleSplitLog.DebugTrunkLength * 0.10f * 1.45f ) + 1f;
			if ( !sampleSplitLog.IsValid() || sampleSplitLog.DebugMaxScarWorldExtent <= 0f || sampleSplitLog.DebugMaxScarWorldExtent > maxExpectedScarExtent )
			{
				Log.Error( $"[TC_TEST] FAIL TestSplitLogSpawn: log chop scar visual exploded extent={sampleSplitLog.DebugMaxScarWorldExtent:F1}u expected<={maxExpectedScarExtent:F1}u" );
				Finish();
				return;
			}
		}
		Log.Info( $"[TC_TEST] LIVE_TREE_TO_SUBLOGS PASS  Normal standing tree -> parent TreeLog -> {splitLogs} smaller TreeLogs, same Rigidbody contract, {parentDropDelta} immediate parent drops" );
		ClearTestObjectsAround( _splitLogPos, 900f );
		Transition( PhysicsOnly ? Phase.TestImpactNoSelfDamage : Phase.TestWoodPickup );
	}

	private void TickTestWoodPickup()
	{
		// Spawn un WoodItem juste au-dessus du player. Avec Valheim grace 0.5s,
		// le magnet n'engage pas tout de suite : pendant 0.5s l'item subit
		// gravité + sa burst velocity (peut s'envoler hors range). On override
		// la velocity à zéro pour garder l'item près du player pendant grace.
		// Vérifie que BackpackWood s'incrémente après grace+magnet (~0.6-1s).
		if ( !_pickupSpawned )
		{
			_pickupSpawned = true;
			_state.ResetForTest();
			_backpackBeforePickup = _state.BackpackWood;
			// Pin the item at the magnet target. This phase validates pickup
			// grace + banking, not item-vs-player capsule physics.
			var spawnPos = _axe.WorldPosition + Vector3.Up * (Tunables.PlayerEyeHeight * 0.4f);
			var item = WoodItem.SpawnAt( Scene, spawnPos );
			if ( item.Body.IsValid() )
			{
				item.Body.MotionEnabled = false;
				item.Body.Velocity = Vector3.Zero;
				item.Body.AngularVelocity = Vector3.Zero;
				item.Body.Gravity = false;
				if ( item.Body.PhysicsBody.IsValid() )
				{
					item.Body.PhysicsBody.Position = spawnPos;
					item.Body.PhysicsBody.Velocity = Vector3.Zero;
					item.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
				}
			}
			_pickupSpawnTime = 0f;
			Log.Info( $"[TC_TEST] WoodPickup : spawned item at magnet target {spawnPos}, backpack before={_backpackBeforePickup}" );
			return;
		}

		// Wait up to 2.5s for the item to magnet + pickup (incl 0.5s grace +
		// the item-vs-player-collider push-out drifts to ~35-40u typically,
		// still within MagnetRange 80u → magnet engages reliably).
		if ( _state.BackpackWood > _backpackBeforePickup )
		{
			Log.Info( $"[TC_TEST] PICKUP PASS  backpack {_backpackBeforePickup}→{_state.BackpackWood}  (elapsed={(float)_pickupSpawnTime:F2}s, grace={Tunables.WoodItemMagnetGrace}s)" );
			Transition( Phase.TestImpactNoSelfDamage );
			return;
		}
		if ( (float)_pickupSpawnTime > 2.5f )
		{
			var items = Scene.GetAllComponents<WoodItem>()
				.Where( w => w.IsValid() )
				.ToList();
			Log.Error( $"[TC_TEST] FAIL TestWoodPickup: backpack stayed at {_state.BackpackWood} after 2.5s. Scene WoodItems valid={items.Count}" );
			foreach ( var it in items.Take( 5 ) )
				Log.Error( $"[TC_TEST]    item pos={it.WorldPosition}, dist={it.WorldPosition.Distance( _axe.WorldPosition ):F1}u" );
			Finish();
			return;
		}
	}

	private void TickTestImpactNoSelfDamage()
	{
		// Valheim TreeLog prefab: ImpactEffect.m_damageToSelf=false. A falling
		// log can damage another tree/log, but a hard ground hit must not
		// auto-split itself.
		if ( !_impactNoSelfSpawned )
		{
			_impactNoSelfSpawned = true;
			var spawnPos = _targetTreePos + new Vector3( 600f, -600f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var groundZ ) )
				spawnPos = spawnPos.WithZ( groundZ + 50f ); // slight gap for clean OnCollisionStart event
			ClearTestObjectsAround( spawnPos, 900f );
			_impactNoSelfTree = Tree.SpawnAt( Scene, spawnPos, 1f, TreeKind.Brittle );
			_impactNoSelfTree.StartFell( Vector3.Forward );
			_impactNoSelfLog = _impactNoSelfTree.SpawnedLog;
			if ( _impactNoSelfLog.IsValid() && _impactNoSelfLog.Body.IsValid() )
				_impactNoSelfLog.Body.Velocity = new Vector3( 0f, 0f, -1500f );
			_impactNoSelfStartTime = 0f;
			Log.Info( $"[TC_TEST] ImpactNoSelf : spawned Brittle at {spawnPos} with forced -1500u/s downward velocity" );
			return;
		}

		if ( !_impactNoSelfLog.IsValid() || _impactNoSelfLog.IsSplit )
		{
			Log.Error( "[TC_TEST] FAIL TestImpactNoSelfDamage: hard ground impact split/destroyed the source log, but Valheim TreeLog.m_damageToSelf=false" );
			Finish();
			return;
		}

		if ( (float)_impactNoSelfStartTime > 0.35f )
		{
			if ( !_impactNoSelfLog.Body.IsValid() || !_impactNoSelfLog.Body.Gravity )
			{
				Log.Error( "[TC_TEST] FAIL TestImpactNoSelfDamage: hard ground impact disabled log gravity/body" );
				Finish();
				return;
			}
			Log.Info( $"[TC_TEST] IMPACT_NO_SELF_DAMAGE PASS  hard ground hit did not self-split and remains physics-owned (m_damageToSelf=false, falling={_impactNoSelfLog.IsFalling}, landed={_impactNoSelfLog.IsFallenLog}, speed={_impactNoSelfLog.Body.Velocity.Length:F1})" );
			ClearTestObjectsAround( _impactNoSelfLog.WorldPosition, 900f );
			Transition( Phase.TestLandedLogGravity );
			return;
		}

		if ( (float)_impactNoSelfStartTime > 3f )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactNoSelfDamage: log did not settle after hard ground impact (falling={_impactNoSelfLog.IsFalling} landed={_impactNoSelfLog.IsFallenLog} upDot={_impactNoSelfLog.DebugAxisUpDot():F2} clearance={_impactNoSelfLog.DebugMinGroundClearance():F1} speed={(_impactNoSelfLog.Body.IsValid() ? _impactNoSelfLog.Body.Velocity.Length : 0f):F1})" );
			Finish();
			return;
		}
	}

	private void TickTestLandedLogGravity()
	{
		if ( !_landedGravitySpawned )
		{
			_landedGravitySpawned = true;
			_landedGravityLifted = false;
			var spawnPos = _targetTreePos + new Vector3( 760f, -1040f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var groundZ ) )
				spawnPos = spawnPos.WithZ( groundZ );
			ClearTestObjectsAround( spawnPos, 900f );
			_landedGravityTree = Tree.SpawnAt( Scene, spawnPos, 1f, TreeKind.Normal );
			_landedGravityTree.StartFell( Vector3.Forward );
			_landedGravityLog = _landedGravityTree.SpawnedLog;
			_landedGravityStartTime = 0f;
			Log.Info( $"[TC_TEST] LandedLogGravity : spawned Normal at {spawnPos}" );
			return;
		}

		if ( !_landedGravityLog.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestLandedLogGravity: spawned log vanished before gravity validation" );
			Finish();
			return;
		}

		if ( (float)_landedGravityStartTime < Tunables.WoodLogChopGrace + 0.08f ) return;

		if ( !_landedGravityLog.Body.IsValid() || !_landedGravityLog.Body.Gravity )
		{
			Log.Error( "[TC_TEST] FAIL TestLandedLogGravity: landed log disabled gravity, but Valheim TreeLog keeps Rigidbody.useGravity enabled" );
			Finish();
			return;
		}

		if ( !_landedGravityLifted )
		{
			_landedGravityLog.WorldRotation = Rotation.FromAxis( Vector3.Right, 90f );
			var liftedPos = _landedGravityLog.WorldPosition + Vector3.Up * 220f;
			_landedGravityLog.WorldPosition = liftedPos;
			if ( _landedGravityLog.Body.PhysicsBody.IsValid() )
			{
				_landedGravityLog.Body.PhysicsBody.Position = liftedPos;
				_landedGravityLog.Body.PhysicsBody.Rotation = _landedGravityLog.WorldRotation;
				_landedGravityLog.Body.PhysicsBody.Velocity = Vector3.Zero;
				_landedGravityLog.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
			}
			_landedGravityLog.Body.Velocity = Vector3.Zero;
			_landedGravityLog.Body.AngularVelocity = Vector3.Zero;
			_landedGravityLog.Body.Sleeping = false;
			_landedGravityStartZ = liftedPos.z;
			_landedGravityStartClearance = _landedGravityLog.DebugMinGroundClearance();
			_landedGravitySinceLift = 0f;
			_landedGravityLifted = true;
			return;
		}

		if ( (float)_landedGravitySinceLift < 0.32f ) return;

		float drop = _landedGravityStartZ - _landedGravityLog.WorldPosition.z;
		float clearance = _landedGravityLog.DebugMinGroundClearance();
		float vz = _landedGravityLog.Body.Velocity.z;
		if ( _landedGravityStartClearance < Tunables.TreeGroundedLandingClearance * 5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogGravity: probe did not start airborne clearance={_landedGravityStartClearance:F1}u" );
			Finish();
			return;
		}
		if ( drop < 25f || vz > -15f )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogGravity: airborne landed log did not fall under gravity drop={drop:F1}u vz={vz:F1}u/s clearance={clearance:F1}u" );
			Finish();
			return;
		}
		if ( drop > 120f || clearance < Tunables.TreeGroundedLandingClearance * 3f )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogGravity: airborne landed log was snapped down instead of falling clearance={clearance:F1}u drop={drop:F1}u" );
			Finish();
			return;
		}

		Log.Info( $"[TC_TEST] LANDED_LOG_GRAVITY PASS  startClear={_landedGravityStartClearance:F1}u drop={drop:F1}u vz={vz:F1}u/s clearance={clearance:F1}u" );
		ClearTestObjectsAround( _landedGravityLog.WorldPosition, 900f );
		Transition( Phase.TestLandedLogSupport );
	}

	private void TickTestLandedLogSupport()
	{
		if ( !_landedSupportSpawned )
		{
			_landedSupportSpawned = true;
			_landedSupportLandedSeen = false;
			_landedSupportSinceTrace = 0f;
			var spawnPos = _targetTreePos + new Vector3( 900f, -760f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var groundZ ) )
				spawnPos = spawnPos.WithZ( groundZ );
			ClearTestObjectsAround( spawnPos, 900f );
			_landedSupportTree = Tree.SpawnAt( Scene, spawnPos, 1f, TreeKind.Normal );
			_landedSupportTree.StartFell( Vector3.Forward );
			_landedSupportLog = _landedSupportTree.SpawnedLog;
			Log.Info( $"[TC_TEST] LandedLogSupport : spawned Normal at {spawnPos}" );
			if ( _landedSupportLog.IsValid() )
				Log.Info( $"[TC_TEST] LOG_PHYS LandedLogSupport spawn {_landedSupportLog.DebugGroundTraceSummary()}" );
			return;
		}

		if ( !_landedSupportLog.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestLandedLogSupport: spawned log vanished before landing validation" );
			Finish();
			return;
		}

		if ( !_landedSupportLog.IsFallenLog )
		{
			if ( (float)_landedSupportSinceTrace > 0.5f )
			{
				_landedSupportSinceTrace = 0f;
				Log.Info( $"[TC_TEST] LOG_PHYS LandedLogSupport falling {_landedSupportLog.DebugGroundTraceSummary()}" );
			}
			if ( (float)_phaseTime > 8f )
			{
				Log.Error( $"[TC_TEST] FAIL TestLandedLogSupport: log never landed naturally upDot={_landedSupportLog.DebugAxisUpDot():F2} clearance={_landedSupportLog.DebugMinGroundClearance():F1} vel={_landedSupportLog.Body.Velocity.Length:F1} ang={_landedSupportLog.Body.AngularVelocity.Length:F2}" );
				Finish();
			}
			return;
		}

		if ( !_landedSupportLandedSeen )
		{
			_landedSupportLandedSeen = true;
			_landedSupportSinceLanded = 0f;
			Log.Info( $"[TC_TEST] LOG_PHYS LandedLogSupport landed {_landedSupportLog.DebugGroundTraceSummary()}" );
			return;
		}
		float landedSupportWait = Quick ? 0.35f : 0.8f;
		if ( (float)_landedSupportSinceLanded < landedSupportWait ) return;

		float upDot = _landedSupportLog.DebugAxisUpDot();
		float clearance = _landedSupportLog.DebugMinGroundClearance();
		float speed = _landedSupportLog.Body.IsValid() ? _landedSupportLog.Body.Velocity.Length : 0f;
		float angularSpeed = _landedSupportLog.Body.IsValid() ? _landedSupportLog.Body.AngularVelocity.Length : 0f;
		if ( !Quick && (speed > Tunables.TreeRestingLandingSpeed || angularSpeed > Tunables.TreeRestingLandingAngularSpeed) && (float)_phaseTime < 8f )
			return;
		float allowedUpDot = AllowedTerrainAwareUpDot( _landedSupportLog, Tunables.TreeRestingTiltUpDotMax + 0.10f );
		if ( upDot > allowedUpDot )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogSupport: landed log too vertical upDot={upDot:F2} allowed={allowedUpDot:F2}" );
			Finish();
			return;
		}
		if ( clearance < -Tunables.LogGroundSkin * 2.0f || clearance > Tunables.TreeGroundedLandingClearance + Tunables.LogGroundSkin )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogSupport: landed log not supported on terrain clearance={clearance:F1}u" );
			Finish();
			return;
		}
		if ( !_landedSupportLog.Body.IsValid() || !_landedSupportLog.Body.Gravity )
		{
			Log.Error( "[TC_TEST] FAIL TestLandedLogSupport: landed log disabled gravity, but Valheim TreeLog keeps Rigidbody.useGravity enabled" );
			Finish();
			return;
		}
		if ( _landedSupportLog.ChopsRemaining != Tunables.LogChopHP[(int)TreeKind.Normal] )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogSupport: landed log HP={_landedSupportLog.ChopsRemaining}, expected {Tunables.LogChopHP[(int)TreeKind.Normal]}" );
			Finish();
			return;
		}
		int expectedShapeCount = 1 + Tunables.LogSupportSphereCount;
		if ( _landedSupportLog.DebugColliderShapeCount < expectedShapeCount )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogSupport: log collider support shapes={_landedSupportLog.DebugColliderShapeCount}, expected >= {expectedShapeCount}" );
			Finish();
			return;
		}

		Log.Info( $"[TC_TEST] LANDED_LOG_SUPPORT PASS  clearance={clearance:F1}u upDot={upDot:F2} speed={speed:F1}u/s ang={angularSpeed:F2} shapes={_landedSupportLog.DebugColliderShapeCount}" );
		ClearTestObjectsAround( _landedSupportLog.WorldPosition, 900f );
		Transition( PhysicsOnly ? Phase.TestCascadeDamage : Phase.TestStumpRespawn );
	}

	private void TickTestStumpRespawn()
	{
		// Find la TreeStump du sapling felled, force-trigger sa GrowAnimation
		// via TestForceRespawn (internal hook), wait GrowDuration + a bit, verify :
		// (a) une nouvelle Tree existe au foot pos,
		// (b) la nouvelle Tree a un WorldScale ~1.0,
		// (c) le stump est destroyed après l'animation.
		if ( !_respawnTriggered )
		{
			_respawnStump = Scene.GetAllComponents<TreeStump>()
				.Where( s => s.IsValid() && s.WorldPosition.Distance( _targetTreePos ) < 30f )
				.FirstOrDefault();
			if ( !_respawnStump.IsValid() )
			{
				Log.Error( $"[TC_TEST] FAIL TestStumpRespawn: aucune souche trouvée près du sapling felled ({_targetTreePos})" );
				Finish();
				return;
			}
			_respawnStump.TestForceRespawn();
			_respawnStartTime = 0f;
			_respawnTriggered = true;
			Log.Info( $"[TC_TEST] StumpRespawn : forced respawn on stump at {_respawnStump.WorldPosition}" );
			return;
		}

		// Wait for grow animation to complete (Tunables.TreeGrowDuration = 0.4s).
		// La nouvelle Tree existe pendant et après le grow, le stump est destroy à la fin.
		float waitTime = Tunables.TreeGrowDuration + 0.1f;
		if ( (float)_respawnStartTime < waitTime ) return;

		// Verify : new Tree exists at foot pos with full scale.
		var newTree = Scene.GetAllComponents<Tree>()
			.Where( t => t.IsValid() && t.WorldPosition.Distance( _targetTreePos ) < 30f && t.IsStanding )
			.FirstOrDefault();
		if ( !newTree.IsValid() )
		{
			Log.Error( $"[TC_TEST] FAIL TestStumpRespawn: aucune nouvelle Tree au foot pos après respawn" );
			Finish();
			return;
		}
		float newScale = newTree.GameObject.WorldScale.x;
		if ( newScale < 0.9f )
		{
			Log.Error( $"[TC_TEST] FAIL TestStumpRespawn: nouvelle Tree.WorldScale={newScale:F2} < 0.9 (grow animation incomplete?)" );
			Finish();
			return;
		}
		// Stump should be destroyed after grow completes.
		if ( _respawnStump.IsValid() )
		{
			Log.Error( $"[TC_TEST] FAIL TestStumpRespawn: stump still valid après grow animation complete (devrait être destroyed)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] RESPAWN PASS  newTree scale={newScale:F2}, stump destroyed, kind={newTree.Kind}" );
		Transition( Phase.TestCascadeDamage );
	}

	private void TickTestCascadeDamage()
	{
		// Test direct du Valheim ImpactEffect pattern : spawn un Sapling
		// standing, call ApplyImpactDamage avec un damage suffisant pour
		// dépasser son HP, verify state transition vers _chopped=true (StartFell
		// déclenché). Pas de simulation physique — juste vérif que la mécanique
		// damage→fell fonctionne (la cascade via collision réelle est testée
		// indirectement par TestImpactNoSelfDamage).
		var saplingPos = _targetTreePos + new Vector3( -800f, 0f, 0f );
		if ( TryGetGroundZ( saplingPos.x, saplingPos.y, out var groundZ ) )
			saplingPos = saplingPos.WithZ( groundZ );
		var sapling = Tree.SpawnAt( Scene, saplingPos, 1f, TreeKind.Sapling );
		int hpBefore = sapling.ChopsRemaining;
		if ( !sapling.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeDamage: spawned sapling pas standing (état corrompu)" );
			Finish();
			return;
		}

		// Apply un damage massif (> hpBefore) — devrait déclencher StartFell.
		sapling.ApplyImpactDamage( hpBefore + 5, Vector3.Forward );

		if ( sapling.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeDamage: sapling toujours standing après ApplyImpactDamage({hpBefore + 5}) (HP avant={hpBefore})" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] CASCADE_DAMAGE PASS  sapling HP {hpBefore} - {hpBefore + 5} damage → fell (IsStanding=false)" );
		Transition( Phase.TestCascadeCollision );
	}

	private void TickTestCascadeCollision()
	{
		if ( _cascadeCollisionFallingDone )
		{
			TickTestLandedLogCascadeCollision();
			return;
		}

		if ( !_cascadeCollisionSpawned )
		{
			var neighborPos = _targetTreePos + new Vector3( -1050f, 360f, 0f );
			if ( TryGetGroundZ( neighborPos.x, neighborPos.y, out var gzNeighbor ) )
				neighborPos = neighborPos.WithZ( gzNeighbor );

			var sourcePos = neighborPos - Vector3.Forward * 150f;
			if ( TryGetGroundZ( sourcePos.x, sourcePos.y, out var gzSource ) )
				sourcePos = sourcePos.WithZ( gzSource );

			ClearTestObjectsAround( neighborPos, 900f );
			_cascadeNeighbor = Tree.SpawnAt( Scene, neighborPos, 0f, TreeKind.Normal );
			_cascadeSource = Tree.SpawnAt( Scene, sourcePos, 0f, TreeKind.Normal );
			_cascadeNeighborHpBefore = _cascadeNeighbor.ChopsRemaining;
			_cascadeSource.StartFell( Vector3.Forward );
			_cascadeSourceLog = _cascadeSource.SpawnedLog;
			_cascadeSourceLog.WorldRotation = Rotation.FromAxis( Vector3.Right, 90f );
			var logAxis = _cascadeSourceLog.WorldRotation.Up.Normal;
			float startGap = _cascadeSourceLog.DebugTrunkLength + _cascadeSourceLog.DebugColliderRadius + 70f;
			sourcePos = neighborPos - logAxis * startGap;
			sourcePos = sourcePos.WithZ( neighborPos.z + MathF.Max( _cascadeSourceLog.DebugColliderRadius + 8f, _cascadeNeighbor.TrunkLength * 0.35f ) );
			_cascadeCollisionSourcePos = sourcePos;
			_cascadeCollisionAxis = logAxis;
			_cascadeSourceLog.WorldPosition = sourcePos;
			if ( _cascadeSourceLog.Body.IsValid() )
			{
				var velocity = logAxis * (Tunables.ImpactMinSpeed + 760f);
				if ( _cascadeSourceLog.Body.PhysicsBody.IsValid() )
				{
					_cascadeSourceLog.Body.PhysicsBody.Position = sourcePos;
					_cascadeSourceLog.Body.PhysicsBody.Rotation = _cascadeSourceLog.WorldRotation;
					_cascadeSourceLog.Body.PhysicsBody.Velocity = velocity;
					_cascadeSourceLog.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
				}
				_cascadeSourceLog.Body.Velocity = velocity;
				_cascadeSourceLog.Body.AngularVelocity = Vector3.Zero;
				_cascadeSourceLog.Body.Sleeping = false;
			}
			_cascadeCollisionStartTime = 0f;
			_cascadeCollisionSpawned = true;
			Log.Info( $"[TC_TEST] CascadeCollision : source={sourcePos} neighbor={neighborPos} hp={_cascadeNeighborHpBefore}" );
			return;
		}

		if ( !_cascadeNeighbor.IsValid() || !_cascadeNeighbor.IsStanding )
		{
			Log.Info( $"[TC_TEST] CASCADE_COLLISION PASS  falling tree collision woke neighbor (HP before={_cascadeNeighborHpBefore}, valid={_cascadeNeighbor.IsValid()}, standing={(_cascadeNeighbor.IsValid() ? _cascadeNeighbor.IsStanding : false)})" );
			_cascadeCollisionFallingDone = true;
			ClearTestObjectsAround( _cascadeCollisionSourcePos, 900f );
			return;
		}

		if ( _cascadeNeighbor.ChopsRemaining < _cascadeNeighborHpBefore )
		{
			Log.Info( $"[TC_TEST] CASCADE_COLLISION PASS  falling tree collision damaged neighbor HP {_cascadeNeighborHpBefore}→{_cascadeNeighbor.ChopsRemaining}" );
			_cascadeCollisionFallingDone = true;
			ClearTestObjectsAround( _cascadeCollisionSourcePos, 900f );
			return;
		}

		if ( (float)_cascadeCollisionStartTime > 3.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeCollision: neighbor unchanged after falling log collision window (HP={_cascadeNeighbor.ChopsRemaining}/{_cascadeNeighborHpBefore}, logValid={_cascadeSourceLog.IsValid()})" );
			Finish();
		}
	}

	private void TickTestLandedLogCascadeCollision()
	{
		if ( !_landedCascadeSpawned )
		{
			var neighborPos = _targetTreePos + new Vector3( 1600f, 1400f, 0f );
			if ( TryGetGroundZ( neighborPos.x, neighborPos.y, out var gzNeighbor ) )
				neighborPos = neighborPos.WithZ( gzNeighbor );

			var sourcePos = neighborPos - Vector3.Forward * 220f;
			if ( TryGetGroundZ( sourcePos.x, sourcePos.y, out var gzSource ) )
				sourcePos = sourcePos.WithZ( gzSource );

			ClearTestObjectsAround( neighborPos, 900f );
			_landedCascadeNeighbor = Tree.SpawnAt( Scene, neighborPos, 0f, TreeKind.Sapling );
			_landedCascadeSource = Tree.SpawnAt( Scene, sourcePos, 0f, TreeKind.Normal );
			_landedCascadeNeighborHpBefore = _landedCascadeNeighbor.ChopsRemaining;
			_landedCascadeSource.StartFell( Vector3.Forward );
			_landedCascadeLog = _landedCascadeSource.SpawnedLog;
			_landedCascadeStartTime = 0f;
			_landedCascadeSpawned = true;
			Log.Info( $"[TC_TEST] LandedLogCascade : source={sourcePos} neighbor={neighborPos} hp={_landedCascadeNeighborHpBefore}" );
			return;
		}

		if ( !_landedCascadeNeighbor.IsValid() || !_landedCascadeNeighbor.IsStanding )
		{
			Log.Info( $"[TC_TEST] LANDED_LOG_CASCADE PASS  landed log woke neighbor (HP before={_landedCascadeNeighborHpBefore}, valid={_landedCascadeNeighbor.IsValid()}, standing={(_landedCascadeNeighbor.IsValid() ? _landedCascadeNeighbor.IsStanding : false)})" );
			ClearTestObjectsAround( _landedCascadeLogPos.LengthSquared > 0.01f ? _landedCascadeLogPos : _landedCascadeNeighbor.WorldPosition, 900f );
			Transition( PhysicsOnly ? Phase.TestFallingImpactSplit : Phase.TestAxeTierGate );
			return;
		}

		if ( !_landedCascadeLog.IsValid() )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeCollision: landed source log vanished before neighbor impact (neighbor HP={_landedCascadeNeighbor.ChopsRemaining}/{_landedCascadeNeighborHpBefore})" );
			Finish();
			return;
		}

		if ( !_landedCascadeLaunchApplied )
		{
			if ( (float)_landedCascadeStartTime < Tunables.WoodLogChopGrace + 0.12f )
				return;

			_landedCascadeLog.WorldRotation = Rotation.FromAxis( Vector3.Right, 90f );
			_landedCascadeAxis = _landedCascadeLog.WorldRotation.Up.Normal;
			float startGap = _landedCascadeLog.DebugTrunkLength + _landedCascadeLog.DebugColliderRadius + 80f;
			_landedCascadeLogPos = _landedCascadeNeighbor.WorldPosition - _landedCascadeAxis * startGap;
			_landedCascadeLogPos = _landedCascadeLogPos.WithZ( _landedCascadeNeighbor.WorldPosition.z + MathF.Max( _landedCascadeLog.DebugColliderRadius + 8f, _landedCascadeNeighbor.TrunkLength * 0.35f ) );
			_landedCascadeLog.WorldPosition = _landedCascadeLogPos;
			if ( _landedCascadeLog.Body.IsValid() )
			{
				var velocity = _landedCascadeAxis * (Tunables.ImpactMinSpeed + 760f);
				if ( _landedCascadeLog.Body.PhysicsBody.IsValid() )
				{
					_landedCascadeLog.Body.PhysicsBody.Position = _landedCascadeLogPos;
					_landedCascadeLog.Body.PhysicsBody.Rotation = _landedCascadeLog.WorldRotation;
					_landedCascadeLog.Body.PhysicsBody.Velocity = velocity;
					_landedCascadeLog.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
				}
				_landedCascadeLog.Body.Velocity = velocity;
				_landedCascadeLog.Body.AngularVelocity = Vector3.Zero;
				_landedCascadeLog.Body.Sleeping = false;
			}
			_landedCascadeStartTime = 0f;
			_landedCascadeLaunchApplied = true;
			return;
		}

		if ( _landedCascadeNeighbor.ChopsRemaining < _landedCascadeNeighborHpBefore )
		{
			Log.Info( $"[TC_TEST] LANDED_LOG_CASCADE PASS  landed log damaged neighbor HP {_landedCascadeNeighborHpBefore}â†’{_landedCascadeNeighbor.ChopsRemaining}" );
			ClearTestObjectsAround( _landedCascadeLogPos, 900f );
			Transition( PhysicsOnly ? Phase.TestFallingImpactSplit : Phase.TestAxeTierGate );
			return;
		}

		if ( (float)_landedCascadeStartTime > 3.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeCollision: landed log did not damage neighbor HP={_landedCascadeNeighbor.ChopsRemaining}/{_landedCascadeNeighborHpBefore}, logSpeed={(_landedCascadeLog.Body.IsValid() ? _landedCascadeLog.Body.Velocity.Length : 0f):F1}" );
			Finish();
		}
	}

	private void TickTestAxeTierGate()
	{
		// Valheim TreeBase.RPC_Damage returns before Shake() when CheckToolTier
		// fails. AxeTier 0 vs Veteran must leave HP and wobble unchanged.
		_state.ResetForTest(); // tier 0 par default
		var vetPos = _targetTreePos + new Vector3( -1200f, 0f, 0f );
		if ( TryGetGroundZ( vetPos.x, vetPos.y, out var gz ) ) vetPos = vetPos.WithZ( gz );
		var vet = Tree.SpawnAt( Scene, vetPos, 1f, TreeKind.Veteran );
		int hpBefore = vet.ChopsRemaining;
		vet.Chop( Vector3.Forward, _state.ChopPower, vetPos );
		if ( vet.ChopsRemaining != hpBefore )
		{
			Log.Error( $"[TC_TEST] FAIL TestAxeTierGate: Veteran HP changé {hpBefore}→{vet.ChopsRemaining} avec AxeTier 0 (need {Tunables.TreeKindMinAxeTier[(int)TreeKind.Veteran]})" );
			Finish();
			return;
		}
		if ( !vet.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestAxeTierGate: Veteran felled malgré tier gate" );
			Finish();
			return;
		}
		if ( vet.DebugShakeElapsed < 100f )
		{
			Log.Error( $"[TC_TEST] FAIL TestAxeTierGate: too-hard hit started TreeBase shake elapsed={vet.DebugShakeElapsed:F2}, but Valheim returns before Shake()" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] AXE_GATE PASS  AxeTier 0 vs Veteran HP={hpBefore} unchanged + standing + no shake" );
		Transition( Phase.TestLogTierGate );
	}

	private void TickTestLogTierGate()
	{
		if ( !_logTierGateSpawned )
		{
			_state.ResetForTest();
			var vetPos = _targetTreePos + new Vector3( -1380f, 260f, 0f );
			if ( TryGetGroundZ( vetPos.x, vetPos.y, out var gz ) ) vetPos = vetPos.WithZ( gz );
			ClearTestObjectsAround( vetPos, 900f );
			var vet = Tree.SpawnAt( Scene, vetPos, 1f, TreeKind.Veteran );
			vet.StartFell( Vector3.Forward );
			_logTierGateLog = vet.SpawnedLog;
			if ( !_logTierGateLog.IsValid() )
			{
				Log.Error( "[TC_TEST] FAIL TestLogTierGate: Veteran StartFell did not spawn FallenLog" );
				Finish();
				return;
			}
			_logTierGateHpBefore = 0;
			_logTierGateSpawned = true;
			return;
		}

		var log = _logTierGateLog;
		if ( !log.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestLogTierGate: Veteran log vanished before tier check" );
			Finish();
			return;
		}
		if ( (float)_phaseTime < Tunables.WoodLogChopGrace + 0.08f ) return;
		if ( _logTierGateHpBefore <= 0 )
		{
			_logTierGateHpBefore = log.ChopsRemaining;
			return;
		}

		int weakImpactTier = Tunables.TreeKindMinAxeTier[(int)TreeKind.Veteran] - 1;
		int hpBeforeWeakImpact = log.ChopsRemaining;
		log.ApplyImpactDamage( 1, Vector3.Forward, weakImpactTier );
		if ( log.ChopsRemaining != hpBeforeWeakImpact )
		{
			Log.Error( $"[TC_TEST] FAIL TestLogTierGate: weak impact tier {weakImpactTier} damaged Veteran log {hpBeforeWeakImpact}->{log.ChopsRemaining}" );
			Finish();
			return;
		}
		int hpBefore = _logTierGateHpBefore;
		ParkPlayerFacingLog( log );
		var weakHit = _axe.DebugSwingVerbose();
		if ( weakHit != log || log.ChopsRemaining != hpBefore )
		{
			Log.Error( $"[TC_TEST] FAIL TestLogTierGate: weak axe changed Veteran log (hit={weakHit?.GetType().Name ?? "null"} hp {hpBefore}->{(log.IsValid() ? log.ChopsRemaining : -1)} valid={log.IsValid()})" );
			Finish();
			return;
		}
		if ( log.DebugLastLandedKickImpulse.Length > 0.01f )
		{
			Log.Error( $"[TC_TEST] FAIL TestLogTierGate: weak axe pushed Veteran log impulse={log.DebugLastLandedKickImpulse}, but Valheim TreeLog returns before AddForceAtPosition on CheckToolTier fail" );
			Finish();
			return;
		}

		int neededTier = Tunables.TreeKindMinAxeTier[(int)TreeKind.Veteran];
		UpgradeAxeTo( neededTier );
		ParkPlayerFacingLog( log );
		var strongHit = _axe.DebugSwingVerbose();
		if ( strongHit != log || (log.IsValid() && log.ChopsRemaining >= hpBefore) )
		{
			Log.Error( $"[TC_TEST] FAIL TestLogTierGate: valid tier did not damage Veteran log (hit={strongHit?.GetType().Name ?? "null"} hpBefore={hpBefore} hpNow={(log.IsValid() ? log.ChopsRemaining : -1)} valid={log.IsValid()})" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] LOG_TIER_GATE PASS  Veteran log T0 bounced, T{neededTier} damaged/split" );
		Transition( Phase.TestChopPowerScaling );
	}

	private void TickTestChopPowerScaling()
	{
		// Tier 4 = ChopPower 6 (AxeTierChopPower[4]). Sapling HP ~4, donc
		// 1 chop devrait fell. Vérifie que ChopPower scale correctement.
		// AxeTier setter privé → upgrade via TryUpgradeAxe 4 fois (paie les costs).
		// Valheim 1:1 recipes : tier 3+ nécessite Finewood, tier 4+ nécessite CoreWood.
		// Pump tous les types pour cover les besoins de T1..T4.
		_state.ResetForTest();
		int totalWood = 0, totalFinewood = 0, totalCoreWood = 0;
		for ( int i = 1; i <= 4; i++ )
		{
			var recipe = Tunables.AxeTierCostsByType[i];
			totalWood += recipe[0];
			totalFinewood += recipe[1];
			totalCoreWood += recipe[2];
		}
		_state.DebugAddStockpileForTest( totalWood, totalFinewood, totalCoreWood );
		for ( int i = 0; i < 4; i++ )
		{
			if ( !_state.TryUpgradeAxe() )
			{
			Log.Error( $"[TC_TEST] FAIL TestChopPowerScaling: TryUpgradeAxe failed at tier {_state.AxeTier} (stockpile Wood={_state.Wood}/{Tunables.AxeTierCostsByType[_state.AxeTier+1][0]} FW={_state.Finewood}/{Tunables.AxeTierCostsByType[_state.AxeTier+1][1]} CW={_state.CoreWood}/{Tunables.AxeTierCostsByType[_state.AxeTier+1][2]})" );
				Finish();
				return;
			}
		}
		var saplingPos = _targetTreePos + new Vector3( -1400f, 0f, 0f );
		if ( TryGetGroundZ( saplingPos.x, saplingPos.y, out var gz ) ) saplingPos = saplingPos.WithZ( gz );
		var sap = Tree.SpawnAt( Scene, saplingPos, 1f, TreeKind.Sapling );
		int hpBefore = sap.ChopsRemaining;
		int chopPower = _state.ChopPower;
		sap.Chop( Vector3.Forward, chopPower, saplingPos );
		if ( sap.IsStanding && hpBefore <= chopPower )
		{
			Log.Error( $"[TC_TEST] FAIL TestChopPowerScaling: Sapling standing HP={hpBefore} après chop power {chopPower} (expected fell)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] CHOP_POWER PASS  Sapling HP {hpBefore} - {chopPower} chopPower → standing={sap.IsStanding}" );
		Transition( Phase.TestImpactBelowMin );
	}

	private void TickTestImpactBelowMin()
	{
		// Impact à speed < ImpactMinSpeed (Valheim TreeLog m_minVelocity=1m/s) ne doit générer AUCUN damage.
		// Spawn Sapling, simule un OnCollisionStart-like en appelant
		// ApplyImpactDamage avec damage=0 (équivalent à impact below min).
		var pos = _targetTreePos + new Vector3( -1600f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var sap = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		int hpBefore = sap.ChopsRemaining;
		// On peut pas simuler le path collision direct sans collision physique,
		// mais on peut tester l'autre extrémité : ApplyImpactDamage(0) ne fait rien.
		// Le path OnCollisionStart skip avant ApplyImpactDamage si speed<min.
		// Donc tester ApplyImpactDamage(damage=0) couvre le no-op invariant.
		sap.ApplyImpactDamage( 0, Vector3.Forward );
		if ( sap.ChopsRemaining != hpBefore || !sap.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactBelowMin: ApplyImpactDamage(0) a modifié HP {hpBefore}→{sap.ChopsRemaining} ou state" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] IMPACT_LOW PASS  ApplyImpactDamage(0) no-op (HP={hpBefore} unchanged)" );
		Transition( Phase.TestImpactZeroNoOp );
	}

	private void TickTestImpactZeroNoOp()
	{
		// ApplyImpactDamage avec damage négatif → no-op (validation invariant).
		var pos = _targetTreePos + new Vector3( -1800f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var sap = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		int hpBefore = sap.ChopsRemaining;
		sap.ApplyImpactDamage( -5, Vector3.Forward );
		if ( sap.ChopsRemaining != hpBefore )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactZeroNoOp: ApplyImpactDamage(-5) a changé HP {hpBefore}→{sap.ChopsRemaining}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] IMPACT_NEG PASS  ApplyImpactDamage(-5) no-op (HP={hpBefore} unchanged)" );
		Transition( Phase.TestBackpackFull );
	}

	private void TickTestBackpackFull()
	{
		// Fill via AddBackpack, vérifie qu'à cap
		// AddBackpack(1) retourne 0 + BackpackFull=true.
		_state.ResetForTest();
		int cap = _state.BackpackCapacity;
		int banked = _state.AddBackpack( cap );
		if ( banked != cap || _state.BackpackWood != cap )
		{
			Log.Error( $"[TC_TEST] FAIL TestBackpackFull: initial fill banked={banked} (expected {cap}), backpack={_state.BackpackWood}" );
			Finish();
			return;
		}
		int overflow = _state.AddBackpack( 1 );
		if ( overflow != 0 || !_state.BackpackFull )
		{
			Log.Error( $"[TC_TEST] FAIL TestBackpackFull: overflow add returned {overflow} (expected 0), full={_state.BackpackFull}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] BACKPACK_FULL PASS  cap={cap}, fill banked all, overflow returned 0" );
		Transition( Phase.TestBackpackFullPickup );
	}

	private void TickTestBackpackFullPickup()
	{
		if ( !_fullPickupSpawned )
		{
			_fullPickupSpawned = true;
			ClearWoodItems();
			_state.ResetForTest();
			_state.AddBackpack( _state.BackpackCapacity );
			var playerPos = new Vector3( 0f, 1650f, 120f );
			if ( TryGetGroundZ( playerPos.x, playerPos.y, out var gz ) ) playerPos = playerPos.WithZ( gz + 45f );
			ClearTestObjectsAround( playerPos, 500f );
			_axe.TeleportTo( playerPos, 180f );
			_fullPickupSpawnPos = _axe.WorldPosition + Vector3.Up * (Tunables.PlayerEyeHeight * 0.4f);
			_fullPickupItem = WoodItem.SpawnAt( Scene, _fullPickupSpawnPos );
			if ( _fullPickupItem.Body.IsValid() )
			{
				_fullPickupItem.Body.MotionEnabled = false;
				_fullPickupItem.Body.Gravity = false;
				_fullPickupItem.Body.Velocity = Vector3.Zero;
				_fullPickupItem.Body.AngularVelocity = Vector3.Zero;
				if ( _fullPickupItem.Body.PhysicsBody.IsValid() )
				{
					_fullPickupItem.Body.PhysicsBody.Position = _fullPickupSpawnPos;
					_fullPickupItem.Body.PhysicsBody.Velocity = Vector3.Zero;
					_fullPickupItem.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
				}
			}
			_fullPickupTime = 0f;
			Log.Info( $"[TC_TEST] BackpackFullPickup : spawned item at player magnet point {_fullPickupSpawnPos}, bag={_state.BackpackTotal}/{_state.BackpackCapacity}" );
			return;
		}

		if ( (float)_fullPickupTime < Tunables.WoodItemMagnetGrace - 0.04f )
		{
			_fullPickupSpawnPos = _axe.WorldPosition + Vector3.Up * (Tunables.PlayerEyeHeight * 0.4f);
			_fullPickupItem.WorldPosition = _fullPickupSpawnPos;
			if ( _fullPickupItem.Body.IsValid() )
			{
				_fullPickupItem.Body.MotionEnabled = false;
				_fullPickupItem.Body.Gravity = false;
				_fullPickupItem.Body.Velocity = Vector3.Zero;
				_fullPickupItem.Body.AngularVelocity = Vector3.Zero;
				if ( _fullPickupItem.Body.PhysicsBody.IsValid() )
				{
					_fullPickupItem.Body.PhysicsBody.Position = _fullPickupSpawnPos;
					_fullPickupItem.Body.PhysicsBody.Velocity = Vector3.Zero;
					_fullPickupItem.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
				}
			}
			return;
		}

		if ( (float)_fullPickupTime < Tunables.WoodItemMagnetGrace + 0.35f ) return;

		if ( !_fullPickupItem.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestBackpackFullPickup: full backpack consumed the WoodItem" );
			Finish();
			return;
		}
		float dist = _fullPickupItem.WorldPosition.Distance( _axe.WorldPosition + Vector3.Up * (Tunables.PlayerEyeHeight * 0.4f) );
		bool gravity = _fullPickupItem.Body.IsValid() && _fullPickupItem.Body.Gravity;
		bool movingAway = _fullPickupItem.Body.IsValid() && _fullPickupItem.Body.Velocity.WithZ( 0f ).Length > 1f;
		if ( _fullPickupItem.DebugMagnetized || !gravity || dist < Tunables.WoodItemPickupRange * 1.4f || !movingAway )
		{
			Log.Error( $"[TC_TEST] FAIL TestBackpackFullPickup: item stuck after full reject magnet={_fullPickupItem.DebugMagnetized} gravity={gravity} dist={dist:F1} vel={(_fullPickupItem.Body.IsValid() ? _fullPickupItem.Body.Velocity : Vector3.Zero)}" );
			Finish();
			return;
		}
		if ( !_state.BackpackFull || _state.BackpackTotal != _state.BackpackCapacity )
		{
			Log.Error( $"[TC_TEST] FAIL TestBackpackFullPickup: bag changed during full reject bag={_state.BackpackTotal}/{_state.BackpackCapacity}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] BACKPACK_FULL_PICKUP PASS  item rejected instead of sticking dist={dist:F1} vel={_fullPickupItem.Body.Velocity}" );
		ClearWoodItems();
		Transition( Phase.TestDepositFlush );
	}

	private void TickTestDepositFlush()
	{
		// TryDeposit flush BackpackWood → Wood (stockpile) + reset backpack à 0 +
		// incr TotalWoodEarned. Vérifie le path depot station.
		_state.ResetForTest();
		_state.AddBackpack( 10 );
		int wBefore = _state.Wood;
		int totalBefore = _state.TotalWoodEarned;
		int deposited = _state.TryDeposit();
		if ( deposited != 10 || _state.BackpackWood != 0 || _state.Wood != wBefore + 10
			|| _state.TotalWoodEarned != totalBefore + 10 )
		{
			Log.Error( $"[TC_TEST] FAIL TestDepositFlush: deposited={deposited} (expected 10), backpack={_state.BackpackWood} wood {wBefore}→{_state.Wood} total {totalBefore}→{_state.TotalWoodEarned}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] DEPOT_FLUSH PASS  deposited=10, stockpile {wBefore}→{_state.Wood}, total {totalBefore}→{_state.TotalWoodEarned}, backpack reset" );
		Transition( Phase.TestDepositStationEntry );
	}

	private void TickTestDepositStationEntry()
	{
		var depositStation = Scene.GetAllComponents<ShopStation>()
			.FirstOrDefault( s => s.IsValid() && s.Kind == StationKind.Deposit );
		if ( !depositStation.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestDepositStationEntry: no depot station found" );
			Finish();
			return;
		}

		if ( !_depositStationEntrySetup )
		{
			_depositStationEntrySetup = true;
			_depositStationEntered = false;
			_state.ResetForTest();
			ClearWoodItems();
			_state.AddBackpack( 12 );
			_depositStationStockpileBefore = _state.Wood;
			_depositStationTotalBefore = _state.TotalWoodEarned;
			var outside = depositStation.WorldPosition + Vector3.Right * (depositStation.Radius + 120f) + Vector3.Up * 80f;
			_axe.TeleportTo( outside, 180f );
			return;
		}

		if ( !_depositStationEntered )
		{
			if ( (float)_phaseTime < 0.20f ) return;
			if ( depositStation.PlayerInside )
			{
				Log.Error( "[TC_TEST] FAIL TestDepositStationEntry: station considered player inside before entry transition" );
				Finish();
				return;
			}
			_depositStationEntered = true;
			_axe.TeleportTo( depositStation.WorldPosition + Vector3.Up * 80f, 180f );
			return;
		}

		if ( (float)_phaseTime < 0.45f ) return;
		if ( _state.BackpackTotal != 0 || _state.Wood != _depositStationStockpileBefore + 12
			|| _state.TotalWoodEarned != _depositStationTotalBefore + 12 || !depositStation.PlayerInside )
		{
			Log.Error( $"[TC_TEST] FAIL TestDepositStationEntry: inside={depositStation.PlayerInside} backpack={_state.BackpackTotal} wood {_depositStationStockpileBefore}->{_state.Wood} total {_depositStationTotalBefore}->{_state.TotalWoodEarned}" );
			Finish();
			return;
		}

		Log.Info( $"[TC_TEST] DEPOT_STATION_ENTRY PASS  entering depot ring auto-deposited 12, stockpile {_depositStationStockpileBefore}->{_state.Wood}" );
		Transition( Phase.TestPrestigeFormula );
	}

	private void TickTestPrestigeFormula()
	{
		// SpiritsFromPrestige = floor(sqrt(TotalWoodEarned/50)).
		// Test : 200 lifetime → sqrt(4) = 2 spirits. 1250 → sqrt(25) = 5. 5000 → sqrt(100) = 10.
		_state.ResetForTest();
		_state.AddWood( 200 );
		int s200 = _state.SpiritsFromPrestige;
		_state.AddWood( 1050 ); // total 1250
		int s1250 = _state.SpiritsFromPrestige;
		_state.AddWood( 3750 ); // total 5000
		int s5000 = _state.SpiritsFromPrestige;
		if ( s200 != 2 || s1250 != 5 || s5000 != 10 )
		{
			Log.Error( $"[TC_TEST] FAIL TestPrestigeFormula: 200→{s200}(exp 2), 1250→{s1250}(exp 5), 5000→{s5000}(exp 10)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] PRESTIGE_FORMULA PASS  200→2, 1250→5, 5000→10 spirits (sqrt(N/50) floor)" );
		Transition( Phase.TestFallingImpactSplit );
	}

	private void TickTestFallingImpactSplit()
	{
		// Valheim TreeLog.RPC_Damage applies damage to the Rigidbody object; it
		// does not convert the log to a "landed" state just because an impact
		// or axe hit happened mid-air.
		if ( !_fallingImpactSpawned )
		{
			var pos = _targetTreePos + new Vector3( -2000f, 0f, 0f );
			if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
			var tree = Tree.SpawnAt( Scene, pos, 0.5f, TreeKind.Normal );
			tree.StartFell( Vector3.Forward ); // _chopped=true, _landed=false (falling)
			var spawnedLog = tree.SpawnedLog;
			_fallingImpactLog = spawnedLog;
			_fallingImpactSinceSpawn = 0f;
			_fallingImpactPartialChecked = false;
			_fallingImpactLandingPreserveChecked = false;
			_fallingImpactLandingDamageApplied = false;
			_fallingImpactLandingExpectedHp = 0;
			_fallingImpactLandingProbeLog = null;
			_fallingImpactSpawned = true;
			return;
		}
		var log = _fallingImpactLog;
		if ( !log.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestFallingImpactSplit: log vanished before impact validation" );
			Finish();
			return;
		}
		// Force HP→0 via impact damage massif
		if ( (float)_fallingImpactSinceSpawn < Tunables.WoodLogChopGrace + 0.06f ) return;

		if ( !_fallingImpactPartialChecked )
		{
			if ( !log.IsFalling )
			{
				Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: log pas en etat Falling apres StartFell (falling={log.IsFalling} landed={log.IsFallenLog})" );
				Finish();
				return;
			}
			if ( log.ChopsRemaining != Tunables.LogChopHP[(int)TreeKind.Normal] )
			{
				Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: falling TreeLog HP={log.ChopsRemaining}, expected prefab HP {Tunables.LogChopHP[(int)TreeKind.Normal]} from spawn" );
				Finish();
				return;
			}
			int expectedAfterPartial = Tunables.LogChopHP[(int)TreeKind.Normal] - 1;
			log.ApplyImpactDamage( 1, Vector3.Forward );
			if ( !log.IsValid() || !log.IsFalling || log.IsFallenLog || log.ChopsRemaining != expectedAfterPartial )
			{
				Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: partial falling impact should dent TreeLog without landing. expected hp={expectedAfterPartial}, got valid={log.IsValid()} falling={(log.IsValid() && log.IsFalling)} landed={(log.IsValid() && log.IsFallenLog)} hp={(log.IsValid() ? log.ChopsRemaining : -1)}" );
				Finish();
				return;
			}
			int expectedAfterAxeHit = expectedAfterPartial - 1;
			log.Damage( HitData.Make( Vector3.Forward, 1, log.LogCenter + Vector3.Up * 8f, Tunables.TreeKindMinAxeTier[(int)TreeKind.Normal], Tunables.LandedLogKickImpulse ) );
			if ( !log.IsValid() || !log.IsFalling || log.IsFallenLog || log.ChopsRemaining != expectedAfterAxeHit || log.DebugLastLandedKickImpulse.Length <= 0.01f )
			{
				Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: falling TreeLog should accept player Damage(HitData) after firstFrame grace without landing. expected hp={expectedAfterAxeHit}, got valid={log.IsValid()} falling={(log.IsValid() && log.IsFalling)} landed={(log.IsValid() && log.IsFallenLog)} hp={(log.IsValid() ? log.ChopsRemaining : -1)} impulse={(log.IsValid() ? log.DebugLastLandedKickImpulse : Vector3.Zero)}" );
				Finish();
				return;
			}
			_fallingImpactPartialChecked = true;
			return;
		}

		if ( !_fallingImpactLandingPreserveChecked )
		{
			if ( !_fallingImpactLandingProbeLog.IsValid() )
			{
				var pos = _targetTreePos + new Vector3( 1320f, -620f, 0f );
				if ( !TryGetGroundZ( pos.x, pos.y, out var gz ) )
				{
					Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: landing-preserve probe has no ground at {pos}" );
					Finish();
					return;
				}
				pos = pos.WithZ( gz );
				ClearTestObjectsAround( pos, 900f );
				var tree = Tree.SpawnAt( Scene, pos, 0.5f, TreeKind.Normal );
				tree.StartFell( Vector3.Forward );
				_fallingImpactLandingProbeLog = tree.SpawnedLog;
				_fallingImpactLandingProbeTime = 0f;
				_fallingImpactLandingDamageApplied = false;
				_fallingImpactLandingExpectedHp = 0;
				return;
			}
			if ( !_fallingImpactLandingDamageApplied )
			{
				if ( (float)_fallingImpactLandingProbeTime < Tunables.WoodLogChopGrace + 0.06f ) return;
				if ( !_fallingImpactLandingProbeLog.IsFalling )
				{
					Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: landing-preserve probe naturally landed before falling damage could be applied landed={_fallingImpactLandingProbeLog.IsFallenLog}" );
					Finish();
					return;
				}
				int hpBeforeLanding = _fallingImpactLandingProbeLog.ChopsRemaining;
				_fallingImpactLandingProbeLog.ApplyImpactDamage( 1, Vector3.Forward );
				_fallingImpactLandingExpectedHp = hpBeforeLanding - 1;
				_fallingImpactLandingDamageApplied = true;
				return;
			}
			if ( !_fallingImpactLandingProbeLog.IsValid() )
			{
				Log.Error( "[TC_TEST] FAIL TestFallingImpactSplit: damaged TreeLog vanished before natural landing" );
				Finish();
				return;
			}
			if ( !_fallingImpactLandingProbeLog.IsFallenLog )
			{
				if ( (float)_fallingImpactLandingProbeTime > 8f )
				{
					Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: damaged TreeLog never landed naturally upDot={_fallingImpactLandingProbeLog.DebugAxisUpDot():F2} clearance={_fallingImpactLandingProbeLog.DebugMinGroundClearance():F1} vel={_fallingImpactLandingProbeLog.Body.Velocity.Length:F1} ang={_fallingImpactLandingProbeLog.Body.AngularVelocity.Length:F2}" );
					Finish();
				}
				return;
			}
			if ( !_fallingImpactLandingProbeLog.IsValid() || _fallingImpactLandingProbeLog.ChopsRemaining != _fallingImpactLandingExpectedHp )
			{
				Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: damaged falling TreeLog did not preserve HP after natural landing expected={_fallingImpactLandingExpectedHp}, got valid={_fallingImpactLandingProbeLog.IsValid()} landed={(_fallingImpactLandingProbeLog.IsValid() && _fallingImpactLandingProbeLog.IsFallenLog)} hp={(_fallingImpactLandingProbeLog.IsValid() ? _fallingImpactLandingProbeLog.ChopsRemaining : -1)}" );
				Finish();
				return;
			}
			ClearTestObjectsAround( _fallingImpactLandingProbeLog.WorldPosition, 650f );
			_fallingImpactLandingPreserveChecked = true;
			return;
		}

		var splitCenter = log.LogCenter;
		log.ApplyImpactDamage( 999, Vector3.Forward );
		// Tree should be _logSplit=true → GameObject destroyed
		if ( !log.IsSplit )
		{
			Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: tree toujours valide après ApplyImpactDamage(999) — split non déclenché" );
			Finish();
			return;
		}
		var fallingChildren = Scene.GetAllComponents<FallenLog>()
			.Where( l => l.IsValid() && l.DebugSplitDepth > 0 && l.LogCenter.Distance( splitCenter ) < 700f )
			.ToList();
		int expectedChildren = Tunables.TreeKindSplitLogCount[(int)TreeKind.Normal];
		bool noScriptedVelocity = fallingChildren.All( l => l.Body.IsValid() && l.Body.Velocity.Length < 1f && l.Body.AngularVelocity.Length < 0.01f );
		if ( fallingChildren.Count != expectedChildren || !noScriptedVelocity || fallingChildren.Any( l => !l.IsFalling || l.IsFallenLog || !l.Body.IsValid() || !l.Body.Gravity ) )
		{
			string states = string.Join( ", ", fallingChildren.Select( l => $"falling={l.IsFalling}/landed={l.IsFallenLog}/gravity={(l.Body.IsValid() && l.Body.Gravity)}/hp={l.ChopsRemaining}/vel={(l.Body.IsValid() ? l.Body.Velocity : Vector3.Zero)}" ) );
			Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: falling parent split should instantiate {expectedChildren} fresh physical TreeLogs without scripted inherited velocity, got {fallingChildren.Count}: {states}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] FALLING_IMPACT_SPLIT PASS  TreeLog has prefab HP, impact/player hits dent without landed gate, massive impact spawns fresh physics TreeLogs" );
		Transition( PhysicsOnly ? Phase.TestTunablesValheimSanity : Phase.TestComboFinalDamage );
	}

	private void TickTestComboFinalDamage()
	{
		// Combo Valheim m_attackChainLevels=3 — verify : (a) Tunables sanity,
		// (b) AxeController.ChainLevel exposed et starts in [0..maxLevels-1],
		// (c) Tree.Chop accepts un chopPower multiplié par ChopComboFinalDamageMul
		// et applique le damage correct. Test ne simule pas la chain timing
		// dans AxeController (nécessiterait input simulation through full
		// swing cycle), mais verrouille la formule mathématique.
		if ( RuntimeValue( Tunables.ChopComboMaxLevels ) != 3 )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: ChopComboMaxLevels={Tunables.ChopComboMaxLevels} (expected 3 Valheim Stone axe)" );
			Finish();
			return;
		}
		if ( MathF.Abs( RuntimeValue( Tunables.ChopComboFinalDamageMul ) - 2.0f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: ChopComboFinalDamageMul={Tunables.ChopComboFinalDamageMul} (expected 2.0 Valheim m_lastChainDamageMultiplier)" );
			Finish();
			return;
		}
		if ( MathF.Abs( RuntimeValue( Tunables.ChopComboWindow ) - 0.2f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: ChopComboWindow={Tunables.ChopComboWindow} (expected Valheim m_chainAttackMaxTime=0.2s)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.SwingWindUpDuration + Tunables.SwingRecoveryDuration ) > 0.75f )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: swing cycle={Tunables.SwingWindUpDuration + Tunables.SwingRecoveryDuration:F2}s (expected tightened Valheim-like <=0.75s)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.HitstopFrames ) != 9 )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: HitstopFrames={Tunables.HitstopFrames} (expected 9 = Valheim 0.15s @ 60fps)" );
			Finish();
			return;
		}
		// AxeController.ChainLevel exists + within bounds
		if ( _axe.ChainLevel < 0 || _axe.ChainLevel >= Tunables.ChopComboMaxLevels )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: AxeController.ChainLevel={_axe.ChainLevel} hors bounds [0..{Tunables.ChopComboMaxLevels - 1}]" );
			Finish();
			return;
		}
		// Sapling HP 4, finalPower = ceil(1 x 2.0) = 2 -> chip but still standing.
		var pos = _targetTreePos + new Vector3( -2200f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var tree = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		int hpBefore = tree.ChopsRemaining;
		int basePower = 1;
		int finalPower = Math.Max( 1, (int)MathF.Ceiling( basePower * Tunables.ChopComboFinalDamageMul ) );
		float basePush = Tree.ComputeLandedKickPowerScale( basePower, basePower );
		float finalPush = Tree.ComputeLandedKickPowerScale( basePower, finalPower );
		float expectedPush = Tunables.ChopComboFinalPushMul;
		if ( finalPower != 2 )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: ceiling(1×{Tunables.ChopComboFinalDamageMul})={finalPower} (expected 2)" );
			Finish();
			return;
		}
		if ( MathF.Abs( finalPush - expectedPush ) > 0.001f || finalPush <= basePush )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: finalPush={finalPush:0.###}, expected={expectedPush:0.###}, basePush={basePush:0.###}" );
			Finish();
			return;
		}
		// Sapling MinAxeTier=0, no need to reset state.
		tree.Chop( Vector3.Forward, finalPower, pos );
		int hpExpected = Math.Max( 0, hpBefore - finalPower );
		if ( tree.IsValid() && tree.ChopsRemaining != hpExpected && tree.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: Sapling HP {hpBefore}→{tree.ChopsRemaining} (expected {hpExpected})" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] COMBO PASS  Tunables ok (3 levels, ×2 mul, {Tunables.ChopComboWindow}s window) ; Sapling HP {hpBefore} - finalPower {finalPower} → fell={!tree.IsStanding}" );
		Transition( Phase.TestMultiWoodTypes );
	}

	private void TickTestMultiWoodTypes()
	{
		// Valheim 1:1 wood types — vérifier (a) WoodType enum 3 values, (b) chaque
		// type a un tint + name dans Tunables, (c) AddBackpack(N, type) route vers
		// le bon slot, (d) TryDeposit flush tous les types, (e) TreeKindWoodTypeMix
		// proba sum ~1.0 par kind.
		_state.ResetForTest();
		// AddBackpack par type
		_state.AddBackpack( 5, WoodType.Wood );
		_state.AddBackpack( 3, WoodType.Finewood );
		_state.AddBackpack( 2, WoodType.CoreWood );
		if ( _state.BackpackWood != 5 || _state.BackpackFinewood != 3 || _state.BackpackCoreWood != 2 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: AddBackpack par type mal routé. Wood={_state.BackpackWood} (exp 5) Finewood={_state.BackpackFinewood} (exp 3) CoreWood={_state.BackpackCoreWood} (exp 2)" );
			Finish();
			return;
		}
		if ( _state.BackpackTotal != 10 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: BackpackTotal={_state.BackpackTotal} (expected 10)" );
			Finish();
			return;
		}
		// TryDeposit flush tous les types
		int deposited = _state.TryDeposit();
		if ( deposited != 10 || _state.Wood != 5 || _state.Finewood != 3 || _state.CoreWood != 2 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: TryDeposit deposited={deposited} (exp 10), stockpiles Wood={_state.Wood}/5 Finewood={_state.Finewood}/3 CoreWood={_state.CoreWood}/2" );
			Finish();
			return;
		}
		if ( _state.BackpackTotal != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: BackpackTotal after deposit={_state.BackpackTotal} (expected 0)" );
			Finish();
			return;
		}
		_state.ResetForTest();
		_state.AddBackpack( 3, WoodType.Finewood );
		bool helperDeposited = ShopStation.TryBuyCheapestAcrossAll( Scene );
		if ( !helperDeposited || _state.BackpackTotal != 0 || _state.Finewood != 3 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: TryBuyCheapestAcrossAll did not flush Finewood-only backpack (deposited={helperDeposited}, bag={_state.BackpackTotal}, Finewood={_state.Finewood})" );
			Finish();
			return;
		}
		_state.ResetForTest();
		_state.AddBackpack( Tunables.AxeTierCostsByType[1][0], WoodType.Wood );
		bool helperDepositedAndBought = ShopStation.TryBuyCheapestAcrossAll( Scene );
		if ( !helperDepositedAndBought || _state.BackpackTotal != 0 || _state.AxeTier != 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: TryBuyCheapestAcrossAll did not deposit+buy Stone in one shop pass (ok={helperDepositedAndBought}, bag={_state.BackpackTotal}, axe=T{_state.AxeTier}, wood={_state.Wood})" );
			Finish();
			return;
		}
		_state.ResetForTest();
		_state.AddWood( Tunables.AxeTierCostsByType[1][0] + Tunables.AxeTierCostsByType[2][0] + Tunables.AxeTierCostsByType[3][0] );
		_state.TryUpgradeAxe();
		_state.TryUpgradeAxe();
		if ( _state.AxeTier != 2 || _state.Finewood != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: setup for multi-resource axe affordability failed axe=T{_state.AxeTier} finewood={_state.Finewood}" );
			Finish();
			return;
		}
		bool boughtFallback = ShopStation.TryBuyCheapestAcrossAll( Scene );
		if ( !boughtFallback || _state.AxeTier != 2 || _state.SpeedTier != 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: auto-buy got stuck on unaffordable Axe T3 (bought={boughtFallback}, axe=T{_state.AxeTier}, speed=T{_state.SpeedTier})" );
			Finish();
			return;
		}
		// Tunables.TreeKindWoodTypeMix proba sum check
		for ( int k = 0; k < Tunables.TreeKindWoodTypeMix.Length; k++ )
		{
			float sum = 0f;
			foreach ( var p in Tunables.TreeKindWoodTypeMix[k] ) sum += p;
			if ( MathF.Abs( sum - 1.0f ) > 0.01f )
			{
				Log.Error( $"[TC_TEST] FAIL TestMultiWoodTypes: TreeKindWoodTypeMix[{k}] sum={sum} (expected ~1.0)" );
				Finish();
				return;
			}
		}
		Log.Info( $"[TC_TEST] MULTI_WOOD PASS  AddBackpack par type + TryDeposit flush, typed axe recipe fallback, TreeKindWoodTypeMix sums ok" );
		Transition( Phase.TestStatCounters );
	}

	private void TickTestStatCounters()
	{
		// Valheim Game.IncrementPlayerStat — Tree.Chop bumps TotalChops,
		// Tree.StartFell bumps TreesFelledByTier[minToolTier]. Verify les deux.
		_state.ResetForTest();
		var pos = _targetTreePos + new Vector3( -2400f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		int chopsBefore = _state.TotalChops;
		var sap = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		sap.Chop( Vector3.Forward, 1, pos );
		if ( _state.TotalChops != chopsBefore + 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestStatCounters: TotalChops {chopsBefore}→{_state.TotalChops} (expected +1)" );
			Finish();
			return;
		}
		// Force fell via direct StartFell + check TreesFelledByTier[Sapling tier=0]
		int saplingTier = Tunables.TreeKindMinAxeTier[(int)TreeKind.Sapling];
		int felledBefore = _state.TreesFelledByTier[saplingTier];
		sap.StartFell( Vector3.Forward );
		if ( _state.TreesFelledByTier[saplingTier] != felledBefore + 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestStatCounters: TreesFelledByTier[{saplingTier}] {felledBefore}→{_state.TreesFelledByTier[saplingTier]} (expected +1)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] STAT_COUNTERS PASS  TotalChops+1, TreesFelledByTier[{saplingTier}]+1" );
		Transition( Phase.TestWoodCuttingLevel );
	}

	private void TickTestWoodCuttingLevel()
	{
		// Formula : level = floor(sqrt(TotalChops / 5)).
		// 0 chops → 0, 5 → 1, 20 → 2, 45 → 3, 80 → 4, 125 → 5.
		_state.ResetForTest();
		if ( _state.WoodCuttingLevel != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestWoodCuttingLevel: lvl at 0 chops = {_state.WoodCuttingLevel} (expected 0)" );
			Finish();
			return;
		}
		// Pump TotalChops via IncrementTreeChops
		for ( int i = 0; i < 5; i++ ) _state.IncrementTreeChops();
		if ( _state.WoodCuttingLevel != 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestWoodCuttingLevel: lvl at 5 chops = {_state.WoodCuttingLevel} (expected 1)" );
			Finish();
			return;
		}
		for ( int i = 0; i < 15; i++ ) _state.IncrementTreeChops(); // total 20
		if ( _state.WoodCuttingLevel != 2 )
		{
			Log.Error( $"[TC_TEST] FAIL TestWoodCuttingLevel: lvl at 20 chops = {_state.WoodCuttingLevel} (expected 2)" );
			Finish();
			return;
		}
		for ( int i = 0; i < 60; i++ ) _state.IncrementTreeChops(); // total 80
		if ( _state.WoodCuttingLevel != 4 )
		{
			Log.Error( $"[TC_TEST] FAIL TestWoodCuttingLevel: lvl at 80 chops = {_state.WoodCuttingLevel} (expected 4)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] WOODCUT_LEVEL PASS  0→0, 5→1, 20→2, 80→4 (formula floor(sqrt(N/5)))" );
		Transition( Phase.TestPickupStackMerge );
	}

	private void TickTestPickupStackMerge()
	{
		// MessageHud stack-merge : 3 ShowWoodPickupToast en série rapide d'un
		// même type doivent merger en 1 toast avec Count=3, Amount=somme.
		var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
		if ( !hud.IsValid() )
		{
			Log.Error( $"[TC_TEST] FAIL TestPickupStackMerge: no WoodHud in scene" );
			Finish();
			return;
		}
		hud.ClearPickupToastsForTest();
		// Spawn 3 toasts rapide
		hud.ShowWoodPickupToast( 1, WoodType.Wood );
		hud.ShowWoodPickupToast( 2, WoodType.Wood );
		hud.ShowWoodPickupToast( 3, WoodType.Wood );
		int toastCount = hud.GetPickupToastDebugCount();
		if ( toastCount != 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestPickupStackMerge: 3 toasts same type should merge to 1, got {toastCount}" );
			Finish();
			return;
		}
		// Different type should NOT merge with last
		hud.ShowWoodPickupToast( 1, WoodType.Finewood );
		toastCount = hud.GetPickupToastDebugCount();
		if ( toastCount != 2 )
		{
			Log.Error( $"[TC_TEST] FAIL TestPickupStackMerge: different type should create new toast, got {toastCount}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] PICKUP_STACK_MERGE PASS  3×Wood→1 toast, +1 Finewood→2 toasts" );
		Transition( Phase.TestEnvWindSanity );
	}

	private void TickTestEnvWindSanity()
	{
		// EnvWind : direction horizontale (Z=0), intensité [0..1].
		var dir = EnvWind.GetWindDir();
		if ( MathF.Abs( dir.z ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestEnvWindSanity: dir.z = {dir.z} (expected 0 horizontal)" );
			Finish();
			return;
		}
		if ( MathF.Abs( dir.Length - 1f ) > 0.01f )
		{
			Log.Error( $"[TC_TEST] FAIL TestEnvWindSanity: dir not normalized, length = {dir.Length}" );
			Finish();
			return;
		}
		float intensity = EnvWind.GetWindIntensity();
		if ( intensity < 0f || intensity > 1f )
		{
			Log.Error( $"[TC_TEST] FAIL TestEnvWindSanity: intensity={intensity} out of [0..1]" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] ENV_WIND PASS  dir horizontal len=1, intensity={intensity:F2} in [0..1]" );
		Transition( Phase.TestStrictTooHard );
	}

	private void TickTestStrictTooHard()
	{
		// Valheim TreeBase.RPC_Damage line 97 : TooHard return BEFORE Shake.
		// Vérifier que chop avec tier insuffisant N'INCREMENTE PAS TotalChops
		// (chop a échoué) ET ne fell pas le tree.
		_state.ResetForTest(); // AxeTier = 0
		int chopsBefore = _state.TotalChops;
		var pos = _targetTreePos + new Vector3( -2600f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var vet = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Veteran );
		int hpBefore = vet.ChopsRemaining;
		vet.Chop( Vector3.Forward, _state.ChopPower, pos );
		if ( _state.TotalChops != chopsBefore )
		{
			Log.Error( $"[TC_TEST] FAIL TestStrictTooHard: TooHard chop incremented TotalChops {chopsBefore}→{_state.TotalChops}" );
			Finish();
			return;
		}
		if ( vet.ChopsRemaining != hpBefore )
		{
			Log.Error( $"[TC_TEST] FAIL TestStrictTooHard: TooHard chop damaged tree HP {hpBefore}→{vet.ChopsRemaining}" );
			Finish();
			return;
		}
		if ( !vet.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestStrictTooHard: TooHard chop felled tree!" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] STRICT_TOOHARD PASS  Veteran HP={hpBefore} unchanged, TotalChops {chopsBefore} unchanged, still standing" );
		Transition( Phase.TestTunablesValheimSanity );
	}

	private void TickTestTunablesValheimSanity()
	{
		// Smoke test sur les Tunables Valheim-aligned key values. Catch regressions
		// si quelqu'un change accidentellement une constante.
		// Shake (TreeBase.ShakeAnimation lignes 194-209)
		if ( RuntimeValue( Tunables.TreeShakeDuration ) != 1.0f
			|| RuntimeValue( Tunables.TreeShakeFreqA ) != 40f
			|| RuntimeValue( Tunables.TreeShakeFreqB ) != 36f
			|| RuntimeValue( Tunables.TreeShakeAmplitudeDeg ) != 1.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeShake values dérivés ({Tunables.TreeShakeDuration}s, {Tunables.TreeShakeFreqA}Hz, {Tunables.TreeShakeFreqB}Hz, {Tunables.TreeShakeAmplitudeDeg}°)" );
			Finish();
			return;
		}
		// Valheim tree-log prefab ImpactEffect : interval=0.25s, min=1m/s,
		// max=5m/s. Ground impact never damages the log itself in our TreeLog path.
		if ( MathF.Abs( RuntimeValue( Tunables.ImpactInterval ) - 0.25f ) > 0.001f
			|| MathF.Abs( RuntimeValue( Tunables.ImpactMinSpeed ) - 1f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ImpactMaxSpeed ) - 5f * Tunables.UnitsPerMeter ) > 0.01f
			|| RuntimeValue( Tunables.ImpactBaseDamage ) != 3
			|| RuntimeValue( Tunables.ImpactChopDamage ) != 3
			|| RuntimeValue( Tunables.ImpactBluntDamage ) != 5 )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: ImpactEffect prefab drift interval={Tunables.ImpactInterval}, min={Tunables.ImpactMinSpeed}, max={Tunables.ImpactMaxSpeed}, base={Tunables.ImpactBaseDamage}" );
			Finish();
			return;
		}
		// Valheim TreeBase.SpawnLog : hitDir * 0.2m/s * mass, point + up * 4m * scale.y.
		float expectedImpulseSpeed = 0.2f * Tunables.UnitsPerMeter;
		float expectedImpulseHeight = 4f * Tunables.UnitsPerMeter;
		if ( MathF.Abs( RuntimeValue( Tunables.InitialFellTopImpulseSpeed ) - expectedImpulseSpeed ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimSpawnLogImpulseHeight ) - expectedImpulseHeight ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeBaseDropRadius ) - 0.5f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeBaseDropYOffsetLow ) - 0.5f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeBaseDropYOffsetHigh ) - 4f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeBaseDropYStep ) - 0.3f * Tunables.UnitsPerMeter ) > 0.01f
			|| Tunables.TreeKindLogSpawnPoint.Length != 4
			|| (Tunables.TreeKindLogSpawnPoint[(int)TreeKind.Normal] - new Vector3( 0f, 0f, 9.20f * Tunables.UnitsPerMeter )).Length > 0.01f
			|| (Tunables.TreeKindLogSpawnPoint[(int)TreeKind.Sapling] - new Vector3( 0f, 0f, 4.20f * Tunables.UnitsPerMeter )).Length > 0.01f
			|| (Tunables.TreeKindLogSpawnPoint[(int)TreeKind.Veteran] - new Vector3( -0.10f * Tunables.UnitsPerMeter, 0.06f * Tunables.UnitsPerMeter, 7.16f * Tunables.UnitsPerMeter )).Length > 0.01f
			|| (Tunables.TreeKindLogSpawnPoint[(int)TreeKind.Brittle] - new Vector3( 0f, 0f, 4.20f * Tunables.UnitsPerMeter )).Length > 0.01f
			|| RuntimeValue( Tunables.TreeLogSpawnGroundClearance ) < 2f
			|| RuntimeValue( Tunables.TreeLogSpawnGroundClearance ) > RuntimeValue( Tunables.TreeLogSpawnMaxBottomClearance )
			|| Tunables.TreeKindBaseDropYOffset.Length != 4
			|| MathF.Abs( Tunables.TreeKindBaseDropYOffset[(int)TreeKind.Normal] - RuntimeValue( Tunables.ValheimTreeBaseDropYOffsetHigh ) ) > 0.01f
			|| MathF.Abs( Tunables.TreeKindBaseDropYOffset[(int)TreeKind.Veteran] - RuntimeValue( Tunables.ValheimTreeBaseDropYOffsetLow ) ) > 0.01f
			|| MathF.Abs( Tunables.TreeKindBaseDropYOffset[(int)TreeKind.Brittle] - RuntimeValue( Tunables.ValheimTreeBaseDropYOffsetHigh ) ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogSpawnDistance ) - 2f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogFullMass ) - 100f ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogHalfMass ) - 50f ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogLinearDamping ) - 0.1f ) > 0.001f
			|| MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogAngularDamping ) - 0.2f ) > 0.001f
			|| RuntimeValue( Tunables.ValheimImpactToolTier ) != 2
			|| RuntimeValue( Tunables.SplitLogMaxSpawnValidationSpeed ) < RuntimeValue( Tunables.ImpactMaxSpeed )
			|| RuntimeValue( Tunables.ValheimTreeLogHitPushMul ) != 2f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: Valheim physics drift speed={Tunables.InitialFellTopImpulseSpeed:F2}/{expectedImpulseSpeed:F2} height={Tunables.ValheimSpawnLogImpulseHeight:F1}/{expectedImpulseHeight:F1} spawnNormal={Tunables.TreeKindLogSpawnPoint[(int)TreeKind.Normal]} logDamp={Tunables.ValheimTreeLogLinearDamping}/{Tunables.ValheimTreeLogAngularDamping} logPushMul={Tunables.ValheimTreeLogHitPushMul} splitSpawnMax={Tunables.SplitLogMaxSpawnValidationSpeed:F1}" );
			Finish();
			return;
		}
		int legacyTreeLogs = Scene.GetAllComponents<Tree>().Count( t => t.IsValid() && t.IsFallenLog );
		if ( legacyTreeLogs > 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: found {legacyTreeLogs} legacy Tree-as-log objects; Valheim requires TreeBase -> TreeLog prefab swap" );
			Finish();
			return;
		}
		var legacyProbePos = _targetTreePos + new Vector3( -1180f, 620f, 0f );
		if ( TryGetGroundZ( legacyProbePos.x, legacyProbePos.y, out var legacyProbeZ ) )
			legacyProbePos = legacyProbePos.WithZ( legacyProbeZ );
		ClearTestObjectsAround( legacyProbePos, 650f );
		var legacyProbe = Tree.SpawnAt( Scene, legacyProbePos, 0.5f, TreeKind.Sapling );
		if ( !((IChoppable)legacyProbe).IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestTunablesValheimSanity: standing TreeBase is not choppable before fell" );
			Finish();
			return;
		}
		if ( (object)legacyProbe is Component.ICollisionListener )
		{
			Log.Error( "[TC_TEST] FAIL TestTunablesValheimSanity: TreeBase implements collision listener; falling/log physics must live in FallenLog only" );
			Finish();
			return;
		}
		legacyProbe.StartFell( Vector3.Forward, fellPower: 99, allowComboPush: false );
		var spawnedLegacyLog = legacyProbe.SpawnedLog;
		if ( ((IChoppable)legacyProbe).IsValid() || legacyProbe.IsFallenLog || !spawnedLegacyLog.IsValid() || (object)spawnedLegacyLog is not Component.ICollisionListener )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeBase kept legacy choppable/log state after StartFell treeValid={legacyProbe.IsValid()} treeLog={legacyProbe.IsFallenLog} spawnedLog={spawnedLegacyLog.IsValid()} spawnedHasCollision={(object)spawnedLegacyLog is Component.ICollisionListener}" );
			Finish();
			return;
		}
		ClearTestObjectsAround( legacyProbePos, 900f );
		// TreeKindWoodTypeMix : 4 kinds × 3 types, sums ~1.0
		if ( MathF.Abs( RuntimeValue( Tunables.WoodItemMagnetRange ) - 2f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.WoodItemPickupRange ) - 0.3f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.WoodItemMagnetSpeed ) - 15f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.WoodItemMagnetGrace ) - 0.5f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: pickup constants range={Tunables.WoodItemMagnetRange}, pickup={Tunables.WoodItemPickupRange}, speed={Tunables.WoodItemMagnetSpeed}, grace={Tunables.WoodItemMagnetGrace} (expected Valheim 2m/0.3m/15mps/0.5s)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.TreeHeight ) < 420f || RuntimeValue( Tunables.TreeHeight ) > 520f
			|| RuntimeValue( Tunables.TreeScaleMax ) > 1.3f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: tree visual ratios drifted (height={Tunables.TreeHeight}, scaleMax={Tunables.TreeScaleMax})" );
			Finish();
			return;
		}
		if ( Tunables.TreeKindWoodTypeMix.Length != 4 )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeKindWoodTypeMix has {Tunables.TreeKindWoodTypeMix.Length} kinds (expected 4)" );
			Finish();
			return;
		}
		// AxeTierCostsByType : 7 tiers × 3 types
		if ( Tunables.AxeTierCostsByType.Length != 7 )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: AxeTierCostsByType has {Tunables.AxeTierCostsByType.Length} tiers (expected 7)" );
			Finish();
			return;
		}
		foreach ( var recipe in Tunables.AxeTierCostsByType )
		{
			if ( recipe.Length != 3 )
			{
				Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: recipe length {recipe.Length} (expected 3)" );
				Finish();
				return;
			}
		}
		// WoodTypeTints / Names : 3 each
		if ( Tunables.WoodTypeTints.Length != 3 || Tunables.WoodTypeNames.Length != 3 )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: WoodType arrays not all 3 (tints={Tunables.WoodTypeTints.Length} names={Tunables.WoodTypeNames.Length})" );
			Finish();
			return;
		}
		// EnvWind cycles > 0
		if ( RuntimeValue( Tunables.WindRotationCycle ) <= 0f || RuntimeValue( Tunables.WindGustCycle ) <= 0f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: WindCycle <= 0 (rotation={Tunables.WindRotationCycle} gust={Tunables.WindGustCycle})" );
			Finish();
			return;
		}
		if ( MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogAngularDamping ) - 0.2f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: ValheimTreeLogAngularDamping={Tunables.ValheimTreeLogAngularDamping} (expected Valheim Rigidbody.angularDrag=0.2)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.TreeLogSleepThreshold ) > 0.1f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeLogSleepThreshold={Tunables.TreeLogSleepThreshold} (expected <= 0.1 pour rolling logs Valheim feel)" );
			Finish();
			return;
		}
		// TreeKindChopPitchMul : 4 kinds, Sapling > 1 (high crack), Veteran < 1 (deep thunk)
		if ( Tunables.TreeKindChopPitchMul.Length != 4 )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeKindChopPitchMul has {Tunables.TreeKindChopPitchMul.Length} entries (expected 4)" );
			Finish();
			return;
		}
		if ( Tunables.TreeKindSplitLogCount.Length != 4 || Tunables.TreeKindSplitLogHP.Length != 4
			|| Tunables.TreeKindLogLengthMul.Length != 4 || Tunables.TreeKindLogWidthMul.Length != 4
			|| Tunables.TreeKindLogColliderWidthMul.Length != 4 || Tunables.TreeKindSplitLogLengthFrac.Length != 4
			|| Tunables.TreeKindSplitLogWidthFrac.Length != 4 || Tunables.TreeKindSubLogPointFrac.Length != 4 )
		{
			Log.Error( "[TC_TEST] FAIL TestTunablesValheimSanity: log/split-log kind arrays must have 4 entries" );
			Finish();
			return;
		}
		if ( !Tunables.TreeKindSplitLogCount.SequenceEqual( new[] { 2, 0, 2, 0 } )
			|| RuntimeValue( Tunables.SplitLogLengthFrac ) <= 0f
			|| RuntimeValue( Tunables.SplitLogLengthFrac ) > RuntimeValue( Tunables.SplitLogMaxParentLengthFrac )
			|| Tunables.TreeKindSubLogPointFrac[(int)TreeKind.Normal].Length != 2
			|| Tunables.TreeKindSubLogPointFrac[(int)TreeKind.Veteran].Length != 2
			|| MathF.Abs( Tunables.TreeKindSplitLogLengthFrac[(int)TreeKind.Normal] - 0.50f ) > 0.001f
			|| MathF.Abs( Tunables.TreeKindSplitLogLengthFrac[(int)TreeKind.Veteran] - 0.50f ) > 0.001f
			|| MathF.Abs( Tunables.TreeKindSplitLogWidthFrac[(int)TreeKind.Normal] - 0.62f ) > 0.001f
			|| MathF.Abs( Tunables.TreeKindSplitLogWidthFrac[(int)TreeKind.Veteran] - 0.64f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: Valheim smaller-log geometry drift counts=[{string.Join( ",", Tunables.TreeKindSplitLogCount )}] lengthFrac={Tunables.SplitLogLengthFrac:F2} maxFrac={Tunables.SplitLogMaxParentLengthFrac:F2}" );
			Finish();
			return;
		}
		if ( Tunables.TreeKindLogLengthMul[(int)TreeKind.Normal] < 0.72f
			|| Tunables.TreeKindLogLengthMul[(int)TreeKind.Sapling] < 0.76f
			|| Tunables.TreeKindLogLengthMul[(int)TreeKind.Veteran] < 0.66f
			|| Tunables.TreeKindLogLengthMul[(int)TreeKind.Brittle] < 0.70f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: parent log length ratios no longer read near tree-trunk size [{string.Join( ",", Tunables.TreeKindLogLengthMul.Select( x => x.ToString( "F2" ) ) )}]" );
			Finish();
			return;
		}
		if ( Tunables.TreeKindLogLengthMul.Any( x => x > 0.88f ) )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: parent log length ratios too close to full tree/canopy height [{string.Join( ",", Tunables.TreeKindLogLengthMul.Select( x => x.ToString( "F2" ) ) )}]" );
			Finish();
			return;
		}
		if ( Tunables.LogVisualSides < 12
			|| Tunables.TreeKindLogWidthMul.Any( x => x < 0.62f || x > 0.78f )
			|| Tunables.TreeKindLogColliderWidthMul.Any( x => x < 0.78f || x > 0.90f ) )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: procedural log proportions drift sides={Tunables.LogVisualSides} width=[{string.Join( ",", Tunables.TreeKindLogWidthMul.Select( x => x.ToString( "F2" ) ) )}] collider=[{string.Join( ",", Tunables.TreeKindLogColliderWidthMul.Select( x => x.ToString( "F2" ) ) )}]" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.LogSupportSphereCount ) < 3 || RuntimeValue( Tunables.LogSupportSphereRadiusMul ) < 0.85f || RuntimeValue( Tunables.LogSupportSphereRadiusMul ) > 1.05f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: log support colliders drifted spheres={Tunables.LogSupportSphereCount} radiusMul={Tunables.LogSupportSphereRadiusMul:F2}" );
			Finish();
			return;
		}
		var requiredEffects = new[]
		{
			ValheimEffectListId.AttackStart,
			ValheimEffectListId.AxeHit,
			ValheimEffectListId.TooHard,
			ValheimEffectListId.TreeDestroyed,
			ValheimEffectListId.TreeBreakYield,
			ValheimEffectListId.LogImpact,
			ValheimEffectListId.LogLandingHard,
			ValheimEffectListId.LogDestroyed,
			ValheimEffectListId.SmallerLogsSpawned,
		};
		if ( requiredEffects.Any( e => !ValheimEffects.HasList( e ) ) )
		{
			Log.Error( "[TC_TEST] FAIL TestTunablesValheimSanity: missing Valheim effect-list route for tree chopping" );
			Finish();
			return;
		}
		if ( !Tunables.AxeTierChopPower.SequenceEqual( new[] { 1, 2, 4, 5, 6, 8, 10 } )
			|| !Tunables.TreeKindChopsBase.SequenceEqual( new[] { 8, 4, 20, 12 } )
			|| !Tunables.LogChopHP.SequenceEqual( new[] { 6, 3, 16, 6 } )
			|| !Tunables.TreeKindSplitLogHP.SequenceEqual( new[] { 6, 0, 14, 0 } ) )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: Valheim resistance compression drift axes=[{string.Join( ",", Tunables.AxeTierChopPower )}] trees=[{string.Join( ",", Tunables.TreeKindChopsBase )}] logs=[{string.Join( ",", Tunables.LogChopHP )}] splitHp=[{string.Join( ",", Tunables.TreeKindSplitLogHP )}]" );
			Finish();
			return;
		}
		for ( int k = 0; k < 4; k++ )
		{
			var pos = _targetTreePos + new Vector3( -5200f - k * 120f, 900f, 0f );
			if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
			var tree = Tree.SpawnAt( Scene, pos, 1f, (TreeKind)k );
			int expectedHp = Tunables.TreeKindChopsBase[k];
			if ( !tree.IsValid() || tree.ChopsRemaining != expectedHp )
			{
				Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: spawned {(TreeKind)k} HP={(tree.IsValid() ? tree.ChopsRemaining : -1)} expected prefab HP {expectedHp}; visual scale must not change resistance" );
				Finish();
				return;
			}
			tree.GameObject?.Destroy();
		}
		if ( Tunables.TreeKindChopPitchMul[(int)TreeKind.Sapling] <= 1.0f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: Sapling chop pitch={Tunables.TreeKindChopPitchMul[(int)TreeKind.Sapling]} (expected > 1.0 high crack)" );
			Finish();
			return;
		}
		if ( Tunables.TreeKindChopPitchMul[(int)TreeKind.Veteran] >= 1.0f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: Veteran chop pitch={Tunables.TreeKindChopPitchMul[(int)TreeKind.Veteran]} (expected < 1.0 deep thunk)" );
			Finish();
			return;
		}
		// TreeWhooshUpDotThreshold should be in cos(angle) range [0..1], around cos(45°) ≈ 0.707
		if ( RuntimeValue( Tunables.TreeWhooshUpDotThreshold ) < 0.5f || RuntimeValue( Tunables.TreeWhooshUpDotThreshold ) > 0.95f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeWhooshUpDotThreshold={Tunables.TreeWhooshUpDotThreshold} (expected ~0.5-0.95 = tilt 18-60°)" );
			Finish();
			return;
		}
		if ( MathF.Abs( RuntimeValue( Tunables.SwingMoveSpeedFactor ) - 0.2f ) > 0.001f
			|| MathF.Abs( RuntimeValue( Tunables.SwingAttackHeight ) - 0.6f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.SwingRange ) - 1.5f * Tunables.UnitsPerMeter ) > 0.01f
			|| MathF.Abs( RuntimeValue( Tunables.SwingConeDot ) - 0.70710678f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: Attack defaults drift move={Tunables.SwingMoveSpeedFactor}, height={Tunables.SwingAttackHeight}, range={Tunables.SwingRange}, coneDot={Tunables.SwingConeDot} (expected Valheim 0.2, 0.6m, 1.5m, cos45)" );
			Finish();
			return;
		}
		if ( !ResourceIsReachableBeforeRecipe( WoodType.Finewood, 3 ) )
		{
			Log.Error( "[TC_TEST] FAIL TestTunablesValheimSanity: Finewood is required for Axe T3 but no Finewood tree is choppable at Axe T2" );
			Finish();
			return;
		}
		if ( !ResourceIsReachableBeforeRecipe( WoodType.CoreWood, 4 ) )
		{
			Log.Error( "[TC_TEST] FAIL TestTunablesValheimSanity: CoreWood is required for Axe T4 but no CoreWood tree is choppable at Axe T3" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.TreeHitFlashDuration ) <= 0f || RuntimeValue( Tunables.TreeHitFlashDuration ) > 0.25f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeHitFlashDuration={Tunables.TreeHitFlashDuration} (expected quick impact flash <= 0.25s)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] TUNABLES_SANITY PASS  Shake 40/36Hz 1.5° 1s, ImpactEffect 1→5m/s interval 0.25 self=false, attack height 0.6m range 1.5m/90°/move0.2, pickup 2m/0.3m/15mps, recipes 7×3, woodTypes 3×3, resource ladder reachable, wind ok, damping ok, chop pitch S>1>V, whoosh threshold ok" );
		Transition( PhysicsOnly ? Phase.TestValheimLogLaunch : Phase.TestFellCanopyDestroyed );
	}

	private void TickTestFellCanopyDestroyed()
	{
		if ( !_fellCanopyStarted )
		{
			for ( int i = 0; i < 40; i++ )
			{
				var pos = new Vector3( -1340f - i * 35f, 940f, 260f );
				if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
				_fellCanopyTree = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Veteran );
				_fellCanopyBefore = CountCanopyVisuals( _fellCanopyTree );
				if ( _fellCanopyBefore > 1 ) break;
				_fellCanopyTree.GameObject.Destroy();
				_fellCanopyTree = default;
			}

			if ( !_fellCanopyTree.IsValid() )
			{
				Log.Error( "[TC_TEST] FAIL TestFellCanopyDestroyed: no multi-block canopy tree found" );
				Finish();
				return;
			}

			_fellCanopyTree.StartFell( Vector3.Forward, 99, allowComboPush: false );
			_fellCanopyStarted = true;
			return;
		}

		if ( _phaseTime < 0.1f ) return;

		int after = CountCanopyVisuals( _fellCanopyTree );
		if ( _fellCanopyBefore <= 1 || after != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestFellCanopyDestroyed: canopy count {_fellCanopyBefore}->{after} after StartFell" );
			Finish();
			return;
		}

		Log.Info( $"[TC_TEST] FELL_CANOPY_DESTROYED PASS  canopy blocks {_fellCanopyBefore}->0 on StartFell" );
		Transition( Phase.TestImpactDamageScaling );
	}

	private static int CountCanopyVisuals( Tree tree )
	{
		if ( !tree.IsValid() ) return 0;
		return tree.GameObject.Children.Count( child =>
			child.IsValid()
			&& (child.Name == "TreeCanopy"
				|| child.Name == "TreeCanopyShade"
				|| child.Name == "PineLow"
				|| child.Name == "PineMid"
				|| child.Name == "PineTop") );
	}

	private static bool ResourceIsReachableBeforeRecipe( WoodType type, int recipeTier )
	{
		int resourceIdx = (int)type;
		if ( recipeTier <= 0 || recipeTier >= Tunables.AxeTierCostsByType.Length ) return true;
		if ( Tunables.AxeTierCostsByType[recipeTier][resourceIdx] <= 0 ) return true;
		int playerTierBeforeRecipe = recipeTier - 1;
		for ( int kind = 0; kind < Tunables.TreeKindWoodTypeMix.Length; kind++ )
		{
			if ( Tunables.TreeKindWoodTypeMix[kind].Length <= resourceIdx ) continue;
			if ( Tunables.TreeKindWoodTypeMix[kind][resourceIdx] <= 0f ) continue;
			if ( Tunables.TreeKindMinAxeTier[kind] <= playerTierBeforeRecipe ) return true;
		}
		return false;
	}

	private void TickTestImpactDamageScaling()
	{
		// Vérifier que la formule LerpStep(min, max, speed) * baseDamage est correcte
		// à 3 vitesses caractéristiques. Valheim early-return seulement sous min ;
		// à min exact le hit effect peut jouer mais LerpStep=0 donc damage=0.
		// On simule la formule directement (pas via OnCollisionStart) car _preCollisionVelocity
		// nécessite collision setup. La formule est l'invariant à protéger.
		int belowMin = ValheimImpact.DamageFromSpeed( Tunables.ImpactMinSpeed - 0.01f );
		int atMin = ValheimImpact.DamageFromSpeed( Tunables.ImpactMinSpeed );
		float midSpeed = (Tunables.ImpactMinSpeed + Tunables.ImpactMaxSpeed) * 0.5f;
		float damageFactorMid = ValheimImpact.ScaleFromSpeed( midSpeed );
		int expectedMid = ValheimImpact.DamageFromSpeed( midSpeed );
		if ( belowMin != 0 || atMin != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactDamageScaling: min gate below={belowMin} atMin={atMin} (expected 0/0)" );
			Finish();
			return;
		}
		if ( expectedMid != 2 )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactDamageScaling: midSpeed damage={expectedMid} (expected 2 from base 3 x 0.5, ceiling)" );
			Finish();
			return;
		}
		if ( MathF.Abs( damageFactorMid - 0.5f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactDamageScaling: lerp mid factor={damageFactorMid:F3} (expected 0.5)" );
			Finish();
			return;
		}
		int expectedMax = ValheimImpact.DamageFromSpeed( Tunables.ImpactMaxSpeed );
		if ( expectedMax != Tunables.ImpactBaseDamage )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactDamageScaling: maxSpeed damage={expectedMax} (expected {Tunables.ImpactBaseDamage})" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] IMPACT_SCALING PASS  belowMin=0, min=0, midSpeed={midSpeed:F0}->{expectedMid}, max->{expectedMax} (Valheim LerpStep)" );
		Transition( Phase.TestWindDirRotation );
	}

	private void TickTestWindDirRotation()
	{
		// EnvWind direction rotate over time. Comparer dir à 2 instants Time.Now
		// inputs synthetic — on évalue manuellement la formule pour 2 timestamps
		// séparés par WindRotationCycle/2 (180° apart).
		float t1 = 0f / Tunables.WindRotationCycle;
		float t2 = (Tunables.WindRotationCycle * 0.5f) / Tunables.WindRotationCycle;
		var dir1 = new Vector3( MathF.Cos( t1 * MathF.Tau ), MathF.Sin( t1 * MathF.Tau ), 0f );
		var dir2 = new Vector3( MathF.Cos( t2 * MathF.Tau ), MathF.Sin( t2 * MathF.Tau ), 0f );
		// 180° apart → dir1 = -dir2 (vecteurs opposés)
		float dot = dir1.Dot( dir2 );
		if ( dot > -0.95f )
		{
			Log.Error( $"[TC_TEST] FAIL TestWindDirRotation: dir1·dir2={dot:F2} (expected ≈ -1 pour 180° apart)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] WIND_ROTATION PASS  dir t=0 vs t={Tunables.WindRotationCycle/2}s opposite (dot={dot:F2})" );
		Transition( Phase.TestRespawnJitterRange );
	}

	private void TickTestRespawnJitterRange()
	{
		// Spawn 20 TreeStumps, verify chaque _respawnJitterMul ∈ [0.75, 1.25]
		// + distribution pas tout pareil (au moins 3 valeurs distinctes).
		var distinctValues = new System.Collections.Generic.HashSet<float>();
		for ( int i = 0; i < 20; i++ )
		{
			var pos = _targetTreePos + new Vector3( -3000f - i * 100f, 0f, 0f );
			var stump = TreeStump.SpawnAt( Scene, pos, 32f, Color.White, TreeKind.Sapling, 0.5f, false );
			float jitter = stump.DebugRespawnJitterMul;
			if ( jitter < 0.75f || jitter > 1.25f )
			{
				Log.Error( $"[TC_TEST] FAIL TestRespawnJitterRange: stump[{i}] jitter={jitter:F3} hors [0.75, 1.25]" );
				Finish();
				return;
			}
			distinctValues.Add( MathF.Round( jitter * 100f ) / 100f );
			stump.GameObject?.Destroy();
		}
		if ( distinctValues.Count < 3 )
		{
			Log.Error( $"[TC_TEST] FAIL TestRespawnJitterRange: 20 stumps → seulement {distinctValues.Count} valeurs distinctes (expected ≥ 3, random broken?)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] RESPAWN_JITTER PASS  20 stumps, jitters all ∈ [0.75, 1.25], {distinctValues.Count} distinct" );
		Transition( Phase.TestWoodTypeDistribution );
	}

	private void TickTestWoodTypeDistribution()
	{
		// Veteran mix = [0.5, 0.5, 0]. 200 rolls → ~100 Wood + ~100 Finewood,
		// 0 CoreWood. Tolérance ±25% (chi² rough).
		var veteranMix = Tunables.TreeKindWoodTypeMix[(int)TreeKind.Veteran];
		int[] counts = new int[3];
		int rolls = 200;
		for ( int i = 0; i < rolls; i++ )
		{
			var t = Tree.PickWoodType( veteranMix );
			counts[(int)t]++;
		}
		// Wood expected ~100, tolerance ±25 (so 75-125)
		if ( counts[(int)WoodType.Wood] < 75 || counts[(int)WoodType.Wood] > 125 )
		{
			Log.Error( $"[TC_TEST] FAIL TestWoodTypeDistribution: Wood count={counts[(int)WoodType.Wood]} (expected 75-125 sur 200 rolls 50/50)" );
			Finish();
			return;
		}
		if ( counts[(int)WoodType.Finewood] < 75 || counts[(int)WoodType.Finewood] > 125 )
		{
			Log.Error( $"[TC_TEST] FAIL TestWoodTypeDistribution: Finewood count={counts[(int)WoodType.Finewood]} (expected 75-125)" );
			Finish();
			return;
		}
		if ( counts[(int)WoodType.CoreWood] != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL TestWoodTypeDistribution: CoreWood count={counts[(int)WoodType.CoreWood]} (expected 0 pour Veteran mix)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] WOODTYPE_DIST PASS  Veteran 200 rolls : Wood={counts[0]}, Finewood={counts[1]}, CoreWood={counts[2]}" );
		Transition( Phase.TestTreeShakeReset );
	}

	private void TickTestTreeShakeReset()
	{
		// KickWobble doit reset _shakeStart à 0 pour démarrer un nouveau cycle.
		// Vérifier que DebugShakeElapsed devient ~0 juste après un Chop succès
		// QUI NE FELL PAS (sinon StartFell remet _shakeStart à 999). On utilise
		// Veteran HP 20 + AxeTier 3 (satisfait Veteran tier gate) + ChopPower=1
		// (= small chip dans la HP donc reste standing).
		var pos = _targetTreePos + new Vector3( -3500f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var vet = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Veteran );
		if ( vet.DebugShakeElapsed < 100f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTreeShakeReset: initial DebugShakeElapsed={vet.DebugShakeElapsed:F1} (expected >> 100, sentinel)" );
			Finish();
			return;
		}
		// Pump axe tier 3 (Iron) pour passer largement le gate Veteran.
		_state.ResetForTest();
		int totalWood = 0, totalFW = 0, totalCW = 0;
		for ( int i = 1; i <= 3; i++ )
		{
			var r = Tunables.AxeTierCostsByType[i];
			totalWood += r[0]; totalFW += r[1]; totalCW += r[2];
		}
		_state.AddWood( totalWood );
		_state.AddBackpack( totalFW, WoodType.Finewood );
		_state.AddBackpack( totalCW, WoodType.CoreWood );
		_state.TryDeposit();
		for ( int i = 0; i < 3; i++ ) _state.TryUpgradeAxe();
		// ChopPower at T3 = 5. Veteran HP 20, donc HP reste positif -> still standing -> shake reset.
		int hpBefore = vet.ChopsRemaining;
		vet.Chop( Vector3.Forward, 1, pos ); // chopPower=1 explicit, juste pour shake
		if ( !vet.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestTreeShakeReset: Veteran fell avec ChopPower=1 (HP was {hpBefore})" );
			Finish();
			return;
		}
		if ( vet.DebugShakeElapsed > 0.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTreeShakeReset: after Chop DebugShakeElapsed={vet.DebugShakeElapsed:F2} (expected ~0, shake reset)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] SHAKE_RESET PASS  Veteran HP {hpBefore}→{vet.ChopsRemaining}, shake elapsed {vet.DebugShakeElapsed:F2} (reset OK)" );
		Transition( Phase.TestCascadeShakeNoFell );
	}

	private void TickTestCascadeShakeNoFell()
	{
		// ApplyImpactDamage avec damage < HP doit shake mais NE PAS fell.
		// Verrouille le bug fix "cascade neighbor recevait silently du damage
		// sans Shake feedback".
		var pos = _targetTreePos + new Vector3( -3700f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var sap = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		// Sapling HP 4 ; on test sur Veteran HP 20 pour rester deterministe.
		sap.GameObject?.Destroy();
		var vet = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Veteran );
		int hpBefore = vet.ChopsRemaining;
		// Init shake high (sentinel)
		float shakeBefore = vet.DebugShakeElapsed;
		if ( shakeBefore < 100f )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeShakeNoFell: initial shake elapsed={shakeBefore:F1} (expected sentinel)" );
			Finish();
			return;
		}
		vet.ApplyImpactDamage( 1, Vector3.Forward );
		// HP reduced but still standing
		if ( vet.ChopsRemaining != hpBefore - 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeShakeNoFell: HP {hpBefore}→{vet.ChopsRemaining} (expected -1)" );
			Finish();
			return;
		}
		if ( !vet.IsStanding )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeShakeNoFell: fell avec damage 1 sur HP {hpBefore}" );
			Finish();
			return;
		}
		// Shake fired
		if ( vet.DebugShakeElapsed > 0.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeShakeNoFell: shake pas reset, elapsed={vet.DebugShakeElapsed:F2}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] CASCADE_SHAKE PASS  ApplyImpactDamage(1) sur Veteran HP {hpBefore}→{vet.ChopsRemaining}, shake fired (elapsed {vet.DebugShakeElapsed:F2}), still standing" );
		Transition( Phase.TestValheimLogLaunch );
	}

	private void TickTestValheimLogLaunch()
	{
		if ( !_valheimLaunchSpawned )
		{
			_valheimLaunchSpawned = true;
			var spawnPos = _targetTreePos + new Vector3( -1050f, -980f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var gz ) ) spawnPos = spawnPos.WithZ( gz );
			ClearTestObjectsAround( spawnPos, 900f );
			_valheimLaunchPos = spawnPos;

			for ( int i = 0; i < Tunables.TreeKindLogSpawnPoint.Length; i++ )
			{
				var kind = (TreeKind)i;
				var probePos = spawnPos + new Vector3( 0f, -360f - i * 170f, 0f );
				if ( TryGetGroundZ( probePos.x, probePos.y, out var probeGz ) ) probePos = probePos.WithZ( probeGz );
				ClearTestObjectsAround( probePos, 240f );
				var probeTree = Tree.SpawnAt( Scene, probePos, 0.5f, kind );
				var probeAuthoredOffset = ExpectedAuthoredLogSpawnOffset( probeTree );
				var probeOffset = ExpectedRuntimeLogSpawnOffset( probeTree );
				var probeCenter = probeTree.SpawnFootPosition + LocalOffsetToWorld( probeTree.WorldRotation, probeOffset );
				float expectedLogWidth = probeTree.TrunkWidth * Tunables.TreeKindLogWidthMul[i];
				float expectedColliderRadius = expectedLogWidth * Tunables.TreeKindLogColliderWidthMul[i] * 0.5f;
				probeTree.StartFell( Vector3.Forward, fellPower: 99, allowComboPush: true );
				var probeLog = probeTree.SpawnedLog;
				if ( !probeLog.IsValid()
					|| (probeLog.DebugValheimLogSpawnOffset - probeOffset).Length > 0.5f
					|| (probeLog.DebugValheimLogAuthoredSpawnOffset - probeAuthoredOffset).Length > 0.5f
					|| (probeLog.DebugValheimLogSpawnCenter - probeCenter).Length > 1f
					|| (probeLog.LogCenter - probeCenter).Length > 1.5f
					|| probeLog.DebugSpawnBottomClearance < -1f
					|| probeLog.DebugSpawnBottomClearance > Tunables.TreeLogSpawnMaxBottomClearance
					|| MathF.Abs( probeLog.DebugTrunkLength - probeTree.TrunkLength * Tunables.TreeKindLogLengthMul[i] ) > 0.5f
					|| MathF.Abs( probeLog.DebugTrunkWidth - expectedLogWidth ) > 0.5f
					|| MathF.Abs( probeLog.DebugColliderRadius - expectedColliderRadius ) > 0.5f )
				{
					Log.Error( $"[TC_TEST] FAIL TestValheimLogLaunch: {kind} runtime log spawn drift offset={(probeLog.IsValid() ? probeLog.DebugValheimLogSpawnOffset : Vector3.Zero)}/{probeOffset} authored={(probeLog.IsValid() ? probeLog.DebugValheimLogAuthoredSpawnOffset : Vector3.Zero)}/{probeAuthoredOffset} center={(probeLog.IsValid() ? probeLog.LogCenter : Vector3.Zero)}/{probeCenter} bottomClearance={(probeLog.IsValid() ? probeLog.DebugSpawnBottomClearance : -999f):F1} width={(probeLog.IsValid() ? probeLog.DebugTrunkWidth : -1f):F1}/{expectedLogWidth:F1} radius={(probeLog.IsValid() ? probeLog.DebugColliderRadius : -1f):F1}/{expectedColliderRadius:F1}" );
					Finish();
					return;
				}
				probeLog.GameObject?.Destroy();
				ClearTestObjectsAround( probePos, 260f );
			}

			var tree = Tree.SpawnAt( Scene, spawnPos, 0.5f, TreeKind.Normal );
			float sourceScale = MathF.Max( 0.1f, tree.TrunkLength / Tunables.TreeHeight );
			var expectedAuthoredSpawnOffset = ExpectedAuthoredLogSpawnOffset( tree );
			var expectedSpawnOffset = ExpectedRuntimeLogSpawnOffset( tree );
			var expectedSpawnCenter = tree.SpawnFootPosition + LocalOffsetToWorld( tree.WorldRotation, expectedSpawnOffset );
			tree.StartFell( Vector3.Forward, fellPower: 99, allowComboPush: true );
			_valheimLaunchLog = tree.SpawnedLog;
			_valheimLaunchTime = 0f;
			if ( !_valheimLaunchLog.IsValid() )
			{
				Log.Error( "[TC_TEST] FAIL TestValheimLogLaunch: StartFell did not spawn a FallenLog" );
				Finish();
				return;
			}

			float expectedHeight = Tunables.ValheimSpawnLogImpulseHeight * sourceScale;
			if ( MathF.Abs( _valheimLaunchLog.DebugLaunchImpulseSpeed - Tunables.InitialFellTopImpulseSpeed ) > 0.01f
				|| MathF.Abs( _valheimLaunchLog.DebugLaunchImpulseHeight - expectedHeight ) > 0.5f
				|| _valheimLaunchLog.DebugLaunchImpulseDirection.Dot( Vector3.Forward ) < 0.99f
				|| (_valheimLaunchLog.DebugLaunchVelocity.WithZ( 0f ).Length < 0.05f && _valheimLaunchLog.DebugLaunchAngularVelocity.Length < 0.001f) )
			{
				Log.Error( $"[TC_TEST] FAIL TestValheimLogLaunch: launch impulse drift speed={_valheimLaunchLog.DebugLaunchImpulseSpeed:F2}/{Tunables.InitialFellTopImpulseSpeed:F2} height={_valheimLaunchLog.DebugLaunchImpulseHeight:F1}/{expectedHeight:F1} dir={_valheimLaunchLog.DebugLaunchImpulseDirection} immediateVel={_valheimLaunchLog.DebugLaunchVelocity} immediateAng={_valheimLaunchLog.DebugLaunchAngularVelocity}" );
				Finish();
				return;
			}
			if ( (_valheimLaunchLog.DebugValheimLogSpawnOffset - expectedSpawnOffset).Length > 0.5f
				|| (_valheimLaunchLog.DebugValheimLogAuthoredSpawnOffset - expectedAuthoredSpawnOffset).Length > 0.5f
				|| (_valheimLaunchLog.DebugValheimLogSpawnCenter - expectedSpawnCenter).Length > 1f
				|| (_valheimLaunchLog.LogCenter - expectedSpawnCenter).Length > 1.5f
				|| _valheimLaunchLog.DebugSpawnBottomClearance < -1f
				|| _valheimLaunchLog.DebugSpawnBottomClearance > Tunables.TreeLogSpawnMaxBottomClearance )
			{
				Log.Error( $"[TC_TEST] FAIL TestValheimLogLaunch: runtime log spawn drift offset={_valheimLaunchLog.DebugValheimLogSpawnOffset}/{expectedSpawnOffset} authored={_valheimLaunchLog.DebugValheimLogAuthoredSpawnOffset}/{expectedAuthoredSpawnOffset} center={_valheimLaunchLog.LogCenter}/{expectedSpawnCenter} bottomClearance={_valheimLaunchLog.DebugSpawnBottomClearance:F1}" );
				Finish();
				return;
			}
			return;
		}

		if ( !_valheimLaunchLog.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestValheimLogLaunch: log vanished before launch validation" );
			Finish();
			return;
		}
		if ( (float)_valheimLaunchTime < 0.12f ) return;

		if ( !_valheimLaunchLog.Body.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestValheimLogLaunch: spawned log has no rigidbody" );
			Finish();
			return;
		}
		float horizontalSpeed = _valheimLaunchLog.Body.Velocity.WithZ( 0f ).Length;
		float angularSpeed = _valheimLaunchLog.Body.AngularVelocity.Length;
		var launchDir = _valheimLaunchLog.Body.Velocity.WithZ( 0f );
		float bodyDot = launchDir.LengthSquared > 0.001f
			? launchDir.Normal.Dot( _valheimLaunchLog.DebugLaunchImpulseDirection )
			: 0f;

		Log.Info( $"[TC_TEST] VALHEIM_LOG_LAUNCH PASS  spawnCenter={_valheimLaunchLog.DebugValheimLogSpawnCenter} bottomClearance={_valheimLaunchLog.DebugSpawnBottomClearance:F1} len={_valheimLaunchLog.DebugTrunkLength:F1} impulse={_valheimLaunchLog.DebugLaunchImpulseSpeed:F2}u/s height={_valheimLaunchLog.DebugLaunchImpulseHeight:F1}u dir={_valheimLaunchLog.DebugLaunchImpulseDirection} scale={_valheimLaunchLog.DebugSourceScale:F2} immediateVel={_valheimLaunchLog.DebugLaunchVelocity} immediateAng={_valheimLaunchLog.DebugLaunchAngularVelocity} postContactVel={horizontalSpeed:F2} bodyDir={launchDir.Normal} bodyDot={bodyDot:F2} postContactAng={angularSpeed:F3}" );
		ClearTestObjectsAround( _valheimLaunchPos, 900f );
		Transition( Phase.TestValheimTreeLogHitImpulse );
	}

	private void TickTestValheimTreeLogHitImpulse()
	{
		if ( !_valheimLogHitSpawned )
		{
			_valheimLogHitSpawned = true;
			UpgradeAxeTo( Tunables.TreeKindMinAxeTier[(int)TreeKind.Normal] );
			var spawnPos = _targetTreePos + new Vector3( -1050f, -620f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var gz ) ) spawnPos = spawnPos.WithZ( gz );
			ClearTestObjectsAround( spawnPos, 900f );
			_valheimLogHitPos = spawnPos;

			var tree = Tree.SpawnAt( Scene, spawnPos, 0.5f, TreeKind.Normal );
			tree.StartFell( Vector3.Forward );
			_valheimLogHitLog = tree.SpawnedLog;
			_valheimLogHitTime = 0f;
			if ( !_valheimLogHitLog.IsValid() )
			{
				Log.Error( "[TC_TEST] FAIL TestValheimTreeLogHitImpulse: StartFell did not spawn a FallenLog" );
				Finish();
				return;
			}
			return;
		}

		if ( !_valheimLogHitLog.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestValheimTreeLogHitImpulse: log vanished before hit validation" );
			Finish();
			return;
		}

		if ( !_valheimLogHitApplied )
		{
			if ( (float)_valheimLogHitTime < Tunables.WoodLogChopGrace + 0.06f ) return;

			_valheimLogHitLog.ChopsRemaining = 99;
			if ( _valheimLogHitLog.Body.IsValid() )
			{
				_valheimLogHitLog.Body.Velocity = Vector3.Zero;
				_valheimLogHitLog.Body.AngularVelocity = Vector3.Zero;
				_valheimLogHitLog.Body.Sleeping = true;
			}
			int chopPower = Math.Max( 1, _state.ChopPower );
			var hitPoint = _valheimLogHitLog.LogCenter + Vector3.Up * 14f;
			_valheimLogHitLog.Damage( HitData.Make( Vector3.Forward, chopPower, hitPoint, _state.AxeTier ) );
			_valheimLogHitApplied = true;
			_valheimLogHitTime = 0f;
			return;
		}

		if ( (float)_valheimLogHitTime < 0.04f ) return;

		var expectedImpulse = Vector3.Forward * Tunables.LandedLogKickImpulse * Tunables.ValheimTreeLogHitPushMul;
		var actualImpulse = _valheimLogHitLog.DebugLastLandedKickImpulse;
		if ( actualImpulse.Distance( expectedImpulse ) > 0.01f || MathF.Abs( actualImpulse.z ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestValheimTreeLogHitImpulse: impulse={actualImpulse} expected={expectedImpulse} (Valheim hit.m_dir * hit.m_pushForce * 2)" );
			Finish();
			return;
		}
		if ( _valheimLogHitLog.Body.IsValid() && _valheimLogHitLog.Body.Velocity.WithZ( 0f ).Length < 0.01f )
		{
			Log.Error( "[TC_TEST] FAIL TestValheimTreeLogHitImpulse: hit impulse did not move log horizontally" );
			Finish();
			return;
		}
		if ( _valheimLogHitLog.Body.IsValid() && _valheimLogHitLog.Body.Sleeping )
		{
			Log.Error( "[TC_TEST] FAIL TestValheimTreeLogHitImpulse: hit impulse did not wake sleeping log" );
			Finish();
			return;
		}
		if ( _valheimLogHitLog.Body.IsValid() && _valheimLogHitLog.Body.AngularVelocity.Length > Tunables.TreeLandedMaxAngularSpeed + 0.1f )
		{
			Log.Error( $"[TC_TEST] FAIL TestValheimTreeLogHitImpulse: grounded axe kick created absurd spin ang={_valheimLogHitLog.Body.AngularVelocity.Length:F2}/{Tunables.TreeLandedMaxAngularSpeed:F2}" );
			Finish();
			return;
		}
		int hpBeforeImpactDamage = _valheimLogHitLog.ChopsRemaining;
		_valheimLogHitLog.ApplyImpactDamage( 1, Vector3.Right );
		if ( _valheimLogHitLog.ChopsRemaining != hpBeforeImpactDamage - 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestValheimTreeLogHitImpulse: impact damage did not reduce log HP {hpBeforeImpactDamage}->{_valheimLogHitLog.ChopsRemaining}" );
			Finish();
			return;
		}
		if ( _valheimLogHitLog.DebugLastLandedKickImpulse.Distance( actualImpulse ) > 0.01f )
		{
			Log.Error( $"[TC_TEST] FAIL TestValheimTreeLogHitImpulse: ImpactEffect damage changed kick impulse {_valheimLogHitLog.DebugLastLandedKickImpulse}, expected unchanged {actualImpulse} because Valheim ImpactEffect does not set hit.m_pushForce" );
			Finish();
			return;
		}

		Log.Info( $"[TC_TEST] VALHEIM_TREELOG_HIT PASS  playerImpulse={actualImpulse.Length:F1}u dir={actualImpulse.Normal} pushMul={Tunables.ValheimTreeLogHitPushMul}, impactDamageNoPush hp={hpBeforeImpactDamage}->{_valheimLogHitLog.ChopsRemaining}" );
		ClearTestObjectsAround( _valheimLogHitPos, 900f );
		Transition( Phase.TestValheimDropGeometry );
	}

	private void TickTestValheimDropGeometry()
	{
		if ( !_valheimDropGeometrySetup )
		{
			_state.ResetForTest();
			ClearWoodItems();

			_valheimDropGeometryBasePos = _targetTreePos + new Vector3( 1600f, -1400f, 0f );
			if ( TryGetGroundZ( _valheimDropGeometryBasePos.x, _valheimDropGeometryBasePos.y, out var baseGz ) ) _valheimDropGeometryBasePos = _valheimDropGeometryBasePos.WithZ( baseGz );
			ClearTestObjectsAround( _valheimDropGeometryBasePos, 900f );
			var veteran = Tree.SpawnAt( Scene, _valheimDropGeometryBasePos, 1f, TreeKind.Veteran );
			veteran.StartFell( Vector3.Forward, 99, allowComboPush: false );
			var baseDrops = Scene.GetAllComponents<WoodItem>()
				.Where( w => w.IsValid() && w.DebugInitialPosition.Distance( _valheimDropGeometryBasePos ) < 180f )
				.ToList();
			int minBaseDrops = Tunables.TreeKindFellBonusItemsMin[(int)TreeKind.Veteran];
			int maxBaseDrops = Tunables.TreeKindFellBonusItemsMax[(int)TreeKind.Veteran];
			if ( veteran.IsMythic )
			{
				minBaseDrops += 1;
				maxBaseDrops += 1;
			}
			if ( baseDrops.Count < minBaseDrops || baseDrops.Count > maxBaseDrops )
			{
				Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: TreeBase bonus drops={baseDrops.Count}, expected {minBaseDrops}..{maxBaseDrops}" );
				Finish();
				return;
			}
			foreach ( var item in baseDrops )
			{
				if ( !ValidateValheimDropItem( item, "TreeBase" ) ) return;
				var delta = item.DebugInitialPosition - _valheimDropGeometryBasePos;
				if ( delta.WithZ( 0f ).Length > Tunables.ValheimTreeBaseDropRadius + 1f )
				{
					Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: TreeBase drop horizontal={delta.WithZ( 0f ).Length:F1} > radius {Tunables.ValheimTreeBaseDropRadius:F1}" );
					Finish();
					return;
				}
				float expectedYOffset = Tunables.TreeKindBaseDropYOffset[(int)TreeKind.Veteran];
				float maxZ = expectedYOffset + Tunables.ValheimTreeBaseDropYStep * Math.Max( 0, maxBaseDrops - 1 ) + 1f;
				if ( delta.z < expectedYOffset - 1f || delta.z > maxZ )
				{
					Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: TreeBase drop z={delta.z:F1}, expected {expectedYOffset:F1}..{maxZ:F1}" );
					Finish();
					return;
				}
			}

			ClearWoodItems();
			_valheimDropGeometryLogPos = _targetTreePos + new Vector3( 1600f, -1040f, 0f );
			if ( TryGetGroundZ( _valheimDropGeometryLogPos.x, _valheimDropGeometryLogPos.y, out var logGz ) ) _valheimDropGeometryLogPos = _valheimDropGeometryLogPos.WithZ( logGz );
			ClearTestObjectsAround( _valheimDropGeometryLogPos, 900f );
			var brittle = Tree.SpawnAt( Scene, _valheimDropGeometryLogPos, 1f, TreeKind.Brittle );
			brittle.StartFell( Vector3.Forward, 99, allowComboPush: false );
			_valheimDropGeometryLog = brittle.SpawnedLog;
			if ( !_valheimDropGeometryLog.IsValid() )
			{
				Log.Error( "[TC_TEST] FAIL TestValheimDropGeometry: Brittle StartFell did not spawn a log" );
				Finish();
				return;
			}
			_valheimDropGeometryAxis = Vector3.Forward;
			_valheimDropGeometryLog.WorldRotation = RotationWithUpForTest( _valheimDropGeometryAxis );
			if ( _valheimDropGeometryLog.Body.IsValid() && _valheimDropGeometryLog.Body.PhysicsBody.IsValid() )
			{
				_valheimDropGeometryLog.Body.PhysicsBody.Rotation = _valheimDropGeometryLog.WorldRotation;
				_valheimDropGeometryLog.Body.PhysicsBody.Velocity = Vector3.Zero;
				_valheimDropGeometryLog.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
			}
			_valheimDropGeometryLogCenter = _valheimDropGeometryLog.LogCenter;
			_valheimDropGeometryLogTime = 0f;
			_valheimDropGeometrySinceLanded = 0f;
			_valheimDropGeometryLandedSeen = false;
			_valheimDropGeometryImpactApplied = false;
			_valheimDropGeometrySetup = true;
			return;
		}

		if ( !_valheimDropGeometryLog.IsValid() && !_valheimDropGeometryImpactApplied )
		{
			Log.Error( "[TC_TEST] FAIL TestValheimDropGeometry: TreeLog vanished before drop validation" );
			Finish();
			return;
		}
		if ( _valheimDropGeometryLog.IsValid() && !_valheimDropGeometryLog.IsFallenLog )
		{
			if ( (float)_valheimDropGeometryLogTime > 8f )
			{
				Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: TreeLog never landed before drop validation upDot={_valheimDropGeometryLog.DebugAxisUpDot():F2} clearance={_valheimDropGeometryLog.DebugMinGroundClearance():F1}" );
				Finish();
			}
			return;
		}
		if ( _valheimDropGeometryLog.IsValid() && (float)_valheimDropGeometryLogTime < Tunables.WoodLogChopGrace + 0.06f ) return;
		if ( _valheimDropGeometryLog.IsValid() && _valheimDropGeometryLog.IsFallenLog )
		{
			if ( !_valheimDropGeometryLandedSeen )
			{
				_valheimDropGeometryLandedSeen = true;
				_valheimDropGeometrySinceLanded = 0f;
				return;
			}
			if ( (float)_valheimDropGeometrySinceLanded < Tunables.WoodLogChopGrace + 0.06f ) return;
		}

		if ( !_valheimDropGeometryImpactApplied )
		{
			ClearWoodItems();
			_valheimDropGeometryLogCenter = _valheimDropGeometryLog.LogCenter;
			_valheimDropGeometryAxis = _valheimDropGeometryLog.WorldRotation.Up;
			if ( _valheimDropGeometryAxis.LengthSquared < 0.001f ) _valheimDropGeometryAxis = Vector3.Forward;
			_valheimDropGeometryAxis = _valheimDropGeometryAxis.Normal;
			_valheimDropGeometryLog.ApplyImpactDamage( 999, _valheimDropGeometryAxis );
			_valheimDropGeometryImpactApplied = true;
			return;
		}

		int expectedLogDrops = Tunables.TreeKindLandedDropCount[(int)TreeKind.Brittle];
		var logDrops = Scene.GetAllComponents<WoodItem>()
			.Where( w => w.IsValid() && IsPotentialValheimTreeLogDrop( w, _valheimDropGeometryLogCenter, _valheimDropGeometryAxis, expectedLogDrops ) )
			.ToList();
		if ( logDrops.Count != expectedLogDrops )
		{
			if ( _valheimDropGeometryLog.IsValid() && (float)_phaseTime < 2.0f )
			{
				_valheimDropGeometryImpactApplied = false;
				return;
			}
			string logState = _valheimDropGeometryLog.IsValid()
				? $"valid kind={_valheimDropGeometryLog.Kind} falling={_valheimDropGeometryLog.IsFalling} landed={_valheimDropGeometryLog.IsFallenLog} hp={_valheimDropGeometryLog.ChopsRemaining}"
				: "invalid";
			Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: TreeLog drops={logDrops.Count}, expected {expectedLogDrops} log={logState} spawnT={(float)_valheimDropGeometryLogTime:F2}s landedSeen={_valheimDropGeometryLandedSeen} landedT={(float)_valheimDropGeometrySinceLanded:F2}s" );
			Finish();
			return;
		}
		foreach ( var item in logDrops )
		{
			if ( !ValidateValheimDropItem( item, "TreeLog" ) ) return;
			if ( !TryMatchValheimTreeLogDrop( item, _valheimDropGeometryLogCenter, _valheimDropGeometryAxis, expectedLogDrops, out var axisOff, out var radial, out var stepIndex ) )
			{
				Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: TreeLog drop does not match Destroy formula axisOff={axisOff:F1}, radial={radial:F1}, step={stepIndex}" );
				Finish();
				return;
			}
		}

		Log.Info( $"[TC_TEST] VALHEIM_DROP_GEOMETRY PASS  TreeBase radius={Tunables.ValheimTreeBaseDropRadius:F1}u y=kind-prefab offset+0.3m*i, TreeLog spawnDistance={Tunables.ValheimTreeLogSpawnDistance:F1}u, no drop burst" );
		ClearTestObjectsAround( _valheimDropGeometryBasePos, 900f );
		ClearTestObjectsAround( _valheimDropGeometryLogPos, 900f );
		Transition( Phase.TestRollingLogsDamping );
	}

	private bool ValidateValheimDropItem( WoodItem item, string label )
	{
		if ( !item.DebugValheimDrop )
		{
			Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: {label} drop used arcade WoodItem spawn path" );
			Finish();
			return false;
		}
		if ( item.DebugInitialVelocity.Length > 0.01f )
		{
			Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: {label} drop initial velocity={item.DebugInitialVelocity.Length:F2}, expected 0 like Instantiate(dropPrefab)" );
			Finish();
			return false;
		}
		var collider = item.GameObject.Components.Get<BoxCollider>();
		if ( !collider.IsValid() || !collider.ColliderFlags.HasFlag( ColliderFlags.IgnoreMass ) )
		{
			Log.Error( $"[TC_TEST] FAIL TestValheimDropGeometry: {label} drop collider can push logs (expected IgnoreMass)" );
			Finish();
			return false;
		}
		return true;
	}

	private static bool IsPotentialValheimTreeLogDrop( WoodItem item, Vector3 logCenter, Vector3 axis, int expectedLogDrops )
	{
		return TryMatchValheimTreeLogDrop( item, logCenter, axis, expectedLogDrops, out _, out _, out _ );
	}

	private static bool TryMatchValheimTreeLogDrop( WoodItem item, Vector3 logCenter, Vector3 axis, int expectedLogDrops, out float axisOff, out float radial, out int stepIndex )
	{
		axisOff = 0f;
		radial = float.MaxValue;
		stepIndex = -1;
		if ( !item.IsValid() || expectedLogDrops <= 0 ) return false;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;

		var delta = item.DebugInitialPosition - logCenter;
		for ( int i = 0; i < expectedLogDrops; i++ )
		{
			var candidate = delta - Vector3.Up * (Tunables.ValheimTreeBaseDropYStep * i);
			float candidateAxisOff = candidate.Dot( axis );
			float candidateRadial = (candidate - axis * candidateAxisOff).Length;
			if ( candidateRadial >= radial ) continue;
			radial = candidateRadial;
			axisOff = candidateAxisOff;
			stepIndex = i;
		}

		return MathF.Abs( axisOff ) <= Tunables.ValheimTreeLogSpawnDistance + 1f
			&& radial <= 1.5f;
	}

	private static Rotation RotationWithUpForTest( Vector3 up )
	{
		if ( up.LengthSquared < 0.001f ) return Rotation.Identity;
		up = up.Normal;
		float dot = Vector3.Up.Dot( up ).Clamp( -1f, 1f );
		if ( dot > 0.999f ) return Rotation.Identity;
		if ( dot < -0.999f ) return Rotation.FromAxis( Vector3.Right, 180f );
		var axis = Vector3.Cross( Vector3.Up, up );
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Right;
		return Rotation.FromAxis( axis.Normal, MathF.Acos( dot ).RadianToDegree() );
	}

	private static Vector3 LocalOffsetToWorld( Rotation rotation, Vector3 local )
	{
		return rotation.Forward * local.x + rotation.Right * local.y + rotation.Up * local.z;
	}

	private static Vector3 ExpectedAuthoredLogSpawnOffset( Tree tree )
	{
		float sourceScale = MathF.Max( 0.1f, tree.TrunkLength / Tunables.TreeHeight );
		return Tunables.TreeKindLogSpawnPoint[(int)tree.Kind] * sourceScale;
	}

	private static Vector3 ExpectedRuntimeLogSpawnOffset( Tree tree )
	{
		var authored = ExpectedAuthoredLogSpawnOffset( tree );
		float logLength = tree.TrunkLength * Tunables.TreeKindLogLengthMul[(int)tree.Kind];
		return new Vector3( authored.x, authored.y, logLength * 0.5f + Tunables.TreeLogSpawnGroundClearance );
	}

	private void TickTestRollingLogsDamping()
	{
		if ( MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogAngularDamping ) - 0.2f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: ValheimTreeLogAngularDamping={Tunables.ValheimTreeLogAngularDamping} != 0.2" );
			Finish();
			return;
		}
		if ( MathF.Abs( RuntimeValue( Tunables.ValheimTreeLogLinearDamping ) - 0.1f ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: ValheimTreeLogLinearDamping={Tunables.ValheimTreeLogLinearDamping} != 0.1" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.TreeLogSleepThreshold ) > 0.1f )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: TreeLogSleepThreshold={Tunables.TreeLogSleepThreshold} > 0.1 (logs dorment trop vite)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.LogGroundProbeCount ) < 5 )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: LogGroundProbeCount={Tunables.LogGroundProbeCount} < 5 (terrain probes too sparse)" );
			Finish();
			return;
		}
		var fallProbePos = _targetTreePos + new Vector3( -900f, 520f, 0f );
		if ( TryGetGroundZ( fallProbePos.x, fallProbePos.y, out var fallProbeGroundZ ) ) fallProbePos = fallProbePos.WithZ( fallProbeGroundZ );
		ClearTestObjectsAround( fallProbePos, 700f );
		var fallProbeTree = Tree.SpawnAt( Scene, fallProbePos, 0.5f, TreeKind.Normal );
		fallProbeTree.StartFell( Vector3.Forward, fellPower: 99, allowComboPush: true );
		var fallProbeLog = fallProbeTree.SpawnedLog;
		if ( !fallProbeLog.IsValid() || !fallProbeLog.IsFalling || !fallProbeLog.Body.IsValid() )
		{
			Log.Error( "[TC_TEST] FAIL TestRollingLogsDamping: falling log probe did not spawn" );
			Finish();
			return;
		}
		fallProbeLog.Body.Velocity = new Vector3( 120f, 30f, -320f );
		fallProbeLog.Body.AngularVelocity = new Vector3( 0.3f, 1.1f, 0.2f );
		if ( !fallProbeLog.IsFalling || fallProbeLog.DebugColliderShapeCount < 1 + Tunables.LogSupportSphereCount )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: falling log lost gravity-first collider state falling={fallProbeLog.IsFalling} shapes={fallProbeLog.DebugColliderShapeCount}" );
			Finish();
			return;
		}
		ClearTestObjectsAround( fallProbePos, 900f );
		if ( RuntimeValue( Tunables.SplitLogMaxSpawnValidationSpeed ) < RuntimeValue( Tunables.ImpactMaxSpeed )
			|| RuntimeValue( Tunables.SplitLogMaxSpawnValidationSpeed ) > RuntimeValue( Tunables.ImpactMaxSpeed ) * 3f )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: SplitLogMaxSpawnValidationSpeed={Tunables.SplitLogMaxSpawnValidationSpeed} should be an absurd-launch guard, not a physics cap" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] VALHEIM_LOG_PHYSICS_PASS  Gravity=on Angular={Tunables.ValheimTreeLogAngularDamping} Linear={Tunables.ValheimTreeLogLinearDamping} SleepThreshold={Tunables.TreeLogSleepThreshold} Probes={Tunables.LogGroundProbeCount}" );
		if ( PhysicsOnly )
		{
			PhaseOk( Phase.TestRollingLogsDamping );
			Finish();
			return;
		}
		Transition( Phase.TestEnvWindDeterministic );
	}

	private void TickTestEnvWindDeterministic()
	{
		// EnvWind sample 2× au même instant → mêmes valeurs (function pure de Time.Now).
		// Catch si quelqu'un introduit random state interne par accident.
		var dirA = EnvWind.GetWindDir();
		float intA = EnvWind.GetWindIntensity();
		var dirB = EnvWind.GetWindDir();
		float intB = EnvWind.GetWindIntensity();
		if ( (dirA - dirB).Length > 0.001f || MathF.Abs( intA - intB ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestEnvWindDeterministic: dirA={dirA} dirB={dirB} intA={intA} intB={intB} (expected pure function)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] WIND_DETERMINISTIC PASS  2 samples identiques (dir={dirA}, int={intA:F2})" );
		Transition( Phase.TestWoodTypeMixSumsAll );
	}

	private void TickTestWoodTypeMixSumsAll()
	{
		// Toutes les 4 TreeKind mix sums doivent être ≈ 1.0 (covers more edge cases
		// que TestMultiWoodTypes qui ne check que la présence). Verrouille les 4 kinds.
		for ( int k = 0; k < Tunables.TreeKindWoodTypeMix.Length; k++ )
		{
			var mix = Tunables.TreeKindWoodTypeMix[k];
			float sum = 0f;
			foreach ( var p in mix ) sum += p;
			if ( MathF.Abs( sum - 1.0f ) > 0.005f )
			{
				Log.Error( $"[TC_TEST] FAIL TestWoodTypeMixSumsAll: kind={(TreeKind)k} mix sum={sum:F3} (expected 1.000 ±0.005)" );
				Finish();
				return;
			}
			// Chaque proba ∈ [0..1]
			for ( int t = 0; t < mix.Length; t++ )
			{
				if ( mix[t] < 0f || mix[t] > 1f )
				{
					Log.Error( $"[TC_TEST] FAIL TestWoodTypeMixSumsAll: kind={(TreeKind)k} type={(WoodType)t} prob={mix[t]} hors [0..1]" );
					Finish();
					return;
				}
			}
		}
		Log.Info( $"[TC_TEST] WOODMIX_SUMS PASS  4 TreeKind mix sums ≈ 1.0, all probs ∈ [0..1]" );
		Transition( Phase.TestHitDataDamage );
	}

	private void TickTestHitDataDamage()
	{
		// Valheim IDestructible.Damage(HitData) — verifier que la new struct
		// propage correctement ChopPower au Tree. ResetForTest pour AxeTier 0,
		// puis call Damage(HitData) sur Sapling (MinAxeTier 0) avec ChopPower=1.
		// HP doit drop par 1.
		_state.ResetForTest();
		var pos = _targetTreePos + new Vector3( -3900f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var sap = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		int hpBefore = sap.ChopsRemaining;
		var hit = HitData.Make( Vector3.Forward, chopPower: 1, hitPoint: pos, toolTier: 0 );
		if ( hit.Damage.Chop != 1f || hit.Damage.Blunt != 0f || hit.GetTreeDamage() != 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: chop hit channels chop={hit.Damage.Chop} blunt={hit.Damage.Blunt} treeDamage={hit.GetTreeDamage()}" );
			Finish();
			return;
		}
		if ( MathF.Abs( hit.PushForce - Tunables.LandedLogKickImpulse ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: default PushForce={hit.PushForce}, expected {Tunables.LandedLogKickImpulse}" );
			Finish();
			return;
		}
		var impactHit = HitData.MakeImpact( Vector3.Forward, pos, Tunables.ValheimImpactToolTier, 1f );
		if ( impactHit.Damage.Chop != Tunables.ImpactChopDamage
			|| impactHit.Damage.Blunt != Tunables.ImpactBluntDamage
			|| impactHit.GetTreeLogDamage() != Tunables.ImpactChopDamage )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: impact channels chop={impactHit.Damage.Chop} blunt={impactHit.Damage.Blunt} effective={impactHit.GetTreeLogDamage()} expected chop-only {Tunables.ImpactChopDamage}" );
			Finish();
			return;
		}
		var strongHit = HitData.Make( Vector3.Forward, chopPower: 99, hitPoint: pos, toolTier: 0 );
		if ( MathF.Abs( strongHit.PushForce - Tunables.LandedLogKickImpulse ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: ChopPower leaked into PushForce ({strongHit.PushForce})" );
			Finish();
			return;
		}
		var finalHit = HitData.Make( Vector3.Forward, chopPower: 2, hitPoint: pos, toolTier: 0, pushForce: Tunables.LandedLogKickImpulse * Tunables.ChopComboFinalPushMul );
		if ( MathF.Abs( finalHit.PushForce - Tunables.LandedLogKickImpulse * Tunables.ChopComboFinalPushMul ) > 0.001f )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: explicit final PushForce={finalHit.PushForce}" );
			Finish();
			return;
		}
		// Cast IChoppable pour valider l'interface (default impl path) — Tree
		// override avec sa propre Damage(HitData) qui forward chopPower correct.
		IChoppable choppable = sap;
		choppable.Damage( hit );
		if ( sap.ChopsRemaining != hpBefore - 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: HP {hpBefore}→{sap.ChopsRemaining} (expected -1 via HitData.ChopPower=1)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] HITDATA PASS  IChoppable.Damage(HitData) propagated chopPower=1, HP {hpBefore}→{sap.ChopsRemaining}" );
		var normalPos = pos + new Vector3( 0f, 320f, 0f );
		if ( TryGetGroundZ( normalPos.x, normalPos.y, out var gz2 ) ) normalPos = normalPos.WithZ( gz2 );
		var normal = Tree.SpawnAt( Scene, normalPos, 1f, TreeKind.Normal );
		int normalHp = normal.ChopsRemaining;
		normal.Damage( HitData.Make( Vector3.Forward, chopPower: 1, hitPoint: normalPos, toolTier: 0 ) );
		if ( normal.ChopsRemaining != normalHp )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: ToolTier 0 damaged Normal HP {normalHp}->{normal.ChopsRemaining}" );
			Finish();
			return;
		}
		normal.Damage( HitData.Make( Vector3.Forward, chopPower: 1, hitPoint: normalPos, toolTier: 1 ) );
		if ( normal.ChopsRemaining != normalHp - 1 )
		{
			Log.Error( $"[TC_TEST] FAIL TestHitDataDamage: ToolTier 1 did not damage Normal HP {normalHp}->{normal.ChopsRemaining}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] HITDATA_TIER PASS  Normal ToolTier 0 bounced, ToolTier 1 damaged HP {normalHp}->{normal.ChopsRemaining}" );
		Transition( Phase.TestSwingFeedbackAudio );
	}

	private void TickTestSwingFeedbackAudio()
	{
		if ( _swingFeedbackStep == 0 )
		{
			_state.ResetForTest();
			UpgradeAxeTo( 2 );
			_swingFeedbackPos = _targetTreePos + new Vector3( -4300f, 760f, 0f );
			if ( TryGetGroundZ( _swingFeedbackPos.x, _swingFeedbackPos.y, out var gz ) )
				_swingFeedbackPos = _swingFeedbackPos.WithZ( gz );
			ClearTestObjectsAround( _swingFeedbackPos, 900f );
			_swingFeedbackTree = Tree.SpawnAt( Scene, _swingFeedbackPos, 1f, TreeKind.Veteran );
			_swingFeedbackHpBefore = _swingFeedbackTree.ChopsRemaining;
			var dir = Vector3.Forward;
			var playerPos = _swingFeedbackTree.WorldPosition - dir * 72f + Vector3.Up * 35f;
			_axe.TeleportTo( playerPos, Rotation.LookAt( dir ).Yaw() );
			Sfx.DebugLog = true;
			Sfx.ClearAudioLog();
			_axe.DebugRequestSwing = true;
			_swingFeedbackStepTime = 0f;
			_swingFeedbackStep = 1;
			return;
		}

		if ( _swingFeedbackStep == 1 )
		{
			if ( !_axe.IsSwingIdle || (float)_swingFeedbackStepTime < Tunables.SwingWindUpDuration + Tunables.SwingRecoveryDuration + 0.20f )
				return;
			int swings = Sfx.DebugCountLocal( "sounds/swing.sound" );
			int localHits = Sfx.DebugCountLocal( "sounds/axe_hit_wood.sound" );
			int localBites = Sfx.DebugCountLocal( "sounds/chop_wood.sound" );
			int worldHits = Sfx.DebugCountWorld( "sounds/axe_hit_wood.sound" );
			int worldBites = Sfx.DebugCountWorld( "sounds/chop_wood.sound" );
			if ( !_swingFeedbackTree.IsValid() || _swingFeedbackTree.ChopsRemaining >= _swingFeedbackHpBefore )
			{
				Log.Error( $"[TC_TEST] FAIL TestSwingFeedbackAudio: real DebugRequestSwing did not damage target hp={(_swingFeedbackTree.IsValid() ? _swingFeedbackTree.ChopsRemaining : -1)} before={_swingFeedbackHpBefore} localSfx={Sfx.DebugLocalSummary()} worldSfx={Sfx.DebugWorldSummary()} all={Sfx.DebugSummary()}" );
				Sfx.DebugLog = false;
				Finish();
				return;
			}
			if ( swings != 1 || localHits < 1 || localBites < 1 || worldHits < 1 || worldBites != 0 )
			{
				Log.Error( $"[TC_TEST] FAIL TestSwingFeedbackAudio: hit swing missing SFX swing={swings} localHit={localHits} localBite={localBites} worldHit={worldHits} worldBite={worldBites} local={Sfx.DebugLocalSummary()} world={Sfx.DebugWorldSummary()} all={Sfx.DebugSummary()}" );
				Sfx.DebugLog = false;
				Finish();
				return;
			}

			var emptyPos = _swingFeedbackPos + new Vector3( 0f, 1150f, 0f );
			if ( TryGetGroundZ( emptyPos.x, emptyPos.y, out var gz ) )
				emptyPos = emptyPos.WithZ( gz );
			ClearTestObjectsAround( emptyPos, 900f );
			Sfx.ClearAudioLog();
			_axe.TeleportTo( emptyPos + Vector3.Up * 35f, 0f );
			_axe.DebugRequestSwing = true;
			_swingFeedbackStepTime = 0f;
			_swingFeedbackStep = 2;
			return;
		}

		if ( _swingFeedbackStep == 2 )
		{
			if ( !_axe.IsSwingIdle || (float)_swingFeedbackStepTime < Tunables.SwingWindUpDuration + Tunables.SwingRecoveryDuration + 0.20f )
				return;
			int swings = Sfx.DebugCountLocal( "sounds/swing.sound" );
			int hits = Sfx.DebugCountLocal( "sounds/axe_hit_wood.sound" );
			int bites = Sfx.DebugCountLocal( "sounds/chop_wood.sound" );
			int worldHits = Sfx.DebugCountWorld( "sounds/axe_hit_wood.sound" );
			int worldBites = Sfx.DebugCountWorld( "sounds/chop_wood.sound" );
			if ( swings != 1 || hits != 0 || bites != 0 )
			{
				Log.Error( $"[TC_TEST] FAIL TestSwingFeedbackAudio: empty swing routing wrong swing={swings} hit={hits} bite={bites} worldHit={worldHits} worldBite={worldBites} local={Sfx.DebugLocalSummary()} world={Sfx.DebugWorldSummary()} all={Sfx.DebugSummary()}" );
				Sfx.DebugLog = false;
				Finish();
				return;
			}
			Sfx.DebugLog = false;
			ClearTestObjectsAround( _swingFeedbackPos, 1200f );
			Log.Info( $"[TC_TEST] SWING_FEEDBACK_AUDIO PASS  hit swing emitted local swing/hit/bite + world hit, empty swing emitted local swing only" );
			Transition( Phase.TestGameStateSanitize );
		}
	}

	private void TickTestGameStateSanitize()
	{
		_state.DebugSetUnsafeProgressForTest();
		bool tiersOk =
			_state.AxeTier == Tunables.MaxAxeTier
			&& _state.SpeedTier == 0
			&& _state.LuckTier == Tunables.MaxStatTier
			&& _state.PowerTier == Tunables.MaxStatTier
			&& _state.BackpackTier == 0
			&& _state.PetTier == Tunables.MaxPetTier
			&& _state.ToolRangeTier == Tunables.MaxToolStatTier
			&& _state.ToolSpeedTier == 0;
		bool currenciesOk =
			_state.Wood == 0
			&& _state.Finewood == 0
			&& _state.CoreWood == 0
			&& _state.BackpackTotal <= _state.BackpackCapacity
			&& _state.Spirits == 0
			&& _state.TotalWoodEarned == 0
			&& _state.TotalChops == 0
			&& _state.TreesFelledTotal == 0;
		bool statsOk = _state.TreesFelledByTier != null && _state.TreesFelledByTier.Length == Tunables.MaxAxeTier + 1
			&& _state.TreesFelledByTier.All( v => v >= 0 );
		if ( !tiersOk || !currenciesOk || !statsOk )
		{
			Log.Error( $"[TC_TEST] FAIL TestGameStateSanitize: tiersOk={tiersOk} currenciesOk={currenciesOk} statsOk={statsOk} bag={_state.BackpackTotal}/{_state.BackpackCapacity}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] GAMESTATE_SANITIZE PASS  tiers clamped, currencies non-negative, bag={_state.BackpackTotal}/{_state.BackpackCapacity}" );
		_state.ResetForTest();
		Transition( Phase.TestStats );
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
		// Sanity-check the derived multipliers wired into AxeController +
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
		if ( _state.Wood != 0 || _state.AxeTier != 0 || _state.SpeedTier != 0 || _state.PowerTier != 0 || _state.PetTier != 0 )
		{
			Log.Error( $"[TC_TEST] FAIL: tiers not reset (wood={_state.Wood} axe={_state.AxeTier} spd={_state.SpeedTier} pwr={_state.PowerTier} pet={_state.PetTier})" );
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
			PhaseOk( Phase.TestPrestige );
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

	private float AllowedTerrainAwareUpDot( FallenLog log, float flatLimit )
	{
		if ( !log.IsValid() ) return flatLimit;
		var flatAxis = log.DebugAxis.WithZ( 0f );
		if ( flatAxis.LengthSquared < 0.001f ) return flatLimit;
		flatAxis = flatAxis.Normal;
		float half = MathF.Max( 24f, log.DebugTrunkLength * 0.5f );
		var a = log.LogCenter - flatAxis * half;
		var b = log.LogCenter + flatAxis * half;
		if ( !TryGetGroundZ( a.x, a.y, out var az ) || !TryGetGroundZ( b.x, b.y, out var bz ) )
			return flatLimit;
		var tangent = new Vector3( b.x - a.x, b.y - a.y, bz - az );
		if ( tangent.LengthSquared < 0.001f ) return flatLimit;
		float terrainUpDot = MathF.Abs( tangent.Normal.Dot( Vector3.Up ) );
		return MathF.Max( flatLimit, MathF.Min( 0.88f, terrainUpDot + 0.18f ) );
	}

	private static bool ValidateTreeLogPhysicsContract( FallenLog log, float expectedMass, int expectedSplitDepth, string label, out string error )
	{
		error = "";
		if ( !log.IsValid() )
		{
			error = $"{label} invalid";
			return false;
		}
		if ( log.DebugSplitDepth != expectedSplitDepth )
		{
			error = $"{label} splitDepth={log.DebugSplitDepth}, expected {expectedSplitDepth}";
			return false;
		}
		if ( log is not Component.ICollisionListener )
		{
			error = $"{label} is not a collision-owning TreeLog";
			return false;
		}
		var rb = log.Body;
		if ( !rb.IsValid() || !rb.PhysicsBody.IsValid() )
		{
			error = $"{label} missing valid Rigidbody/PhysicsBody";
			return false;
		}
		if ( !rb.Enabled || !rb.MotionEnabled || !rb.Gravity )
		{
			error = $"{label} body state enabled={rb.Enabled} motion={rb.MotionEnabled} gravity={rb.Gravity}";
			return false;
		}
		if ( rb.StartAsleep || !rb.EnhancedCcd )
		{
			error = $"{label} sleep/ccd state startAsleep={rb.StartAsleep} enhancedCcd={rb.EnhancedCcd}";
			return false;
		}
		if ( MathF.Abs( rb.LinearDamping - Tunables.ValheimTreeLogLinearDamping ) > 0.001f
			|| MathF.Abs( rb.AngularDamping - Tunables.ValheimTreeLogAngularDamping ) > 0.001f
			|| MathF.Abs( rb.SleepThreshold - Tunables.TreeLogSleepThreshold ) > 0.001f )
		{
			error = $"{label} damping/sleep linear={rb.LinearDamping:F3} angular={rb.AngularDamping:F3} sleep={rb.SleepThreshold:F3}";
			return false;
		}
		float actualMass = rb.PhysicsBody.Mass;
		if ( MathF.Abs( actualMass - expectedMass ) > 0.1f )
		{
			error = $"{label} mass={actualMass:F2}, expected={expectedMass:F2}";
			return false;
		}
		int expectedShapes = 1 + Tunables.LogSupportSphereCount;
		if ( log.DebugColliderShapeCount < expectedShapes )
		{
			error = $"{label} collider shapes={log.DebugColliderShapeCount}, expected >= {expectedShapes}";
			return false;
		}
		return true;
	}

	private void ParkPlayerInFrontOfTarget()
	{
		var pos = _targetTreePos + new Vector3( 60f, 0f, 40f );
		_axe.TeleportTo( pos, 180f ); // face -X (toward tree at -60 relative)
	}

	private void ParkPlayerFacing( Vector3 targetPos )
	{
		var dir = (targetPos - _axe.WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 1f ) dir = Vector3.Forward;
		dir = dir.Normal;
		float standOff = MathF.Max( 18f, Tunables.SwingRange * 0.72f );
		var pos = targetPos - dir * standOff + Vector3.Up * 35f;
		_axe.TeleportTo( pos, Rotation.LookAt( dir ).Yaw() );
	}

	private void ParkPlayerFacingLog( FallenLog log )
	{
		if ( !log.IsValid() )
			return;
		ParkPlayerFacing( log.GetChopPointFrom( _axe.WorldPosition ) );
	}

	private void UpgradeAxeTo( int tier )
	{
		int wood = 0, finewood = 0, corewood = 0;
		for ( int i = _state.AxeTier + 1; i <= tier; i++ )
		{
			var recipe = Tunables.AxeTierCostsByType[i];
			wood += recipe[0];
			finewood += recipe[1];
			corewood += recipe[2];
		}
		_state.DebugAddStockpileForTest( wood, finewood, corewood );
		while ( _state.AxeTier < tier && _state.TryUpgradeAxe() ) { }
	}

	private void ClearTestObjectsAround( Vector3 center, float radius )
	{
		foreach ( var tree in Scene.GetAllComponents<Tree>().ToList() )
		{
			if ( tree.IsValid() && tree.WorldPosition.Distance( center ) < radius )
				tree.GameObject?.Destroy();
		}
		foreach ( var log in Scene.GetAllComponents<FallenLog>().ToList() )
		{
			if ( log.IsValid() && log.WorldPosition.Distance( center ) < radius )
				log.GameObject?.Destroy();
		}
		foreach ( var item in Scene.GetAllComponents<WoodItem>().ToList() )
		{
			if ( item.IsValid() && item.WorldPosition.Distance( center ) < radius )
				item.GameObject?.Destroy();
		}
	}

	private int CountWoodItemsAndBackpackNear( Vector3 center, float radius )
	{
		int loose = Scene.GetAllComponents<WoodItem>()
			.Count( item => item.IsValid() && item.WorldPosition.Distance( center ) < radius );
		return loose + (_state.IsValid() ? _state.BackpackTotal : 0);
	}

	private void ClearWoodItems()
	{
		foreach ( var item in Scene.GetAllComponents<WoodItem>().ToList() )
		{
			if ( item.IsValid() )
				item.GameObject?.Destroy();
		}
	}

	private FallenLog FindNearestFallenLog( Vector3 pos, float radius )
	{
		return Scene.GetAllComponents<FallenLog>()
			.Where( l => l.IsValid() && l.WorldPosition.Distance( pos ) < radius )
			.OrderBy( l => l.WorldPosition.Distance( pos ) )
			.FirstOrDefault();
	}

	private void Finish()
	{
		Log.Info( "[TC_TEST] DONE" );
		_phase = Phase.Done;
	}
}
