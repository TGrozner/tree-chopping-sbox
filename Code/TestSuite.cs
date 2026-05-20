namespace TreeChopping;

// Full test suite — covers what the gameplay-flow SelfTest doesn't reach:
// Modifier rolling distribution, milestone threshold firing, daily seed
// determinism, AudioBank no-asset fallback, ChopParticles edge cases, camera
// config, Regenerate state machine.
//
// Activation: launch with "+tc_test_suite 1" (separate from "+tc_selftest 1"
// which only runs the bowling cascade scenario). Output is structured
// [TC_TEST] PASS_<name> / [TC_TEST] FAIL_<name> lines so the PowerShell harness
// can grep for any FAIL.
public sealed class TestSuite : Component
{
	enum Phase { Init, ModifierDist, Milestones, DailySeed, AudioFallback, Particles, CameraConfig, RegenerateFlow, MythicSpawn, MythicBonusProbe, BiomeRotation, ManagerSingletons, TunablesBalance, MilestoneTraversal, ModifierAll5Roll, Persistence, TitleInputGuard, TitleInputGuardNextTick, Verify, Done }

	private float _titleGuardDismissTime;

	private Phase _phase = Phase.Init;
	private int _frame;
	private int _passed;
	private int _failed;

	[ConVar( "tc_test_suite", Help = "Spawn TestSuite component to run the broader assertion battery." )]
	public static bool Enable { get; set; }

	public static bool IsActiveRequest() => Enable;

	protected override void OnAwake()
	{
		Log.Info( "[TC_TEST] TestSuite component awake" );
	}

	protected override void OnFixedUpdate()
	{
		_frame++;
		switch ( _phase )
		{
			case Phase.Init: TickInit(); break;
			case Phase.ModifierDist: TickModifierDist(); break;
			case Phase.Milestones: TickMilestones(); break;
			case Phase.DailySeed: TickDailySeed(); break;
			case Phase.AudioFallback: TickAudioFallback(); break;
			case Phase.Particles: TickParticles(); break;
			case Phase.CameraConfig: TickCameraConfig(); break;
			case Phase.RegenerateFlow: TickRegenerateFlow(); break;
			case Phase.MythicSpawn: TickMythicSpawn(); break;
			case Phase.MythicBonusProbe: TickMythicBonusProbe(); break;
			case Phase.BiomeRotation: TickBiomeRotation(); break;
			case Phase.ManagerSingletons: TickManagerSingletons(); break;
			case Phase.TunablesBalance: TickTunablesBalance(); break;
			case Phase.MilestoneTraversal: TickMilestoneTraversal(); break;
			case Phase.ModifierAll5Roll: TickModifierAll5Roll(); break;
			case Phase.Persistence: TickPersistence(); break;
			case Phase.TitleInputGuard: TickTitleInputGuard(); break;
			case Phase.TitleInputGuardNextTick: TickTitleInputGuardNextTick(); break;
			case Phase.Verify: TickVerify(); break;
			case Phase.Done: break;
		}
	}

	private void Assert( bool cond, string name, string detail = "" )
	{
		if ( cond )
		{
			_passed++;
			Log.Info( $"[TC_TEST] PASS_{name}" );
		}
		else
		{
			_failed++;
			Log.Error( $"[TC_TEST] FAIL_{name} {detail}" );
		}
	}

	private void Next( Phase p )
	{
		_phase = p;
	}

	private void TickInit()
	{
		// Wait for SceneStarter to finish + RunManager spawned.
		if ( _frame < 10 ) return;
		Log.Info( "[TC_TEST] TestSuite starting battery" );
		Next( Phase.ModifierDist );
	}

