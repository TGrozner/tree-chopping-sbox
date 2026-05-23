namespace TreeChopping;

public sealed class FallenLog : Component, IChoppable, Component.ICollisionListener
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public int ChopsRemaining { get; set; }
	[Property] public TreeKind Kind { get; set; }
	[Property] public bool IsMythic { get; set; }

	public Color TrunkTint => _trunkTint;
	internal float TrunkWidth => _trunkWidth;
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
	private bool _hardLandingBurstApplied;
	private bool _groundContactSeen;
	private bool _whooshFired;
	private Vector3 _fellDir = Vector3.Forward;
	private Vector3 _preCollisionVelocity;
	private float _fallElapsed;
	private TimeSince _timeSinceLanded;
	private TimeSince _timeSinceSpawned = 0f;
	private TimeSince _timeSinceLastImpactDamage = 999f;
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
	private float _sourceScale = 1f;
	private Vector3 _debugValheimLogSpawnCenter;
	private Vector3 _debugValheimLogSpawnOffset;
	private Vector3 _debugValheimLogAuthoredSpawnOffset;
	private float _debugSpawnBottomClearance;
	private float _debugLaunchImpulseSpeed;
	private float _debugLaunchImpulseHeight;
	private Vector3 _debugLaunchImpulseDirection;
	private Vector3 _debugLaunchVelocity;
	private Vector3 _debugLaunchAngularVelocity;
	private Vector3 _debugLastLandedKickImpulse;
	private Vector3 _debugLastLandedKickPoint;
	private float _debugMaxScarWorldExtent;
	private float _debugColliderRadius;
	private int _debugColliderShapeCount;

	public static FallenLog SpawnFromTree( Tree tree, Vector3 direction )
	{
		if ( !tree.IsValid() || tree.Scene is null ) return null;

		int kindIdx = (int)tree.Kind;
		float lengthMul = Tunables.TreeKindLogLengthMul[kindIdx];
		float widthMul = Tunables.TreeKindLogWidthMul[kindIdx];
		float trunkH = tree.TrunkLength * lengthMul;
		float trunkW = tree.TrunkWidth * widthMul;
		float colliderW = trunkW * Tunables.TreeKindLogColliderWidthMul[kindIdx];
		var tint = tree.TrunkTint;
		float sourceScale = MathF.Max( 0.1f, tree.TrunkLength / Tunables.TreeHeight );
		var authoredSpawnOffset = Tunables.TreeKindLogSpawnPoint[kindIdx] * sourceScale;
		var spawnOffset = new Vector3( authoredSpawnOffset.x, authoredSpawnOffset.y, trunkH * 0.5f + Tunables.TreeLogSpawnGroundClearance );
		var spawnCenter = tree.SpawnFootPosition + LocalOffsetToWorld( tree.WorldRotation, spawnOffset );
		var axis = tree.WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;

		var go = tree.Scene.CreateObject();
		go.Name = "FallenLog";
		go.WorldRotation = tree.WorldRotation;
		go.WorldPosition = spawnCenter - axis.Normal * (trunkH * 0.5f);
		go.Tags.Add( "tree" );

		var lowerMr = AddLogVisual( tree.Scene, go, "FallenLogTrunk", trunkH, trunkW, tint );

		float radius = MathF.Max( colliderW * 0.5f, 1f );
		int colliderShapeCount = AddLogColliders( go, trunkH, radius );

		var rb = go.AddComponent<Rigidbody>();
		ConfigureTreeLogBody( rb, Tunables.ValheimTreeLogFullMass * sourceScale );
		if ( rb.PhysicsBody.IsValid() )
		{
			rb.PhysicsBody.Position = go.WorldPosition;
			rb.PhysicsBody.Rotation = go.WorldRotation;
		}

		var log = go.AddComponent<FallenLog>();
		log.Body = rb;
		log.Kind = tree.Kind;
		log.IsMythic = tree.IsMythic;
		log.ChopsRemaining = Math.Max( 1, Tunables.LogChopHP[kindIdx] );
		log._trunkLen = trunkH;
		log._trunkWidth = trunkW;
		log._trunkTint = tint;
		log._trunkDamageMul = tree.TrunkDamageMul;
		log._trunkLowerMr = lowerMr;
		log._biomeDifficulty = tree.BiomeDifficulty;
		log._sourceScale = sourceScale;
		log._debugValheimLogSpawnCenter = spawnCenter;
		log._debugValheimLogSpawnOffset = spawnOffset;
		log._debugValheimLogAuthoredSpawnOffset = authoredSpawnOffset;
		log._debugSpawnBottomClearance = go.WorldPosition.z - tree.SpawnFootPosition.z;
		log._debugColliderRadius = radius;
		log._debugColliderShapeCount = colliderShapeCount;
		log._fellDir = direction.WithZ( 0f ).Normal;
		if ( log._fellDir.LengthSquared < 0.001f ) log._fellDir = Vector3.Forward;
		log.Launch();
		return log;
	}

	private static Vector3 LocalOffsetToWorld( Rotation rotation, Vector3 local )
	{
		return rotation.Forward * local.x + rotation.Right * local.y + rotation.Up * local.z;
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
		mr.Tint = new Color( MathF.Min( 1f, tint.r * 1.06f ), MathF.Min( 1f, tint.g * 1.06f ), MathF.Min( 1f, tint.b * 1.06f ), 1f );
		AddTrunkDetail( scene, go, "LogBarkSideA", new Vector3( 0.52f, -0.10f, 0.00f ), new Vector3( 0.045f, 0.18f, 0.74f ), new Color( tint.r * 0.56f, tint.g * 0.50f, tint.b * 0.44f, 1f ) );
		AddTrunkDetail( scene, go, "LogBarkSideB", new Vector3( -0.52f, 0.08f, -0.08f ), new Vector3( 0.040f, 0.16f, 0.58f ), new Color( tint.r * 0.72f, tint.g * 0.62f, tint.b * 0.52f, 1f ) );
		AddTrunkDetail( scene, go, "LogBarkTop", new Vector3( -0.06f, 0.52f, 0.12f ), new Vector3( 0.20f, 0.040f, 0.62f ), new Color( MathF.Min( 1f, tint.r * 1.18f ), MathF.Min( 1f, tint.g * 1.08f ), MathF.Min( 1f, tint.b * 0.92f ), 1f ) );
		AddTrunkDetail( scene, go, "LogCutLower", new Vector3( 0f, 0f, -0.505f ), new Vector3( 0.86f, 0.86f, 0.030f ), new Color( MathF.Min( 1f, tint.r * 1.24f ), MathF.Min( 1f, tint.g * 1.14f ), MathF.Min( 1f, tint.b * 0.92f ), 1f ) );
		AddTrunkDetail( scene, go, "LogCutUpper", new Vector3( 0f, 0f, 0.505f ), new Vector3( 0.86f, 0.86f, 0.030f ), new Color( MathF.Min( 1f, tint.r * 1.24f ), MathF.Min( 1f, tint.g * 1.14f ), MathF.Min( 1f, tint.b * 0.92f ), 1f ) );
		return mr;
	}

	private static int AddLogColliders( GameObject go, float length, float radius )
	{
		var capsule = go.AddComponent<CapsuleCollider>();
		capsule.Radius = radius;
		capsule.Start = new Vector3( 0f, 0f, radius );
		capsule.End = new Vector3( 0f, 0f, MathF.Max( radius + 1f, length - radius ) );

		int supportCount = Math.Max( 0, Tunables.LogSupportSphereCount );
		for ( int i = 0; i < supportCount; i++ )
		{
			var support = go.AddComponent<SphereCollider>();
			support.Center = new Vector3( 0f, 0f, LogProbeOffset( i, supportCount, radius, length ) );
			support.Radius = radius * Tunables.LogSupportSphereRadiusMul;
		}
		return 1 + supportCount;
	}

	private void Launch()
	{
		if ( !Body.IsValid() ) return;
		Body.ResetInertiaTensor();
		if ( Body.PhysicsBody.IsValid() )
		{
			float mass = Body.PhysicsBody.Mass;
			_debugLaunchImpulseSpeed = Tunables.InitialFellTopImpulseSpeed;
			_debugLaunchImpulseHeight = Tunables.ValheimSpawnLogImpulseHeight * _sourceScale;
			_debugLaunchImpulseDirection = _fellDir;
			var topPoint = LogCenter + Vector3.Up * _debugLaunchImpulseHeight;
			Body.PhysicsBody.ApplyImpulseAt( topPoint, _fellDir * mass * _debugLaunchImpulseSpeed );
			_debugLaunchVelocity = Body.PhysicsBody.Velocity;
			_debugLaunchAngularVelocity = Body.PhysicsBody.AngularVelocity;
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

	bool IChoppable.IsValid() => !_logSplit && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Axe;
	void IChoppable.Chop( Vector3 direction, Vector3 hitPoint ) => Chop( direction, 1, hitPoint );
	public void Damage( HitData hit ) => Chop( hit.Direction, hit.GetTreeLogDamage(), hit.HitPoint, hit.ToolTier, hit.PushForce );

	public void Chop( Vector3 direction, int chopPower, Vector3 hitPoint )
	{
		Chop( direction, chopPower, hitPoint, null );
	}

	private void Chop( Vector3 direction, int chopPower, Vector3 hitPoint, int? toolTierOverride, float? pushForceOverride = null )
	{
		if ( _logSplit ) return;
		if ( (float)_timeSinceSpawned < Tunables.WoodLogChopGrace ) return;
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

		if ( chopPower <= 0 )
		{
			ApplyTreeLogHitImpulse( direction, hitPoint, pushForceOverride ?? Tunables.LandedLogKickImpulse );
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
		ApplyTreeLogHitImpulse( direction, hitPoint, pushForceOverride ?? Tunables.LandedLogKickImpulse );
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
		_fallElapsed += Time.Delta;
		var upDot = WorldRotation.Up.Dot( Vector3.Up );
		if ( !_whooshFired && upDot < Tunables.TreeWhooshUpDotThreshold )
		{
			_whooshFired = true;
			ValheimEffects.TreeWhoosh( WorldPosition + Vector3.Up * 100f, Kind );
		}

		bool restingOnSomething =
			_fallElapsed > Tunables.TreeRestingLandingDelay
			&& upDot < Tunables.TreeRestingTiltUpDotMax
			&& Body.Velocity.Length < Tunables.TreeRestingLandingSpeed
			&& Body.AngularVelocity.Length < Tunables.TreeRestingLandingAngularSpeed;

		bool fallenEnough = upDot < Tunables.TreeFallenUpDotMax;
		if ( (fallenEnough && _groundContactSeen) || (restingOnSomething && _groundContactSeen) )
			BecomeLandedLog();
	}

	private void BecomeLandedLog()
	{
		_landed = true;
		_timeSinceLanded = 0f;
		float landingSpeed = Body.IsValid() ? Body.Velocity.Length : 0f;
		float impactScale = ValheimImpact.ScaleFromSpeed( landingSpeed );
		float softScale = ((landingSpeed - Tunables.ImpactSoftMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		EmitLogImpactFeedback( LogCenter, softScale, impactScale );
		if ( impactScale >= Tunables.ImpactHardScale )
		{
			ValheimEffects.LogLandingHard( Scene, WorldPosition, impactScale );
			EmitHardLandingChipBurst( impactScale );
		}
	}

	public void ApplyImpactDamage( int damage, Vector3 dir, int toolTier = Tunables.ValheimImpactToolTier )
	{
		if ( _logSplit || damage <= 0 ) return;
		if ( (float)_timeSinceSpawned < Tunables.WoodLogChopGrace ) return;
		if ( toolTier < Tunables.TreeKindMinAxeTier[(int)Kind] ) return;
		ChopsRemaining -= damage;
		PulseHitFlash( ChopsRemaining <= 0 );
		if ( ChopsRemaining <= 0 ) SplitIntoLogs();
	}

	void Component.ICollisionListener.OnCollisionStart( Collision other )
	{
		HandleCollision( other, allowImpactDamage: true );
	}

	void Component.ICollisionListener.OnCollisionUpdate( Collision other )
	{
	}

	private void HandleCollision( Collision other, bool allowImpactDamage )
	{
		if ( _logSplit ) return;
		var otherGo = other.Other.GameObject;
		bool hitGround = otherGo.IsValid() && otherGo.Tags.Has( "ground" );
		var player = otherGo.IsValid()
			? otherGo.Components.Get<PlayerController>() ?? otherGo.Components.Get<PlayerController>( FindMode.InAncestors )
			: null;
		if ( player.IsValid() ) return;
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
			_groundContactSeen = true;
		if ( !allowImpactDamage ) return;
		if ( (float)_timeSinceLastImpactDamage < Tunables.ImpactInterval ) return;
		if ( impactSpeed < Tunables.ImpactSoftMinSpeed ) return;

		float impactScale = ValheimImpact.ScaleFromSpeed( impactSpeed );
		float softScale = ((impactSpeed - Tunables.ImpactSoftMinSpeed) / (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		int damage = ValheimImpact.DamageFromSpeed( impactSpeed );

		EmitLogImpactFeedback( contactPoint, softScale, impactScale );
		bool acceptedImpact = true;
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
			consumedImpact = true;
		}

		if ( acceptedImpact || consumedImpact ) _timeSinceLastImpactDamage = 0f;
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

	private void ApplyTreeLogHitImpulse( Vector3 chopDirection, Vector3 hitPoint, float pushForce = Tunables.LandedLogKickImpulse )
	{
		if ( !Body.IsValid() || !Body.PhysicsBody.IsValid() ) return;
		var flat = chopDirection.WithZ( 0f );
		if ( flat.LengthSquared < 0.001f ) flat = Vector3.Forward;
		flat = flat.Normal;
		var applyPoint = hitPoint.LengthSquared > 0.01f ? hitPoint : LogCenter + Vector3.Up * 8f;
		_debugLastLandedKickImpulse = flat * pushForce * Tunables.ValheimTreeLogHitPushMul;
		_debugLastLandedKickPoint = applyPoint;
		Body.PhysicsBody.ApplyImpulseAt( applyPoint, _debugLastLandedKickImpulse );
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
		scar.LocalScale = new Vector3( 0.42f * bite, 0.055f, 0.10f * bite ) / Tunables.CubeBase;
		scar.LocalRotation = Rotation.FromYaw( Game.Random.Float( -16f, 16f ) );
		Mat.AddTintedCube( scar, finalHit ? new Color( 0.04f, 0.02f, 0.01f, 1f ) : new Color( 0.10f, 0.05f, 0.02f, 1f ) );
		var worldSize = scar.WorldScale * Tunables.CubeBase;
		_debugMaxScarWorldExtent = MathF.Max( _debugMaxScarWorldExtent, MathF.Max( worldSize.x, MathF.Max( worldSize.y, worldSize.z ) ) );
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
		SetRendererTint( _trunkLowerMr, 1.06f, 0.35f );
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
		ValheimEffects.LogImpact( Scene, contactPoint, _preCollisionVelocity, _trunkTint, softScale, damageScale );
	}

	private void EmitHardLandingChipBurst( float speedFrac )
	{
		if ( _hardLandingBurstApplied ) return;
		_hardLandingBurstApplied = true;
		ValheimEffects.DropChip( Scene, LogCenter, Vector3.Up, _trunkTint, 5 + (int)(speedFrac * 8f) );
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
		var dropAxis = WorldRotation.Up;
		if ( dropAxis.LengthSquared < 0.001f ) dropAxis = Vector3.Up;
		dropAxis = dropAxis.Normal;
		if ( _splitDepth == 0 && splitLogCount > 0 && totalItems > 1 )
		{
			int parentDropCount = ComputeParentLogImmediateDropCount( totalItems, splitLogCount );
			if ( parentDropCount > 0 )
				SpawnValheimTreeLogDrops( parentDropCount, dropAxis, chipLimit: 3 );
			SplitIntoSmallerLogs( splitLogCount, Math.Max( 1, totalItems - parentDropCount ) );
			return;
		}

		SpawnValheimTreeLogDrops( totalItems, dropAxis, chipLimit: 5 );
		ValheimEffects.LogDestroyed( Scene, WorldPosition );
		if ( luckTriggered )
		{
			var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
			if ( hud.IsValid() ) hud.ShowDamageText( "LUCKY!", LogCenter, WoodHud.DamageTextBonus, isBonus: true );
		}
		GameObject?.Destroy();
	}

	internal static int ComputeParentLogImmediateDropCount( int totalItems, int splitLogCount )
	{
		if ( totalItems <= 1 || splitLogCount <= 0 ) return 0;
		return Math.Min( totalItems - 1, Math.Max( 1, totalItems / (splitLogCount + 1) ) );
	}

	private void SpawnValheimTreeLogDrops( int totalItems, Vector3 axis, int chipLimit )
	{
		if ( totalItems <= 0 ) return;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		var mix = Tunables.TreeKindWoodTypeMix[(int)Kind];
		float spread = Tunables.ValheimTreeLogSpawnDistance;
		for ( int i = 0; i < totalItems; i++ )
		{
			float off = Game.Random.Float( -spread, spread );
			var pos = LogCenter + axis * off + Vector3.Up * (Tunables.ValheimTreeBaseDropYStep * i);
			WoodType type = Tree.PickWoodType( mix );
			WoodItem.SpawnValheimDropAt( Scene, pos, WorldScale.x, type );
			if ( i < chipLimit ) ValheimEffects.DropChip( Scene, pos, -axis, _trunkTint );
		}
	}

	private void SplitIntoSmallerLogs( int count, int totalItems )
	{
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = _fellDir;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Forward;
		axis = axis.Normal;
		DisableParentPhysicsForSplit();

		int kindIdx = (int)Kind;
		float childLenFrac = Tunables.TreeKindSplitLogLengthFrac[kindIdx] > 0f
			? Tunables.TreeKindSplitLogLengthFrac[kindIdx]
			: Tunables.SplitLogLengthFrac;
		float childWidthFrac = Tunables.TreeKindSplitLogWidthFrac[kindIdx] > 0f
			? Tunables.TreeKindSplitLogWidthFrac[kindIdx]
			: Tunables.SplitLogWidthFrac;
		var pointFracs = Tunables.TreeKindSubLogPointFrac[kindIdx];
		float childLen = _trunkLen * childLenFrac;
		int remaining = totalItems;
		for ( int i = 0; i < count; i++ )
		{
			int slotsLeft = count - i;
			int drops = Math.Max( 1, (int)MathF.Round( remaining / (float)slotsLeft ) );
			remaining -= drops;
			float t = count <= 1 ? 0.5f : i / (float)(count - 1);
			float centerFrac = i < pointFracs.Length
				? pointFracs[i]
				: MathX.Lerp( -Tunables.SplitLogAxisSpawnFrac, Tunables.SplitLogAxisSpawnFrac, t );
			float centerOff = _trunkLen * centerFrac;
			var center = LogCenter + axis * centerOff;
			float childWidth = _trunkWidth * childWidthFrac;
			var childAxis = axis;
			var sideAxis = Vector3.Up.Cross( childAxis );
			if ( sideAxis.LengthSquared < 0.001f ) sideAxis = WorldRotation.Right;
			if ( sideAxis.LengthSquared > 0.001f )
			{
				float sideSign = i % 2 == 0 ? -1f : 1f;
				float sideOffset = MathF.Max( Tunables.SplitLogSideSpawnMin, childWidth * Tunables.SplitLogSideSpawnMul );
				center += sideAxis.Normal * sideOffset * sideSign;
			}
			var start = center - childAxis * (childLen * 0.5f);
			SpawnSmallerLog( start, RotationWithUp( childAxis ), childLen, childWidth, drops, count, childAxis );
			ValheimEffects.DropChip( Scene, center, Vector3.Up, _trunkTint );
		}
		ValheimEffects.SmallerLogsSpawned( WorldPosition );
		GameObject?.Destroy();
	}

	private void DisableParentPhysicsForSplit()
	{
		if ( Body.IsValid() )
		{
			Body.MotionEnabled = false;
			Body.Enabled = false;
		}
		if ( !GameObject.IsValid() ) return;
		foreach ( var capsule in GameObject.Components.GetAll<CapsuleCollider>() )
			capsule.Enabled = false;
		foreach ( var sphere in GameObject.Components.GetAll<SphereCollider>() )
			sphere.Enabled = false;
	}

	private FallenLog SpawnSmallerLog( Vector3 start, Rotation rotation, float length, float width, int dropCount, int splitCount, Vector3 axis )
	{
		var go = Scene.CreateObject();
		go.Name = "FallenLog";
		go.WorldPosition = start;
		go.WorldRotation = rotation;
		go.Tags.Add( "tree" );

		var lowerMr = AddLogVisual( Scene, go, "FallenLogTrunk", length, width, _trunkTint );
		float colliderWidth = width * Tunables.TreeKindLogColliderWidthMul[(int)Kind];
		float radius = MathF.Max( colliderWidth * 0.5f, 1f );
		int colliderShapeCount = AddLogColliders( go, length, radius );

		var rb = go.AddComponent<Rigidbody>();
		ConfigureTreeLogBody( rb, Tunables.ValheimTreeLogHalfMass * MathF.Max( 0.1f, _sourceScale ) );

		var log = go.AddComponent<FallenLog>();
		log.Body = rb;
		log.Kind = Kind;
		log.IsMythic = IsMythic;
		log.ChopsRemaining = Math.Max( 1, Tunables.TreeKindSplitLogHP[(int)Kind] );
		log._landed = false;
		log._splitDepth = _splitDepth + 1;
		log._dropCountOverride = dropCount;
		log._trunkLen = length;
		log._trunkWidth = width;
		log._trunkTint = _trunkTint;
		log._trunkDamageMul = _trunkDamageMul;
		log._trunkLowerMr = lowerMr;
		log._biomeDifficulty = _biomeDifficulty;
		log._sourceScale = _sourceScale;
		log._debugColliderRadius = radius;
		log._debugColliderShapeCount = colliderShapeCount;
		log._fellDir = axis.WithZ( 0f ).Normal;
		if ( log._fellDir.LengthSquared < 0.001f ) log._fellDir = Vector3.Forward;

		if ( rb.PhysicsBody.IsValid() )
		{
			rb.ResetInertiaTensor();
			rb.PhysicsBody.Position = go.WorldPosition;
			rb.PhysicsBody.Rotation = go.WorldRotation;
		}
		return log;
	}

	private static void ConfigureTreeLogBody( Rigidbody rb, float mass )
	{
		rb.MassOverride = mass;
		rb.LinearDamping = Tunables.ValheimTreeLogLinearDamping;
		rb.AngularDamping = Tunables.ValheimTreeLogAngularDamping;
		rb.EnhancedCcd = true;
		rb.SleepThreshold = Tunables.TreeLogSleepThreshold;
		rb.StartAsleep = false;
		rb.MotionEnabled = true;
		rb.Gravity = true;
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
		float clearance = 9999f;
		int count = Math.Max( 3, Tunables.LogGroundProbeCount );
		for ( int i = 0; i < count; i++ )
		{
			float offset = LogProbeOffset( i, count, radius, length );
			AccumulateGroundClearance( WorldPosition + axis * offset, radius, ref clearance );
		}
		return clearance;
	}

	internal Vector3 DebugAxis => WorldRotation.Up.LengthSquared > 0.001f ? WorldRotation.Up.Normal : Vector3.Up;
	internal float DebugAxisUpDot() => MathF.Abs( WorldRotation.Up.Normal.Dot( Vector3.Up ) );
	internal Vector3 DebugValheimLogSpawnCenter => _debugValheimLogSpawnCenter;
	internal Vector3 DebugValheimLogSpawnOffset => _debugValheimLogSpawnOffset;
	internal Vector3 DebugValheimLogAuthoredSpawnOffset => _debugValheimLogAuthoredSpawnOffset;
	internal float DebugSpawnBottomClearance => _debugSpawnBottomClearance;
	internal float DebugLaunchImpulseSpeed => _debugLaunchImpulseSpeed;
	internal float DebugLaunchImpulseHeight => _debugLaunchImpulseHeight;
	internal Vector3 DebugLaunchImpulseDirection => _debugLaunchImpulseDirection;
	internal Vector3 DebugLaunchVelocity => _debugLaunchVelocity;
	internal Vector3 DebugLaunchAngularVelocity => _debugLaunchAngularVelocity;
	internal float DebugSourceScale => _sourceScale;
	internal float DebugTrunkLength => _trunkLen;
	internal float DebugTrunkWidth => _trunkWidth;
	internal float DebugAge => (float)_timeSinceSpawned;
	internal int DebugSplitDepth => _splitDepth;
	internal float DebugLandedAge => _landed ? (float)_timeSinceLanded : 0f;
	internal Vector3 DebugLastLandedKickImpulse => _debugLastLandedKickImpulse;
	internal Vector3 DebugLastLandedKickPoint => _debugLastLandedKickPoint;
	internal float DebugMaxScarWorldExtent => _debugMaxScarWorldExtent;
	internal float DebugColliderRadius => _debugColliderRadius;
	internal int DebugColliderShapeCount => _debugColliderShapeCount;
	internal string DebugGroundTraceSummary()
	{
		var axis = DebugAxis;
		float radius = MathF.Max( _trunkWidth * 0.5f, 1f );
		float length = MathF.Max( _trunkLen, radius * 2f + 1f );
		var p0 = WorldPosition + axis * LogProbeOffset( 0, 3, radius, length );
		var p1 = WorldPosition + axis * LogProbeOffset( 1, 3, radius, length );
		var p2 = WorldPosition + axis * LogProbeOffset( 2, 3, radius, length );
		return $"origin={WorldPosition} center={LogCenter} axis={axis} upDot={DebugAxisUpDot():F2} r={radius:F1} len={length:F1} clear={DebugMinGroundClearance():F1} vel={(Body.IsValid() ? Body.Velocity : Vector3.Zero)} ang={(Body.IsValid() ? Body.AngularVelocity : Vector3.Zero)} g=[{GroundTracePoint( p0, radius )}; {GroundTracePoint( p1, radius )}; {GroundTracePoint( p2, radius )}]";
	}
	internal void DebugResetGraceForTest()
	{
		_timeSinceLanded = 0f;
		_timeSinceSpawned = 0f;
	}
	private void TickLandedDecay()
	{
		if ( !Body.IsValid() ) return;
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

	private static float LogProbeOffset( int index, int count, float radius, float length )
	{
		float start = radius;
		float end = MathF.Max( radius + 1f, length - radius );
		float t = count <= 1 ? 0.5f : index / (float)(count - 1);
		return MathX.Lerp( start, end, t );
	}

	private bool TryGroundZAt( Vector3 point, out float z )
	{
		var hit = Scene.Trace.Ray( point + Vector3.Up * 180f, point - Vector3.Up * 420f )
			.WithAnyTags( "ground" )
			.Run();
		z = hit.EndPosition.z;
		return hit.Hit;
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

	private string GroundTracePoint( Vector3 point, float radius )
	{
		if ( !TryGroundZAt( point, out var groundZ ) )
			return $"{point.z:F1}/no-ground";
		float wantedZ = groundZ + radius + Tunables.LogGroundSkin;
		return $"{point.z:F1}/{groundZ:F1}/{point.z - wantedZ:F1}";
	}
}
