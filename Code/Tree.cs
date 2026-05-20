namespace TreeChopping;

public enum TreeKind { Normal, Sapling, Veteran, Brittle }

// Mow-the-lawn style tree : multiple chops required (per kind), falls
// physically when HP drops to 0, drops wood into the GameState. No cascade
// strikes coded — neighbors only get pushed by natural rigidbody collisions
// (Valheim-style "soft" cascade : a falling trunk bumps a small one).
public sealed class Tree : Component, IChoppable
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public int ChopsRemaining { get; set; } = 3;
	[Property] public TreeKind Kind { get; set; } = TreeKind.Normal;
	[Property] public bool IsMythic { get; set; }
	[Property] public bool IsGate { get; set; }

	private bool _chopped;
	private bool _landed;
	private float _slowTipElapsed;
	private Vector3 _fellDir;
	private TimeSince _timeSinceLanded;
	private GameObject _primaryCanopy;
	private GameObject _rootStump;
	private ModelRenderer _trunkLowerMr;
	private ModelRenderer _trunkUpperMr;
	private ModelRenderer _rootMr;
	private Color _canopyTint;
	private float _ambientPhase;
	private bool _highlighted;
	private bool _woodGiven;
	private Vector3 _spawnFootPos;
	private float _biomeDifficulty;
	private Rotation _baseRotation;
	private bool _baseRotCached;
	private TimeSince _wobbleStart = 999f;
	private Vector3 _wobbleAxis = Vector3.Right;
	private float _wobbleAmplitudeDeg;

	public static Tree SpawnAt( Scene scene, Vector3 footPosition, float biomeDifficulty = 0.5f )
	{
		var go = scene.CreateObject();
		go.Name = "Tree";
		go.WorldPosition = footPosition;
		go.Tags.Add( "tree" );
		int hash = footPosition.GetHashCode();
		float yawDeg = ((hash >> 8) & 0xFFFF) / 65535f * 360f;
		go.WorldRotation = Rotation.FromYaw( yawDeg );

		float scaleNorm = ((hash >> 24) & 0xFF) / 255f;
		float scaleMul = MathX.Lerp( Tunables.TreeScaleMin, Tunables.TreeScaleMax, scaleNorm );

		TreeKind kind = PickKindFromHash( hash, biomeDifficulty );
		int kindIdx = (int)kind;
		scaleMul *= Tunables.TreeKindScaleMul[kindIdx];
		float kindMassMul = Tunables.TreeKindMassMul[kindIdx];
		Color tint = Tunables.TreeKindTrunkTint[kindIdx];

		bool isMythic = ((hash >> 12) & 0xFFFFFF) % Tunables.MythicSpawnRatio == 0;
		if ( isMythic )
		{
			scaleMul *= Tunables.MythicScaleMul;
			tint = Tunables.MythicTrunkTint;
		}
		else
		{
			// Per-tree multiplicative tint jitter — picked by hash so the same
			// tree always gets the same jitter, but neighbouring trees vary.
			float jitter = Tunables.TreeTintJitter[((uint)(hash >> 2) & 0xFFu) % (uint)Tunables.TreeTintJitter.Length];
			tint = new Color(
				(tint.r * jitter).Clamp( 0f, 1f ),
				(tint.g * jitter).Clamp( 0f, 1f ),
				(tint.b * jitter).Clamp( 0f, 1f ),
				1f );
		}

		float trunkW = Tunables.TreeRadius * 1.4f * scaleMul;
		float trunkH = Tunables.TreeHeight * scaleMul;
		// Trunk silhouette : a slight base flare (root) + main column + a
		// narrower upper section gives a tapered tree shape from a uniform
		// cube. Cheap (3 child meshes) and reads as wood not "stack of bricks".
		var rootBase = scene.CreateObject();
		rootBase.Name = "TreeRoot";
		rootBase.SetParent( go );
		rootBase.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.04f );
		rootBase.LocalScale = new Vector3( trunkW * 1.30f, trunkW * 1.30f, trunkH * 0.08f ) / Tunables.CubeBase;
		var rootMr = Mat.AddTintedCube( rootBase, new Color( tint.r * 0.78f, tint.g * 0.78f, tint.b * 0.78f, 1f ) );

		var lower = scene.CreateObject();
		lower.Name = "TreeTrunk";
		lower.SetParent( go );
		lower.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.32f );
		lower.LocalScale = new Vector3( trunkW, trunkW, trunkH * 0.56f ) / Tunables.CubeBase;
		var lowerMr = Mat.AddTintedCube( lower, tint );

		var upper = scene.CreateObject();
		upper.Name = "TreeTrunkUpper";
		upper.SetParent( go );
		upper.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.78f );
		upper.LocalScale = new Vector3( trunkW * 0.78f, trunkW * 0.78f, trunkH * 0.36f ) / Tunables.CubeBase;
		var upperMr = Mat.AddTintedCube( upper, new Color( MathF.Min( 1f, tint.r * 1.08f ), MathF.Min( 1f, tint.g * 1.08f ), MathF.Min( 1f, tint.b * 1.08f ), 1f ) );

		int canopyHash = (hash >> 16) & 0xFF;
		var canopyTint = isMythic
			? Tunables.MythicCanopyTint
			: Tunables.CanopyTints[canopyHash % Tunables.CanopyTints.Length];
		if ( !isMythic )
		{
			float cjit = Tunables.TreeTintJitter[((uint)(hash >> 18) & 0xFFu) % (uint)Tunables.TreeTintJitter.Length];
			canopyTint = new Color(
				(canopyTint.r * cjit).Clamp( 0f, 1f ),
				(canopyTint.g * cjit).Clamp( 0f, 1f ),
				(canopyTint.b * cjit).Clamp( 0f, 1f ),
				1f );
		}
		int species = ((hash >> 4) & 0xFF) % 3;
		var primaryCanopy = SpawnSpeciesCanopy( scene, go, species, scaleMul, trunkH, canopyTint );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( trunkW, trunkW, trunkH );
		col.Center = new Vector3( 0f, 0f, trunkH * 0.5f );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.TreeMass * scaleMul * scaleMul * scaleMul * kindMassMul;
		rb.AngularDamping = 1.2f;
		rb.LinearDamping = 0.3f;
		rb.StartAsleep = true;
		rb.MotionEnabled = false; // CLAUDE.md non-negotiable #8

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
		tree.IsMythic = isMythic;
		tree.Kind = kind;
		// Chops required scales with kind + slight scale jitter.
		tree.ChopsRemaining = Math.Max( 1, (int)(Tunables.TreeKindChopsBase[kindIdx] * (0.7f + scaleNorm * 0.6f)) );
		tree._primaryCanopy = primaryCanopy;
		tree._rootStump = rootBase;
		tree._trunkLowerMr = lowerMr;
		tree._trunkUpperMr = upperMr;
		tree._rootMr = rootMr;
		tree._canopyTint = canopyTint;
		tree._spawnFootPos = footPosition;
		tree._biomeDifficulty = biomeDifficulty;
		// Stagger ambient sway so adjacent trees don't beat in lockstep.
		tree._ambientPhase = ((uint)hash & 0xFFFFu) / 65535f * MathF.PI * 2f;
		return tree;
	}

	// Gate variant : a tall ⊥-shaped barrier with horizontal cross-beams so
	// it reads as a "smashable doorway" rather than a fat red tree. On
	// landing it triggers SceneStarter to expand the playable ring instead
	// of paying wood.
	public static Tree SpawnGate( Scene scene, Vector3 footPosition, int chopsRequired, float yawDeg )
	{
		var go = scene.CreateObject();
		go.Name = "TreeGate";
		go.WorldPosition = footPosition;
		go.Tags.Add( "tree" );
		go.WorldRotation = Rotation.FromYaw( yawDeg );

		float scaleMul = Tunables.TreeKindScaleMul[(int)TreeKind.Veteran] * 1.5f;
		float kindMassMul = Tunables.TreeKindMassMul[(int)TreeKind.Veteran] * 1.5f;
		Color trunkTint = new( 0.42f, 0.18f, 0.18f, 1f );
		Color capTint   = new( 0.62f, 0.30f, 0.20f, 1f );
		Color beamTint  = new( 0.34f, 0.14f, 0.14f, 1f );

		float trunkW = Tunables.TreeRadius * 1.4f * scaleMul;
		float trunkH = Tunables.TreeHeight * scaleMul;
		// Cross-beams extend perpendicular to the player approach (along
		// local Y since the gate's local +X points outward from spawn).
		float beamLen = trunkW * 5.0f;
		float beamThick = trunkW * 0.55f;
		float beamHeight = trunkW * 0.6f;

		var rootBase = scene.CreateObject();
		rootBase.Name = "GateRoot";
		rootBase.SetParent( go );
		rootBase.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.04f );
		rootBase.LocalScale = new Vector3( trunkW * 1.30f, trunkW * 1.30f, trunkH * 0.08f ) / Tunables.CubeBase;
		Mat.AddTintedCube( rootBase, new Color( trunkTint.r * 0.78f, trunkTint.g * 0.78f, trunkTint.b * 0.78f, 1f ) );

		var lower = scene.CreateObject();
		lower.Name = "GateTrunk";
		lower.SetParent( go );
		lower.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.32f );
		lower.LocalScale = new Vector3( trunkW, trunkW, trunkH * 0.56f ) / Tunables.CubeBase;
		Mat.AddTintedCube( lower, trunkTint );

		var upper = scene.CreateObject();
		upper.Name = "GateTrunkUpper";
		upper.SetParent( go );
		upper.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.78f );
		upper.LocalScale = new Vector3( trunkW * 0.78f, trunkW * 0.78f, trunkH * 0.36f ) / Tunables.CubeBase;
		Mat.AddTintedCube( upper, capTint );

		// Horizontal cross-beams perpendicular to the radial direction. Two
		// of them (mid + upper) make the silhouette read as a doorway.
		var beamMid = scene.CreateObject();
		beamMid.Name = "GateBeamMid";
		beamMid.SetParent( go );
		beamMid.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.42f );
		beamMid.LocalScale = new Vector3( beamThick, beamLen, beamHeight ) / Tunables.CubeBase;
		Mat.AddTintedCube( beamMid, beamTint );

		var beamTop = scene.CreateObject();
		beamTop.Name = "GateBeamTop";
		beamTop.SetParent( go );
		beamTop.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.92f );
		beamTop.LocalScale = new Vector3( beamThick, beamLen, beamHeight ) / Tunables.CubeBase;
		Mat.AddTintedCube( beamTop, capTint );

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( trunkW, trunkW, trunkH );
		col.Center = new Vector3( 0f, 0f, trunkH * 0.5f );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.TreeMass * scaleMul * scaleMul * scaleMul * kindMassMul;
		rb.AngularDamping = 1.2f;
		rb.LinearDamping = 0.3f;
		rb.StartAsleep = true;
		rb.MotionEnabled = false;

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
		tree.Kind = TreeKind.Veteran;
		tree.IsGate = true;
		tree.ChopsRemaining = chopsRequired;
		tree._primaryCanopy = upper;
		tree._canopyTint = capTint;
		tree._spawnFootPos = footPosition;
		tree._biomeDifficulty = 1f;
		return tree;
	}

	private static TreeKind PickKindFromHash( int hash, float biomeDifficulty )
	{
		biomeDifficulty = biomeDifficulty.Clamp( 0f, 1f );
		var easy = Tunables.TreeKindWeightsEasy;
		var hard = Tunables.TreeKindWeightsHard;
		int n = easy.Length;
		Span<int> blended = stackalloc int[n];
		int total = 0;
		for ( int i = 0; i < n; i++ )
		{
			blended[i] = (int)MathX.Lerp( easy[i], hard[i], biomeDifficulty );
			total += blended[i];
		}
		if ( total <= 0 ) return TreeKind.Normal;
		int pick = (int)((uint)(hash >> 6) % (uint)total);
		int acc = 0;
		for ( int i = 0; i < n; i++ )
		{
			acc += blended[i];
			if ( pick < acc ) return (TreeKind)i;
		}
		return TreeKind.Normal;
	}

	private static GameObject SpawnSpeciesCanopy( Scene scene, GameObject parent, int species, float scaleMul, float trunkH, Color tint )
	{
		float r = Tunables.TreeRadius * scaleMul;
		// Foliage tinting trick : a slightly darker bottom layer (shaded
		// under-canopy) + the base tint on top reads volumetric vs a single
		// flat cube. Same scheme as the trunk root + base + upper.
		var shade = new Color( tint.r * 0.78f, tint.g * 0.78f, tint.b * 0.78f, 1f );
		var sunlit = new Color( MathF.Min( 1f, tint.r * 1.08f ), MathF.Min( 1f, tint.g * 1.08f ), MathF.Min( 1f, tint.b * 1.08f ), 1f );
		switch ( species )
		{
			default:
			case 0:
			{
				// Round broadleaf : shadow layer at the bottom, lit dome on top.
				MakeCanopyCube( scene, parent, "TreeCanopyShade",
					new Vector3( 0f, 0f, trunkH * 0.74f ),
					new Vector3( r * 4.0f, r * 4.0f, trunkH * 0.18f ), shade );
				return MakeCanopyCube( scene, parent, "TreeCanopy",
					new Vector3( 0f, 0f, trunkH * 0.88f ),
					new Vector3( r * 3.6f, r * 3.6f, trunkH * 0.30f ), sunlit );
			}
			case 1:
			{
				// Pine : 3 tapered tiers with progressive sun-lit gradient.
				float baseW = r * 3.4f;
				MakeCanopyCube( scene, parent, "PineLow",
					new Vector3( 0f, 0f, trunkH * 0.62f ),
					new Vector3( baseW, baseW, trunkH * 0.22f ), shade );
				var mid = MakeCanopyCube( scene, parent, "PineMid",
					new Vector3( 0f, 0f, trunkH * 0.80f ),
					new Vector3( baseW * 0.75f, baseW * 0.75f, trunkH * 0.20f ), tint );
				MakeCanopyCube( scene, parent, "PineTop",
					new Vector3( 0f, 0f, trunkH * 0.95f ),
					new Vector3( baseW * 0.48f, baseW * 0.48f, trunkH * 0.18f ), sunlit );
				return mid;
			}
			case 2:
			{
				// Tall narrow conifer : a slim shaded bottom half + bright apex.
				MakeCanopyCube( scene, parent, "TreeCanopyShade",
					new Vector3( 0f, 0f, trunkH * 0.66f ),
					new Vector3( r * 2.8f, r * 2.8f, trunkH * 0.32f ), shade );
				return MakeCanopyCube( scene, parent, "TreeCanopy",
					new Vector3( 0f, 0f, trunkH * 0.88f ),
					new Vector3( r * 2.4f, r * 2.4f, trunkH * 0.32f ), sunlit );
			}
		}
	}

	private static GameObject MakeCanopyCube( Scene scene, GameObject parent, string name, Vector3 localPos, Vector3 worldUnitsScale, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = name;
		go.SetParent( parent );
		go.LocalPosition = localPos;
		go.LocalScale = worldUnitsScale / Tunables.CubeBase;
		Mat.AddTintedCube( go, tint );
		return go;
	}

	public bool IsFalling => _chopped && !_landed;
	public bool IsStanding => !_chopped;

	// Only standing trees are valid swing targets — a landed log can't be
	// re-chopped (Chop() short-circuits on _chopped) and shouldn't show up
	// in the aim highlight either.
	bool IChoppable.IsValid() => IsStanding && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Axe;

	public void SetAimHighlight( bool on )
	{
		if ( _highlighted == on || !_primaryCanopy.IsValid() ) return;
		_highlighted = on;
		var mr = _primaryCanopy.Components.Get<ModelRenderer>();
		if ( !mr.IsValid() ) return;
		mr.Tint = on
			? new Color( MathF.Min( 1f, _canopyTint.r * 1.55f ), MathF.Min( 1f, _canopyTint.g * 1.55f ), MathF.Min( 1f, _canopyTint.b * 1.55f ), 1f )
			: _canopyTint;
	}

	// Player chop : remove ChopPower from ChopsRemaining. If it hits 0, fell.
	// `chopPower` is the axe tier's per-swing damage (1, 2, 3, 5…).
	public void Chop( Vector3 direction )
	{
		Chop( direction, 1 );
	}

	public void Chop( Vector3 direction, int chopPower )
	{
		if ( _chopped ) return;
		ChopsRemaining -= chopPower;
		if ( ChopsRemaining <= 0 ) StartFell( direction );
		else { KickWobble( direction ); DarkenTrunkOnce(); }
	}

	// Each hit multiplies trunk renderers' tint by ~0.92 — accumulates so
	// near-death trees look visibly bruised. Cap at 0.5 of original so
	// extreme chops don't go pure black.
	private void DarkenTrunkOnce()
	{
		const float factor = 0.92f;
		const float floor = 0.5f;
		DarkenRenderer( _trunkLowerMr, factor, floor );
		DarkenRenderer( _trunkUpperMr, factor, floor );
		DarkenRenderer( _rootMr,       factor, floor );
	}

	private static void DarkenRenderer( ModelRenderer mr, float factor, float floor )
	{
		if ( !mr.IsValid() ) return;
		var t = mr.Tint;
		mr.Tint = new Color(
			MathF.Max( floor * 0.78f, t.r * factor ),
			MathF.Max( floor * 0.78f, t.g * factor ),
			MathF.Max( floor * 0.78f, t.b * factor ),
			1f );
	}

	// Per-hit visible reaction : the standing trunk leans + bounces against
	// the chop direction, decaying over ~0.6s. Valheim-style "tree reacts to
	// being hit". Because the Rigidbody is kinematic (MotionEnabled=false)
	// while standing, we can drive WorldRotation directly without fighting
	// physics ; on StartFell we restore _baseRotation so the unblocked fall
	// starts from the pristine upright pose.
	private void KickWobble( Vector3 fromDirection )
	{
		if ( !_baseRotCached ) { _baseRotation = WorldRotation; _baseRotCached = true; }
		var flat = fromDirection.WithZ( 0f );
		if ( flat.LengthSquared < 0.001f ) flat = Vector3.Forward;
		_wobbleAxis = Vector3.Cross( Vector3.Up, flat.Normal ).Normal;
		_wobbleStart = 0f;
		// Stack hits a bit but cap so a rapid spam doesn't snap to grotesque
		// angles.
		_wobbleAmplitudeDeg = MathF.Min( _wobbleAmplitudeDeg + 4.5f, 9f );
	}

	private void TickWobble()
	{
		if ( _chopped ) return;
		if ( !_baseRotCached ) { _baseRotation = WorldRotation; _baseRotCached = true; }
		// Ambient wind sway — slow 0.55Hz oscillation around world-right at
		// 0.45° peak. Phase staggered per tree so adjacent trees don't beat
		// in lockstep. Always-on (cheap, single sin) — gives the forest a
		// resting heartbeat even before any chops.
		float now = Time.Now;
		float ambient = MathF.Sin( now * 0.55f * MathF.PI * 2f + _ambientPhase ) * 0.45f;
		// Hit-driven wobble — overrides ambient with its own axis + decay.
		float hitAngle = 0f;
		Vector3 hitAxis = Vector3.Right;
		if ( _wobbleAmplitudeDeg >= 0.05f )
		{
			float t = (float)_wobbleStart;
			const float decayPerSec = 4.5f;
			const float freqHz = 9f;
			float decay = MathF.Exp( -t * decayPerSec );
			if ( decay < 0.02f ) _wobbleAmplitudeDeg = 0f;
			else
			{
				hitAngle = _wobbleAmplitudeDeg * decay * MathF.Sin( t * freqHz * MathF.PI * 2f );
				hitAxis = _wobbleAxis;
			}
		}
		WorldRotation = _baseRotation
			* Rotation.FromAxis( Vector3.Right, ambient )
			* Rotation.FromAxis( hitAxis, hitAngle );
	}

	private void StartFell( Vector3 direction )
	{
		_chopped = true;
		_fellDir = direction.WithZ( 0f ).Normal;
		if ( _fellDir.LengthSquared < 0.001f ) _fellDir = Vector3.Forward;
		_slowTipElapsed = 0f;

		// Reset the wobble lean before unfreezing the rigidbody, otherwise
		// the fall starts from a tilted pose and physics fights the leftover
		// rotation transient.
		if ( _baseRotCached ) WorldRotation = _baseRotation;
		_wobbleAmplitudeDeg = 0f;

		// Detach the root flare so it stays in the ground as a stump — the
		// trunk + canopy fall as a free log. SetParent(null) keeps the world
		// transform so the stump sits exactly where the tree's foot was.
		if ( _rootStump.IsValid() )
		{
			_rootStump.Tags.Add( "stump" );
			_rootStump.SetParent( null, true );
			// Add a small fresh-cut surface cap on top — light wood tint so
			// the stump reads as "just chopped" rather than "rotting root".
			// Anchored in world space so it stays on the stationary stump.
			var cap = Scene.CreateObject();
			cap.Name = "StumpCutFace";
			cap.SetParent( _rootStump );
			var stumpScale = _rootStump.LocalScale * Tunables.CubeBase;
			cap.LocalPosition = new Vector3( 0f, 0f, stumpScale.z * 0.55f );
			cap.LocalScale = new Vector3( stumpScale.x * 0.95f, stumpScale.y * 0.95f, stumpScale.z * 0.15f ) / Tunables.CubeBase;
			var liveBase = _trunkLowerMr.IsValid() ? _trunkLowerMr.Tint : new Color( 0.55f, 0.40f, 0.28f, 1f );
			Mat.AddTintedCube( cap, new Color(
				MathF.Min( 1f, liveBase.r * 1.30f + 0.18f ),
				MathF.Min( 1f, liveBase.g * 1.25f + 0.14f ),
				MathF.Min( 1f, liveBase.b * 1.20f + 0.08f ),
				1f ) );
			_rootStump = null;
		}

		if ( Body.IsValid() )
		{
			Body.MotionEnabled = true;
			Body.LinearDamping = 0f;
			Body.AngularDamping = 0.3f;
		}

		if ( _primaryCanopy.IsValid() )
			ChipBurst.SpawnLeaves( Scene, _primaryCanopy.WorldPosition, _fellDir, 14, _canopyTint );
	}

	protected override void OnFixedUpdate()
	{
		if ( _chopped && !_landed ) TickFall();
		else if ( _landed ) TickLandedDecay();
	}

	protected override void OnUpdate()
	{
		if ( !_chopped ) TickWobble();
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
				_fellDir * Tunables.FellPush );
		}
		var upDot = WorldRotation.Up.Dot( Vector3.Up );
		// Land naturally once tilted past the threshold, OR force-land if the
		// tree is stuck against a neighbour (>5s falling without reaching the
		// threshold). Without the timeout, a stuck trunk never pays out wood.
		if ( upDot < Tunables.TreeFallenUpDotMax || _slowTipElapsed > 5f )
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
		float speedFrac = (landingSpeed / 400f).Clamp( 0.4f, 1.3f );
		Sfx.Play( "sounds/log_break.sound", WorldPosition,
			volume: 0.7f * speedFrac,
			pitchMin: 0.78f * speedFrac, pitchMax: 1.05f * speedFrac );

		GiveWoodOnce();
	}

	// Award wood only once even if the trunk lands, rolls, lands again.
	private void GiveWoodOnce()
	{
		if ( _woodGiven ) return;
		_woodGiven = true;
		var gs = GameState.Get( Scene );
		if ( !gs.IsValid() ) return;
		// Gates trigger a forest-ring expansion instead of paying wood.
		if ( IsGate )
		{
			// Triple-sized chip + leaf burst at the gate base so the
			// "barrier shattered, new ring unlocked" beat reads on-screen.
			var burstPos = WorldPosition + Vector3.Up * 80f;
			ChipBurst.Spawn( Scene, burstPos, _fellDir, Tunables.ChipBurstCount * 3 );
			ChipBurst.SpawnLeaves( Scene, burstPos, _fellDir, 32, _canopyTint );
			Sfx.Play( "sounds/log_break.sound", burstPos, volume: 1.20f, pitchMin: 0.55f, pitchMax: 0.75f );

			gs.OnGateBroken();
			var starter = Scene.GetAllComponents<SceneStarter>().FirstOrDefault();
			if ( starter.IsValid() ) starter.OnGateBroken();
			return;
		}
		int kindIdx = (int)Kind;
		int baseWood = Tunables.TreeKindWoodReward[kindIdx];
		if ( IsMythic ) baseWood += Tunables.MythicWoodBonus;
		int gain = Math.Max( 1, (int)Math.Ceiling( baseWood * gs.WoodMultiplier ) );
		bool luckCrit = gs.LuckChance > 0f && Game.Random.Float() < gs.LuckChance;
		if ( luckCrit ) gain *= 2;
		gs.AddWood( gain );
		// Treasure puff — golden leaf burst sized by the wood gain. Crit
		// (Luck stat doubled the drop) bumps the count + uses the brighter
		// Mythic canopy tint so the rare event reads visually.
		int puffCount = Math.Min( 4 + gain / 2, 18 );
		var puffTint = Tunables.MythicTrunkTint;
		if ( luckCrit ) { puffCount = Math.Min( puffCount + 8, 26 ); puffTint = Tunables.MythicCanopyTint; }
		ChipBurst.SpawnLeaves( Scene, WorldPosition + Vector3.Up * 60f, Vector3.Up, puffCount, puffTint );
	}

	private void TickLandedDecay()
	{
		if ( !Body.IsValid() ) return;
		if ( _timeSinceLanded < 0.6f ) return;
		Body.Sleeping = Body.Velocity.LengthSquared < 4f && Body.AngularVelocity.LengthSquared < 0.5f;

		float respawnDelay = Tunables.TreeKindRespawnDelay[(int)Kind];
		if ( IsMythic ) respawnDelay += Tunables.MythicRespawnExtra;
		if ( (float)_timeSinceLanded > respawnDelay )
		{
			Tree.SpawnAt( Scene, _spawnFootPos, _biomeDifficulty );
			GameObject.Destroy();
		}
	}
}

// IChoppable + ToolKind live in BeaverController.cs.