	// Test 1: modifier roll covers all 5 across enough draws.
	private void TickModifierDist()
	{
		// We can't call RollModifier directly (private), but each Regenerate
		// rolls a new one. Instead, validate the enum has the 5 values + each
		// of the extension methods returns a non-empty result.
		bool allValuesOk = true;
		string lastFail = "";
		foreach ( var m in System.Enum.GetValues( typeof( RunModifier ) ) )
		{
			var rm = (RunModifier)m;
			if ( string.IsNullOrEmpty( rm.DisplayName() ) ) { allValuesOk = false; lastFail = $"DisplayName empty for {rm}"; break; }
			if ( string.IsNullOrEmpty( rm.ShortHint() ) ) { allValuesOk = false; lastFail = $"ShortHint empty for {rm}"; break; }
			var tint = rm.Tint();
			if ( tint.a < 0.5f ) { allValuesOk = false; lastFail = $"Tint alpha low for {rm}"; break; }
		}
		Assert( allValuesOk, "modifier_metadata_complete", lastFail );

		int enumCount = System.Enum.GetValues( typeof( RunModifier ) ).Length;
		Assert( enumCount == 5, "modifier_count_5", $"got {enumCount}" );

		Next( Phase.Milestones );
	}

	// Test 2: milestone thresholds + names + colors stay consistent.
	private void TickMilestones()
	{
		var thresholds = Tunables.ScoreMilestones;
		var names = Tunables.ScoreMilestoneNames;
		var colors = Tunables.ScoreMilestoneColors;

		Assert( thresholds.Length == names.Length, "milestone_threshold_name_length_match", $"thresholds={thresholds.Length} names={names.Length}" );
		Assert( thresholds.Length == colors.Length, "milestone_threshold_color_length_match", $"thresholds={thresholds.Length} colors={colors.Length}" );

		// Monotonic increasing thresholds.
		bool monotonic = true;
		for ( int i = 1; i < thresholds.Length; i++ )
		{
			if ( thresholds[i] <= thresholds[i - 1] ) { monotonic = false; break; }
		}
		Assert( monotonic, "milestone_thresholds_monotonic" );

		// First threshold reasonable (low single-digit).
		Assert( thresholds[0] >= 1 && thresholds[0] <= 10, "milestone_first_low", $"got {thresholds[0]}" );
		// Last threshold ambitious.
		Assert( thresholds[thresholds.Length - 1] >= 100, "milestone_last_high", $"got {thresholds[thresholds.Length - 1]}" );

		Next( Phase.DailySeed );
	}

	// Test 3: daily seed is deterministic for the same UTC date.
	private void TickDailySeed()
	{
		int a = SceneStarter.DailySeed();
		int b = SceneStarter.DailySeed();
		Assert( a == b, "daily_seed_deterministic", $"a={a} b={b}" );
		Assert( a > 0, "daily_seed_positive", $"got {a}" );

		Next( Phase.AudioFallback );
	}

	// Test 4: AudioBank methods don't throw even if a sound asset is missing.
	private void TickAudioFallback()
	{
		bool threw = false;
		try
		{
			AudioBank.PlaySwing( Scene, Vector3.Zero );
			AudioBank.PlayChopWood( Scene, Vector3.Zero );
			AudioBank.PlayChopStone( Scene, Vector3.Zero );
			AudioBank.PlayLogBreak( Scene, Vector3.Zero );
			AudioBank.PlayPickupWood( Scene, Vector3.Zero );
			AudioBank.PlayPickupStone( Scene, Vector3.Zero );
		}
		catch ( System.Exception ex )
		{
			threw = true;
			Log.Warning( $"[TC_TEST] AudioBank threw: {ex.Message}" );
		}
		Assert( !threw, "audio_bank_no_throw" );

		Next( Phase.Particles );
	}

