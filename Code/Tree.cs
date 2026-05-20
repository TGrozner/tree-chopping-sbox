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

	private bool _chopped;
	private bool _landed;
	private float _slowTipElapsed;
	private Vector3 _fellDir;
	private TimeSince _timeSinceLanded;
	private GameObject _primaryCanopy;
	private Color _canopyTint;
	private bool _highlighted;
	private bool _woodGiven;
	private Vector3 _spawnFootPos;
	private float _biomeDifficulty;

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
		Mat.AddTintedCube( rootBase, new Color( tint.r * 0.78f, tint.g * 0.78f, tint.b * 0.78f, 1f ) );

		var lower = scene.CreateObject();
		lower.Name = "TreeTrunk";
		lower.SetParent( go );
		lower.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.32f );
		lower.LocalScale = new Vector3( trunkW, trunkW, trunkH * 0.56f ) / Tunables.CubeBase;
		Mat.AddTintedCube( lower, tint );

		var upper = scene.CreateObject();
		upper.Name = "TreeTrunkUpper";
		upper.SetParent( go );
		upper.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.78f );
		upper.LocalScale = new Vector3( trunkW * 0.78f, trunkW * 0.78f, trunkH * 0.36f ) / Tunables.CubeBase;
		Mat.AddTintedCube( upper, new Color( MathF.Min( 1f, tint.r * 1.08f ), MathF.Min( 1f, tint.g * 1.08f ), MathF.Min( 1f, tint.b * 1.08f ), 1f ) );

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
		tree._canopyTint = canopyTint;
		tree._spawnFootPos = footPosition;
		tree._biomeDifficulty = biomeDifficulty;
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

		if ( _primaryCanopy.IsValid() )
			ChipBurst.SpawnLeaves( Scene, _primaryCanopy.WorldPosition, _fellDir, 14, _canopyTint );
	}

	protected override void OnFixedUpdate()
	{
		if ( _chopped && !_landed ) TickFall();
		else if ( _landed ) TickLandedDecay();
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
		int kindIdx = (int)Kind;
		int baseWood = Tunables.TreeKindWoodReward[kindIdx];
		if ( IsMythic ) baseWood += Tunables.MythicWoodBonus;
		// Ceiling so any multiplier > 1 always gives at least +1 vs the
		// previous tier.
		int gain = Math.Max( 1, (int)Math.Ceiling( baseWood * gs.WoodMultiplier ) );
		// Luck stat : per-fell chance to double the drop.
		if ( gs.LuckChance > 0f && Game.Random.Float() < gs.LuckChance ) gain *= 2;
		gs.AddWood( gain );
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
