namespace TreeChopping;

public sealed class Tree : Component, IChoppable, Component.ICollisionListener
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public Color TrunkTint { get; set; } = new( 0.46f, 0.32f, 0.22f, 1f );
	[Property] public int ChopsRemaining { get; set; } = 3;
	[Property] public TreeSpecies Species { get; set; } = TreeSpecies.Beech;

	private bool _chopped;
	private bool _landed;
	private bool _broken;
	private float _slowTipElapsed;
	private Vector3 _fellDir;
	private TimeSince _timeSinceLanded;
	private Vector3 _originalFoot;

	// Captured at OnStart so wind sway is RELATIVE to the planted pose, not compounding.
	private Rotation _baseRotation;
	// Per-tree phase offset so the forest doesn't sway in lockstep — derived from the planted
	// position so it stays stable across hotload / save-load (no random per-instance state).
	private float _windPhaseSeed;

	protected override void OnStart()
	{
		_originalFoot = WorldPosition - Vector3.Up * (Tunables.TreeHeight * 0.5f);
		_baseRotation = WorldRotation;
		// Hash XY into a phase in [0, 2π). The Godot shader used `wp.x*0.5 + wp.z*0.4`
		// directly as a phase; we do the equivalent here with the planted footprint.
		var p = _originalFoot;
		_windPhaseSeed = (p.x * 0.013f + p.y * 0.017f) % MathF.Tau;
	}

	// Legacy 3-arg path — keeps SceneStarter.SpawnTree (and any other existing
	// caller) compiling without modification. Defaults to Beech so the
	// "biome-untyped" spawn still produces a normal-looking starter tree.
	public static Tree SpawnAt( Scene scene, Vector3 footPosition, Color tint )
		=> SpawnAt( scene, footPosition, tint, TreeSpecies.Beech );

	public static Tree SpawnAt( Scene scene, Vector3 footPosition, Color tint, TreeSpecies species )
	{
		// Species-specific look wins over the biome tint argument — both are
		// passed so callers that want to keep the biome cue can still do so by
		// reading TrunkTint downstream; the trunk model itself uses the
		// species tint because it's the more visible per-tree cue.
		var speciesIdx = (int)species;
		var speciesTint = Tunables.SpeciesTrunkTints[speciesIdx];
		var scaleMul = Tunables.SpeciesScaleMul[speciesIdx];
		var chops = Tunables.SpeciesChopsRequired[speciesIdx];

		var go = scene.CreateObject();
		go.Name = $"Tree.{species}";
		go.WorldPosition = footPosition + Vector3.Up * (Tunables.TreeHeight * 0.5f * scaleMul);
		go.Tags.Add( "tree" );
		// Kenney .vmdl already encodes correct intrinsic geometry, so we drop
		// the legacy cube-scaling (was: TreeRadius*2,...,TreeHeight / CubeBase).
		// Per-species `scaleMul` from Tunables still applies so a forest of
		// mixed species reads as a forest. Starting at Vector3.One — if the
		// imported .vmdl reads too small in editor, scale up by ~UnitsPerMeter/4
		// (Godot proto used scale 5.5-6.6m to yield ~9m canopies, ≈ 350u; our
		// 280u envelope wants a similar factor in inches).
		go.WorldScale = Vector3.One * scaleMul;

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Models.TreeFor( species );
		// Species tint multiplies with the Kenney baked vertex colours so the
		// canopy/trunk shading reads species-coded but never goes flat.
		mr.Tint = speciesTint;

		// Physics envelope stays at the legacy box — Tree.cs game-feel (chop
		// cone, cascade impulse, fell torque) was tuned against this size, so
		// the visual swap doesn't disturb collision feel.
		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.TreeMass;
		rb.AngularDamping = 1.2f;
		rb.LinearDamping = 0.3f;
		rb.StartAsleep = true;

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
		tree.TrunkTint = speciesTint;
		tree.Species = species;
		tree.ChopsRemaining = chops;
		return tree;
	}

	public bool IsFalling => _chopped && !_landed && !_broken;
	public bool IsStanding => !_chopped && !_broken;

	// A landed log is still a valid Chop target — Chop() routes to HandleLogHit
	// and shatters the trunk into LogPieces after Tunables.LogBreakHits more
	// strikes. Only "broken" (BreakIntoPieces called, GameObject destroyed) and
	// a still-falling trunk are excluded — both because there's no useful action
	// for the player. (Falling trees keep ticking their fell physics and only
	// becomes a log target once landed.)
	bool IChoppable.IsValid() => !_broken && !IsFalling && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Axe;

	public void Chop( Vector3 direction )
	{
		if ( _broken ) return;

		if ( _landed )
		{
			HandleLogHit( direction );
			return;
		}

		ChopsRemaining--;
		if ( ChopsRemaining > 0 )
		{
			GiveFeedbackJiggle( direction );
			return;
		}

		StartFell( direction );
	}

	private void GiveFeedbackJiggle( Vector3 direction )
	{
		if ( !Body.IsValid() ) return;
		var impulsePos = WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.55f);
		Body.ApplyImpulseAt( impulsePos, -direction.WithZ( 0f ).Normal * 60f );

		// Chip burst at axe-strike height on the side that got hit.
		var hitDir = direction.WithZ( 0f );
		hitDir = hitDir.LengthSquared > 0.0001f ? hitDir.Normal : Vector3.Forward;
		var chipPos = WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.15f) + hitDir * Tunables.TreeRadius;
		ChopParticles.Burst( Scene, chipPos, hitDir, TrunkTint, Tunables.ChipBurstCountWood, Tunables.ChipSpeedWood );
		AudioBank.PlayChopWood( Scene, chipPos );
	}

	private void StartFell( Vector3 direction )
	{
		_chopped = true;
		_fellDir = direction.WithZ( 0f ).Normal;
		if ( _fellDir.LengthSquared < 0.001f ) _fellDir = Vector3.Forward;
		_slowTipElapsed = 0f;

		if ( Body.IsValid() )
		{
			Body.MotionEnabled = true;
			Body.LinearDamping = 0f;
			Body.AngularDamping = 0.3f;
		}
	}

	void Component.ICollisionListener.OnCollisionStart( Collision col )
	{
		// Shatter: a landed log hammered at high speed bypasses its chop count.
		if ( _landed && !_broken )
		{
			TryShatter( col );
			return;
		}

		// Cascade: a falling tree slams into a neighbor → propagate fell.
		if ( !IsFalling ) return;
		if ( !Body.IsValid() ) return;
		var speed = Body.Velocity.Length;
		if ( speed < Tunables.CascadeMinContactSpeed ) return;

		var otherGo = col.Other.GameObject;
		if ( !otherGo.IsValid() || otherGo == GameObject ) return;
		var otherTree = otherGo.Components.Get<Tree>();
		if ( !otherTree.IsValid() || !otherTree.IsStanding ) return;

		var contactWorld = col.Contact.Point;
		var arm = contactWorld - Body.WorldPosition;
		var vAtContact = Body.Velocity + Body.AngularVelocity.Cross( arm );
		var impulse = vAtContact * Body.PhysicsBody.Mass * Tunables.CascadeImpulseTransfer;

		var fellDir = (otherTree.WorldPosition - WorldPosition).WithZ( 0f ).Normal;
		if ( fellDir.LengthSquared < 0.001f ) fellDir = _fellDir;

		otherTree.CascadeStrike( fellDir, contactWorld, impulse );
	}

	private void TryShatter( Collision col )
	{
		var otherBody = col.Other.Body;
		if ( !otherBody.IsValid() ) return;
		var incomingSpeed = otherBody.Velocity.Length;
		if ( incomingSpeed < Tunables.ShatterIncomingSpeed ) return;

		var dir = otherBody.Velocity.WithZ( 0f ).Normal;
		if ( dir.LengthSquared < 0.001f ) dir = Vector3.Forward;
		BreakIntoPieces( dir );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision col ) { }
	void Component.ICollisionListener.OnCollisionStop( CollisionStop col ) { }

	public void CascadeStrike( Vector3 dir, Vector3 contactWorld, Vector3 impulse )
	{
		if ( _broken || _chopped ) return;
		StartFell( dir );
		if ( Body.IsValid() )
		{
			Body.ApplyImpulseAt( contactWorld, impulse );
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( _chopped && !_landed )
		{
			TickFall();
		}
		else if ( _landed )
		{
			TickLandedDecay();
		}
		else if ( !_broken )
		{
			// Only sway while fully standing — fall physics owns rotation once chopped.
			TickWindSway();
		}
	}

	private void TickWindSway()
	{
		// Per-tree constants (Tunables.cs is owned by a parallel agent — keep them local).
		const float WindAmplitudeDeg = 1.5f;   // ±1.5° tilt — reads as a breeze, not a tipover.
		const float WindFreqHz = 0.8f;         // base sway, ~slow human-breath rhythm.
		const float WindFreq2Hz = 1.7f;        // organic second harmonic so it doesn't feel sinusoidal.
		const float WindFreq2Mul = 0.45f;      // small contribution from the harmonic.
		const float WindGustHz = 0.13f;        // very slow envelope → wind arrives in gusts.

		var t = Time.Now;
		var phase = _windPhaseSeed;

		// Slow gust envelope keeps amplitude in [0.5, 1.0] so the canopy "breathes".
		var gust = 0.75f + 0.25f * MathF.Sin( MathF.Tau * WindGustHz * t + phase * 0.5f );

		// Two-axis tilt (around local X and Y) gives an elliptical sway, like real wind.
		var s1 = MathF.Sin( MathF.Tau * WindFreqHz * t + phase );
		var s2 = MathF.Sin( MathF.Tau * WindFreq2Hz * t + phase * 1.7f );
		var tiltX = (s1 + s2 * WindFreq2Mul) * WindAmplitudeDeg * gust;

		var c1 = MathF.Cos( MathF.Tau * WindFreqHz * t + phase * 1.3f );
		var c2 = MathF.Cos( MathF.Tau * WindFreq2Hz * t + phase * 0.9f );
		var tiltY = (c1 + c2 * WindFreq2Mul) * WindAmplitudeDeg * gust;

		// Apply directly — overwriting WorldRotation each tick is fine because we
		// always rebuild from the stable _baseRotation, never from the previous frame.
		WorldRotation = _baseRotation
			* Rotation.FromAxis( Vector3.Right, tiltX )
			* Rotation.FromAxis( Vector3.Forward, tiltY );
	}

	private void TickFall()
	{
		if ( !Body.IsValid() ) return;

		_slowTipElapsed += Time.Delta;
		var t = (_slowTipElapsed / Tunables.SlowTipDuration).Clamp( 0f, 1f );
		var frac = MathX.Lerp( Tunables.SlowTipInitialFrac, Tunables.SlowTipRampFrac, t );
		var torqueAxis = Vector3.Up.Cross( _fellDir );
		Body.ApplyTorque( torqueAxis * Tunables.FellTorque * frac * Time.Delta );

		if ( _slowTipElapsed < 0.1f )
		{
			Body.ApplyImpulseAt(
				WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.85f),
				_fellDir * Tunables.FellPush
			);
		}

		var upDot = WorldRotation.Up.Dot( Vector3.Up );
		if ( upDot < Tunables.TreeFallenUpDotMax )
		{
			BecomeLandedLog();
		}
	}

	private void BecomeLandedLog()
	{
		_landed = true;
		_timeSinceLanded = 0f;
		ChopsRemaining = (int)Tunables.LogBreakHits;
		Tags.Add( "log" );

		if ( Body.IsValid() )
		{
			Body.AngularDamping = Tunables.TreeAngularDampLanded;
			Body.LinearDamping = Tunables.TreeLinearDampLanded;
		}
	}

	private void TickLandedDecay()
	{
		if ( !Body.IsValid() ) return;
		if ( _timeSinceLanded < 0.6f ) return;
		Body.Sleeping = Body.Velocity.LengthSquared < 4f && Body.AngularVelocity.LengthSquared < 0.5f;
	}

	private void HandleLogHit( Vector3 direction )
	{
		ChopsRemaining--;
		var hitPoint = WorldPosition + Vector3.Up * 20f;
		var dirFlat = direction.WithZ( 0f );
		dirFlat = dirFlat.LengthSquared > 0.0001f ? dirFlat.Normal : Vector3.Forward;
		// Heavier burst on the killing blow so the break-up reads.
		var count = ChopsRemaining > 0 ? Tunables.ChipBurstCountWood : Tunables.ChipBurstCountWoodHeavy;
		var speed = ChopsRemaining > 0 ? Tunables.ChipSpeedWood : Tunables.ChipSpeedWoodHeavy;
		ChopParticles.Burst( Scene, hitPoint, dirFlat, TrunkTint, count, speed );
		AudioBank.PlayChopWood( Scene, hitPoint );

		if ( ChopsRemaining > 0 )
		{
			if ( Body.IsValid() )
			{
				Body.ApplyImpulseAt( hitPoint, direction.WithZ( 0.2f ).Normal * 40f );
			}
			return;
		}

		BreakIntoPieces( direction );
	}

	private void BreakIntoPieces( Vector3 direction )
	{
		_broken = true;
		AudioBank.PlayLogBreak( Scene, WorldPosition );
		BiomeManager.Get( Scene )?.NotifyTreeCleared();
		var forward = WorldRotation.Forward;
		var origin = WorldPosition + WorldRotation.Up * (Tunables.TreeHeight * 0.5f);

		var halfLen = Tunables.TreeHeight * 0.5f;
		var spacing = Tunables.LogPieceHeight * 0.55f;
		var n = MathX.CeilToInt( halfLen * 2f / spacing );

		for ( int i = 0; i < n; i++ )
		{
			var t = (i + 0.5f) / n;
			var localOffset = WorldRotation.Up * (halfLen * 2f * (t - 0.5f));
			SpawnLogPiece( WorldPosition + localOffset, WorldRotation, direction );
		}

		Stump.SpawnAt( Scene, _originalFoot, TrunkTint, Species );
		GameObject.Destroy();
	}

	private void SpawnLogPiece( Vector3 pos, Rotation rot, Vector3 direction )
	{
		var go = Scene.CreateObject();
		go.Name = "LogPiece";
		go.WorldPosition = pos;
		// The 90° X-axis rotation aligns the procedural cube's long axis with
		// the trunk; the Kenney log .vmdl is modelled horizontal so it inherits
		// the same world rotation — the resulting piece lies along the felled
		// trunk's axis either way.
		go.WorldRotation = rot * Rotation.FromAxis( Vector3.Right, 90f );
		// Kenney log .vmdl has correct intrinsic dimensions. Start at One and
		// tune per visual feedback later — physics envelope below is unchanged.
		go.WorldScale = Vector3.One;
		go.Tags.Add( "logpiece" );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Models.Log;
		model.Tint = TrunkTint;

		// Physics envelope stays at the legacy box size — LogPiece.cs chop
		// feel + auto-break-on-impact were tuned against this collider.
		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.LogPieceMass;
		rb.LinearDamping = 0.4f;
		rb.AngularDamping = 0.8f;
		rb.ApplyImpulse( direction.WithZ( 0.3f ).Normal * Tunables.LogPieceMass * 1.5f );
		rb.ApplyTorque( Vector3.Random * 80f );

		var piece = go.AddComponent<LogPiece>();
		piece.Body = rb;
		piece.TrunkTint = TrunkTint;
	}
}