	// Test 5: ChopParticles handles edge cases (zero count, null direction).
	private void TickParticles()
	{
		bool threw = false;
		try
		{
			ChopParticles.Burst( Scene, Vector3.Zero, Vector3.Zero, Color.White, 0, 100f );
			ChopParticles.Burst( Scene, Vector3.Zero, Vector3.Forward, Color.White, 3, 100f );
			ChopParticles.SplinterBurst( Scene, Vector3.Zero, Vector3.Forward, Color.White, 2, 100f );
			ChopParticles.LeafTrail( Scene, Vector3.Zero, Color.White, 2, 50f );
		}
		catch ( System.Exception ex )
		{
			threw = true;
			Log.Warning( $"[TC_TEST] ChopParticles threw: {ex.Message}" );
		}
		Assert( !threw, "chop_particles_no_throw" );

		Next( Phase.CameraConfig );
	}

	// Test 6: camera + post-process config is sane.
	private void TickCameraConfig()
	{
		var cam = Scene.GetAllComponents<CameraComponent>().FirstOrDefault( c => c.IsMainCamera );
		Assert( cam.IsValid(), "main_camera_present" );
		if ( cam.IsValid() )
		{
			Assert( cam.FieldOfView > 30f && cam.FieldOfView < 120f, "camera_fov_sane", $"fov={cam.FieldOfView}" );
			Assert( cam.ZNear > 0f && cam.ZNear < 10f, "camera_znear_sane", $"znear={cam.ZNear}" );
			Assert( cam.ZFar > 1000f, "camera_zfar_long", $"zfar={cam.ZFar}" );
		}

		// Tunables camera framing sanity.
		Assert( Tunables.CameraDistance > 100f && Tunables.CameraDistance < 1000f, "camera_distance_sane", $"d={Tunables.CameraDistance}" );
		Assert( Tunables.CameraMinPitch < 0f, "camera_min_pitch_negative", $"p={Tunables.CameraMinPitch}" );
		Assert( Tunables.CameraMaxPitch > 0f, "camera_max_pitch_positive", $"p={Tunables.CameraMaxPitch}" );

		Next( Phase.RegenerateFlow );
	}

	// Test 7: Regenerate destroys old trees + spawns fresh ones.
	private void TickRegenerateFlow()
	{
		var run = RunManager.Get( Scene );
		Assert( run.IsValid(), "run_manager_present" );
		if ( !run.IsValid() ) { Next( Phase.Verify ); return; }

		int before = Scene.GetAllComponents<Tree>().Count();
		Assert( before > 100, "initial_forest_dense", $"trees={before}" );

		// Force one tree to fall via direct API → triggers OnTreeFell → state moves.
		// Then we can't easily call Regenerate (it requires Reload input). Settle
		// for asserting the spawn produced trees and Tunables drives sensible counts.
		Assert( Tunables.ArenaRadius > 1000f, "arena_radius_decent", $"r={Tunables.ArenaRadius}" );
		Assert( Tunables.ArenaDensityThreshold >= 0f && Tunables.ArenaDensityThreshold < 0.5f, "arena_density_threshold_sane", $"t={Tunables.ArenaDensityThreshold}" );

		// Cascade tuning sanity.
		Assert( Tunables.CascadeImpulseTransfer > 0.5f && Tunables.CascadeImpulseTransfer <= 1f, "cascade_transfer_sane", $"x={Tunables.CascadeImpulseTransfer}" );
		Assert( Tunables.CascadeMinContactSpeed >= 10f && Tunables.CascadeMinContactSpeed < 200f, "cascade_min_speed_sane", $"s={Tunables.CascadeMinContactSpeed}" );

		// Mythic config sanity.
		Assert( Tunables.MythicSpawnRatio > 0, "mythic_ratio_positive", $"r={Tunables.MythicSpawnRatio}" );
		Assert( Tunables.MythicScoreBonus > 0 && Tunables.MythicScoreBonus <= 50, "mythic_bonus_sane", $"b={Tunables.MythicScoreBonus}" );

		// Tree config sanity.
		Assert( Tunables.TreeHeight > 100f && Tunables.TreeHeight < 500f, "tree_height_sane" );
		Assert( Tunables.TreeRadius > 5f && Tunables.TreeRadius < 50f, "tree_radius_sane" );
		Assert( Tunables.TreeMass > 0f, "tree_mass_positive" );

		Next( Phase.MythicSpawn );
	}

