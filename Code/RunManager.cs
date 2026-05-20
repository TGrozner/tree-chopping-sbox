namespace TreeChopping;

public enum RunState
{
	// Player hasn't swung yet — single swing per run is the bowling premise.
	WaitingForSwing,
	// The swing fired, the chosen tree is going down, cascade is in motion.
	Cascading,
	// All physics settled — score locked in. "Reload" key starts a new run.
	Scored,
}

// One-shot bowling-with-trees run manager. The player gets ONE swing per run;
// they pick a tree, swing, then watch the cascade resolve and read their score.
// Pressing the Reload input ("R") starts a fresh run with a new procedural arena.
public sealed class RunManager : Component
{
	private const string BestScoreFile = "best_score.json";

	[Property, ReadOnly] public RunState State { get; private set; } = RunState.WaitingForSwing;
	[Property, ReadOnly] public int Score { get; private set; }
	[Property, ReadOnly] public int BestScore { get; private set; }
	[Property, ReadOnly] public RunModifier ActiveModifier { get; private set; } = RunModifier.Standard;
	[Property, ReadOnly] public float CascadeIdleSeconds { get; private set; }
	[Property, ReadOnly] public int InitialTreeCount { get; private set; }
	[Property, ReadOnly] public int LastMilestoneIndex { get; private set; } = -1;
	[Property, ReadOnly] public string LastMilestoneName { get; private set; }
	[Property, ReadOnly] public TimeSince MilestoneShownTime { get; private set; }
	[Property, ReadOnly] public bool HasSwungEver { get; private set; }
	[Property, ReadOnly] public int MythicsFelled { get; private set; }
	[Property, ReadOnly] public float LastCascadeDuration { get; private set; }
	// Best score AT the moment the run resolved — captured so the run-end HUD
	// can show "+N over previous best" without losing the delta after the
	// BestScore field has been bumped up.
	[Property, ReadOnly] public int BestScoreBeforeRun { get; private set; }
	// Public window into _stateEntered so HUDs can drive scored-screen animation
	// timing (fade-in, score reveal) without reaching for the private field.
	public TimeSince StateEntered => _stateEntered;

	private TimeSince _stateEntered;
	private TimeSince _lastMotionSeen;

	// Threshold below which a Tree's rigidbody is considered at rest. Squared
	// values so we compare against LengthSquared without a sqrt per tree per
	// frame — at ~100 trees this matters.
	private const float MotionLinearSqThreshold = 100f;    // 10 u/s — generous, so logs rolling slow on slope count as "settled"
	private const float MotionAngularSqThreshold = 1f;     // 1 rad/s
	// Hard cap : after this duration on Cascading, force Scored regardless of
	// motion. Otherwise rolling logs on the slope can keep the cascade alive
	// indefinitely, blocking the run from resolving.
	private const float MaxCascadeDuration = 9f;
	// Minimum cascade duration so a missed swing doesn't snap-end the run.
	private const float MinCascadeDuration = 1f;
	// Idle time required to declare the cascade settled. Tuned to absorb the
	// tail end of slow-spinning landed logs that haven't quite slept yet.
	private const float SettleIdleDuration = 1.5f;

	public static RunManager Get( Scene scene )
		=> scene?.GetAllComponents<RunManager>().FirstOrDefault();

	public bool CanSwing => State == RunState.WaitingForSwing;
	public bool CanRestart => State == RunState.Scored;

	protected override void OnStart()
	{
		LoadBestScore();
		CaptureInitialTreeCount();
		RollModifier();
	}

