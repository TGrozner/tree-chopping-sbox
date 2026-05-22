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
	private bool _groundContactSeen;
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
	private int _splitDepth;
	private int _dropCountOverride = -1;
	private Color _trunkTint;
	private ModelRenderer _trunkLowerMr;
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

		int kindIdx = (int)tree.Kind;
		float lengthMul = Tunables.TreeKindLogLengthMul[kindIdx];
		float widthMul = Tunables.TreeKindLogWidthMul[kindIdx];
		float trunkH = tree.TrunkLength * lengthMul;
		float trunkW = tree.TrunkWidth * widthMul;
		var tint = tree.TrunkTint;

		var lowerMr = AddLogVisual( tree.Scene, go, "FallenLogTrunk", trunkH, trunkW, tint );

		float radius = MathF.Max( trunkW * 0.5f, 1f );
		var col = go.AddComponent<CapsuleCollider>();
		col.Radius = radius;
		col.Start = new Vector3( 0f, 0f, radius );
		col.End = new Vector3( 0f, 0f, MathF.Max( radius + 1f, trunkH - radius ) );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = MathF.Max( tree.LogMass * MathF.Max( 1.0f, lengthMul * widthMul * 3.0f ), Tunables.TreeMass * 1.8f );
		rb.LinearDamping = 0f;
		rb.AngularDamping = 0.3f;
		rb.EnhancedCcd = true;
		rb.SleepThreshold = Tunables.TreeLogSleepThreshold;
		rb.StartAsleep = false;
		rb.MotionEnabled = true;
		if ( rb.PhysicsBody.IsValid() )
		{
			rb.PhysicsBody.Position = go.WorldPosition;
			rb.PhysicsBody.Rotation = go.WorldRotation;
		}

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
		go.LocalScale = localScale / Tunables.CubeBase;
		Mat.AddTintedCube( go, tint );
	}

	private static ModelRenderer AddLogVisual( Scene scene, GameObject parent, string name, float length, float width, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = name;
		go.SetParent( parent );
		go.LocalPosition = new Vector3( 0f, 0f, length * 0.5f );
		go.LocalScale = new Vector3( width, width, length );
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = LogVisuals.CylinderModel;
		mr.MaterialOverride = Mat.Default;
		mr.Tint = tint;
		AddTrunkDetail( scene, go, "LogBarkSideA", new Vector3( 0.52f, -0.10f, 0.00f ), new Vector3( 0.05f, 0.20f, 0.78f ), new Color( tint.r * 0.56f, tint.g * 0.50f, tint.b * 0.44f, 1f ) );
		AddTrunkDetail( scene, go, "LogCutLower", new Vector3( 0f, 0f, -0.50f ), new Vector3( 0.58f, 0.58f, 0.035f ), new Color( tint.r * 1.18f, tint.g * 1.08f, tint.b * 0.88f, 1f ) );
		AddTrunkDetail( scene, go, "LogCutUpper", new Vector3( 0f, 0f, 0.50f ), new Vector3( 0.58f, 0.58f, 0.035f ), new Color( tint.r * 1.18f, tint.g * 1.08f, tint.b * 0.88f, 1f ) );
		return mr;
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
		if ( !_logSplit ) SweepNearbyCascadeTargets();
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
		float kindTorqueMul = Tunables.TreeKindFellTorqueMul[(int)Kind];
		var upDot = WorldRotation.Up.Dot( Vector3.Up );
		float tiltAssist = upDot.Clamp( 0f, 1f );
		Body.ApplyTorque( Vector3.Up.Cross( _fellDir ) * Tunables.FellTorque * frac * tiltAssist * Time.Delta * massScale * kindTorqueMul );
		bool groundSupported = _groundContactSeen || DebugMinGroundClearance() < Tunables.TreeGroundedLandingClearance;
		if ( groundSupported )
		{
			_groundContactSeen = true;
			ApplyGroundContactLimits();
		}
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

		bool fallenEnough = upDot < Tunables.TreeFallenUpDotMax;
		bool fallbackSupported = _slowTipElapsed > Tunables.TreeFallbackLandingDelay && groundSupported;
		if ( (fallenEnough && groundSupported) || restingOnSomething || fallbackSupported )
			BecomeLandedLog();
	}

	private void BecomeLandedLog()
	{
		_landed = true;
		_timeSinceLanded = 0f;
		ClampAboveGround();
		float landingSpeed = Body.IsValid() ? Body.Velocity.Length : 0f;
		if ( Body.IsValid() )
		{
			Body.AngularDamping = Tunables.TreeAngularDampLanded;
			Body.LinearDamping = Tunables.TreeLinearDampLanded;
			Body.SleepThreshold = Tunables.TreeLogSleepThreshold;
			Body.AngularVelocity *= Tunables.TreeLandedPostImpactAngularMul;
			Body.Velocity *= Tunables.TreeLandedPostImpactLinearMul;
			StabilizeLandedMotion();
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
		HandleCollision( other );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision other )
	{
		HandleCollision( other );
	}

	private void HandleCollision( Collision other )
	{
		if ( _logSplit ) return;
		var otherGo = other.Other.GameObject;
		bool hitGround = otherGo.IsValid() && otherGo.Tags.Has( "ground" );
		var player = otherGo.IsValid()
			? otherGo.Components.Get<PlayerController>() ?? otherGo.Components.Get<PlayerController>( FindMode.InAncestors )
			: null;
		if ( _landed && player.IsValid() )
		{
			DampPlayerBump();
			return;
		}
		Tree tree = null;
		FallenLog log = null;
		if ( otherGo.IsValid() && !hitGround )
		{
			tree = otherGo.Components.Get<Tree>() ?? otherGo.Components.Get<Tree>( FindMode.InAncestors );
			log = otherGo.Components.Get<FallenLog>() ?? otherGo.Components.Get<FallenLog>( FindMode.InAncestors );
		}

		var contactPoint = EstimateImpactPoint( tree, log );
		var impactVelocity = GetImpactVelocityAt( contactPoint );
		if ( tree.IsValid() ) impactVelocity -= tree.GetImpactVelocityAt( contactPoint );
		else if ( log.IsValid() ) impactVelocity -= log.GetImpactVelocityAt( contactPoint );
		float impactSpeed = impactVelocity.Length;
		if ( hitGround )
		{
			_groundContactSeen = true;
			ApplyGroundContactLimits();
		}
		if ( (float)_timeSinceLastImpactDamage < Tunables.ImpactInterval ) return;
		if ( impactSpeed < Tunables.ImpactSoftMinSpeed ) return;

		float impactScale = ((impactSpeed - Tunables.ImpactMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		float softScale = ((impactSpeed - Tunables.ImpactSoftMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		int damage = impactSpeed >= Tunables.ImpactMinSpeed
			? Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * impactScale ) )
			: 0;

		EmitLogImpactFeedback( contactPoint, softScale, impactScale );
		var dir = impactVelocity.WithZ( 0f );
		if ( dir.LengthSquared < 0.01f ) dir = _fellDir;
		if ( dir.LengthSquared < 0.01f ) dir = Vector3.Forward;

		bool consumedImpact = false;
		if ( tree.IsValid() && tree.IsStanding )
		{
			if ( damage > 0 ) tree.ApplyImpactDamage( damage, dir.Normal );
			else tree.ReactToSoftImpactFromLog( dir.Normal, contactPoint );
			consumedImpact = true;
		}
		else if ( log.IsValid() && log != this )
		{
			if ( damage > 0 ) log.ApplyImpactDamage( damage, dir.Normal );
			else log.ApplyLandedKick( dir.Normal, contactPoint );
			consumedImpact = true;
		}

		if ( Tunables.ImpactDamageSelf && damage > 0 )
		{
			float splitSpeed = _landed ? Tunables.WoodLogBreakImpactSpeed : Tunables.TreeSplitImpactSpeed * Tunables.TreeKindSplitImpactMul[(int)Kind];
			if ( impactSpeed >= splitSpeed || (!_landed && impactScale >= Tunables.ImpactViolentScale) )
			{
				ApplyImpactDamage( damage, dir.Normal );
				consumedImpact = true;
			}
		}
		if ( consumedImpact ) _timeSinceLastImpactDamage = 0f;
	}

	internal Vector3 GetImpactVelocityAt( Vector3 point )
	{
		if ( !Body.IsValid() ) return _preCollisionVelocity;
		var pointVelocity = Body.GetVelocityAtPoint( point );
		return pointVelocity.LengthSquared > _preCollisionVelocity.LengthSquared
			? pointVelocity
			: _preCollisionVelocity;
	}

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
			var lowProbe = other.WorldPosition + Vector3.Up * MathF.Max( other.TrunkWidth * 1.4f, other.TrunkLength * 0.12f );
			var midProbe = other.WorldPosition + Vector3.Up * (other.TrunkLength * 0.35f);
			ConsiderCascadeProbe( lowProbe, other.TrunkWidth, a, b, ref bestPoint, ref bestDist, onHit: () => { bestTree = other; bestLog = null; } );
			ConsiderCascadeProbe( midProbe, other.TrunkWidth, a, b, ref bestPoint, ref bestDist, onHit: () => { bestTree = other; bestLog = null; } );
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
		Body.PhysicsBody.ApplyImpulseAt( applyPoint, (flat * 0.96f + Vector3.Up * 0.02f).Normal * Tunables.LandedLogKickImpulse * powerScale );
		var spinAxis = Vector3.Cross( (applyPoint - LogCenter).Normal, flat );
		if ( spinAxis.LengthSquared < 0.001f ) spinAxis = Vector3.Cross( Vector3.Up, flat );
		if ( spinAxis.LengthSquared > 0.001f )
			Body.PhysicsBody.ApplyAngularImpulse( spinAxis.Normal * Tunables.LandedLogKickTorque * powerScale );
	}

	private void DampPlayerBump()
	{
		if ( !Body.IsValid() ) return;
		var v = Body.Velocity;
		if ( v.z > Tunables.TreeGroundContactMaxUpSpeed )
			v = v.WithZ( Tunables.TreeGroundContactMaxUpSpeed );
		Body.Velocity = v.WithZ( 0f ) * 0.45f + Vector3.Up * v.z;
		Body.AngularVelocity *= 0.65f;
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
		ChipBurst.Spawn( Scene, LogCenter, Vector3.Up, 5 + (int)(speedFrac * 8f), _trunkTint );
	}

	private void SplitIntoLogs()
	{
		if ( _logSplit ) return;
		_logSplit = true;
		int kindIdx = (int)Kind;
		int totalItems = _dropCountOverride >= 0 ? _dropCountOverride : Tunables.TreeKindLandedDropCount[kindIdx];
		if ( _dropCountOverride < 0 && IsMythic ) totalItems += 2;
		var gs = GameState.Get( Scene );
		bool luckTriggered = false;
		if ( _dropCountOverride < 0 && gs.IsValid() && gs.LuckChance > 0f && Game.Random.Float() < gs.LuckChance )
		{
			totalItems += Math.Max( 1, totalItems / 2 );
			luckTriggered = true;
		}

		int splitLogCount = Tunables.TreeKindSplitLogCount[kindIdx];
		if ( _splitDepth == 0 && splitLogCount > 0 && totalItems > 1 )
		{
			SplitIntoSmallerLogs( splitLogCount, totalItems );
			return;
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
			var burstDir = (side * sideSign * 0.85f + axis * MathF.Sign( off == 0f ? sideSign : off ) * 0.28f + Vector3.Up * 0.08f).Normal;
			var pos = LogCenter + axis * off + side * sideOff + burstDir * Game.Random.Float( 2f, 7f ) + Vector3.Up * Game.Random.Float( 2f, 8f );
			WoodType type = Tree.PickWoodType( mix );
			WoodItem.SpawnAt( Scene, pos, WorldScale.x, type, burstDir );
			if ( i < 5 ) ChipBurst.Spawn( Scene, pos, -burstDir, 2, _trunkTint );
		}
		Sfx.Play( "sounds/log_break.sound", WorldPosition, volume: 1.0f, pitchMin: 0.62f, pitchMax: 0.82f );
		if ( luckTriggered )
		{
			var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
			if ( hud.IsValid() ) hud.ShowDamageText( "LUCKY!", LogCenter, WoodHud.DamageTextBonus, isBonus: true );
		}
		GameObject?.Destroy();
	}

	private void SplitIntoSmallerLogs( int count, int totalItems )
	{
		ClampAboveGround();
		var axis = WorldRotation.Up.WithZ( 0f );
		if ( axis.LengthSquared < 0.001f ) axis = _fellDir.WithZ( 0f );
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Forward;
		axis = axis.Normal;
		var side = Vector3.Cross( Vector3.Up, axis );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		side = side.Normal;
		var childRotation = RotationWithUp( axis );

		float childLen = MathF.Max( _trunkWidth * 4.5f, _trunkLen / count * 0.88f );
		int remaining = totalItems;
		for ( int i = 0; i < count; i++ )
		{
			int slotsLeft = count - i;
			int drops = Math.Max( 1, (int)MathF.Round( remaining / (float)slotsLeft ) );
			remaining -= drops;
			float t = count <= 1 ? 0.5f : i / (float)(count - 1);
			float centerOff = MathX.Lerp( -_trunkLen * Tunables.SplitLogAxisSpawnFrac, _trunkLen * Tunables.SplitLogAxisSpawnFrac, t );
			float sideSign = (i & 1) == 0 ? 1f : -1f;
			float sideOff = sideSign * MathF.Max( Tunables.SplitLogSideSpawnMin, _trunkWidth * Tunables.SplitLogSideSpawnMul );
			float spawnLift = MathF.Max( _trunkWidth * 0.5f + Tunables.LogGroundSkin + 2f, 8f );
			var center = LogCenter + axis * centerOff + side * sideOff + Vector3.Up * (spawnLift + i * 0.5f);
			var start = center - axis * (childLen * 0.5f);
			var burst = (side * sideSign + Vector3.Up * 0.08f).Normal;
			SpawnSmallerLog( start, childRotation, childLen, _trunkWidth * 0.62f, drops, count, axis );
			ChipBurst.Spawn( Scene, center, burst, 2, _trunkTint );
		}
		Sfx.Play( "sounds/log_break.sound", WorldPosition, volume: 0.95f, pitchMin: 0.58f, pitchMax: 0.78f );
		GameObject?.Destroy();
	}

	private FallenLog SpawnSmallerLog( Vector3 start, Rotation rotation, float length, float width, int dropCount, int splitCount, Vector3 axis )
	{
		var go = Scene.CreateObject();
		go.Name = "FallenLog";
		go.WorldPosition = start;
		go.WorldRotation = rotation;
		go.Tags.Add( "tree" );

		var lowerMr = AddLogVisual( Scene, go, "FallenLogTrunk", length, width, _trunkTint );
		float radius = MathF.Max( width * 0.5f, 1f );
		var col = go.AddComponent<CapsuleCollider>();
		col.Radius = radius;
		col.Start = new Vector3( 0f, 0f, radius );
		col.End = new Vector3( 0f, 0f, MathF.Max( radius + 1f, length - radius ) );

		var rb = go.AddComponent<Rigidbody>();
		float parentMass = Body.IsValid() && Body.PhysicsBody.IsValid() ? Body.PhysicsBody.Mass : Tunables.TreeMass;
		rb.MassOverride = MathF.Max( Tunables.TreeMass * 1.0f, parentMass * 0.85f / MathF.Max( 1, splitCount ) );
		rb.LinearDamping = Tunables.TreeLinearDampLanded;
		rb.AngularDamping = Tunables.TreeAngularDampLanded;
		rb.EnhancedCcd = true;
		rb.SleepThreshold = Tunables.TreeLogSleepThreshold;
		rb.StartAsleep = true;
		rb.MotionEnabled = true;

		var log = go.AddComponent<FallenLog>();
		log.Body = rb;
		log.Kind = Kind;
		log.IsMythic = IsMythic;
		log.ChopsRemaining = Math.Max( 1, Tunables.TreeKindSplitLogHP[(int)Kind] );
		log._landed = true;
		log._splitDepth = _splitDepth + 1;
		log._dropCountOverride = dropCount;
		log._trunkLen = length;
		log._trunkWidth = width;
		log._trunkTint = _trunkTint;
		log._trunkDamageMul = _trunkDamageMul;
		log._trunkLowerMr = lowerMr;
		log._biomeDifficulty = _biomeDifficulty;
		log._fellDir = axis.WithZ( 0f ).Normal;
		if ( log._fellDir.LengthSquared < 0.001f ) log._fellDir = Vector3.Forward;
		log._timeSinceLanded = 0f;
		log._groundContactSeen = true;
		log.SnapRestingLogToGround();

		if ( rb.PhysicsBody.IsValid() )
		{
			rb.ResetInertiaTensor();
			rb.PhysicsBody.Position = go.WorldPosition;
			rb.PhysicsBody.Rotation = go.WorldRotation;
			rb.PhysicsBody.Velocity = Vector3.Zero;
			rb.PhysicsBody.AngularVelocity = Vector3.Zero;
		}
		rb.Sleeping = true;
		return log;
	}

	private static Rotation RotationWithUp( Vector3 up )
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

	internal float DebugMinGroundClearance()
	{
		if ( Scene is null ) return 9999f;
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		float radius = MathF.Max( _trunkWidth * 0.5f, 1f );
		float length = MathF.Max( _trunkLen, radius * 2f + 1f );
		var lower = WorldPosition + axis * radius;
		var upper = WorldPosition + axis * MathF.Max( radius + 1f, length - radius );
		float clearance = 9999f;
		AccumulateGroundClearance( lower, radius, ref clearance );
		AccumulateGroundClearance( (lower + upper) * 0.5f, radius, ref clearance );
		AccumulateGroundClearance( upper, radius, ref clearance );
		return clearance;
	}

	internal float DebugAxisUpDot() => MathF.Abs( WorldRotation.Up.Normal.Dot( Vector3.Up ) );

	private void TickLandedDecay()
	{
		if ( !Body.IsValid() ) return;
		if ( (float)_timeSinceLanded < 0.15f ) ClampAboveGround();
		else if ( DebugMinGroundClearance() < -1f ) ClampAboveGround();
		ApplyGroundContactLimits();
		StabilizeLandedMotion();
		if ( _timeSinceLanded < Tunables.TreeLandedManualSleepDelay ) return;
		Body.Sleeping = Body.Velocity.LengthSquared < Tunables.TreeLandedManualSleepSpeed * Tunables.TreeLandedManualSleepSpeed
			&& Body.AngularVelocity.LengthSquared < Tunables.TreeLandedManualSleepAngularSpeed * Tunables.TreeLandedManualSleepAngularSpeed;
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

	private void ClampAboveGround()
	{
		if ( Scene is null || !Body.IsValid() ) return;
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		float radius = MathF.Max( _trunkWidth * 0.5f, 1f );
		float length = MathF.Max( _trunkLen, radius * 2f + 1f );
		var lower = WorldPosition + axis * radius;
		var upper = WorldPosition + axis * MathF.Max( radius + 1f, length - radius );
		float lift = 0f;
		AccumulateGroundLift( lower, radius, ref lift );
		AccumulateGroundLift( (lower + upper) * 0.5f, radius, ref lift );
		AccumulateGroundLift( upper, radius, ref lift );
		if ( lift <= 0f ) return;
		if ( lift > 800f ) lift = 800f;
		WorldPosition += Vector3.Up * lift;
		if ( Body.PhysicsBody.IsValid() )
		{
			Body.PhysicsBody.Position = WorldPosition;
			if ( Body.PhysicsBody.Velocity.z < 0f )
				Body.PhysicsBody.Velocity = Body.PhysicsBody.Velocity.WithZ( 0f );
		}
	}

	private void SnapRestingLogToGround()
	{
		if ( Scene is null || !Body.IsValid() ) return;
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		float radius = MathF.Max( _trunkWidth * 0.5f, 1f );
		float length = MathF.Max( _trunkLen, radius * 2f + 1f );
		float targetOriginZ = float.NegativeInfinity;
		AccumulateGroundOriginZ( radius, radius, axis, ref targetOriginZ );
		AccumulateGroundOriginZ( length * 0.5f, radius, axis, ref targetOriginZ );
		AccumulateGroundOriginZ( MathF.Max( radius + 1f, length - radius ), radius, axis, ref targetOriginZ );
		if ( float.IsNegativeInfinity( targetOriginZ ) ) return;
		WorldPosition = WorldPosition.WithZ( targetOriginZ );
		if ( Body.PhysicsBody.IsValid() )
			Body.PhysicsBody.Position = WorldPosition;
	}

	private void StabilizeLandedMotion()
	{
		if ( !Body.IsValid() ) return;
		var v = Body.Velocity;
		if ( v.Length > Tunables.TreeLandedMaxSpeed )
			v = v.Normal * Tunables.TreeLandedMaxSpeed;
		v = v.WithZ( v.z.Clamp( -Tunables.TreeLandedMaxVerticalSpeed, Tunables.TreeLandedMaxVerticalSpeed ) );
		Body.Velocity = v;

		var av = Body.AngularVelocity;
		if ( av.Length > Tunables.TreeLandedMaxAngularSpeed )
			av = av.Normal * Tunables.TreeLandedMaxAngularSpeed;
		Body.AngularVelocity = av;
	}

	private void ApplyGroundContactLimits()
	{
		if ( !Body.IsValid() ) return;
		if ( DebugMinGroundClearance() > Tunables.TreeGroundedLandingClearance ) return;

		var v = Body.Velocity;
		if ( v.z > Tunables.TreeGroundContactMaxUpSpeed )
			v = v.WithZ( Tunables.TreeGroundContactMaxUpSpeed );
		var flat = v.WithZ( 0f ) * Tunables.TreeGroundContactHorizontalDrag;
		Body.Velocity = flat + Vector3.Up * v.z;
		Body.AngularVelocity *= Tunables.TreeGroundContactAngularDrag;
	}

	private void AccumulateGroundLift( Vector3 point, float radius, ref float lift )
	{
		var hit = Scene.Trace.Ray( point + Vector3.Up * 140f, point - Vector3.Up * 260f )
			.WithAnyTags( "ground" )
			.Run();
		if ( !hit.Hit ) return;
		float wantedZ = hit.EndPosition.z + radius + Tunables.LogGroundSkin;
		lift = MathF.Max( lift, wantedZ - point.z );
	}

	private void AccumulateGroundOriginZ( float axisOffset, float radius, Vector3 axis, ref float targetOriginZ )
	{
		var point = WorldPosition + axis * axisOffset;
		var hit = Scene.Trace.Ray( point + Vector3.Up * 180f, point - Vector3.Up * 420f )
			.WithAnyTags( "ground" )
			.Run();
		if ( !hit.Hit ) return;
		float wantedPointZ = hit.EndPosition.z + radius + Tunables.LogGroundSkin;
		targetOriginZ = MathF.Max( targetOriginZ, wantedPointZ - axis.z * axisOffset );
	}

	private void AccumulateGroundClearance( Vector3 point, float radius, ref float clearance )
	{
		var hit = Scene.Trace.Ray( point + Vector3.Up * 140f, point - Vector3.Up * 260f )
			.WithAnyTags( "ground" )
			.Run();
		if ( !hit.Hit ) return;
		float wantedZ = hit.EndPosition.z + radius + Tunables.LogGroundSkin;
		clearance = MathF.Min( clearance, point.z - wantedZ );
	}
}