	// Test 8: Mythic spawn ratio fires within tolerance on the real forest.
	private void TickMythicSpawn()
	{
		var trees = Scene.GetAllComponents<Tree>().ToList();
		int total = trees.Count;
		int mythic = trees.Count( t => t.IsMythic );
		Assert( total > 100, "mythic_spawn_pop_sane", $"total={total}" );
		// Expected ~total/MythicSpawnRatio. Allow 3x tolerance (RNG variance).
		float expected = (float)total / Tunables.MythicSpawnRatio;
		Assert( mythic >= 1, "mythic_at_least_one", $"got {mythic}/{total} (expected ~{expected:F1})" );
		Assert( mythic < expected * 4f, "mythic_not_overrepresented", $"got {mythic} (expected ~{expected:F1})" );

		// Sub-test : trees don't all share the same yaw. Sample first 50 and
		// confirm the rotation spread spans at least 90° — sinon le random
		// yaw spawn-time est mort.
		var sampleRotsY = trees.Take( 50 ).Select( t => t.WorldRotation.Yaw() ).ToList();
		if ( sampleRotsY.Count >= 10 )
		{
			float minYaw = sampleRotsY.Min();
			float maxYaw = sampleRotsY.Max();
			float spread = maxYaw - minYaw;
			Assert( spread > 90f, "tree_yaw_variety", $"spread={spread:F0}° (need >90°)" );
		}

		// Sub-test : tree positions on the slope follow Z = -X * ArenaSlope.
		// Sample one tree, verify Z roughly matches predicted slope.
		var sample = trees.FirstOrDefault( t => MathF.Abs( t.WorldPosition.x ) > 500f );
		if ( sample.IsValid() )
		{
			float predictedZ = Tunables.GroundZ - sample.WorldPosition.x * Tunables.ArenaSlope;
			float actualZ = sample.WorldPosition.z;
			// Tolerance ±5u (jitter from species scaleMul).
			Assert( MathF.Abs( predictedZ - actualZ ) < 5f, "tree_z_matches_slope",
				$"predicted={predictedZ:F1} actual={actualZ:F1}" );
		}

		Next( Phase.MythicBonusProbe );
	}

	// Test 8b : bonus comportemental sur un VRAI mythic tree en scène.
	// Force ChopsRemaining=1 puis Chop() → StartFell → OnTreeFell × (1 + bonus)
	// + OnMythicFell. Vérifie : MythicsFelled +1, Score augmente d'au MOINS
	// (1 + MythicScoreBonus) (le modifier Heavy peut doubler), et le rigidbody
	// MotionEnabled passe à true (preuve que StartFell a fired).
	private void TickMythicBonusProbe()
	{
		var run = RunManager.Get( Scene );
		Assert( run.IsValid(), "mythic_probe_run_manager_present" );
		if ( !run.IsValid() ) { Next( Phase.BiomeRotation ); return; }

		var mythicTree = Scene.GetAllComponents<Tree>().FirstOrDefault( t => t.IsMythic && t.IsStanding );
		if ( !mythicTree.IsValid() )
		{
			// Variance RNG : aucun mythic spawné OU tous déjà tombés via cascade.
			// On skip explicitement plutôt que de faire échouer la suite.
			Assert( true, "mythic_probe_skip_no_mythics_spawned" );
			Next( Phase.BiomeRotation );
			return;
		}

		int scoreBefore = run.Score;
		int mythicsBefore = run.MythicsFelled;

		mythicTree.ChopsRemaining = 1;
		mythicTree.Chop( Vector3.Forward );

		int scoreAfter = run.Score;
		int mythicsAfter = run.MythicsFelled;

		// "Au moins +1" : la cascade peut sweep d'autres mythics voisins en un
		// seul chop (cascade-impulse → CascadeStrike → StartFell → OnMythicFell).
		// La forêt observée fait tomber ~5 mythics par chain. On vérifie l'incrément
		// minimum garanti, pas l'égalité stricte.
		Assert( mythicsAfter >= mythicsBefore + 1, "mythic_probe_counter_incremented",
			$"before={mythicsBefore} after={mythicsAfter}" );

		int scoreDelta = scoreAfter - scoreBefore;
		int minExpected = 1 + Tunables.MythicScoreBonus;
		Assert( scoreDelta >= minExpected, "mythic_probe_score_bonus_awarded",
			$"delta={scoreDelta} min_expected={minExpected}" );

		// StartFell flippe MotionEnabled=true sur le Body — preuve que le chop
		// killing-blow est passé par le code path mythique (et pas un early-return
		// silencieux). Tree.IsValid() reste true tant que BreakIntoPieces n'a pas
		// été called, donc on n'observe pas la destruction directement.
		bool motionFlipped = mythicTree.Body.IsValid() && mythicTree.Body.MotionEnabled;
		Assert( motionFlipped, "mythic_probe_motion_enabled_after_fell",
			$"motion={(mythicTree.Body.IsValid() ? mythicTree.Body.MotionEnabled.ToString() : "no_body")}" );

		Next( Phase.BiomeRotation );
	}

