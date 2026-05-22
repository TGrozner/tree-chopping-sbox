namespace TreeChopping;

public sealed class FallenLog : Component, IChoppable, Component.ICollisionListener
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public int ChopsRemaining { get; set; }
	[Property] public TreeKind Kind { get; set; }
	[Property] public bool IsMythic { get; set; }

	public Color TrunkTint => _trunkTint;
	public bool IsFalling => !_landed && !_logSplit;
	public bool IsFallenLog => _landed && !_logSplit;
	public bool IsSplit => _logSplit;
	public Vector3 LogCenter
	{
		get
		{
			var axis = WorldRotation.Up;
			if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
			return WorldPosition + axis.Normal * ((_trunkLen > 0f ? _trunkLen : Tunables.TreeHeight) * 0.5f);
		}
	}

	private bool _landed;
	private bool _logSplit;
	private bool _landingSnapApplied;
	private bool _whooshFired;
	private Vector3 _fellDir = Vector3.Forward;
	private Vector3 _preCollisionVelocity;
	private float _slowTipElapsed;
	private TimeSince _timeSinceLanded;
	private TimeSince _timeSinceLastImpactDamage = 999f;
	private TimeSince _timeSinceLastCascadeSweep = 999f;
	private TimeSince _hitFlashTime = 999f;
	private float _hitFlashStrength;
	private float _trunkDamageMul = 1f;
	private float _trunkLen;
	private float _trunkWidth;
	private Color _trunkTint;
	private ModelRenderer _trunkLowerMr;
	private ModelRenderer _trunkUpperMr;
	private bool _highlighted;
	private float _biomeDifficulty;

	public static FallenLog SpawnFromTree( Tree tree, Vector3 direction, int fellPower, bool allowComboPush )
	{
		if ( !tree.IsValid() || tree.Scene is null ) return null;

		var go = tree.Scene.CreateObject();
		go.Name = "FallenLog";
		go.WorldPosition = tree.SpawnFootPosition;
		go.WorldRotation = tree.WorldRotation;
		go.Tags.Add( "tree" );

		float trunkH = tree.TrunkLength;
		float trunkW = tree.TrunkWidth;
		var tint = tree.TrunkTint;

		var lower = tree.Scene.CreateObject();
		lower.Name = "FallenLogTrunk";
		lower.SetParent( go );
		lower.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.32f );
		lower.LocalScale = new Vector3( trunkW, trunkW, trunkH * 0.56f ) / Tunables.CubeBase;
		var lowerMr = Mat.AddTintedCube( lower, tint );

		var upper = tree.Scene.CreateObject();
		upper.Name = "FallenLogTrunkUpper";
		upper.SetParent( go );
		upper.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.78f );
		upper.LocalScale = new Vector3( trunkW * 0.78f, trunkW * 0.78f, trunkH * 0.36f ) / Tunables.CubeBase;
		var upperMr = Mat.AddTintedCube( upper, new Color( MathF.Min( 1f, tint.r * 1.08f ), MathF.Min( 1f, tint.g * 1.08f ), MathF.Min( 1f, tint.b * 1.08f ), 1f ) );
		AddTrunkDetail( tree.Scene, lower, "LogBarkSideA", new Vector3( 0.515f, -0.10f, 0.00f ), new Vector3( 0.06f, 0.28f, 0.92f ), new Color( tint.r * 0.56f, tint.g * 0.50f, tint.b * 0.44f, 1f ) );
		AddTrunkDetail( tree.Scene, lower, "LogBandLower", new Vector3( 0f, 0f, -0.46f ), new Vector3( 0.88f, 0.88f, 0.035f ), new Color( tint.r * 0.56f, tint.g * 0.50f, tint.b * 0.44f, 1f ) );
		AddTrunkDetail( tree.Scene, upper, "LogBandUpper", new Vector3( 0f, 0f, 0.47f ), new Vector3( 0.90f, 0.90f, 0.04f ), new Color( tint.r * 0.70f, tint.g * 0.62f, tint.b * 0.52f, 1f ) );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( MathF.Max( trunkW, 1f ), MathF.Max( trunkW, 1f ), MathF.Max( trunkH, 1f ) );
		col.Center = new Vector3( 0f, 0f, trunkH * 0.5f );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = MathF.Max( tree.LogMass, 1f );
		rb.LinearDamping = 0f;
		rb.AngularDamping = 0.3f;
		rb.StartAsleep = false;
		rb.MotionEnabled = true;

		var log = go.AddComponent<FallenLog>();
		log.Body = rb;
		log.Kind = tree.Kind;
		log.IsMythic = tree.IsMythic;
		log.ChopsRemaining = 0;
		log._trunkLen = trunkH;
		log._trunkWidth = trunkW;
		log._trunkTint = tint;
		log._trunkDamageMul = tree.TrunkDamageMul;
		log._trunkLowerMr = lowerMr;
		log._trunkUpperMr = upperMr;
		log._biomeDifficulty = tree.BiomeDifficulty;
		log._fellDir = direction.WithZ( 0f ).Normal;
		if ( log._fellDir.LengthSquared < 0.001f ) log._fellDir = Vector3.Forward;
		log.Launch( fellPower, allowComboPush );
		return log;
	}

	private static void AddTrunkDetail( Scene scene, GameObject parent, string name, Vector3 localPos, Vector3 localScale, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = name;
		go.SetParent( parent );
		go.LocalPosition = localPos;
		go.LocalScale = localScale;
		Mat.AddTintedCube( go, tint );
	}

	private void Launch( int fellPower, bool allowComboPush )
	{
		if ( !Body.IsValid() ) return;
		Body.ResetInertiaTensor();
		var spinAxis = Vector3.Up.Cross( _fellDir ).Normal;
		float kindMul = Tunables.TreeKindInitialFellOmegaMul[(int)Kind];
		int baseChopPower = GameState.Get( Scene )?.ChopPower ?? 1;
		if ( fellPower <= 0 ) fellPower = baseChopPower;
		float powerScale = Tree.ComputeFellKickPowerScale( baseChopPower, fellPower, allowComboPush );
		Body.AngularVelocity = spinAxis * Tunables.InitialFellOmega * kindMul;
		if ( Body.PhysicsBody.IsValid() )
		{
			float mass = Body.PhysicsBody.Mass;
			var topPoint = WorldPosition + Vector3.Up * (_trunkLen * 0.78f);
			Body.PhysicsBody.ApplyImpulseAt( topPoint, _fellDir * mass * Tunables.InitialFellTopImpulseSpeed * kindMul * powerScale );
		}
	}

	public Vector3 GetChopPointFrom( Vector3 origin )
	{
		float halfWidth = MathF.Max( _trunkWidth * 0.5f, Tunables.TreeRadius * 0.25f );
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		var a = WorldPosition + axis * (_trunkLen * 0.08f);
		var b = WorldPosition + axis * (_trunkLen * 0.92f);
		var ab = b - a;
		float t = ab.LengthSquared > 0.001f ? (origin - a).Dot( ab ) / ab.LengthSquared : 0f;
		var closest = a + ab * t.Clamp( 0f, 1f );
		var radial = origin - closest;
		if ( radial.LengthSquared < 0.001f ) radial = Vector3.Up.Cross( axis );
		if ( radial.LengthSquared < 0.001f ) radial = Vector3.Right;
		return closest + radial.Normal * halfWidth;
	}

	public void SetAimHighlight( bool on )
	{
		if ( _highlighted == on ) return;
		_highlighted = on;
		UpdateTrunkVisuals();
	}

	bool IChoppable.IsValid() => IsFallenLog && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Axe;
	void IChoppable.Chop( Vector3 direction, Vector3 hitPoint ) => Chop( direction, 1, hitPoint );
	public void Damage( HitData hit ) => Chop( hit.Direction, hit.ChopPower, hit.HitPoint, hit.ToolTier );

	public void Chop( Vector3 direction, int chopPower, Vector3 hitPoint )
	{
		Chop( direction, chopPower, hitPoint, null );
	}

	private void Chop( Vector3 direction, int chopPower, Vector3 hitPoint, int? toolTierOverride )
	{
		if ( _logSplit || !_landed ) return;
		if ( (float)_timeSinceLanded < Tunables.WoodLogChopGrace ) return;
		var gs = GameState.Get( Scene );
		int axeTier = toolTierOverride ?? (gs.IsValid() ? gs.AxeTier : 0);
		int neededTier = Tunables.TreeKindMinAxeTier[(int)Kind];
		if ( axeTier < neededTier )
		{
			var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
			if ( hud.IsValid() )
			{
				hud.ShowAxeTooWeakHint( Kind, neededTier );
				var popupPos = hitPoint.LengthSquared > 0.01f ? hitPoint : LogCenter;
				hud.ShowDamageText( "TROP DUR", popupPos, WoodHud.DamageTextTooHard );
			}
			return;
		}

		ChopsRemaining -= chopPower;
		PulseHitFlash( ChopsRemaining <= 0 );
		bool leveledUp = gs.IsValid() && gs.IncrementTreeChops();
		var damageHud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
		if ( damageHud.IsValid() )
		{
			var popupPos = hitPoint.LengthSquared > 0.01f ? hitPoint : LogCenter;
			damageHud.ShowDamageText( chopPower.ToString(), popupPos, WoodHud.DamageTextNormal );
			if ( leveledUp )
				damageHud.ShowDamageText( $"WoodCutting Lv.{gs.WoodCuttingLevel}", popupPos + Vector3.Up * 50f, WoodHud.DamageTextBonus, isBonus: true );
		}
		ApplyLandedKick( direction, hitPoint, chopPower );
		SpawnLandedChopScar( hitPoint, direction, chopPower, ChopsRemaining <= 0 );
		if ( ChopsRemaining <= 0 ) SplitIntoLogs();
	}

	protected override void OnUpdate()
	{
		TickHitFlash();
	}

	protected override void OnFixedUpdate()
	{
		if ( !_landed ) TickFall();
		else TickLandedDecay();
		if ( Body.IsValid() ) _preCollisionVelocity = Body.Velocity;
	}

	private void TickFall()
	{
		if ( !Body.IsValid() ) return;
		_slowTipElapsed += Time.Delta;
		var t = (_slowTipElapsed / Tunables.SlowTipDuration).Clamp( 0f, 1f );
		var frac = MathX.Lerp( Tunables.SlowTipInitialFrac, Tunables.SlowTipRampFrac, t );
		float massScale = Body.PhysicsBody.IsValid() ? Body.PhysicsBody.Mass / Tunables.TreeMass : 1f;
		Body.ApplyTorque( Vector3.Up.Cross( _fellDir ) * Tunables.FellTorque * frac * Time.Delta * massScale );
		var upDot = WorldRotation.Up.Dot( Vector3.Up );
		SweepNearbyCascadeTargets();
		if ( !_whooshFired && upDot < Tunables.TreeWhooshUpDotThreshold )
		{
			_whooshFired = true;
			float pitchMul = Tunables.TreeKindChopPitchMul[(int)Kind];
			Sfx.Play( "sounds/tree_fall_whoosh.sound", WorldPosition + Vector3.Up * 100f,
				volume: 0.55f, pitchMin: 0.55f * pitchMul, pitchMax: 0.75f * pitchMul );
		}

		bool restingOnSomething =
			_slowTipElapsed > Tunables.TreeRestingLandingDelay
			&& upDot < Tunables.TreeRestingTiltUpDotMax
			&& Body.Velocity.Length < Tunables.TreeRestingLandingSpeed
			&& Body.AngularVelocity.Length < Tunables.TreeRestingLandingAngularSpeed;

		if ( upDot < Tunables.TreeFallenUpDotMax || restingOnSomething || _slowTipElapsed > 5f )
			BecomeLandedLog();
	}

	private void BecomeLandedLog()
	{
		_landed = true;
		_timeSinceLanded = 0f;
		float landingSpeed = Body.IsValid() ? Body.Velocity.Length : 0f;
		if ( Body.IsValid() )
		{
			Body.AngularDamping = Tunables.TreeAngularDampLanded;
			Body.LinearDamping = Tunables.TreeLinearDampLanded;
		}
		float impactScale = ((landingSpeed - Tunables.ImpactMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		float softScale = ((landingSpeed - Tunables.ImpactSoftMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		EmitLogImpactFeedback( LogCenter, softScale, impactScale );
		if ( impactScale >= Tunables.ImpactHardScale )
		{
			var dustTint = new Color( 0.62f, 0.48f, 0.35f, 1f );
			int dustCount = (int)(10 + impactScale * 16f);
			ChipBurst.SpawnLeaves( Scene, WorldPosition + Vector3.Up * 8f, Vector3.Up, dustCount, dustTint );
			SnapTrunkOnImpact( impactScale );
		}
		ChopsRemaining = Tunables.LogChopHP[(int)Kind];
	}

	public void ApplyImpactDamage( int damage, Vector3 dir )
	{
		if ( _logSplit || damage <= 0 ) return;
		if ( !_landed )
		{
			BecomeLandedLog();
			if ( damage >= Tunables.LogChopHP[(int)Kind] ) SplitIntoLogs();
			return;
		}
		ChopsRemaining -= damage;
		if ( ChopsRemaining <= 0 ) SplitIntoLogs();
		else ApplyLandedKick( dir, LogCenter );
	}

	void Component.ICollisionListener.OnCollisionStart( Collision other )
	{
		if ( _logSplit ) return;
		if ( (float)_timeSinceLastImpactDamage < Tunables.ImpactInterval ) return;
		float impactSpeed = _preCollisionVelocity.Length;
		if ( impactSpeed < Tunables.ImpactSoftMinSpeed ) return;
		_timeSinceLastImpactDamage = 0f;

		float impactScale = ((impactSpeed - Tunables.ImpactMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		float softScale = ((impactSpeed - Tunables.ImpactSoftMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		int damage = impactSpeed >= Tunables.ImpactMinSpeed
			? Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * impactScale ) )
			: 0;

		var otherGo = other.Other.GameObject;
		Tree tree = null;
		FallenLog log = null;
		if ( otherGo.IsValid() )
		{
			tree = otherGo.Components.Get<Tree>() ?? otherGo.Components.Get<Tree>( FindMode.InAncestors );
			log = otherGo.Components.Get<FallenLog>() ?? otherGo.Components.Get<FallenLog>( FindMode.InAncestors );
		}

		var contactPoint = EstimateImpactPoint( tree, log );
		EmitLogImpactFeedback( contactPoint, softScale, impactScale );
		var dir = _preCollisionVelocity.WithZ( 0f );
		if ( dir.LengthSquared < 0.01f ) dir = _fellDir;
		if ( dir.LengthSquared < 0.01f ) dir = Vector3.Forward;

		if ( tree.IsValid() && tree.IsStanding )
		{
			if ( damage > 0 ) tree.ApplyImpactDamage( damage, dir.Normal );
			else tree.ReactToSoftImpactFromLog( dir.Normal, contactPoint );
		}
		else if ( log.IsValid() && log != this )
		{
			if ( damage > 0 ) log.ApplyImpactDamage( damage, dir.Normal );
			else log.ApplyLandedKick( dir.Normal, contactPoint );
		}

		if ( Tunables.ImpactDamageSelf && damage > 0 )
		{
			float splitSpeed = _landed ? Tunables.WoodLogBreakImpactSpeed : Tunables.TreeSplitImpactSpeed * Tunables.TreeKindSplitImpactMul[(int)Kind];
			if ( impactSpeed >= splitSpeed || (!_landed && impactScale >= Tunables.ImpactViolentScale) )
				ApplyImpactDamage( damage, dir.Normal );
		}
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision other ) { }
	void Component.ICollisionListener.OnCollisionStop( CollisionStop other ) { }

	private Vector3 EstimateImpactPoint( Tree tree, FallenLog log )
	{
		if ( tree.IsValid() ) return (LogCenter + tree.LogCenter) * 0.5f;
		if ( log.IsValid() ) return (LogCenter + log.LogCenter) * 0.5f;
		return LogCenter + Vector3.Up * 8f;
	}

	private void SweepNearbyCascadeTargets()
	{
		if ( (float)_timeSinceLastCascadeSweep < Tunables.CascadeSweepInterval ) return;
		if ( (float)_timeSinceLastImpactDamage < Tunables.ImpactInterval ) return;
		if ( !Body.IsValid() ) return;

		float motionSpeed = Body.Velocity.Length + Body.AngularVelocity.Length * MathF.Max( _trunkLen, Tunables.TreeHeight ) * 0.20f;
		if ( motionSpeed < Tunables.CascadeSweepMinSpeed ) return;
		_timeSinceLastCascadeSweep = 0f;

		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		var a = WorldPosition + axis * (_trunkLen * 0.10f);
		var b = WorldPosition + axis * (_trunkLen * 0.92f);
		Tree bestTree = null;
		FallenLog bestLog = null;
		Vector3 bestPoint = default;
		float bestDist = float.MaxValue;

		foreach ( var other in Scene.GetAllComponents<Tree>() )
		{
			if ( !other.IsValid() || !other.IsStanding ) continue;
			var probe = other.WorldPosition + Vector3.Up * (other.TrunkLength * 0.35f);
			ConsiderCascadeProbe( probe, other.TrunkWidth, a, b, ref bestPoint, ref bestDist, onHit: () => { bestTree = other; bestLog = null; } );
		}
		foreach ( var other in Scene.GetAllComponents<FallenLog>() )
		{
			if ( !other.IsValid() || other == this || other._logSplit ) continue;
			var probe = other.LogCenter;
			ConsiderCascadeProbe( probe, other._trunkWidth, a, b, ref bestPoint, ref bestDist, onHit: () => { bestTree = null; bestLog = other; } );
		}
		if ( !bestTree.IsValid() && !bestLog.IsValid() ) return;

		_timeSinceLastImpactDamage = 0f;
		float impactScale = ((motionSpeed - Tunables.ImpactMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		float softScale = ((motionSpeed - Tunables.ImpactSoftMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		var targetCenter = bestTree.IsValid() ? bestTree.LogCenter : bestLog.LogCenter;
		var contactPoint = (bestPoint + targetCenter) * 0.5f;
		EmitLogImpactFeedback( contactPoint, softScale, impactScale );
		var dir = Body.Velocity.WithZ( 0f );
		if ( dir.LengthSquared < 0.01f ) dir = _fellDir;
		if ( dir.LengthSquared < 0.01f ) dir = Vector3.Forward;

		int damage = motionSpeed >= Tunables.ImpactMinSpeed
			? Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * impactScale * Tunables.CascadeSweepDamageMul ) )
			: 0;
		if ( bestTree.IsValid() )
		{
			if ( damage > 0 ) bestTree.ApplyImpactDamage( damage, dir.Normal );
			else bestTree.ReactToSoftImpactFromLog( dir.Normal, contactPoint );
		}
		else if ( bestLog.IsValid() )
		{
			if ( damage > 0 ) bestLog.ApplyImpactDamage( damage, dir.Normal );
			else bestLog.ApplyLandedKick( dir.Normal, contactPoint );
		}
	}

	private void ConsiderCascadeProbe( Vector3 probe, float otherWidth, Vector3 a, Vector3 b, ref Vector3 bestPoint, ref float bestDist, Action onHit )
	{
		var p = ClosestPointOnSegment( a, b, probe );
		float dist = (probe - p).Length;
		float reach = (_trunkWidth + otherWidth) * 0.55f + Tunables.CascadeSweepRadius;
		if ( dist > reach || dist >= bestDist ) return;
		bestPoint = p;
		bestDist = dist;
		onHit?.Invoke();
	}

	private void ApplyLandedKick( Vector3 chopDirection, Vector3 hitPoint, int chopPower = 0 )
	{
		if ( !Body.IsValid() || !Body.PhysicsBody.IsValid() ) return;
		var flat = chopDirection.WithZ( 0f );
		if ( flat.LengthSquared < 0.001f ) flat = Vector3.Forward;
		flat = flat.Normal;
		var applyPoint = hitPoint.LengthSquared > 0.01f ? hitPoint : LogCenter + Vector3.Up * 8f;
		int baseChopPower = GameState.Get( Scene )?.ChopPower ?? 1;
		if ( chopPower <= 0 ) chopPower = baseChopPower;
		float powerScale = Tree.ComputeLandedKickPowerScale( baseChopPower, chopPower );
		Body.PhysicsBody.ApplyImpulseAt( applyPoint, (flat * 0.82f + Vector3.Up * 0.10f).Normal * Tunables.LandedLogKickImpulse * powerScale );
		var spinAxis = Vector3.Cross( (applyPoint - LogCenter).Normal, flat );
		if ( spinAxis.LengthSquared < 0.001f ) spinAxis = Vector3.Cross( Vector3.Up, flat );
		if ( spinAxis.LengthSquared > 0.001f )
			Body.PhysicsBody.ApplyAngularImpulse( spinAxis.Normal * Tunables.LandedLogKickTorque * powerScale );
	}

	private void SpawnLandedChopScar( Vector3 hitPoint, Vector3 direction, int chopPower, bool finalHit )
	{
		if ( !_trunkLowerMr.IsValid() ) return;
		var scar = Scene.CreateObject();
		scar.Name = finalHit ? "LogSplitScar" : "LogChopScar";
		scar.SetParent( _trunkLowerMr.GameObject );
		scar.LocalPosition = new Vector3( Game.Random.Float( -0.18f, 0.18f ), 0.54f, Game.Random.Float( -0.38f, 0.38f ) );
		float bite = MathX.Lerp( 1f, 1.45f, MathF.Min( chopPower, 6f ) / 6f );
		if ( finalHit ) bite *= 1.3f;
		scar.LocalScale = new Vector3( 0.42f * bite, 0.055f, 0.10f * bite );
		scar.LocalRotation = Rotation.FromYaw( Game.Random.Float( -16f, 16f ) );
		Mat.AddTintedCube( scar, finalHit ? new Color( 0.04f, 0.02f, 0.01f, 1f ) : new Color( 0.10f, 0.05f, 0.02f, 1f ) );
	}

	private void PulseHitFlash( bool finalHit )
	{
		_hitFlashTime = 0f;
		_hitFlashStrength = finalHit ? 0.62f : 0.38f;
		UpdateTrunkVisuals();
	}

	private void TickHitFlash()
	{
		if ( (float)_hitFlashTime > Tunables.TreeHitFlashDuration )
		{
			if ( _hitFlashStrength > 0f )
			{
				_hitFlashStrength = 0f;
				UpdateTrunkVisuals();
			}
			return;
		}
		UpdateTrunkVisuals();
	}

	private void UpdateTrunkVisuals()
	{
		SetRendererTint( _trunkLowerMr, 1.00f, 0.35f );
		SetRendererTint( _trunkUpperMr, 1.08f, 0.28f );
	}

	private void SetRendererTint( ModelRenderer mr, float brightness, float highlightStrength )
	{
		if ( !mr.IsValid() ) return;
		var baseTint = new Color(
			(_trunkTint.r * brightness * _trunkDamageMul).Clamp( 0.04f, 1f ),
			(_trunkTint.g * brightness * _trunkDamageMul).Clamp( 0.04f, 1f ),
			(_trunkTint.b * brightness * _trunkDamageMul).Clamp( 0.04f, 1f ),
			1f );
		if ( _highlighted ) baseTint = Color.Lerp( baseTint, Color.White, highlightStrength );
		float flashT = (float)_hitFlashTime / Tunables.TreeHitFlashDuration;
		if ( flashT < 1f && _hitFlashStrength > 0f )
		{
			float flash = _hitFlashStrength * (1f - flashT) * (1f - flashT);
			baseTint = Color.Lerp( baseTint, Tunables.ChipSplinterTint, flash );
		}
		mr.Tint = baseTint;
	}

	private void EmitLogImpactFeedback( Vector3 contactPoint, float softScale, float damageScale )
	{
		bool violent = damageScale >= Tunables.ImpactViolentScale;
		bool hard = damageScale >= Tunables.ImpactHardScale;
		float vol = hard ? 0.70f + damageScale * 0.45f : 0.32f + softScale * 0.35f;
		Sfx.Play( hard ? "sounds/log_break.sound" : "sounds/axe_hit_wood.sound", contactPoint,
			volume: vol,
			pitchMin: violent ? 0.50f : (hard ? 0.62f : 0.72f),
			pitchMax: violent ? 0.68f : (hard ? 0.84f : 0.95f) );
		int dustCount = hard ? 8 + (int)(damageScale * 18f) : 2 + (int)(softScale * 5f);
		var dustTint = hard ? new Color( 0.62f, 0.48f, 0.35f, 1f ) : new Color( 0.48f, 0.42f, 0.34f, 1f );
		ChipBurst.SpawnLeaves( Scene, contactPoint, Vector3.Up, dustCount, dustTint );
		if ( violent )
		{
			var sideDir = _preCollisionVelocity.WithZ( 0f );
			if ( sideDir.LengthSquared > 0.01f )
				ChipBurst.SpawnLeaves( Scene, contactPoint, sideDir.Normal, dustCount / 2, _trunkTint );
		}
	}

	private void SnapTrunkOnImpact( float speedFrac )
	{
		if ( _landingSnapApplied ) return;
		_landingSnapApplied = true;
		float snap = MathX.Lerp( 5f, 14f, (speedFrac - 0.4f).Clamp( 0f, 0.9f ) / 0.9f );
		var side = Vector3.Cross( Vector3.Up, _fellDir.WithZ( 0f ) );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		if ( _trunkUpperMr.IsValid() )
		{
			var upper = _trunkUpperMr.GameObject;
			upper.LocalRotation *= Rotation.FromAxis( side.Normal, snap );
			upper.LocalPosition += side.Normal * Game.Random.Float( 2f, 6f );
		}
	}

	private void SplitIntoLogs()
	{
		if ( _logSplit ) return;
		_logSplit = true;
		int kindIdx = (int)Kind;
		int totalItems = Tunables.TreeKindLandedDropCount[kindIdx];
		if ( IsMythic ) totalItems += 2;
		var gs = GameState.Get( Scene );
		bool luckTriggered = false;
		if ( gs.IsValid() && gs.LuckChance > 0f && Game.Random.Float() < gs.LuckChance )
		{
			totalItems += Math.Max( 1, totalItems / 2 );
			luckTriggered = true;
		}

		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		var side = Vector3.Cross( Vector3.Up, axis );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		side = side.Normal;
		var mix = Tunables.TreeKindWoodTypeMix[kindIdx];
		float spread = MathF.Min( _trunkLen * Tunables.LogDropAxisSpreadFrac, Tunables.LogDropAxisSpreadMax );
		for ( int i = 0; i < totalItems; i++ )
		{
			float t = totalItems <= 1 ? 0.5f : (i + Game.Random.Float( 0.18f, 0.82f )) / totalItems;
			float off = MathX.Lerp( -spread, spread, t );
			float sideSign = (i & 1) == 0 ? 1f : -1f;
			float sideOff = sideSign * Game.Random.Float( MathF.Max( _trunkWidth * 0.42f, Tunables.LogDropSideSpread * 0.35f ), MathF.Max( _trunkWidth * 0.85f, Tunables.LogDropSideSpread ) );
			var burstDir = (side * sideSign * 1.15f + axis * MathF.Sign( off == 0f ? sideSign : off ) * 0.42f + Vector3.Up * 0.25f).Normal;
			var pos = LogCenter + axis * off + side * sideOff + burstDir * Game.Random.Float( 6f, 16f ) + Vector3.Up * Game.Random.Float( 8f, 20f );
			WoodType type = Tree.PickWoodType( mix );
			WoodItem.SpawnAt( Scene, pos, WorldScale.x, type, burstDir );
			if ( i < 8 ) ChipBurst.Spawn( Scene, pos, -burstDir, 3, _trunkTint );
		}
		Sfx.Play( "sounds/log_break.sound", WorldPosition, volume: 1.0f, pitchMin: 0.62f, pitchMax: 0.82f );
		if ( luckTriggered )
		{
			var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
			if ( hud.IsValid() ) hud.ShowDamageText( "LUCKY!", LogCenter, WoodHud.DamageTextBonus, isBonus: true );
		}
		GameObject?.Destroy();
	}

	private void TickLandedDecay()
	{
		if ( !Body.IsValid() ) return;
		if ( _timeSinceLanded < 0.6f ) return;
		Body.Sleeping = Body.Velocity.LengthSquared < 4f && Body.AngularVelocity.LengthSquared < 0.5f;
		float despawnDelay = Tunables.TreeKindRespawnDelay[(int)Kind];
		if ( IsMythic ) despawnDelay += Tunables.MythicRespawnExtra;
		if ( (float)_timeSinceLanded > despawnDelay )
			GameObject.Destroy();
	}

	private static Vector3 ClosestPointOnSegment( Vector3 a, Vector3 b, Vector3 p )
	{
		var ab = b - a;
		float lenSq = ab.LengthSquared;
		if ( lenSq < 0.001f ) return a;
		float t = (p - a).Dot( ab ) / lenSq;
		return a + ab * t.Clamp( 0f, 1f );
	}
}
