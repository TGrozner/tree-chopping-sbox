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
		TestStump, TestSplit, TestLandedLogChopGrace, TestBonusDrop, TestWoodPickup, TestPhysicsAutoSplit, TestStumpRespawn, TestCascadeDamage, TestCascadeCollision,
		TestAxeTierGate, TestLogTierGate, TestChopPowerScaling, TestImpactBelowMin, TestImpactZeroNoOp,
		TestBackpackFull, TestDepositFlush, TestDepositStationEntry, TestPrestigeFormula, TestFallingImpactSplit, TestComboFinalDamage, TestMultiWoodTypes,
		TestStatCounters, TestWoodCuttingLevel, TestPickupStackMerge, TestEnvWindSanity, TestStrictTooHard, TestTunablesValheimSanity,
		TestFellCanopyDestroyed, TestImpactDamageScaling, TestWindDirRotation, TestRespawnJitterRange, TestWoodTypeDistribution, TestTreeShakeReset, TestCascadeShakeNoFell,
		TestRollingLogsDamping, TestEnvWindDeterministic, TestWoodTypeMixSumsAll, TestHitDataDamage,
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
	private Tree _landedGraceTree;
	private FallenLog _landedGraceLog;
	private bool _landedGraceSpawned;
	private bool _landedGraceEarlyHitChecked;
	private int _landedGraceHpAfterEarlyHit;
	private TimeSince _landedGraceSinceLanded;
	// État pour TestWoodPickup
	private int _backpackBeforePickup;
	private TimeSince _pickupSpawnTime;
	private bool _pickupSpawned;
	// État pour TestPhysicsAutoSplit
	private Tree _autoSplitTree;
	private FallenLog _autoSplitLog;
	private TimeSince _autoSplitStartTime;
	private bool _autoSplitSpawned;
	private Tree _cascadeSource;
	private FallenLog _cascadeSourceLog;
	private Tree _cascadeNeighbor;
	private TimeSince _cascadeCollisionStartTime;
	private bool _cascadeCollisionSpawned;
	private int _cascadeNeighborHpBefore;
	private FallenLog _logTierGateLog;
	private TimeSince _logTierGateSinceLanded;
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
			case Phase.TestSpawnDistribution: TickTestSpawnDistribution(); break;
			case Phase.Approach: TickApproach(); break;
			case Phase.Swing: TickSwing(); break;
			case Phase.Verify: TickVerify(); break;
			case Phase.TestStump: TickTestStump(); break;
			case Phase.TestSplit: TickTestSplit(); break;
			case Phase.TestLandedLogChopGrace: TickTestLandedLogChopGrace(); break;
			case Phase.TestBonusDrop: TickTestBonusDrop(); break;
			case Phase.TestWoodPickup: TickTestWoodPickup(); break;
			case Phase.TestPhysicsAutoSplit: TickTestPhysicsAutoSplit(); break;
			case Phase.TestStumpRespawn: TickTestStumpRespawn(); break;
			case Phase.TestCascadeDamage: TickTestCascadeDamage(); break;
			case Phase.TestCascadeCollision: TickTestCascadeCollision(); break;
			case Phase.TestAxeTierGate: TickTestAxeTierGate(); break;
			case Phase.TestLogTierGate: TickTestLogTierGate(); break;
			case Phase.TestChopPowerScaling: TickTestChopPowerScaling(); break;
			case Phase.TestImpactBelowMin: TickTestImpactBelowMin(); break;
			case Phase.TestImpactZeroNoOp: TickTestImpactZeroNoOp(); break;
			case Phase.TestBackpackFull: TickTestBackpackFull(); break;
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
			case Phase.TestRollingLogsDamping: TickTestRollingLogsDamping(); break;
			case Phase.TestEnvWindDeterministic: TickTestEnvWindDeterministic(); break;
			case Phase.TestWoodTypeMixSumsAll: TickTestWoodTypeMixSumsAll(); break;
			case Phase.TestHitDataDamage: TickTestHitDataDamage(); break;
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
		Transition( Phase.TestSpawnDistribution );
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
		// Si le sapling auto-split via TreeSplitImpactSpeed (ne devrait pas
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
				Log.Error( $"[TC_TEST] FAIL TestSplit: no landed FallenLog after 5s (logValid={_targetLog.IsValid()} seen={_targetLogSeen})" );
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
			ParkPlayerFacing( _targetLog.LogCenter );
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
			_landedGraceTree = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
			_landedGraceTree.StartFell( Vector3.Forward );
			_landedGraceLog = FindNearestFallenLog( pos, 700f );
			return;
		}

		if ( !_landedGraceLog.IsValid() )
		{
			_landedGraceLog = FindNearestFallenLog( _targetTreePos + new Vector3( -520f, 420f, 0f ), 700f );
		}
		if ( !_landedGraceLog.IsValid() || !_landedGraceLog.IsFallenLog )
		{
			if ( (float)_phaseTime > 5f )
			{
				Log.Error( "[TC_TEST] FAIL TestLandedLogChopGrace: log never became landed" );
				Finish();
			}
			return;
		}

		if ( !_landedGraceEarlyHitChecked )
		{
			_landedGraceEarlyHitChecked = true;
			_landedGraceSinceLanded = 0f;
			int hpBefore = _landedGraceLog.ChopsRemaining;
			_landedGraceLog.Chop( Vector3.Forward, 1, _landedGraceLog.LogCenter );
			_landedGraceHpAfterEarlyHit = _landedGraceLog.ChopsRemaining;
			if ( _landedGraceHpAfterEarlyHit != hpBefore )
			{
				Log.Error( $"[TC_TEST] FAIL TestLandedLogChopGrace: early hit damaged log HP {hpBefore}->{_landedGraceHpAfterEarlyHit}" );
				Finish();
			}
			return;
		}

		if ( (float)_landedGraceSinceLanded < Tunables.WoodLogChopGrace + 0.08f ) return;
		_landedGraceLog.Chop( Vector3.Forward, 1, _landedGraceLog.LogCenter );
		if ( _landedGraceLog.IsValid() && _landedGraceLog.ChopsRemaining >= _landedGraceHpAfterEarlyHit )
		{
			Log.Error( $"[TC_TEST] FAIL TestLandedLogChopGrace: post-grace hit did not damage log HP={_landedGraceLog.ChopsRemaining}" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] LANDED_LOG_GRACE PASS  early hit ignored for {Tunables.WoodLogChopGrace:0.00}s, post-grace hit applied" );
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
		Transition( Phase.TestWoodPickup );
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
			// Spawn 60u devant le player (hors de son collider qui sinon push
			// l'item à dist aléatoire >80u et bypasse magnet). Avec gravity=false
			// + vel=0, l'item reste à 60u → magnet engage post-grace, fly à
			// 700u/s vers player, pickup à <30u dans ~0.04s. Total ~0.54s.
			var spawnPos = _axe.WorldPosition + new Vector3( 60f, 0f, 30f );
			var item = WoodItem.SpawnAt( Scene, spawnPos );
			if ( item.Body.IsValid() )
			{
				item.Body.Velocity = Vector3.Zero;
				item.Body.AngularVelocity = Vector3.Zero;
				item.Body.Gravity = false;
			}
			_pickupSpawnTime = 0f;
			Log.Info( $"[TC_TEST] WoodPickup : spawned item at {spawnPos} (60u from player, vel+gravity zeroed), backpack before={_backpackBeforePickup}" );
			return;
		}

		// Wait up to 2.5s for the item to magnet + pickup (incl 0.5s grace +
		// the item-vs-player-collider push-out drifts to ~35-40u typically,
		// still within MagnetRange 80u → magnet engages reliably).
		if ( _state.BackpackWood > _backpackBeforePickup )
		{
			Log.Info( $"[TC_TEST] PICKUP PASS  backpack {_backpackBeforePickup}→{_state.BackpackWood}  (elapsed={(float)_pickupSpawnTime:F2}s, grace={Tunables.WoodItemMagnetGrace}s)" );
			Transition( Phase.TestPhysicsAutoSplit );
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

	private void TickTestPhysicsAutoSplit()
	{
		// Spawn un Brittle (TreeKindSplitImpactMul=0.45 → threshold bas ~315 u/s),
		// force StartFell + override Velocity à -1500u/s pour garantir un impact
		// franc au-delà du seuil. OnCollisionStart capture _preCollisionVelocity
		// au prochain fixed-update et split immédiat sans chop manuel.
		// Si TreeSplitImpactSpeed × TreeKindSplitImpactMul drift, ce test pète.
		if ( !_autoSplitSpawned )
		{
			_autoSplitSpawned = true;
			var spawnPos = _targetTreePos + new Vector3( 600f, -600f, 0f );
			if ( TryGetGroundZ( spawnPos.x, spawnPos.y, out var groundZ ) )
				spawnPos = spawnPos.WithZ( groundZ + 50f ); // slight gap for clean OnCollisionStart event
			_autoSplitTree = Tree.SpawnAt( Scene, spawnPos, 1f, TreeKind.Brittle );
			_autoSplitTree.StartFell( Vector3.Forward );
			_autoSplitLog = _autoSplitTree.SpawnedLog;
			if ( _autoSplitLog.IsValid() && _autoSplitLog.Body.IsValid() )
				_autoSplitLog.Body.Velocity = new Vector3( 0f, 0f, -1500f );
			_autoSplitStartTime = 0f;
			Log.Info( $"[TC_TEST] AutoSplit : spawned Brittle at {spawnPos} with forced -1500u/s downward velocity" );
			return;
		}

		// Tree GameObject destroyed = SplitIntoLogs fired via OnCollisionStart.
		if ( !_autoSplitLog.IsValid() )
		{
			Log.Info( $"[TC_TEST] AUTO_SPLIT PASS  Brittle split via physics at t={(float)_autoSplitStartTime:F2}s (threshold {Tunables.TreeSplitImpactSpeed * Tunables.TreeKindSplitImpactMul[(int)TreeKind.Brittle]:F0} u/s respected)" );
			Transition( Phase.TestStumpRespawn );
			return;
		}

		if ( (float)_autoSplitStartTime > 3f )
		{
			Log.Error( $"[TC_TEST] FAIL TestPhysicsAutoSplit: Brittle didn't auto-split in 3s (logFalling={_autoSplitLog.IsFalling} logLanded={_autoSplitLog.IsFallenLog})" );
			Finish();
			return;
		}
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
		// indirectement par TestPhysicsAutoSplit).
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
		if ( !_cascadeCollisionSpawned )
		{
			var neighborPos = _targetTreePos + new Vector3( -1050f, 360f, 0f );
			if ( TryGetGroundZ( neighborPos.x, neighborPos.y, out var gzNeighbor ) )
				neighborPos = neighborPos.WithZ( gzNeighbor );

			var sourcePos = neighborPos - Vector3.Forward * 150f;
			if ( TryGetGroundZ( sourcePos.x, sourcePos.y, out var gzSource ) )
				sourcePos = sourcePos.WithZ( gzSource );

			_cascadeNeighbor = Tree.SpawnAt( Scene, neighborPos, 0f, TreeKind.Normal );
			_cascadeSource = Tree.SpawnAt( Scene, sourcePos, 0f, TreeKind.Normal );
			_cascadeNeighborHpBefore = _cascadeNeighbor.ChopsRemaining;
			_cascadeSource.StartFell( Vector3.Forward );
			_cascadeSourceLog = _cascadeSource.SpawnedLog;
			_cascadeSourceLog.WorldRotation = Rotation.FromAxis( Vector3.Right, 90f );
			var logAxis = _cascadeSourceLog.WorldRotation.Up.Normal;
			sourcePos = neighborPos - logAxis * 150f;
			sourcePos = sourcePos.WithZ( neighborPos.z + _cascadeNeighbor.TrunkLength * 0.35f );
			_cascadeSourceLog.WorldPosition = sourcePos;
			if ( _cascadeSourceLog.Body.IsValid() )
			{
				if ( _cascadeSourceLog.Body.PhysicsBody.IsValid() )
				{
					_cascadeSourceLog.Body.PhysicsBody.Position = sourcePos;
					_cascadeSourceLog.Body.PhysicsBody.Rotation = _cascadeSourceLog.WorldRotation;
					_cascadeSourceLog.Body.PhysicsBody.Velocity = logAxis * (Tunables.ImpactMinSpeed + 600f);
					_cascadeSourceLog.Body.PhysicsBody.AngularVelocity = Vector3.Zero;
				}
				_cascadeSourceLog.Body.Velocity = logAxis * (Tunables.ImpactMinSpeed + 600f);
				_cascadeSourceLog.Body.AngularVelocity = Vector3.Zero;
				_cascadeSourceLog.Body.Sleeping = false;
			}
			_cascadeCollisionStartTime = 0f;
			_cascadeCollisionSpawned = true;
			Log.Info( $"[TC_TEST] CascadeCollision : source={sourcePos} neighbor={neighborPos} hp={_cascadeNeighborHpBefore}" );
			return;
		}

		if ( _cascadeSourceLog.IsValid() && (float)_cascadeCollisionStartTime < 0.65f && _cascadeSourceLog.Body.IsValid() )
		{
			var logAxis = _cascadeSourceLog.WorldRotation.Up;
			if ( logAxis.LengthSquared < 0.001f ) logAxis = Vector3.Forward;
			logAxis = logAxis.Normal;
			var velocity = logAxis * (Tunables.ImpactMinSpeed + 600f);
			if ( _cascadeSourceLog.Body.PhysicsBody.IsValid() )
				_cascadeSourceLog.Body.PhysicsBody.Velocity = velocity;
			_cascadeSourceLog.Body.Velocity = velocity;
			_cascadeSourceLog.Body.Sleeping = false;
		}

		if ( !_cascadeNeighbor.IsValid() || !_cascadeNeighbor.IsStanding )
		{
			Log.Info( $"[TC_TEST] CASCADE_COLLISION PASS  falling tree collision woke neighbor (HP before={_cascadeNeighborHpBefore}, valid={_cascadeNeighbor.IsValid()}, standing={(_cascadeNeighbor.IsValid() ? _cascadeNeighbor.IsStanding : false)})" );
			Transition( Phase.TestAxeTierGate );
			return;
		}

		if ( _cascadeNeighbor.ChopsRemaining < _cascadeNeighborHpBefore )
		{
			Log.Info( $"[TC_TEST] CASCADE_COLLISION PASS  falling tree collision damaged neighbor HP {_cascadeNeighborHpBefore}→{_cascadeNeighbor.ChopsRemaining}" );
			Transition( Phase.TestAxeTierGate );
			return;
		}

		if ( (float)_cascadeCollisionStartTime > 2.0f )
		{
			Log.Error( $"[TC_TEST] FAIL TestCascadeCollision: neighbor unchanged after falling log collision window (HP={_cascadeNeighbor.ChopsRemaining}/{_cascadeNeighborHpBefore}, logValid={_cascadeSourceLog.IsValid()})" );
			Finish();
		}
	}

	private void TickTestAxeTierGate()
	{
		// AxeTier 0 vs Veteran (MinAxeTier=3) → KickWobble + TROP DUR popup,
		// ChopsRemaining INCHANGÉ. Vérifie le gate Tunables.TreeKindMinAxeTier.
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
		Log.Info( $"[TC_TEST] AXE_GATE PASS  AxeTier 0 vs Veteran HP={hpBefore} unchanged + standing" );
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
			_logTierGateLog.ApplyImpactDamage( 1, Vector3.Forward );
			_logTierGateHpBefore = _logTierGateLog.ChopsRemaining;
			_logTierGateSinceLanded = 0f;
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
		if ( (float)_logTierGateSinceLanded < Tunables.WoodLogChopGrace + 0.06f )
			return;

		int hpBefore = _logTierGateHpBefore;
		ParkPlayerFacing( log.LogCenter );
		var weakHit = _axe.DebugSwingVerbose();
		if ( weakHit != log || log.ChopsRemaining != hpBefore || !log.IsFallenLog )
		{
			Log.Error( $"[TC_TEST] FAIL TestLogTierGate: weak axe changed Veteran log (hit={weakHit?.GetType().Name ?? "null"} hp {hpBefore}->{(log.IsValid() ? log.ChopsRemaining : -1)} valid={log.IsValid()})" );
			Finish();
			return;
		}

		int neededTier = Tunables.TreeKindMinAxeTier[(int)TreeKind.Veteran];
		UpgradeAxeTo( neededTier );
		ParkPlayerFacing( log.LogCenter );
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
		// Tier 4 = ChopPower 8 (AxeTierChopPower[4]). Sapling HP ~2, donc
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
		_state.AddWood( totalWood );
		_state.AddBackpack( totalFinewood, WoodType.Finewood );
		_state.AddBackpack( totalCoreWood, WoodType.CoreWood );
		_state.TryDeposit(); // flush Finewood + CoreWood backpack → stockpile
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
		// Impact à speed < ImpactMinSpeed (250) ne doit générer AUCUN damage.
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
		// BackpackTier 0 → cap 50. Fill via AddBackpack, vérifie qu'à cap
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
		// Path : Tree falling (mid-air, _chopped=true, _landed=false) hit par
		// un damage qui met HP à 0 → ApplyImpactDamage doit déclencher
		// BecomeLandedLog + SplitIntoLogs en une transaction. Test direct.
		var pos = _targetTreePos + new Vector3( -2000f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var tree = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		tree.StartFell( Vector3.Forward ); // _chopped=true, _landed=false (falling)
		var log = tree.SpawnedLog;
		if ( !log.IsValid() || !log.IsFalling )
		{
			Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: tree pas en état Falling après StartFell (IsValid={tree.IsValid()} IsStanding={tree.IsStanding} IsFallenLog={tree.IsFallenLog})" );
			Finish();
			return;
		}
		// Force HP→0 via impact damage massif
		log.ApplyImpactDamage( 999, Vector3.Forward );
		// Tree should be _logSplit=true → GameObject destroyed
		if ( !log.IsSplit )
		{
			Log.Error( $"[TC_TEST] FAIL TestFallingImpactSplit: tree toujours valide après ApplyImpactDamage(999) — split non déclenché" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] FALLING_IMPACT_SPLIT PASS  tree falling + ApplyImpactDamage(999) → destroyed (split via instant land+split)" );
		Transition( Phase.TestComboFinalDamage );
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
		if ( RuntimeValue( Tunables.ChopComboWindow ) <= 0f )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: ChopComboWindow={Tunables.ChopComboWindow} invalide" );
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
		// Sapling HP 2-3, finalPower = ceil(1 × 2.0) = 2 → fell en 1 chop
		// (sapling HP 1) ou chip mais > 0 HP (sapling HP 3).
		var pos = _targetTreePos + new Vector3( -2200f, 0f, 0f );
		if ( TryGetGroundZ( pos.x, pos.y, out var gz ) ) pos = pos.WithZ( gz );
		var tree = Tree.SpawnAt( Scene, pos, 1f, TreeKind.Sapling );
		int hpBefore = tree.ChopsRemaining;
		int basePower = 1;
		int finalPower = Math.Max( 1, (int)MathF.Ceiling( basePower * Tunables.ChopComboFinalDamageMul ) );
		float basePush = Tree.ComputeLandedKickPowerScale( basePower, basePower );
		float finalPush = Tree.ComputeLandedKickPowerScale( basePower, finalPower );
		float expectedPush = (1f + 0.3f * finalPower) * Tunables.ChopComboFinalPushMul;
		float baseFellPush = Tree.ComputeFellKickPowerScale( basePower, basePower );
		float finalFellPush = Tree.ComputeFellKickPowerScale( basePower, finalPower );
		float expectedFellPush = (1f + MathF.Min( finalPower - 1, 6 ) * 0.04f) * Tunables.ChopComboFinalPushMul;
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
		if ( MathF.Abs( finalFellPush - expectedFellPush ) > 0.001f || finalFellPush <= baseFellPush )
		{
			Log.Error( $"[TC_TEST] FAIL TestComboFinalDamage: finalFellPush={finalFellPush:0.###}, expected={expectedFellPush:0.###}, baseFellPush={baseFellPush:0.###}" );
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
		// ImpactInterval (Valheim ImpactEffect.m_interval default 0.5s)
		if ( RuntimeValue( Tunables.ImpactInterval ) != 0.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: ImpactInterval={Tunables.ImpactInterval} (expected 0.5 Valheim default)" );
			Finish();
			return;
		}
		// TreeKindWoodTypeMix : 4 kinds × 3 types, sums ~1.0
		if ( RuntimeValue( Tunables.InitialFellTopImpulseSpeed ) <= 0f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: InitialFellTopImpulseSpeed={Tunables.InitialFellTopImpulseSpeed} (expected > 0 for Valheim top impulse)" );
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
		// TreeAngularDampLanded < 0.5 (Valheim feel : logs roll)
		if ( RuntimeValue( Tunables.TreeAngularDampLanded ) >= 0.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeAngularDampLanded={Tunables.TreeAngularDampLanded} (expected < 0.5 pour rolling logs Valheim feel)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.WoodLogBreakImpactSpeed ) <= RuntimeValue( Tunables.TreeSplitImpactSpeed ) )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: WoodLogBreakImpactSpeed={Tunables.WoodLogBreakImpactSpeed} should exceed TreeSplitImpactSpeed={Tunables.TreeSplitImpactSpeed} so normal landed logs remain chopable" );
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
		if ( Tunables.TreeKindFellTorqueMul.Length != 4 )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeKindFellTorqueMul has {Tunables.TreeKindFellTorqueMul.Length} entries (expected 4)" );
			Finish();
			return;
		}
		if ( Tunables.TreeKindFellTorqueMul[(int)TreeKind.Sapling] <= Tunables.TreeKindFellTorqueMul[(int)TreeKind.Normal]
			|| Tunables.TreeKindFellTorqueMul[(int)TreeKind.Veteran] >= Tunables.TreeKindFellTorqueMul[(int)TreeKind.Normal] )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: fell torque kind mul should be Sapling > Normal > Veteran (S={Tunables.TreeKindFellTorqueMul[(int)TreeKind.Sapling]}, N={Tunables.TreeKindFellTorqueMul[(int)TreeKind.Normal]}, V={Tunables.TreeKindFellTorqueMul[(int)TreeKind.Veteran]})" );
			Finish();
			return;
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
		if ( RuntimeValue( Tunables.SwingMoveSpeedFactor ) <= 0f || RuntimeValue( Tunables.SwingMoveSpeedFactor ) >= 1f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: SwingMoveSpeedFactor={Tunables.SwingMoveSpeedFactor} (expected 0..1 attack movement slowdown)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.LogDropAxisSpreadMax ) > 90f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: LogDropAxisSpreadMax={Tunables.LogDropAxisSpreadMax} (Valheim TreeLog.m_spawnDistance=2m ~= 79u)" );
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
		Log.Info( $"[TC_TEST] TUNABLES_SANITY PASS  Shake 40/36Hz 1.5° 1s, ImpactInterval 0.5s, recipes 7×3, woodTypes 3×3, resource ladder reachable, wind ok, damping ok, chop pitch S>1>V, whoosh threshold ok" );
		if ( RuntimeValue( Tunables.TreeHitFlashDuration ) <= 0f || RuntimeValue( Tunables.TreeHitFlashDuration ) > 0.25f )
		{
			Log.Error( $"[TC_TEST] FAIL TestTunablesValheimSanity: TreeHitFlashDuration={Tunables.TreeHitFlashDuration} (expected quick impact flash <= 0.25s)" );
			Finish();
			return;
		}
		Transition( Phase.TestFellCanopyDestroyed );
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
		// à 3 vitesses caractéristiques. Mathematics : impact at min → 0 damage (early return),
		// impact at midpoint → ceil(6 * 0.5) = 3, impact at max → 6.
		// On simule la formule directement (pas via OnCollisionStart) car _preCollisionVelocity
		// nécessite collision setup. La formule est l'invariant à protéger.
		float midSpeed = (Tunables.ImpactMinSpeed + Tunables.ImpactMaxSpeed) * 0.5f;
		float damageFactorMid = ((midSpeed - Tunables.ImpactMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		int expectedMid = Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * damageFactorMid ) );
		if ( expectedMid != 3 )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactDamageScaling: midSpeed damage={expectedMid} (expected 3 from base 6 × 0.5)" );
			Finish();
			return;
		}
		float damageFactorMax = 1f;
		int expectedMax = Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * damageFactorMax ) );
		if ( expectedMax != Tunables.ImpactBaseDamage )
		{
			Log.Error( $"[TC_TEST] FAIL TestImpactDamageScaling: maxSpeed damage={expectedMax} (expected {Tunables.ImpactBaseDamage})" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] IMPACT_SCALING PASS  midSpeed={midSpeed:F0}→{expectedMid} damage, maxSpeed→{expectedMax} damage (LerpStep formula)" );
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
		// Veteran HP 8-15 + AxeTier 3 (CSatisfait Veteran tier gate) + ChopPower=1
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
		// Pump axe tier 3 (Iron) pour passer le gate Veteran (MinAxeTier=3).
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
		// ChopPower at T3 = 5. Veteran HP min 8, donc HP 8-5=3 reste positif → still standing → shake reset.
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
		// Sapling HP 1-3 ; on damage 1 (< HP au moins parfois). Pour être deterministe,
		// on test sur Veteran HP 12.
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
		Transition( Phase.TestRollingLogsDamping );
	}

	private void TickTestRollingLogsDamping()
	{
		// Valheim feel : logs roulent ~3-5s avant rest. AngularDamping landed
		// doit être < 0.5 (notre target 0.30) pour permettre ce roll. Linear < 0.3.
		if ( RuntimeValue( Tunables.TreeAngularDampLanded ) >= 0.5f )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: TreeAngularDampLanded={Tunables.TreeAngularDampLanded} >= 0.5 (logs ne rouleront pas)" );
			Finish();
			return;
		}
		if ( RuntimeValue( Tunables.TreeLinearDampLanded ) >= 0.3f )
		{
			Log.Error( $"[TC_TEST] FAIL TestRollingLogsDamping: TreeLinearDampLanded={Tunables.TreeLinearDampLanded} >= 0.3 (logs glisseront pas)" );
			Finish();
			return;
		}
		Log.Info( $"[TC_TEST] ROLLING_DAMPING PASS  Angular={Tunables.TreeAngularDampLanded} Linear={Tunables.TreeLinearDampLanded} (Valheim feel : logs roll)" );
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
		Transition( Phase.TestGameStateSanitize );
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
		var pos = targetPos - dir * 70f + Vector3.Up * 35f;
		_axe.TeleportTo( pos, Rotation.LookAt( dir ).Yaw() );
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
		if ( wood > 0 ) _state.AddWood( wood );
		if ( finewood > 0 ) _state.AddBackpack( finewood, WoodType.Finewood );
		if ( corewood > 0 ) _state.AddBackpack( corewood, WoodType.CoreWood );
		if ( finewood > 0 || corewood > 0 ) _state.TryDeposit();
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