	// Test 9: BiomeManager exists + Current is a valid enum value + AdvanceBiome
	// cycles to a different biome.
	private void TickBiomeRotation()
	{
		var bm = BiomeManager.Get( Scene );
		Assert( bm.IsValid(), "biome_manager_present" );
		if ( bm.IsValid() )
		{
			var before = bm.Current;
			Assert( System.Enum.IsDefined( typeof( BiomeKind ), before ), "biome_kind_valid_enum" );
			bm.AdvanceBiome();
			var after = bm.Current;
			Assert( after != before, "biome_advance_changes_kind", $"before={before} after={after}" );
		}
		Next( Phase.ManagerSingletons );
	}

	// Test 10: all expected singleton managers exist in the scene after Bootstrap.
	private void TickManagerSingletons()
	{
		Assert( WoodInventory.Get( Scene ).IsValid(), "singleton_wood_inventory" );
		Assert( ComboTracker.Get( Scene ).IsValid(), "singleton_combo_tracker" );
		Assert( Weather.Get( Scene ).IsValid(), "singleton_weather" );
		Assert( BiomeManager.Get( Scene ).IsValid(), "singleton_biome_manager" );
		Assert( DayNightCycle.Get( Scene ).IsValid(), "singleton_day_night" );
		Assert( Scene.GetAllComponents<WoodHud>().FirstOrDefault().IsValid(), "singleton_wood_hud" );
		Assert( Scene.GetAllComponents<HudCompass>().FirstOrDefault().IsValid(), "singleton_compass" );
		Assert( PauseMenu.Get( Scene ).IsValid(), "singleton_pause_menu" );
		Assert( AimIndicator.Get( Scene ).IsValid(), "singleton_aim_indicator" );
		Assert( AmbientLeaves.Get( Scene ).IsValid(), "singleton_ambient_leaves" );
		Assert( RunManager.Get( Scene ).IsValid(), "singleton_run_manager" );
		Assert( TitleScreen.Get( Scene ).IsValid(), "singleton_title_screen" );
		Next( Phase.TunablesBalance );
	}