	private void LoadBestScore()
	{
		try
		{
			if ( FileSystem.Data.FileExists( BestScoreFile ) )
			{
				var loaded = FileSystem.Data.ReadJsonOrDefault<BestScoreEntry>( BestScoreFile, null );
				if ( loaded != null )
				{
					BestScore = loaded.Score;
					Log.Info( $"[Run] Loaded best score from disk: {BestScore} (set on {loaded.Date})" );
				}
			}
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Run] Failed to load best score: {ex.Message}" );
		}
	}

	private void SaveBestScore()
	{
		try
		{
			var entry = new BestScoreEntry { Score = BestScore, Date = DateTime.UtcNow.ToString( "yyyy-MM-dd HH:mm" ) };
			FileSystem.Data.WriteJson( BestScoreFile, entry );
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Run] Failed to save best score: {ex.Message}" );
		}
	}

	private class BestScoreEntry
	{
		public int Score { get; set; }
		public string Date { get; set; }
	}

	private void RollModifier()
	{
		// Equal weights across non-Standard for variety; Standard rolls ~20% as
		// a "control" run.
		var rng = new Random();
		int r = rng.Next( 100 );
		ActiveModifier = r < 20 ? RunModifier.Standard
			: r < 40 ? RunModifier.Explosive
			: r < 60 ? RunModifier.Frozen
			: r < 80 ? RunModifier.Heavy
			: RunModifier.ChainLightning;
		Log.Info( $"[Run] Modifier rolled: {ActiveModifier}" );
	}

	public void OnSwingFired()
	{
		if ( State != RunState.WaitingForSwing ) return;
		State = RunState.Cascading;
		_stateEntered = 0f;
		_lastMotionSeen = 0f;
		HasSwungEver = true;
		// Cascade trigger trauma burst — gros punch initial pour marquer "ça commence".
		ComboTracker.Get( Scene )?.AddTrauma( 0.45f );
		Log.Info( "[Run] Swing fired — cascade tracking begins" );
	}

	// Called by Tree.StartFell — accumulates the per-run score so we don't have
	// to scan the scene for fallen trees at resolution time. Cheap, accurate
	// (matches the player's mental model of "trees I knocked over"), and works
	// for both chopped trees and cascade-struck ones.
	//
	// Auto-engages cascade tracking if the swing-fired callback hasn't run yet:
	// Tree.StartFell fires from inside Chop(), which the controller calls just
	// BEFORE its OnSwingFired() call. Without this catch-up the very first
	// felled tree wouldn't count.
	public void OnMythicFell() { MythicsFelled++; }

	public void OnTreeFell()
	{
		if ( State == RunState.Scored ) return; // post-cascade physics noise — ignore
		if ( State == RunState.WaitingForSwing ) OnSwingFired();
		int delta = ActiveModifier == RunModifier.Heavy ? 3 : 1;
		Score += delta;

		// Milestone check — pop a banner + trauma pulse the moment we cross a threshold.
		int newIdx = LastMilestoneIndex;
		for ( int i = Tunables.ScoreMilestones.Length - 1; i > LastMilestoneIndex; i-- )
		{
			if ( Score >= Tunables.ScoreMilestones[i] )
			{
				newIdx = i;
				break;
			}
		}
		if ( newIdx > LastMilestoneIndex )
		{
			LastMilestoneIndex = newIdx;
			LastMilestoneName = Tunables.ScoreMilestoneNames[newIdx];
			MilestoneShownTime = 0f;
			// Trauma escalates with tier — last milestone is a screen-shaker.
			float trauma = 0.4f + newIdx * 0.12f;
			ComboTracker.Get( Scene )?.AddTrauma( trauma );
			// Milestone log moved to verbose-only — selftest harness was logging
			// 6 milestones × N runs × variants = noisy stdout. Visible side-effects
			// (banner, trauma, slowmo) prove it fired.

			// Milestone "ding" — reuse pickup_stone (short high-pitch click) au
			// scene center pour celebrer le palier franchi.
			var beaver = Scene.GetAllComponents<BeaverController>().FirstOrDefault();
			var dingPos = beaver.IsValid() ? beaver.WorldPosition : Vector3.Zero;
			AudioBank.PlayPickupStone( Scene, dingPos );

			// Milestone 1 and 2 ("Chain Reaction", "Lumberjack") trigger a brief slowmo
			// so the player can drink in the carnage. Reuses ComboTracker.SlowmoElapsed
			// since it already drives Scene.TimeScale interpolation.
			if ( newIdx >= 1 && newIdx <= 2 )
			{
				ComboTracker.Get( Scene )?.TriggerSlowmo();
			}
		}
	}

	protected override void OnUpdate()
	{
		switch ( State )
		{
			case RunState.Cascading:
				TickCascading();
				break;
			case RunState.Scored:
				TickScored();
				break;
		}
	}

	private void TickCascading()
	{
		bool anyMoving = false;
		foreach ( var t in Scene.GetAllComponents<Tree>() )
		{
			if ( !t.IsValid() ) continue;
			if ( t.Body is null || !t.Body.IsValid() ) continue;
			if ( !t.Body.MotionEnabled ) continue; // standing trees are kinematic — ignore
			if ( t.Body.Velocity.LengthSquared > MotionLinearSqThreshold
				|| t.Body.AngularVelocity.LengthSquared > MotionAngularSqThreshold )
			{
				anyMoving = true;
				break;
			}
		}

		if ( anyMoving ) _lastMotionSeen = 0f;
		CascadeIdleSeconds = (float)_lastMotionSeen;

		if ( (float)_stateEntered < MinCascadeDuration ) return;

		// Hard cap : pas de cascade > MaxCascadeDuration, peu importe les logs
		// qui roulent encore sur la slope. Sans ça un run reste bloqué et la
		// banner "Press R" n'apparaît jamais.
		bool hardCapHit = (float)_stateEntered > MaxCascadeDuration;
		if ( !hardCapHit && (float)_lastMotionSeen < SettleIdleDuration ) return;

		BestScoreBeforeRun = BestScore;
		if ( Score > BestScore )
		{
			BestScore = Score;
			SaveBestScore();
		}
		LastCascadeDuration = (float)_stateEntered;
		State = RunState.Scored;
		_stateEntered = 0f;
		// Audio finale — log-break joue comme un "results bell" au scoring lock-in.
		// Petit trauma final pour ponctuer la résolution.
		var sceneCenter = GetSceneCenter();
		AudioBank.PlayLogBreak( Scene, sceneCenter );
		ComboTracker.Get( Scene )?.AddTrauma( 0.20f );

		// Celebrate burst — 80 leaves/chip-confetti from cascade center, full 360°
		// spray, tinted by milestone-tier color (cyan/purple/gold scaled per score).
		// Donne le "fanfare" visuel au moment de la résolution avant que la cam
		// fasse son extra pull-back vers wide establishing shot (iter10).
		Color confettiTint;
		if ( Score >= Tunables.ScoreMilestones[5] ) confettiTint = Tunables.ScoreMilestoneColors[5];        // TIMBER SHOCK cyan
		else if ( Score >= Tunables.ScoreMilestones[4] ) confettiTint = Tunables.ScoreMilestoneColors[4];   // Forest Killer purple
		else if ( Score >= Tunables.ScoreMilestones[3] ) confettiTint = Tunables.ScoreMilestoneColors[3];   // Domino King hot pink
		else if ( Score >= Tunables.ScoreMilestones[2] ) confettiTint = Tunables.ScoreMilestoneColors[2];   // Lumberjack orange-red
		else if ( Score >= Tunables.ScoreMilestones[1] ) confettiTint = Tunables.ScoreMilestoneColors[1];   // Chain Reaction amber
		else if ( Score >= Tunables.ScoreMilestones[0] ) confettiTint = Tunables.ScoreMilestoneColors[0];   // Spark gold
		else confettiTint = new Color( 0.7f, 0.7f, 0.7f, 1f );                                              // low score : muted
		ChopParticles.Burst( Scene, sceneCenter + Vector3.Up * 120f, Vector3.Up, confettiTint, 80, 360f );
		ChopParticles.Burst( Scene, sceneCenter + Vector3.Up * 60f, Vector3.Forward, confettiTint, 40, 280f );
		// Mixed-palette accent bursts — biome canopy color + neutral white pour
		// "feu d'artifice multi-couleur" feel au scoring lock-in.
		ChopParticles.Burst( Scene, sceneCenter + Vector3.Up * 90f, Vector3.Right, Tunables.SpeciesCanopyTints[0], 30, 240f );
		ChopParticles.Burst( Scene, sceneCenter + Vector3.Up * 90f, -Vector3.Right, new Color( 1f, 1f, 1f, 1f ), 20, 220f );
		Log.Info( $"[Run] Cascade resolved {(hardCapHit ? "(HARD CAP)" : "(settled)")}, score={Score}, best={BestScore}" );
	}

	private Vector3 GetSceneCenter()
	{
		var sum = Vector3.Zero;
		int count = 0;
		foreach ( var t in Scene.GetAllComponents<Tree>() )
		{
			if ( !t.IsValid() ) continue;
			if ( t.Body is null || t.Body.MotionEnabled == false ) continue;
			sum += t.WorldPosition;
			count++;
		}
		if ( count > 0 ) return sum / count;
		var beaver = Scene.GetAllComponents<BeaverController>().FirstOrDefault();
		return beaver.IsValid() ? beaver.WorldPosition : Vector3.Zero;
	}

	private void TickScored()
	{
		if ( TitleScreen.ShouldBlockInput ) return;
		// PauseMenu gates other inputs but a "play again" should still respond
		// even if it overlaps a momentarily-paused state.
		if ( Input.Pressed( "Reload" ) )
		{
			Regenerate();
		}
	}

	// Public wrapper so SelfTest can drive the regenerate path headlessly
	// without exposing the private method to the rest of the codebase.
	public void RegenerateForTest() => Regenerate();

	private void Regenerate()
	{
		Log.Info( "[Run] Restart requested — regenerating arena" );

		// Tear down everything spawned by the previous run. Beaver / camera /
		// HUD / managers / ground all persist.
		DestroyAllOfType<Tree>();
		DestroyAllOfType<LogPiece>();
		DestroyAllOfType<WoodChunk>();
		DestroyAllOfType<Stump>();
		DestroyAllOfType<ChopChipLifetime>();

		// Reset beaver pose to SceneStarter.BeaverSpawn (uphill at -X). Le yaw
		// est remis à 0 → beaver regarde +X (downhill, vers la forêt qui descend).
		var starter = Scene.GetAllComponents<SceneStarter>().FirstOrDefault();
		var beaver = Scene.GetAllComponents<BeaverController>().FirstOrDefault();
		if ( beaver.IsValid() )
		{
			beaver.WorldPosition = starter.IsValid() ? starter.BeaverSpawn : new Vector3( -1500f, 0f, 380f );
			beaver.WorldRotation = Rotation.Identity;
			beaver.ResetLookForNewRun();
			var rb = beaver.Components.Get<Rigidbody>();
			if ( rb.IsValid() )
			{
				rb.Velocity = Vector3.Zero;
				rb.AngularVelocity = Vector3.Zero;
			}
		}

		// Per-run biome variety: cycle Forest → Autumn → Frost so each restart
		// reads as a fresh arena (different bank tint + trunk tints + species bias).
		BiomeManager.Get( Scene )?.AdvanceBiome();
		if ( starter.IsValid() ) starter.RegenerateForest();

		CaptureInitialTreeCount();
		Score = 0;
		LastMilestoneIndex = -1;
		LastMilestoneName = null;
		MythicsFelled = 0;
		LastCascadeDuration = 0f;
		State = RunState.WaitingForSwing;
		_stateEntered = 0f;
		_lastMotionSeen = 0f;
		RollModifier();
	}

	private void DestroyAllOfType<T>() where T : Component
	{
		// GameObject.Destroy() is queued — the object survives the current frame
		// and keeps ticking physics + collision events. Without disabling first,
		// in-flight falling trees from the previous run can CascadeStrike the
		// freshly-spawned standing trees of the new run, kicking the new
		// RunState back into Cascading before the player even sees it.
		// Setting Enabled=false stops Components / physics / collisions THIS
		// frame; Destroy() then cleans up next frame as usual.
		foreach ( var c in Scene.GetAllComponents<T>().ToList() )
		{
			if ( !c.IsValid() ) continue;
			var go = c.GameObject;
			if ( go.IsValid() )
			{
				go.Enabled = false;
				go.Destroy();
			}
		}
	}

	private void CaptureInitialTreeCount()
	{
		InitialTreeCount = Scene.GetAllComponents<Tree>().Count( t => t.IsValid() );
	}

	// Test-only : remet le RunManager dans son état initial sans toucher au
	// monde (pas de Regenerate, pas de Tree.Destroy). Utilisé par TestSuite
	// MilestoneTraversal pour pouvoir injecter une série d'OnTreeFell sans
	// dépendre du state setup au démarrage de scène.
	public void ResetForTest()
	{
		Score = 0;
		LastMilestoneIndex = -1;
		LastMilestoneName = null;
		MythicsFelled = 0;
		HasSwungEver = false;
		State = RunState.WaitingForSwing;
		_stateEntered = 0f;
		_lastMotionSeen = 0f;
	}

	// Test-only : force le modifier sans passer par RollModifier (qui est
	// aléatoire). Permet à TestSuite ModifierAll5Roll de vérifier les 5
	// valeurs d'enum de façon déterministe.
	public void SetModifierForTest( RunModifier m )
	{
		ActiveModifier = m;
	}

	// Test-only : round-trip BestScore par le disque. Snapshot la valeur
	// actuelle, force une écriture arbitraire, relit, asserte égalité, puis
	// restaure. Renvoie true si save→load preserve la valeur.
	public bool TestPersistenceRoundTrip()
	{
		int original = BestScore;
		const int TestValue = 4242;
		try
		{
			BestScore = TestValue;
			SaveBestScore();
			BestScore = 0; // wipe in-memory so LoadBestScore is source of truth
			LoadBestScore();
			bool ok = BestScore == TestValue;
			// Restore original to disk (idempotent across test runs).
			BestScore = original;
			SaveBestScore();
			return ok;
		}
		catch
		{
			BestScore = original;
			try { SaveBestScore(); } catch { }
			return false;
		}
	}
}
