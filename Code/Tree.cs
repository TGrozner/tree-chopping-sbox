namespace TreeChopping;

public sealed class Tree : Component, IChoppable, Component.ICollisionListener
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public Color TrunkTint { get; set; } = new( 0.46f, 0.32f, 0.22f, 1f );
	[Property] public int ChopsRemaining { get; set; } = 3;

	private bool _chopped;
	private bool _landed;
	private bool _broken;
	private float _slowTipElapsed;
	private Vector3 _fellDir;
	private TimeSince _timeSinceLanded;
	private Vector3 _originalFoot;

	protected override void OnStart()
	{
		_originalFoot = WorldPosition - Vector3.Up * (Tunables.TreeHeight * 0.5f);
	}

	public static Tree SpawnAt( Scene scene, Vector3 footPosition, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = "Tree";
		go.WorldPosition = footPosition + Vector3.Up * (Tunables.TreeHeight * 0.5f);
		go.Tags.Add( "tree" );
		go.WorldScale = new Vector3( Tunables.TreeRadius * 2f, Tunables.TreeRadius * 2f, Tunables.TreeHeight ) / Tunables.CubeBase;

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		mr.Tint = tint;

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.TreeMass;
		rb.AngularDamping = 1.2f;
		rb.LinearDamping = 0.3f;
		rb.StartAsleep = true;

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
		tree.TrunkTint = tint;
		return tree;
	}

	public bool IsFalling => _chopped && !_landed && !_broken;
	public bool IsStanding => !_chopped && !_broken;

	bool IChoppable.IsValid() => !_broken && !_landed && this.IsValid();
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

		Stump.SpawnAt( Scene, _originalFoot, TrunkTint );
		GameObject.Destroy();
	}

	private void SpawnLogPiece( Vector3 pos, Rotation rot, Vector3 direction )
	{
		var go = Scene.CreateObject();
		go.Name = "LogPiece";
		go.WorldPosition = pos;
		go.WorldRotation = rot * Rotation.FromAxis( Vector3.Right, 90f );
		go.WorldScale = new Vector3( Tunables.LogPieceRadius * 2f, Tunables.LogPieceRadius * 2f, Tunables.LogPieceHeight ) / Tunables.CubeBase;
		go.Tags.Add( "logpiece" );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Model.Cube;
		model.Tint = TrunkTint;

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