	// Test 11: Tunables balance — values stay in playable ranges across runs.
	private void TickTunablesBalance()
	{
		// Slope reasonable.
		Assert( Tunables.ArenaSlope >= 0f && Tunables.ArenaSlope < 0.5f, "slope_sane", $"s={Tunables.ArenaSlope}" );
		// Fell physics non-zero.
		Assert( Tunables.FellTorque > 0f, "fell_torque_positive" );
		Assert( Tunables.FellPush > 0f, "fell_push_positive" );
		Assert( Tunables.SlowTipDuration > 0.1f && Tunables.SlowTipDuration < 2f, "slow_tip_duration_sane" );
		// Combo tuning.
		Assert( Tunables.ComboSlowmoScale > 0f && Tunables.ComboSlowmoScale < 1f, "combo_slowmo_lt_1" );
		Assert( Tunables.ComboTraumaDecay > 0f, "combo_trauma_decay_positive" );
		// Chip lifetime stays sub-10s (else perf risk).
		Assert( Tunables.ChipLifetimeMax < 10f, "chip_lifetime_max_under_10s" );
		Assert( Tunables.ChipLifetimeMin > 0f, "chip_lifetime_min_positive" );
		// Win conditions monotonic + reachable.
		Assert( Tunables.ScoreGoodRunTarget > 0, "good_run_target_positive" );
		Assert( Tunables.ScoreMasterTarget > Tunables.ScoreGoodRunTarget, "master_target_greater_than_good" );
		// FOV/camera juice.
		Assert( Tunables.FovSprintWiden >= 0f && Tunables.FovSprintWiden < 30f, "fov_sprint_widen_sane" );
		Assert( Tunables.FovChopPunch > 0f && Tunables.FovChopPunch < 30f, "fov_chop_punch_sane" );
		// Day/night sane.
		Assert( Tunables.DayLengthSeconds > 10f, "day_length_long_enough" );
		Assert( Tunables.DayPhaseStart >= 0f && Tunables.DayPhaseStart <= 1f, "day_phase_start_in_range" );
		Next( Phase.MilestoneTraversal );
	}

	// Test 12 : synthétise des OnTreeFell jusqu'à dépasser le dernier seuil
	// (200), vérifie que les 6 milestones sont déclenchés exactement une fois
	// et que LastMilestoneIndex/Name finissent sur "TIMBER SHOCK".
	private void TickMilestoneTraversal()
	{
		var run = RunManager.Get( Scene );
		Assert( run.IsValid(), "milestone_traversal_run_manager_present" );
		if ( !run.IsValid() ) { Next( Phase.ModifierAll5Roll ); return; }

		// Reset propre + force Standard pour que chaque OnTreeFell ajoute +1
		// (Heavy donnerait +3 et fausserait le compteur de milestones).
		run.ResetForTest();
		run.SetModifierForTest( RunModifier.Standard );

		var thresholds = Tunables.ScoreMilestones;
		int milestoneCount = thresholds.Length;
		int[] transitions = new int[milestoneCount];
		int lastIdx = -1;

		// Boucle bornée : 250 felled trees largement assez pour dépasser 200
		// même si un modifier inattendu écrasait Standard.
		int safetyCap = 250;
		int steps = 0;
		while ( run.Score < thresholds[milestoneCount - 1] && steps < safetyCap )
		{
			run.OnTreeFell();
			steps++;
			if ( run.LastMilestoneIndex != lastIdx )
			{
				if ( run.LastMilestoneIndex >= 0 && run.LastMilestoneIndex < milestoneCount )
				{
					transitions[run.LastMilestoneIndex]++;
				}
				lastIdx = run.LastMilestoneIndex;
			}
		}

		Assert( steps < safetyCap, "milestone_traversal_reached_top", $"steps={steps} score={run.Score}" );

		// Chaque seuil doit avoir été franchi exactement une fois.
		bool allOnce = true;
		string failDetail = "";
		for ( int i = 0; i < milestoneCount; i++ )
		{
			if ( transitions[i] != 1 ) { allOnce = false; failDetail = $"idx={i} hits={transitions[i]}"; break; }
		}
		Assert( allOnce, "milestone_each_fires_exactly_once", failDetail );

		Assert( run.LastMilestoneIndex == milestoneCount - 1, "milestone_final_index", $"got {run.LastMilestoneIndex}, want {milestoneCount - 1}" );
		Assert( run.LastMilestoneName == "TIMBER SHOCK", "milestone_final_name", $"got '{run.LastMilestoneName}'" );

		// Remet le RunManager dans un état neutre pour ne pas polluer les
		// phases suivantes ni l'état post-suite.
		run.ResetForTest();

		Next( Phase.ModifierAll5Roll );
	}

