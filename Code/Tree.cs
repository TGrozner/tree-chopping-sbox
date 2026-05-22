namespace TreeChopping;

public enum TreeKind { Normal, Sapling, Veteran, Brittle }

// Mow-the-lawn style tree : multiple chops required (per kind), falls
// physically when HP drops to 0, becomes a landed log, then splits into
// WoodItems. Falling trunks use Valheim-style ImpactEffect damage to wake or
// split other trees/logs while standing trees stay kinematic until felled.
public sealed class Tree : Component, IChoppable, Component.ICollisionListener
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public int ChopsRemaining { get; set; } = 3;
	[Property] public TreeKind Kind { get; set; } = TreeKind.Normal;
	[Property] public bool IsMythic { get; set; }
	// Expose le trunk tint pour que AxeController puisse passer la couleur
	// aux ChipBurst (Valheim chips reflect tree wood color).
	public Color TrunkTint => _trunkTint;
	public Vector3 LogCenter
	{
		get
		{
			var axis = WorldRotation.Up;
			if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
			return WorldPosition + axis.Normal * ((_trunkLen > 0f ? _trunkLen : Tunables.TreeHeight) * 0.5f);
		}
	}

	public Vector3 GetChopPointFrom( Vector3 origin )
	{
		float halfWidth = MathF.Max( _trunkWidth * 0.5f, Tunables.TreeRadius * 0.25f );
		if ( !_landed )
		{
			var fromTree = (origin - WorldPosition).WithZ( 0f );
			if ( fromTree.LengthSquared < 0.001f ) fromTree = -Vector3.Forward;
			float z = origin.z.Clamp( WorldPosition.z + 20f, WorldPosition.z + _trunkLen * 0.75f );
			return WorldPosition.WithZ( z ) + fromTree.Normal * halfWidth;
		}

		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		var a = WorldPosition + axis * (_trunkLen * 0.08f);
		var b = WorldPosition + axis * (_trunkLen * 0.92f);
		var ab = b - a;
		float t = ab.LengthSquared > 0.001f
			? (origin - a).Dot( ab ) / ab.LengthSquared
			: 0f;
		var closest = a + ab * t.Clamp( 0f, 1f );
		var radial = origin - closest;
		if ( radial.LengthSquared < 0.001f ) radial = Vector3.Up.Cross( axis );
		if ( radial.LengthSquared < 0.001f ) radial = Vector3.Right;
		return closest + radial.Normal * halfWidth;
	}

	private bool _chopped;
	private bool _landed;
	private float _slowTipElapsed;
	private Vector3 _fellDir;
	private TimeSince _timeSinceLanded;
	// Velocity captured at the end of each FixedUpdate so OnCollisionStart can
	// read "speed going into the impact" without depending on the Collision
	// struct's Contact field (struct shape varies across s&box SDK versions).
	private Vector3 _preCollisionVelocity;
	// Valheim feel : trees pleuvent des feuilles continuously while falling.
	// Throttled Ã  80ms entre bursts, count scaled by angular velocity (faster
	// rotation = plus de feuilles arrachÃ©es).
	private TimeSince _timeSinceLastLeafShed = 999f;
	// Whoosh SFX pendant la chute â€” fire ONCE par fell quand tilt past 45Â°.
	// Reset au StartFell pour le prochain cycle.
	private bool _whooshFired;
	private GameObject _primaryCanopy;
	private GameObject _rootStump;
	private ModelRenderer _trunkLowerMr;
	private ModelRenderer _trunkUpperMr;
	private ModelRenderer _rootMr;
	private Color _canopyTint;
	private float _ambientPhase;
	private bool _highlighted;
	private bool _landingSnapApplied;
	private Vector3 _spawnFootPos;
	private float _biomeDifficulty;
	// Cached at spawn so landed-log splitting preserves the original tree
	// size (Saplings = small item burst, Veterans = big).
	private float _trunkLen;
	private float _trunkWidth;
	private Color _trunkTint;
	private Rotation _baseRotation;
	private bool _baseRotCached;
	// Valheim TreeBase.ShakeAnimation : buzz vibrato Ã  40Hz Sin (pitch) + 36Hz
	// Cos (roll) avec cubic decay (1-t)Â³ Ã— 1.5Â° sur 1s. Replace l'ancien
	// single-axis lean. _shakeStart = TimeSince dÃ©but du shake courant.
	private TimeSince _shakeStart = 999f;
	// Test hook : expose elapsed depuis le dernier shake dÃ©but. Used par
	// TestTreeShakeReset pour vÃ©rifier que KickWobble reset Ã  0.
	internal float DebugShakeElapsed => (float)_shakeStart;
	// Valheim ImpactEffect.m_interval (0.5s) â€” cooldown entre 2 impacts cascade
	// successifs. Ã‰vite spam quand un log roule sur un voisin et re-fire OnCollisionStart.
	private TimeSince _timeSinceLastImpactDamage = 999f;
	private TimeSince _timeSinceLastCascadeSweep = 999f;

	public static Tree SpawnAt( Scene scene, Vector3 footPosition, float biomeDifficulty = 0.5f, TreeKind? forceKind = null )
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

		TreeKind kind = forceKind ?? PickKindFromHash( hash, biomeDifficulty );
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
			// Per-tree multiplicative tint jitter â€” picked by hash so the same
			// tree always gets the same jitter, but neighbouring trees vary.
			float jitter = Tunables.TreeTintJitter[((uint)(hash >> 2) & 0xFFu) % (uint)Tunables.TreeTintJitter.Length];
			tint = new Color(
				(tint.r * jitter).Clamp( 0f, 1f ),
				(tint.g * jitter).Clamp( 0f, 1f ),
				(tint.b * jitter).Clamp( 0f, 1f ),
				1f );
		}

		float trunkW = Tunables.TreeRadius * 1.4f * scaleMul;
		float trunkVisualW = trunkW * Tunables.TreeKindVisualTrunkWidthMul[kindIdx];
		float trunkH = Tunables.TreeHeight * scaleMul;

		// Cube-trunk fallback : a slight base flare (root) + main column + a
		// narrower upper section gives a tapered tree shape from a uniform
		// cube. Cheap (3 child meshes) and reads as wood not "stack of bricks".
		var rootBase = scene.CreateObject();
		rootBase.Name = "TreeRoot";
		rootBase.SetParent( go );
		rootBase.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.04f );
		rootBase.LocalScale = new Vector3( trunkVisualW * 1.30f, trunkVisualW * 1.30f, trunkH * 0.08f ) / Tunables.CubeBase;
		var rootMr = Mat.AddTintedCube( rootBase, new Color( tint.r * 0.78f, tint.g * 0.78f, tint.b * 0.78f, 1f ) );

		var lower = scene.CreateObject();
		lower.Name = "TreeTrunk";
		lower.SetParent( go );
		lower.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.32f );
		lower.LocalScale = new Vector3( trunkVisualW, trunkVisualW, trunkH * 0.56f ) / Tunables.CubeBase;
		var lowerMr = Mat.AddTintedCube( lower, tint );

		var upper = scene.CreateObject();
		upper.Name = "TreeTrunkUpper";
		upper.SetParent( go );
		upper.LocalPosition = new Vector3( 0f, 0f, trunkH * 0.78f );
		upper.LocalScale = new Vector3( trunkVisualW * 0.78f, trunkVisualW * 0.78f, trunkH * 0.36f ) / Tunables.CubeBase;
		var upperMr = Mat.AddTintedCube( upper, new Color( MathF.Min( 1f, tint.r * 1.08f ), MathF.Min( 1f, tint.g * 1.08f ), MathF.Min( 1f, tint.b * 1.08f ), 1f ) );
		SpawnTrunkDetails( scene, lower, upper, tint );

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
		tree._trunkLen = trunkH;
		tree._trunkWidth = trunkVisualW;
		tree._trunkTint = tint;
		// Stagger ambient sway so adjacent trees don't beat in lockstep.
		tree._ambientPhase = ((uint)hash & 0xFFFFu) / 65535f * MathF.PI * 2f;
		return tree;
	}

	private static void SpawnTrunkDetails( Scene scene, GameObject lower, GameObject upper, Color tint )
	{
		var dark = new Color( tint.r * 0.56f, tint.g * 0.50f, tint.b * 0.44f, 1f );
		var mid = new Color( tint.r * 0.70f, tint.g * 0.62f, tint.b * 0.52f, 1f );
		AddTrunkDetail( scene, lower, "BarkSideA", new Vector3( 0.515f, -0.10f, 0.00f ), new Vector3( 0.06f, 0.28f, 0.92f ), dark );
		AddTrunkDetail( scene, lower, "BarkSideB", new Vector3( -0.515f, 0.12f, -0.06f ), new Vector3( 0.05f, 0.24f, 0.72f ), dark );
		AddTrunkDetail( scene, lower, "BarkFace", new Vector3( -0.10f, 0.515f, 0.08f ), new Vector3( 0.38f, 0.055f, 0.66f ), mid );
		AddTrunkDetail( scene, lower, "LogBandLower", new Vector3( 0f, 0f, -0.46f ), new Vector3( 0.88f, 0.88f, 0.035f ), dark );
		AddTrunkDetail( scene, upper, "BarkSideUpper", new Vector3( 0.515f, 0.02f, -0.02f ), new Vector3( 0.055f, 0.22f, 0.82f ), dark );
		AddTrunkDetail( scene, upper, "LogBandUpper", new Vector3( 0f, 0f, 0.47f ), new Vector3( 0.90f, 0.90f, 0.04f ), mid );
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
	public bool IsFallenLog => _landed && !_logSplit;

	// Valid swing target = standing tree OR landed log waiting to be split.
	// Mid-fall trees are skipped (Chop short-circuits) â€” no in-air chops.
	bool IChoppable.IsValid() => (IsStanding || IsFallenLog) && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Axe;

	private bool _logSplit;

	public void SetAimHighlight( bool on )
	{
		if ( _highlighted == on ) return;
		_highlighted = on;
		if ( _primaryCanopy.IsValid() )
		{
			var mr = _primaryCanopy.Components.Get<ModelRenderer>();
			if ( mr.IsValid() )
			{
				mr.Tint = on
					? new Color( MathF.Min( 1f, _canopyTint.r * 1.55f ), MathF.Min( 1f, _canopyTint.g * 1.55f ), MathF.Min( 1f, _canopyTint.b * 1.55f ), 1f )
					: _canopyTint;
			}
		}
		SetTrunkHighlight( on );
	}

	private void SetTrunkHighlight( bool on )
	{
		if ( !_landed ) return;
		if ( _trunkLowerMr.IsValid() ) _trunkLowerMr.Tint = on ? Color.Lerp( _trunkTint, Color.White, _landed ? 0.35f : 0.18f ) : _trunkTint;
		if ( _trunkUpperMr.IsValid() ) _trunkUpperMr.Tint = on ? Color.Lerp( _trunkTint, Color.White, _landed ? 0.28f : 0.14f ) : _trunkTint;
		if ( _rootMr.IsValid() ) _rootMr.Tint = on ? Color.Lerp( _trunkTint, Color.White, 0.16f ) : _trunkTint;
	}

	// Player chop : remove ChopPower from ChopsRemaining. If it hits 0, fell.
	// `chopPower` is the axe tier's per-swing damage (1, 2, 3, 5â€¦).
	void IChoppable.Chop( Vector3 direction, Vector3 hitPoint )
	{
		Chop( direction, 1, hitPoint );
	}

	public void Chop( Vector3 direction )
	{
		Chop( direction, 1, default );
	}

	public void Chop( Vector3 direction, int chopPower )
	{
		Chop( direction, chopPower, default );
	}

	// Valheim IDestructible.Damage(HitData) override â€” universal damage entrypoint.
	// Lit hit.ChopPower (= m_pushForce/m_damage Valheim) au lieu de prendre le
	// param sÃ©parÃ©. Pattern Valheim 1:1.
	public void Damage( HitData hit )
	{
		Chop( hit.Direction, hit.ChopPower, hit.HitPoint );
	}

	public void Chop( Vector3 direction, int chopPower, Vector3 hitPoint )
	{
		// Mid-fall : no chops accepted (tree is in physics flight). Split-
		// destroyed trees also short-circuit (handled by GameObject destroy).
		if ( IsFalling || _logSplit ) return;

		// Axe-tier gate (Phase E) only on the standing tree â€” once it's a
		// FallenLog the kind tier doesn't matter (any axe can chop a log).
		if ( !_landed )
		{
			var gs = GameState.Get( Scene );
			int axeTier = gs.IsValid() ? gs.AxeTier : 0;
			int neededTier = Tunables.TreeKindMinAxeTier[(int)Kind];
			if ( axeTier < neededTier )
			{
				// Valheim 1:1 : TreeBase.RPC_Damage return BEFORE Shake() si
				// CheckToolTier fail. Le feedback "axe bounced" vient du weapon
				// side (chip burst + thunk Sfx dÃ©clenchÃ©s par AxeController).
				var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
				if ( hud.IsValid() )
				{
					hud.ShowAxeTooWeakHint( Kind, neededTier );
					// Valheim DamageText.ShowText(TooHard, hit.m_point, 0f) â€” float-up
					// au point d'impact pour signal local en plus du banner global.
					var popupPos = hitPoint.LengthSquared > 0.01f
						? hitPoint
						: WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.4f);
					hud.ShowDamageText( "TROP DUR", popupPos, WoodHud.DamageTextTooHard );
				}
				return;
			}
		}

		ChopsRemaining -= chopPower;
		// Valheim Game.IncrementPlayerStat(PlayerStatType.TreeChops) â€” track per-chop.
		// Returns true si on vient de passer un palier de "WoodCutting level".
		var gsChop = GameState.Get( Scene );
		bool leveledUp = gsChop.IsValid() && gsChop.IncrementTreeChops();
		// Damage number float-up â€” Valheim DamageText.ShowText(modifier, point, totalDamage).
		var hud2 = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
		if ( hud2.IsValid() )
		{
			var popupPos = hitPoint.LengthSquared > 0.01f
				? hitPoint
				: WorldPosition + Vector3.Up * (_landed ? 20f : Tunables.TreeHeight * 0.4f);
			hud2.ShowDamageText( chopPower.ToString(), popupPos, WoodHud.DamageTextNormal );
			// Valheim Skills.RaiseSkill level-up notification (DamageText.Bonus
			// orange popup au point d'impact). Cosmetic only â€” pas d'effet
			// gameplay vu qu'on reste tier-based. Donne le feel "+1 skill".
			if ( leveledUp )
			{
				var levelPos = popupPos + Vector3.Up * 50f;
				hud2.ShowDamageText( $"WoodCutting Lv.{gsChop.WoodCuttingLevel}", levelPos,
					WoodHud.DamageTextBonus, isBonus: true );
			}
		}
		if ( _landed )
		{
			ApplyLandedKick( direction, hitPoint, chopPower );
			SpawnLandedChopScar( hitPoint, direction, chopPower, ChopsRemaining <= 0 );
		}
		else
		{
			DarkenTrunkOnce();
			SpawnChopNotch( hitPoint, direction, chopPower, ChopsRemaining <= 0 );
			KickWobble( direction );
		}

		if ( ChopsRemaining > 0 )
		{
			return;
		}

		// HP=0 reached â€” branch on phase.
		if ( !_landed )
		{
			EmitBreakYield( direction, hitPoint );
			StartFell( direction, chopPower );
		}
		else SplitIntoLogs();
	}

	// Small dark cube stuck on the trunk's side at chop height â€” accumulates
	// per hit so a near-felled tree has visible damage scars instead of just
	// a global darkening. Parented to the lower-trunk renderer so it inherits
	// the trunk's scale and the wobble/fell rotations.
	private void SpawnChopNotch( Vector3 hitPoint, Vector3 direction, int chopPower, bool finalHit )
	{
		if ( !_trunkLowerMr.IsValid() ) return;
		var trunkGO = _trunkLowerMr.GameObject;
		var notch = Scene.CreateObject();
		notch.Name = "ChopNotch";
		notch.SetParent( trunkGO );

		var radial = hitPoint.LengthSquared > 0.01f
			? (hitPoint - WorldPosition).WithZ( 0f )
			: -direction.WithZ( 0f );
		if ( radial.LengthSquared < 0.001f ) radial = -Vector3.Forward;
		radial = radial.Normal;

		float x = radial.Dot( WorldRotation.Forward );
		float y = radial.Dot( WorldRotation.Right );
		float angle = MathF.Atan2( y, x );
		float lowerCenterZ = WorldPosition.z + _trunkLen * 0.32f;
		float lowerHeight = MathF.Max( 1f, _trunkLen * 0.56f );
		float localZ = ((hitPoint.z - lowerCenterZ) / lowerHeight).Clamp( -0.36f, 0.34f );

		notch.LocalPosition = new Vector3(
			MathF.Cos( angle ) * 0.55f,
			MathF.Sin( angle ) * 0.55f,
			localZ );
		float bite = MathX.Lerp( 1f, 1.45f, MathF.Min( chopPower, 6f ) / 6f );
		if ( finalHit ) bite *= 1.25f;
		float variantWide = Game.Random.Float( 0.85f, 1.35f );
		float variantTall = Game.Random.Float( 0.70f, 1.25f );
		notch.LocalScale = new Vector3( 0.22f * bite * variantWide, 0.34f * bite, 0.13f * bite * variantTall );
		notch.LocalRotation = Rotation.FromYaw( angle.RadianToDegree() + Game.Random.Float( -18f, 18f ) );
		Mat.AddTintedCube( notch, new Color( 0.08f, 0.04f, 0.02f, 1f ) );

		if ( chopPower > 1 || finalHit )
		{
			var chipCut = Scene.CreateObject();
			chipCut.Name = "ChopCut";
			chipCut.SetParent( trunkGO );
			chipCut.LocalPosition = notch.LocalPosition + new Vector3( 0f, 0f, Game.Random.Float( -0.08f, 0.10f ) );
			chipCut.LocalScale = new Vector3( 0.10f * bite, 0.52f * bite, 0.055f * bite );
			chipCut.LocalRotation = Rotation.FromYaw( angle.RadianToDegree() + 70f + Game.Random.Float( -12f, 12f ) );
			Mat.AddTintedCube( chipCut, new Color( 0.13f, 0.065f, 0.025f, 1f ) );
		}

		if ( finalHit )
		{
			var split = Scene.CreateObject();
			split.Name = "ChopSplit";
			split.SetParent( trunkGO );
			split.LocalPosition = notch.LocalPosition + new Vector3( 0f, 0f, 0.10f );
			split.LocalScale = new Vector3( 0.12f, 0.46f, 0.08f );
			split.LocalRotation = Rotation.FromYaw( angle.RadianToDegree() + 90f );
			Mat.AddTintedCube( split, new Color( 0.04f, 0.02f, 0.01f, 1f ) );
		}
	}

	private void SpawnLandedChopScar( Vector3 hitPoint, Vector3 direction, int chopPower, bool finalHit )
	{
		if ( !_trunkLowerMr.IsValid() ) return;
		var trunkGO = _trunkLowerMr.GameObject;
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		var basePoint = WorldPosition + axis * (_trunkLen * 0.08f);
		var along = hitPoint.LengthSquared > 0.01f
			? ((hitPoint - basePoint).Dot( axis ) / MathF.Max( _trunkLen, 1f )).Clamp( 0.08f, 0.92f )
			: 0.5f;
		var scar = Scene.CreateObject();
		scar.Name = finalHit ? "LogSplitScar" : "LogChopScar";
		scar.SetParent( trunkGO );
		scar.LocalPosition = new Vector3( Game.Random.Float( -0.18f, 0.18f ), 0.54f, MathX.Lerp( -0.42f, 0.42f, along ) );
		float bite = MathX.Lerp( 1f, 1.45f, MathF.Min( chopPower, 6f ) / 6f );
		if ( finalHit ) bite *= 1.3f;
		scar.LocalScale = new Vector3( 0.42f * bite, 0.055f, 0.10f * bite );
		scar.LocalRotation = Rotation.FromYaw( Game.Random.Float( -16f, 16f ) );
		Mat.AddTintedCube( scar, finalHit ? new Color( 0.04f, 0.02f, 0.01f, 1f ) : new Color( 0.10f, 0.05f, 0.02f, 1f ) );
	}

	private void EmitBreakYield( Vector3 direction, Vector3 hitPoint )
	{
		var point = hitPoint.LengthSquared > 0.01f
			? hitPoint
			: WorldPosition + Vector3.Up * (_trunkLen * 0.38f);
		var dir = direction.WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = _fellDir.LengthSquared > 0.001f ? _fellDir : Vector3.Forward;
		dir = dir.Normal;
		ChipBurst.Spawn( Scene, point, dir, Tunables.ChipBurstCount + Tunables.ChipBurstCount / 2, _trunkTint );
		ChipBurst.SpawnLeaves( Scene, point + Vector3.Up * 12f, dir, 10, new Color( 0.52f, 0.40f, 0.28f, 1f ) );
		Sfx.Play( "sounds/log_break.sound", point,
			volume: 0.60f, pitchMin: 0.92f * Tunables.TreeKindGroanPitchMul[(int)Kind], pitchMax: 1.10f * Tunables.TreeKindGroanPitchMul[(int)Kind] );
		if ( _trunkUpperMr.IsValid() )
		{
			var upper = _trunkUpperMr.GameObject;
			var side = Vector3.Cross( Vector3.Up, dir );
			if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
			upper.LocalRotation *= Rotation.FromAxis( side.Normal, 4f );
			upper.LocalPosition += dir * 3f;
		}
	}

	// Each hit multiplies trunk renderers' tint by ~0.92 â€” accumulates so
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
	// Per-hit jolt on the landed log. WorldRotation drive is off-limits here
	// (the body is in physics) so we push the rigidbody : linear impulse high
	// up on the trunk (offset = lever arm â†’ tip rotates) + a small angular
	// impulse perpendicular to chop dir = the log visibly rocks on each hit.
	// Magnitudes intentionally small : we react, we don't displace.
	internal static float ComputeLandedKickPowerScale( int baseChopPower, int actualChopPower )
	{
		baseChopPower = Math.Max( 1, baseChopPower );
		actualChopPower = Math.Max( 1, actualChopPower );
		float scale = 1f + 0.3f * actualChopPower;
		if ( actualChopPower > baseChopPower )
			scale *= Tunables.ChopComboFinalPushMul;
		return scale;
	}

	internal static float ComputeFellKickPowerScale( int baseChopPower, int actualChopPower, bool allowComboPush = true )
	{
		baseChopPower = Math.Max( 1, baseChopPower );
		actualChopPower = Math.Max( 1, actualChopPower );
		float scale = 1f + MathF.Min( actualChopPower - 1, 6 ) * 0.04f;
		if ( allowComboPush && actualChopPower > baseChopPower )
			scale *= Tunables.ChopComboFinalPushMul;
		return scale;
	}

	private void ApplyLandedKick( Vector3 chopDirection, Vector3 hitPoint, int chopPower = 0 )
	{
		if ( !Body.IsValid() || !Body.PhysicsBody.IsValid() ) return;
		var flat = chopDirection.WithZ( 0f );
		if ( flat.LengthSquared < 0.001f ) flat = Vector3.Forward;
		flat = flat.Normal;
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		// Mimicke Valheim TreeLog.RPC_Damage : `hit.m_dir * hit.m_pushForce * 2f`
		// m_pushForce vient de la HitData de l'arme et scale avec son tier.
		// Le final combo garde aussi le push bonus Valheim (x1.2).
		int baseChopPower = GameState.Get( Scene )?.ChopPower ?? 1;
		if ( chopPower <= 0 ) chopPower = baseChopPower;
		float powerScale = ComputeLandedKickPowerScale( baseChopPower, chopPower );
		// Valheim Destructible.RPC_Damage applique l'impulse Ã  `hit.m_point` :
		// le tronc rebondit autour de l'endroit oÃ¹ l'axe le touche. Si pas de
		// hit point disponible (DebugSwing, fallback) â†’ ancre fixe au-dessus.
		var applyPoint = hitPoint.LengthSquared > 0.01f
			? hitPoint
			: LogCenter + Vector3.Up * 8f;
		var centerToHit = applyPoint - LogCenter;
		float lever = (centerToHit.Length / MathF.Max( _trunkLen * 0.5f, 1f )).Clamp( 0.25f, 1.0f );
		var side = Vector3.Cross( axis, flat );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Cross( Vector3.Up, flat );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		side = side.Normal;
		var impulseDir = (flat * 0.82f + side * Game.Random.Float( -0.22f, 0.22f ) + Vector3.Up * 0.10f).Normal;
		Body.PhysicsBody.ApplyImpulseAt( applyPoint, impulseDir * Tunables.LandedLogKickImpulse * powerScale );
		var spinAxis = Vector3.Cross( centerToHit.Normal, impulseDir );
		if ( spinAxis.LengthSquared < 0.001f ) spinAxis = Vector3.Cross( Vector3.Up, flat );
		if ( spinAxis.LengthSquared < 0.001f ) spinAxis = side;
		Body.PhysicsBody.ApplyAngularImpulse( spinAxis.Normal * Tunables.LandedLogKickTorque * powerScale * MathX.Lerp( 0.8f, 1.0f + Tunables.LandedLogHitPointTorqueMul, lever ) );
	}

	// Valheim TreeBase.ShakeAnimation (RPC_Shake) â€” coroutine lignes 194-209.
	// Two-axis buzz vibrato : Sin(t*40Hz) * (1-t)Â³ * 1.5Â° en pitch, Cos(t*36Hz)
	// * (1-t)Â³ * 1.5Â° en roll. Duration 1s. Restart le shake Ã  chaque hit
	// (Valheim StopCoroutine + StartCoroutine â€” Ã©quivalent ici reset _shakeStart=0).
	// La direction du hit n'influence PAS le shake (Valheim non plus) â€” c'est
	// un buzz vibrato uniforme, "axe biting wood".
	private void KickWobble( Vector3 fromDirection )
	{
		if ( !_baseRotCached ) { _baseRotation = WorldRotation; _baseRotCached = true; }
		_shakeStart = 0f;
	}

	private void TickWobble()
	{
		if ( _chopped ) return;
		if ( !_baseRotCached ) { _baseRotation = WorldRotation; _baseRotCached = true; }
		// Ambient wind sway â€” Valheim EnvMan.GetWindDir/Intensity. All trees sway
		// together along the wind direction (sway axis = perpendicular to wind in
		// horizontal plane), intensity gusts. Phase staggered per tree so adjacent
		// trees don't beat in lockstep but they LEAN in the same direction.
		float now = Time.Now;
		var windDir = EnvWind.GetWindDir();
		float windIntensity = EnvWind.GetWindIntensity();
		var swayAxis = Vector3.Cross( Vector3.Up, windDir ).Normal;
		if ( swayAxis.LengthSquared < 0.01f ) swayAxis = Vector3.Right;
		// 0.55Hz oscillation, 0.45Â° peak amplitude, modulated by global wind intensity.
		float ambient = MathF.Sin( now * 0.55f * MathF.PI * 2f + _ambientPhase ) * 0.45f * windIntensity;
		// Valheim shake buzz â€” dual-axis Sin/Cos cubic-decay vibrato. Override
		// l'ambient seulement pendant la fenÃªtre du shake.
		float pitchDeg = 0f;
		float rollDeg = 0f;
		float t = (float)_shakeStart;
		if ( t < Tunables.TreeShakeDuration )
		{
			float frac = (t / Tunables.TreeShakeDuration).Clamp( 0f, 1f );
			float decay = 1f - frac;
			float cubicDecay = decay * decay * decay;
			float amp = cubicDecay * Tunables.TreeShakeAmplitudeDeg;
			pitchDeg = MathF.Sin( now * Tunables.TreeShakeFreqA ) * amp;
			rollDeg = MathF.Cos( now * Tunables.TreeShakeFreqB ) * amp;
		}
		WorldRotation = _baseRotation
			* Rotation.FromAxis( swayAxis, ambient )
			* Rotation.FromAxis( Vector3.Right, pitchDeg )
			* Rotation.FromAxis( Vector3.Forward, rollDeg );
	}

	// Single continuous fell : unfreeze rigidbody immediately + groan + leaves
	// shed. The scripted creak pause was scrapped 2026-05-21 ("part vite puis
	// se freeze, c'est pas terrible") â€” the scripted lean + physics handoff
	// created a velocity discontinuity. The gentle Valheim feel comes from
	// the SlowTipInitialFrac + SlowTipDuration physics ramp instead, which
	// is naturally continuous. The groan SFX still fires here = the "creak"
	// audio cue, just without a corresponding kinematic visual freeze.
	internal void StartFell( Vector3 direction, int fellPower = 0, bool allowComboPush = true )
	{
		_chopped = true;
		_fellDir = direction.WithZ( 0f ).Normal;
		if ( _fellDir.LengthSquared < 0.001f ) _fellDir = Vector3.Forward;
		_slowTipElapsed = 0f;

		if ( _baseRotCached ) WorldRotation = _baseRotation;
		_shakeStart = 999f; // kill any in-progress buzz

		// Valheim Game.IncrementPlayerStat(PlayerStatType.TreeTierN) â€” indexÃ©
		// par le tier min requis pour ce kind. Lifetime stat, survit prestige.
		int felledTier = Tunables.TreeKindMinAxeTier[(int)Kind];
		GameState.Get( Scene )?.IncrementTreeFelledByTier( felledTier );
		_whooshFired = false; // armÃ© pour ce cycle de fell

		// Le _rootStump rotatif (parented au tree) va tomber avec lui â€” on le
		// destroy, et on spawn Ã  sa place une TreeStump indÃ©pendante au foot
		// pos qui survit au GameObject.Destroy du tree (cf. SplitIntoLogs).
		if ( _rootStump.IsValid() )
		{
			_rootStump.Destroy();
			_rootStump = null;
		}
		TreeStump.SpawnAt( Scene, _spawnFootPos, _trunkWidth, _trunkTint, Kind, _biomeDifficulty, IsMythic );

		if ( Body.IsValid() )
		{
			Body.MotionEnabled = true;
			Body.LinearDamping = 0f;
			Body.AngularDamping = 0.3f;
			// Refresh the inertia tensor after collider/mass setup â€” Valheim
			// TreeBase.SpawnLog le fait juste avant l'impulse de fell.
			Body.ResetInertiaTensor();
			// Casse l'Ã©quilibre instable â€” sans ce kick l'arbre reste droit
			// (gravity-torque = 0 exactement Ã  theta=0). Direction = perpendiculaire
			// Ã  _fellDir pour faire pivoter le tree dans le sens du fell.
			var spinAxis = Vector3.Up.Cross( _fellDir ).Normal;
			float kindMul = Tunables.TreeKindInitialFellOmegaMul[(int)Kind];
			int baseChopPower = GameState.Get( Scene )?.ChopPower ?? 1;
			if ( fellPower <= 0 ) fellPower = baseChopPower;
			float powerScale = ComputeFellKickPowerScale( baseChopPower, fellPower, allowComboPush );
			Body.AngularVelocity = spinAxis * Tunables.InitialFellOmega * kindMul;
			if ( Body.PhysicsBody.IsValid() )
			{
				float mass = Body.PhysicsBody.Mass;
				var topPoint = WorldPosition + Vector3.Up * (_trunkLen * 0.78f);
				Body.PhysicsBody.ApplyImpulseAt( topPoint, _fellDir * mass * Tunables.InitialFellTopImpulseSpeed * kindMul * powerScale );
			}
			// Lurch linÃ©aire â€” Valheim TreeBase.SpawnLog applique un AddForceAtPosition
			// haut sur le tronc qui crÃ©e Ã  la fois rotation + slide. On le dÃ©compose
			// en deux pour avoir le contrÃ´le (nos unitÃ©s sont trop grandes pour
			// reproduire le ratio exact avec un seul impulse).
			Body.Velocity = _fellDir * Tunables.InitialFellLurchSpeed * kindMul * powerScale;
		}

		if ( _primaryCanopy.IsValid() )
		{
			ChipBurst.SpawnLeaves( Scene, _primaryCanopy.WorldPosition, _fellDir, 36, _canopyTint );
			ChipBurst.SpawnLeaves( Scene, _primaryCanopy.WorldPosition, Vector3.Up, 24, _canopyTint );
			ChipBurst.SpawnLeaves( Scene, _primaryCanopy.WorldPosition, -_fellDir, 18, _canopyTint );
		}
		// Per-kind groan : sapling = aigu et net, veteran = profond et lent,
		// brittle = crack sec. Multiplie autour du range Normal {0.48, 0.62}.
		float pitchMul = Tunables.TreeKindGroanPitchMul[(int)Kind];
		Sfx.Play( "sounds/log_break.sound", WorldPosition + Vector3.Up * 40f,
			volume: 0.85f, pitchMin: 0.48f * pitchMul, pitchMax: 0.62f * pitchMul );

		// Mythic = Valheim DamageText.Bonus popup (orange +50% size, 3s lifetime).
		// Le joueur voit "MYTHIC!" floating au-dessus de l'arbre rare dorÃ©.
		if ( IsMythic )
		{
			var bonusHud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
			if ( bonusHud.IsValid() )
			{
				var trunkH = _trunkLen > 0f ? _trunkLen : Tunables.TreeHeight;
				bonusHud.ShowDamageText( "MYTHIC!", WorldPosition + Vector3.Up * (trunkH * 0.7f),
					WoodHud.DamageTextBonus, isBonus: true );
			}
		}

		// Valheim TreeBase.RPC_Damage drops items DIRECTEMENT au fell (en plus
		// du log qui tombe), via m_dropWhenDestroyed (DropTable). On reproduit :
		// roll dropChance, puis pick random count dans [Min..Max]. Brittle pleut
		// quasi-systÃ©matiquement, Sapling sometimes rien.
		int kindIdx = (int)Kind;
		if ( Game.Random.Float() < Tunables.TreeKindFellBonusDropChance[kindIdx] )
		{
			int bonusMin = Tunables.TreeKindFellBonusItemsMin[kindIdx];
			int bonusMax = Tunables.TreeKindFellBonusItemsMax[kindIdx];
			int bonusItems = Game.Random.Int( bonusMin, bonusMax );
			if ( IsMythic ) bonusItems += 1;
			for ( int i = 0; i < bonusItems; i++ )
			{
				float ang = Game.Random.Float( 0f, MathF.Tau );
				var ring = new Vector3( MathF.Cos( ang ), MathF.Sin( ang ), 0f ) * Game.Random.Float( 12f, 24f );
				WoodItem.SpawnAt( Scene, _spawnFootPos + ring + Vector3.Up * (10f + i * 4f) );
			}
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( _chopped && !_landed ) TickFall();
		else if ( _landed ) TickLandedDecay();
		// Sampled AFTER tick logic â€” gives us the velocity the body had during
		// the most recent physics step, which is what we want when a collision
		// resolves on the next step. Reading post-impact Body.Velocity is wrong
		// because the integrator has already cancelled the contact-normal speed.
		if ( Body.IsValid() ) _preCollisionVelocity = Body.Velocity;
	}

	void Component.ICollisionListener.OnCollisionStart( Collision other )
	{
		if ( _logSplit ) return;
		// Standing trees (kinematic) reÃ§oivent l'event collision MAIS leur
		// _preCollisionVelocity = 0 (kinematic ne bouge pas), donc impactSpeed
		// est 0 et l'early-return MinSpeed kicke. Le damage cascade vient de
		// l'autre tronc qui tombe (lui IS chopped, IS falling), qui nous
		// appelle ApplyImpactDamage via SON OnCollisionStart.
		if ( !_chopped ) return;

		// Valheim ImpactEffect.m_interval cooldown â€” Ã©vite spam damage si log
		// reste en contact avec un voisin (rolling/bouncing fire multiple
		// OnCollisionStart events). 0.5s entre deux cascade damages.
		if ( (float)_timeSinceLastImpactDamage < Tunables.ImpactInterval ) return;

		float impactSpeed = _preCollisionVelocity.Length;
		if ( impactSpeed < Tunables.ImpactSoftMinSpeed ) return;
		_timeSinceLastImpactDamage = 0f;

		// Valheim feel : cascade impact crÃ©e un thud + dust burst au point de
		// collision. Sans ce feedback, le tronc qui crash dans un voisin paraissait
		// silencieux. Volume + leaf count scalÃ©s par impactSpeed (= dommage).
		float impactScale = ((impactSpeed - Tunables.ImpactMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		float softScale = ((impactSpeed - Tunables.ImpactSoftMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		var contactPoint = EstimateImpactPoint( null );

		// Valheim ImpactEffect.OnCollisionEnter formula verbatim :
		//   damageFactor = LerpStep(minVelocity, maxVelocity, magnitude)
		//   damage = m_damages Ã— damageFactor
		float damageFactor = ((impactSpeed - Tunables.ImpactMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		int damage = impactSpeed >= Tunables.ImpactMinSpeed
			? Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * damageFactor ) )
			: 0;

		// Cascade â€” damage l'autre tronc s'il est un Tree (Valheim TreeLog
		// crash dans TreeBase voisin = damage standing tree HP, peut le fell).
		var otherGo = other.Other.GameObject;
		Tree neighbor = null;
		if ( otherGo.IsValid() )
		{
			neighbor = otherGo.Components.Get<Tree>()
				?? otherGo.Components.Get<Tree>( FindMode.InAncestors );
		}
		var cascadeVelocity = _preCollisionVelocity;
		if ( neighbor.IsValid() )
		{
			cascadeVelocity -= neighbor._preCollisionVelocity;
			float relativeImpactSpeed = cascadeVelocity.Length;
			impactSpeed = relativeImpactSpeed;
			impactScale = ((impactSpeed - Tunables.ImpactMinSpeed)
				/ (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
			softScale = ((impactSpeed - Tunables.ImpactSoftMinSpeed)
				/ (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
			contactPoint = EstimateImpactPoint( neighbor );
			EmitLogImpactFeedback( contactPoint, softScale, impactScale );
			damage = impactSpeed >= Tunables.ImpactMinSpeed
				? Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * impactScale ) )
				: 0;
		}
		else
		{
			EmitLogImpactFeedback( contactPoint, softScale, impactScale );
		}
		if ( neighbor.IsValid() && neighbor != this )
		{
			var dirOther = cascadeVelocity.WithZ( 0f );
			if ( dirOther.LengthSquared < 0.01f )
				dirOther = (neighbor.WorldPosition - WorldPosition).WithZ( 0f );
			if ( dirOther.LengthSquared > 0.01f )
			{
				if ( damage > 0 ) neighbor.ApplyImpactDamage( damage, dirOther.Normal );
				else neighbor.ReactToSoftImpact( dirOther.Normal, contactPoint );
			}
		}

		// Self damage (m_damageToSelf=true Valheim TreeLog) : crash dur peut
		// auto-split le tronc qui tombe sans chop manuel.
		if ( Tunables.ImpactDamageSelf && damage > 0 )
		{
			float splitSpeed = _landed
				? Tunables.WoodLogBreakImpactSpeed
				: Tunables.TreeSplitImpactSpeed * Tunables.TreeKindSplitImpactMul[(int)Kind];
			if ( impactSpeed >= splitSpeed || (!_landed && impactScale >= Tunables.ImpactViolentScale) )
			{
				var selfDir = _preCollisionVelocity.WithZ( 0f );
				if ( selfDir.LengthSquared < 0.01f ) selfDir = Vector3.Forward;
				ApplyImpactDamage( damage, selfDir.Normal );
			}
		}
	}

	private Vector3 EstimateImpactPoint( Tree neighbor )
	{
		if ( neighbor.IsValid() ) return (LogCenter + neighbor.LogCenter) * 0.5f;
		return LogCenter + Vector3.Up * 8f;
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
		var dustTint = hard
			? new Color( 0.62f, 0.48f, 0.35f, 1f )
			: new Color( 0.48f, 0.42f, 0.34f, 1f );
		ChipBurst.SpawnLeaves( Scene, contactPoint, Vector3.Up, dustCount, dustTint );
		if ( violent )
		{
			var sideDir = _preCollisionVelocity.WithZ( 0f );
			if ( sideDir.LengthSquared > 0.01f )
				ChipBurst.SpawnLeaves( Scene, contactPoint, sideDir.Normal, dustCount / 2, _trunkTint );
		}
	}

	private void ReactToSoftImpact( Vector3 dir, Vector3 contactPoint )
	{
		if ( _logSplit ) return;
		if ( !_chopped )
		{
			KickWobble( dir );
			return;
		}
		if ( _landed ) ApplyLandedKick( dir, contactPoint );
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
		Tree best = null;
		Vector3 bestPoint = default;
		float bestDist = float.MaxValue;

		foreach ( var other in Scene.GetAllComponents<Tree>() )
		{
			if ( !other.IsValid() || other == this || other._logSplit ) continue;
			if ( !other.IsStanding && !other.IsFallenLog ) continue;
			var probe = other.IsStanding ? other.WorldPosition + Vector3.Up * (other._trunkLen * 0.35f) : other.LogCenter;
			var p = ClosestPointOnSegment( a, b, probe );
			float dist = (probe - p).Length;
			float reach = (_trunkWidth + other._trunkWidth) * 0.55f + Tunables.CascadeSweepRadius;
			if ( dist > reach || dist >= bestDist ) continue;
			best = other;
			bestPoint = p;
			bestDist = dist;
		}

		if ( !best.IsValid() ) return;

		_timeSinceLastImpactDamage = 0f;
		float impactScale = ((motionSpeed - Tunables.ImpactMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		float softScale = ((motionSpeed - Tunables.ImpactSoftMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		var contactPoint = (bestPoint + best.LogCenter) * 0.5f;
		EmitLogImpactFeedback( contactPoint, softScale, impactScale );

		var dir = Body.Velocity.WithZ( 0f );
		if ( dir.LengthSquared < 0.01f ) dir = (best.WorldPosition - WorldPosition).WithZ( 0f );
		if ( dir.LengthSquared < 0.01f ) dir = _fellDir;
		if ( dir.LengthSquared < 0.01f ) dir = Vector3.Forward;

		int damage = motionSpeed >= Tunables.ImpactMinSpeed
			? Math.Max( 1, (int)MathF.Ceiling( Tunables.ImpactBaseDamage * impactScale * Tunables.CascadeSweepDamageMul ) )
			: 0;
		if ( damage > 0 ) best.ApplyImpactDamage( damage, dir.Normal );
		else best.ReactToSoftImpact( dir.Normal, contactPoint );
	}

	private static Vector3 ClosestPointOnSegment( Vector3 a, Vector3 b, Vector3 p )
	{
		var ab = b - a;
		float lenSq = ab.LengthSquared;
		if ( lenSq < 0.001f ) return a;
		float t = (p - a).Dot( ab ) / lenSq;
		return a + ab * t.Clamp( 0f, 1f );
	}


	void Component.ICollisionListener.OnCollisionUpdate( Collision other ) { }
	void Component.ICollisionListener.OnCollisionStop( CollisionStop other ) { }

	// Damage entrypoint pour impact physique (cascade + self-damage). Mirror du
	// TreeBase.RPC_Damage / TreeLog.RPC_Damage Valheim path sans les RPC/ZDO
	// singleplayer. HP=0 dÃ©clenche la state transition :
	//   standing â†’ StartFell (cascade domino)
	//   falling  â†’ BecomeLandedLog + SplitIntoLogs (auto-split sur crash dur)
	//   landed   â†’ SplitIntoLogs (chop de finition par impact)
	public void ApplyImpactDamage( int damage, Vector3 dir )
	{
		if ( _logSplit ) return;
		if ( damage <= 0 ) return;

		ChopsRemaining -= damage;
		// Valheim TreeBase.RPC_Damage : Shake() fire Ã  chaque hit qui pÃ©nÃ¨tre,
		// que ce soit chop OU ImpactEffect collision. Cascade damage doit aussi
		// faire visible le tronc qui prend (sinon le hit invisible visuellement).
		if ( ChopsRemaining > 0 )
		{
			if ( !_chopped ) KickWobble( dir );
			return;
		}

		if ( !_chopped )
		{
			StartFell( dir, damage, allowComboPush: false );
		}
		else if ( _landed )
		{
			SplitIntoLogs();
		}
		else
		{
			BecomeLandedLog();
			SplitIntoLogs();
		}
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
		// Valheim feel 2026-05-21 : torque scaled by mass so saplings and
		// veterans get the same angular acceleration (was unscaled = small
		// trees flew). Linear impulse dropped entirely â€” gravity + torque
		// is the entire fall, no "kick" that throws light trees.
		float massScale = Body.PhysicsBody.IsValid() ? Body.PhysicsBody.Mass / Tunables.TreeMass : 1f;
		var torqueAxis = Vector3.Up.Cross( _fellDir );
		Body.ApplyTorque( torqueAxis * Tunables.FellTorque * frac * Time.Delta * massScale );
		var upDot = WorldRotation.Up.Dot( Vector3.Up );
		SweepNearbyCascadeTargets();

		// Whoosh SFX une fois quand le tree passe past ~45Â° tilt. Match Valheim
		// trees qui ont un whoosh audible en plein air pendant la chute. Pitch
		// scaled par kind (Sapling = high whip, Veteran = low rumble whoosh).
		if ( !_whooshFired && upDot < Tunables.TreeWhooshUpDotThreshold )
		{
			_whooshFired = true;
			float pitchMul = Tunables.TreeKindChopPitchMul[(int)Kind];
			Sfx.Play( "sounds/tree_fall_whoosh.sound", WorldPosition + Vector3.Up * 100f,
				volume: 0.55f, pitchMin: 0.55f * pitchMul, pitchMax: 0.75f * pitchMul );
		}

		// Valheim feel : continuous leaf shed pendant la chute. Throttled Ã  80ms,
		// count scale par angular velocity. Plus le tronc rotate vite, plus de
		// feuilles arrachÃ©es. Couleur _canopyTint (per-kind tint prÃ©servÃ©).
		// SFX : leaves_rustle (CC0 pack rustle01..10) added 2026-05-22 â€” Valheim
		// Hit_Leaves equivalent qu'on n'avait pas en sound.
		if ( _primaryCanopy.IsValid() && (float)_timeSinceLastLeafShed > 0.08f )
		{
			float angVel = Body.AngularVelocity.Length;
			// Threshold 0.2 (was 0.5) car Veteran starts at 0.385 (omega 0.55 Ã—
			// kindMul 0.7) â€” 0.5 ratait Veteran complÃ¨tement. Sapling=0.77,
			// Brittle=0.69, Normal=0.55. Tous au-dessus 0.2.
			if ( angVel > 0.2f )
			{
				// Scale count par angVel mais avec min de 2 (toujours visible une fois passÃ© le threshold).
				int count = (int)MathF.Max( 2f, MathF.Min( 6f, angVel * 5f ) );
				if ( count > 0 )
				{
					var shedDir = _fellDir + Vector3.Up * 0.3f;
					ChipBurst.SpawnLeaves( Scene, _primaryCanopy.WorldPosition, shedDir.Normal, count, _canopyTint );
					// Random rustle SFX trÃ¨s discret (vol scale par count) â€” too loud serait
					// gÃªnant car fired ~12Ã—/s pendant la chute.
					float rustleVol = (count / 6f).Clamp( 0.1f, 0.4f );
					Sfx.Play( "sounds/leaves_rustle.sound", _primaryCanopy.WorldPosition,
						volume: rustleVol, pitchMin: 0.85f, pitchMax: 1.15f );
				}
				_timeSinceLastLeafShed = 0f;
			}
		}

		bool restingOnSomething =
			_slowTipElapsed > Tunables.TreeRestingLandingDelay
			&& upDot < Tunables.TreeRestingTiltUpDotMax
			&& Body.Velocity.Length < Tunables.TreeRestingLandingSpeed
			&& Body.AngularVelocity.Length < Tunables.TreeRestingLandingAngularSpeed;

		// Land naturally once tilted past the threshold, or once it has clearly
		// come to rest against ground/terrain/another trunk. Valheim's TreeLog
		// is already a physical log after spawn; our single-object transition
		// needs this contact-rest escape hatch so large trunks don't hang in a
		// "falling but stopped" limbo until timeout.
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
		float impactScale = ((landingSpeed - Tunables.ImpactMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactMinSpeed)).Clamp( 0f, 1f );
		float softScale = ((landingSpeed - Tunables.ImpactSoftMinSpeed)
			/ (Tunables.ImpactMaxSpeed - Tunables.ImpactSoftMinSpeed)).Clamp( 0f, 1f );
		EmitLogImpactFeedback( LogCenter, softScale, impactScale );
		if ( impactScale >= Tunables.ImpactHardScale )
		{
			var dustTint = new Color( 0.62f, 0.48f, 0.35f, 1f );
			int dustCount = (int)(10 + impactScale * 16f);
			ChipBurst.SpawnLeaves( Scene, WorldPosition + Vector3.Up * 8f, Vector3.Up,       dustCount, dustTint );
			ChipBurst.SpawnLeaves( Scene, WorldPosition + Vector3.Up * 4f, Vector3.Right,    dustCount / 2, dustTint );
			ChipBurst.SpawnLeaves( Scene, WorldPosition + Vector3.Up * 4f, Vector3.Left,     dustCount / 2, dustTint );
			SnapTrunkOnImpact( impactScale );
		}

		// Phase F : DON'T credit wood here anymore. Reset ChopsRemaining
		// to the kind's log HP â€” the player has to actively chop the landed
		// log to split it directly into pickup items.
		ChopsRemaining = Tunables.LogChopHP[(int)Kind];
	}

	private void SnapTrunkOnImpact( float speedFrac )
	{
		if ( _landingSnapApplied ) return;
		_landingSnapApplied = true;

		float snap = MathX.Lerp( 5f, 14f, (speedFrac - 0.4f).Clamp( 0f, 0.9f ) / 0.9f );
		var side = Vector3.Cross( Vector3.Up, _fellDir.WithZ( 0f ) );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		side = side.Normal;

		if ( _trunkUpperMr.IsValid() )
		{
			var upper = _trunkUpperMr.GameObject;
			upper.LocalRotation *= Rotation.FromAxis( side, snap );
			upper.LocalPosition += side * Game.Random.Float( 2f, 6f );
		}
		if ( _primaryCanopy.IsValid() )
		{
			_primaryCanopy.LocalRotation *= Rotation.FromAxis( side, snap * 0.65f );
			_primaryCanopy.LocalPosition += side * Game.Random.Float( 4f, 10f );
		}
	}

	// Valheim TreeLog.Destroy : Ã  HP=0 du landed log, drop directement les
	// items via DropTable, pas de sub-log intermediate. Items distribuÃ©s le
	// long de l'axe du tronc avec offset random (m_spawnDistance).
	// Pick a WoodType from a probability mix [Wood, Finewood, CoreWood].
	// Mix probs must sum to ~1.0. Valheim DropTable weighted random pattern
	// simplifiÃ© Ã  3 types. Internal pour permettre TestWoodTypeDistribution.
	internal static WoodType PickWoodType( float[] mix )
	{
		float roll = Game.Random.Float( 0f, 1f );
		float acc = 0f;
		for ( int i = 0; i < mix.Length; i++ )
		{
			acc += mix[i];
			if ( roll <= acc ) return (WoodType)i;
		}
		return WoodType.Wood;
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

		float trunkH = _trunkLen > 0f ? _trunkLen : Tunables.TreeHeight;
		var axis = WorldRotation.Up;
		if ( axis.LengthSquared < 0.001f ) axis = Vector3.Up;
		axis = axis.Normal;
		var trunkCenter = LogCenter;
		var side = Vector3.Cross( Vector3.Up, axis );
		if ( side.LengthSquared < 0.001f ) side = Vector3.Right;
		side = side.Normal;
		float spread = trunkH * Tunables.LogDropAxisSpreadFrac;

		// Spawn items along the trunk axis avec offset random (TreeLog.Destroy
		// pattern : position = transform.position + transform.up Ã— Random(-d, d)
		// + Vector3.up Ã— 0.3 Ã— i). AdaptÃ© Ã  nos unitÃ©s. Item scale = WorldScale
		// du tree (Valheim TreeBase.SpawnLog : log/drops adoptent l'Ã©chelle parent).
		// Type Valheim 1:1 : Beech-equivalent (Normal/Sapling) â†’ Wood pur ;
		// Oak-equivalent (Veteran) â†’ Wood + Finewood mix ; Pine-equivalent
		// (Brittle) â†’ Wood + CoreWood mix. Per-item roll dans TreeKindWoodTypeMix.
		float itemScaleMul = WorldScale.x;
		var mix = Tunables.TreeKindWoodTypeMix[kindIdx];
		for ( int i = 0; i < totalItems; i++ )
		{
			float t = totalItems <= 1 ? 0.5f : (i + Game.Random.Float( 0.18f, 0.82f )) / totalItems;
			float off = MathX.Lerp( -spread, spread, t );
			float sideSign = (i & 1) == 0 ? 1f : -1f;
			float sideOff = sideSign * Game.Random.Float( MathF.Max( _trunkWidth * 0.42f, Tunables.LogDropSideSpread * 0.35f ), MathF.Max( _trunkWidth * 0.85f, Tunables.LogDropSideSpread ) );
			var burstDir = (side * sideSign * 1.15f + axis * MathF.Sign( off == 0f ? sideSign : off ) * 0.42f + Vector3.Up * 0.25f).Normal;
			var pos = trunkCenter + axis * off + side * sideOff + burstDir * Game.Random.Float( 6f, 16f ) + Vector3.Up * Game.Random.Float( 8f, 20f );
			WoodType type = PickWoodType( mix );
			WoodItem.SpawnAt( Scene, pos, itemScaleMul, type, burstDir );
			if ( i < 8 )
			{
				ChipBurst.Spawn( Scene, pos, -burstDir, 3, _trunkTint );
			}
		}

		Sfx.Play( "sounds/log_break.sound", WorldPosition,
			volume: 1.0f, pitchMin: 0.62f, pitchMax: 0.82f );
		if ( _primaryCanopy.IsValid() )
			ChipBurst.SpawnLeaves( Scene, _primaryCanopy.WorldPosition, Vector3.Up, 18, _canopyTint );

		// Luck-triggered extra drop â†’ Bonus popup au centre du log (Valheim
		// pattern : DamageText.Bonus est utilisÃ© pour les skill-bonus yields
		// dans Pickable.Interact).
		if ( luckTriggered )
		{
			var luckHud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
			if ( luckHud.IsValid() )
				luckHud.ShowDamageText( "LUCKY!", trunkCenter, WoodHud.DamageTextBonus, isBonus: true );
		}

		GameObject?.Destroy();
	}

	private void TickLandedDecay()
	{
		if ( !Body.IsValid() ) return;
		if ( _timeSinceLanded < 0.6f ) return;
		Body.Sleeping = Body.Velocity.LengthSquared < 4f && Body.AngularVelocity.LengthSquared < 0.5f;

		// Respawn maintenant gÃ©rÃ© par TreeStump (spawnÃ©e au StartFell, survit
		// au split). Le tronc landed lui-mÃªme despawn juste aprÃ¨s le delay si
		// le joueur l'a laissÃ© pourrir â€” la nouvelle tree est dÃ©jÃ  venue
		// repousser sur la souche.
		float despawnDelay = Tunables.TreeKindRespawnDelay[(int)Kind];
		if ( IsMythic ) despawnDelay += Tunables.MythicRespawnExtra;
		if ( (float)_timeSinceLanded > despawnDelay )
		{
			GameObject.Destroy();
		}
	}
}

// IChoppable + ToolKind live in AxeController.cs.