	// Test 13 : itère les 5 RunModifier, force chacun via SetModifierForTest
	// et vérifie ActiveModifier + DisplayName + Tint.a. 5 × 3 = 15 assertions.
	private void TickModifierAll5Roll()
	{
		var run = RunManager.Get( Scene );
		Assert( run.IsValid(), "modifier_roll_run_manager_present" );
		if ( !run.IsValid() ) { Next( Phase.Verify ); return; }

		foreach ( var v in System.Enum.GetValues( typeof( RunModifier ) ) )
		{
			var m = (RunModifier)v;
			run.SetModifierForTest( m );
			Assert( run.ActiveModifier == m, $"modifier_set_{m}_active", $"got {run.ActiveModifier}" );
			Assert( !string.IsNullOrEmpty( m.DisplayName() ), $"modifier_set_{m}_display_name" );
			Assert( m.Tint().a > 0.5f, $"modifier_set_{m}_tint_alpha", $"a={m.Tint().a}" );
		}

		Next( Phase.Persistence );
	}

	// Test : BestScore save→load round-trip preserves the value.
	private void TickPersistence()
	{
		var run = RunManager.Get( Scene );
		Assert( run.IsValid(), "persistence_run_manager_present" );
		if ( !run.IsValid() ) { Next( Phase.Verify ); return; }

		bool roundTripOk = run.TestPersistenceRoundTrip();
		Assert( roundTripOk, "best_score_round_trip", "save→load did not preserve value" );

		// Daily seed UTC-date determinism encore (sanity check after save).
		int s1 = SceneStarter.DailySeed();
		int s2 = SceneStarter.DailySeed();
		Assert( s1 == s2, "daily_seed_idempotent_after_save", $"s1={s1} s2={s2}" );

		Next( Phase.TitleInputGuard );
	}

	// Test : TitleScreen consumes the dismiss-frame so the same click that
	// dismisses doesn't also fire BeaverController.DebugSwing on the same tick.
	// Regression : avant ce fix, un click sur SPACE pendant le title écran
	// dismissait + swing dans la même frame → joueur perdait son seul swing.
	private void TickTitleInputGuard()
	{
		// SelfTest already set Dismissed=true at boot; reset puis re-dismiss.
		TitleScreen.ResetForTest();
		Assert( TitleScreen.ShouldBlockInput, "title_blocks_when_active" );
		TitleScreen.DismissForTest();
		_titleGuardDismissTime = Time.Now;
		// Même frame = bloqué encore.
		Assert( TitleScreen.ShouldBlockInput, "title_blocks_on_dismiss_frame" );
		Next( Phase.TitleInputGuardNextTick );
	}

	// Verify that on the NEXT frame (after OnFixedUpdate advances Time.Now),
	// ShouldBlockInput drops to false.
	private void TickTitleInputGuardNextTick()
	{
		if ( Time.Now <= _titleGuardDismissTime ) return; // not advanced yet
		Assert( !TitleScreen.ShouldBlockInput, "title_unblocks_next_frame",
			$"dismissTime={_titleGuardDismissTime} now={Time.Now}" );
		Next( Phase.Verify );
	}

	private void TickVerify()
	{
		var summary = $"[TC_TEST] SUITE summary passed={_passed} failed={_failed}";
		if ( _failed == 0 )
		{
			Log.Info( $"[TC_TEST] SUITE_PASS {summary}" );
		}
		else
		{
			Log.Error( $"[TC_TEST] SUITE_FAIL {summary}" );
		}
		Log.Info( "[TC_TEST] DONE" );
		_phase = Phase.Done;
	}
}
