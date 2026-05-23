namespace TreeChopping;

public enum TreeKind { Normal, Sapling, Veteran, Brittle }

// Mow-the-lawn style TreeBase: multiple chops required, then it swaps to a
// physical FallenLog prefab equivalent. Standing trees stay kinematic until
// felled; all log physics lives in FallenLog.
public sealed class Tree : Component, IChoppable
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public int ChopsRemaining { get; set; } = 3;
	[Property] public TreeKind Kind { get; set; } = TreeKind.Normal;
	[Property] public bool IsMythic { get; set; }
	// Expose le trunk tint pour que AxeController puisse passer la couleur
	// aux ChipBurst (Valheim chips reflect tree wood color).
	public Color TrunkTint => _trunkTint;
	internal FallenLog SpawnedLog => _spawnedLog;
	internal float TrunkLength => _trunkLen;
	internal float TrunkWidth => _trunkWidth;
	internal Color CanopyTint => _canopyTint;
	internal Vector3 SpawnFootPosition => _spawnFootPos;
	internal float BiomeDifficulty => _biomeDifficulty;
	internal float TrunkDamageMul => _trunkDamageMul;
	internal float LogMass => Body.IsValid() && Body.PhysicsBody.IsValid() ? Body.PhysicsBody.Mass : Tunables.TreeMass;
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
		var fromTree = (origin - WorldPosition).WithZ( 0f );
		if ( fromTree.LengthSquared < 0.001f ) fromTree = -Vector3.Forward;
		float z = origin.z.Clamp( WorldPosition.z + 20f, WorldPosition.z + _trunkLen * 0.75f );
		return WorldPosition.WithZ( z ) + fromTree.Normal * halfWidth;
	}

	private bool _chopped;
	private Vector3 _fellDir;
	private FallenLog _spawnedLog;
	private GameObject _primaryCanopy;
	private GameObject _rootStump;
	private ModelRenderer _trunkLowerMr;
	private ModelRenderer _trunkUpperMr;
	private ModelRenderer _rootMr;
	private Color _canopyTint;
	private float _ambientPhase;
	private bool _highlighted;
	private Vector3 _spawnFootPos;
	private float _biomeDifficulty;
	// Cached at spawn so landed-log splitting preserves the original tree
	// size (Saplings = small item burst, Veterans = big).
	private float _trunkLen;
	private float _trunkWidth;
	private Color _trunkTint;
	private float _trunkDamageMul = 1f;
	private TimeSince _hitFlashTime = 999f;
	private float _hitFlashStrength;
	private Rotation _baseRotation;
	private bool _baseRotCached;
	// Valheim TreeBase.ShakeAnimation : buzz vibrato Ã  40Hz Sin (pitch) + 36Hz
	// Cos (roll) avec cubic decay (1-t)Â³ Ã— 1.5Â° sur 1s. Replace l'ancien
	// single-axis lean. _shakeStart = TimeSince dÃ©but du shake courant.
	private TimeSince _shakeStart = 999f;
	// Test hook : expose elapsed depuis le dernier shake dÃ©but. Used par
	// TestTreeShakeReset pour vÃ©rifier que KickWobble reset Ã  0.
	internal float DebugShakeElapsed => (float)_shakeStart;
	// Valheim tree-log ImpactEffect.m_interval (0.25s) — cooldown entre 2 impacts cascade
	// successifs. Ã‰vite spam quand un log roule sur un voisin et re-fire OnCollisionStart.

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
		rb.EnhancedCcd = true;
		rb.SleepThreshold = Tunables.TreeLogSleepThreshold;
		rb.StartAsleep = true;
		rb.MotionEnabled = false; // CLAUDE.md non-negotiable #8

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
		tree.IsMythic = isMythic;
		tree.Kind = kind;
		// Valheim keeps resistance on the prefab, not on random visual scale.
		tree.ChopsRemaining = Math.Max( 1, Tunables.TreeKindChopsBase[kindIdx] );
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

	private void DestroyCanopyVisuals()
	{
		foreach ( var child in GameObject.Children )
		{
			if ( !child.IsValid() ) continue;
			bool isCanopy =
				child.Name == "TreeCanopy"
				|| child.Name == "TreeCanopyShade"
				|| child.Name == "PineLow"
				|| child.Name == "PineMid"
				|| child.Name == "PineTop";
			if ( isCanopy ) child.Destroy();
		}
		_primaryCanopy = null;
	}

	public bool IsFalling => _chopped && _spawnedLog.IsValid() && _spawnedLog.IsFalling;
	public bool IsStanding => !_chopped;
	public bool IsFallenLog => false;

	// Valid swing target = standing tree OR landed log waiting to be split.
	// Mid-fall trees are skipped (Chop short-circuits) â€” no in-air chops.
	bool IChoppable.IsValid() => IsStanding && this.IsValid();
	bool IChoppable.AcceptsTool( ToolKind tool ) => tool == ToolKind.Axe;


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
		UpdateTrunkVisuals();
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
	// Damage and push stay separate like Valheim m_damage vs m_pushForce.
	public void Damage( HitData hit )
	{
		Chop( hit.Direction, hit.GetTreeDamage(), hit.HitPoint, hit.ToolTier, hit.PushForce );
	}

	public void Chop( Vector3 direction, int chopPower, Vector3 hitPoint )
	{
		Chop( direction, chopPower, hitPoint, null );
	}

	private void Chop( Vector3 direction, int chopPower, Vector3 hitPoint, int? toolTierOverride, float? pushForceOverride = null )
	{
		// Mid-fall : no chops accepted (tree is in physics flight). Split-
		// destroyed trees also short-circuit (handled by GameObject destroy).
		if ( !IsStanding ) return;

		// Valheim gates both TreeBase and TreeLog with m_minToolTier. The
		// normal path spawns FallenLog, but this fallback stays aligned too.
		if ( IsStanding )
		{
			var gs = GameState.Get( Scene );
			int axeTier = toolTierOverride ?? (gs.IsValid() ? gs.AxeTier : 0);
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

		if ( chopPower <= 0 ) return;

		ChopsRemaining -= chopPower;
		PulseHitFlash( ChopsRemaining <= 0 );
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
				: WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.4f);
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
		DarkenTrunkOnce();
		SpawnChopNotch( hitPoint, direction, chopPower, ChopsRemaining <= 0 );
		KickWobble( direction );

		if ( ChopsRemaining > 0 )
		{
			return;
		}

		// HP=0 reached â€” branch on phase.
		EmitBreakYield( direction, hitPoint );
		StartFell( direction, chopPower );
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

		var fresh = Scene.CreateObject();
		fresh.Name = "FreshChopFace";
		fresh.SetParent( trunkGO );
		fresh.LocalPosition = notch.LocalPosition + new Vector3( 0f, 0.035f, 0.01f );
		fresh.LocalScale = new Vector3( 0.15f * bite * variantWide, 0.055f, 0.095f * bite * variantTall );
		fresh.LocalRotation = Rotation.FromYaw( angle.RadianToDegree() + Game.Random.Float( -12f, 12f ) );
		Mat.AddTintedCube( fresh, Color.Lerp( _trunkTint, Tunables.ChipSplinterTint, finalHit ? 0.72f : 0.52f ) );

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

	private void EmitBreakYield( Vector3 direction, Vector3 hitPoint )
	{
		var point = hitPoint.LengthSquared > 0.01f
			? hitPoint
			: WorldPosition + Vector3.Up * (_trunkLen * 0.38f);
		var dir = direction.WithZ( 0f );
		if ( dir.LengthSquared < 0.001f ) dir = _fellDir.LengthSquared > 0.001f ? _fellDir : Vector3.Forward;
		dir = dir.Normal;
		ValheimEffects.TreeBreakYield( Scene, point, dir, _trunkTint, Kind );
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
		_trunkDamageMul = MathF.Max( 0.50f, _trunkDamageMul * 0.92f );
		UpdateTrunkVisuals();
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
		SetRendererTint( _trunkLowerMr, 1.00f, 0.20f );
		SetRendererTint( _trunkUpperMr, 1.08f, 0.16f );
		SetRendererTint( _rootMr, 0.78f, 0.14f );
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

	// Per-hit visible reaction : the standing trunk leans + bounces against
	// the chop direction, decaying over ~0.6s. Valheim-style "tree reacts to
	// being hit". Because the Rigidbody is kinematic (MotionEnabled=false)
	// while standing, we can drive WorldRotation directly without fighting
	// physics ; on StartFell we restore _baseRotation so the unblocked fall
	// starts from the pristine upright pose.
	// Valheim Attack final chain boosts m_pushForce by 1.2, but TreeBase.SpawnLog
	// itself only receives hit.m_dir. Keep chop damage and landed-log push separate.
	internal static float ComputeLandedKickPowerScale( int baseChopPower, int actualChopPower )
	{
		baseChopPower = Math.Max( 1, baseChopPower );
		actualChopPower = Math.Max( 1, actualChopPower );
		return actualChopPower > baseChopPower ? Tunables.ChopComboFinalPushMul : 1f;
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

	// Single Valheim-style fell : spawn a physical log, reset inertia, apply one
	// top impulse. The groan SFX is the "creak" cue, not a scripted physics ramp.
	internal void StartFell( Vector3 direction, int fellPower = 0, bool allowComboPush = true )
	{
		_chopped = true;
		_fellDir = direction.WithZ( 0f ).Normal;
		if ( _fellDir.LengthSquared < 0.001f ) _fellDir = Vector3.Forward;
		if ( _baseRotCached ) WorldRotation = _baseRotation;
		_shakeStart = 999f; // kill any in-progress buzz

		// Valheim Game.IncrementPlayerStat(PlayerStatType.TreeTierN) â€” indexÃ©
		// par le tier min requis pour ce kind. Lifetime stat, survit prestige.
		int felledTier = Tunables.TreeKindMinAxeTier[(int)Kind];
		GameState.Get( Scene )?.IncrementTreeFelledByTier( felledTier );
		// Le _rootStump rotatif (parented au tree) va tomber avec lui â€” on le
		// destroy, et on spawn Ã  sa place une TreeStump indÃ©pendante au foot
		// pos qui survit au GameObject.Destroy du tree et au split du FallenLog.
		if ( _rootStump.IsValid() )
		{
			_rootStump.Destroy();
			_rootStump = null;
		}
		TreeStump.SpawnAt( Scene, _spawnFootPos, _trunkWidth, _trunkTint, Kind, _biomeDifficulty, IsMythic );
		_spawnedLog = FallenLog.SpawnFromTree( this, _fellDir );

		if ( !_spawnedLog.IsValid() )
		{
			Log.Error( "[Tree] FallenLog.SpawnFromTree failed; refusing legacy Tree-as-log fallback to preserve Valheim TreeBase->TreeLog parity." );
			GameObject?.Destroy();
			return;
		}

		if ( _primaryCanopy.IsValid() )
		{
			var canopyPos = _primaryCanopy.WorldPosition;
			ValheimEffects.TreeDestroyed( Scene, canopyPos, _fellDir, _canopyTint );
		}
		DestroyCanopyVisuals();
		// Per-kind groan : sapling = aigu et net, veteran = profond et lent,
		// brittle = crack sec. Multiplie autour du range Normal {0.48, 0.62}.
		ValheimEffects.TreeGroan( WorldPosition + Vector3.Up * 40f, Kind );

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
			var mix = Tunables.TreeKindWoodTypeMix[kindIdx];
			float dropYOffset = Tunables.TreeKindBaseDropYOffset[kindIdx];
			for ( int i = 0; i < bonusItems; i++ )
			{
				float ang = Game.Random.Float( 0f, MathF.Tau );
				var ring = new Vector3( MathF.Cos( ang ), MathF.Sin( ang ), 0f ) * Game.Random.Float( 0f, Tunables.ValheimTreeBaseDropRadius );
				var pos = _spawnFootPos + ring + Vector3.Up * (dropYOffset + Tunables.ValheimTreeBaseDropYStep * i);
				WoodItem.SpawnValheimDropAt( Scene, pos, WorldScale.x, PickWoodType( mix ) );
			}
		}

		if ( _spawnedLog.IsValid() )
			GameObject?.Destroy();
	}

	internal Vector3 GetImpactVelocityAt( Vector3 point )
	{
		if ( !Body.IsValid() ) return Vector3.Zero;
		return Body.GetVelocityAtPoint( point );
	}

	internal void ReactToSoftImpactFromLog( Vector3 dir, Vector3 contactPoint )
	{
		if ( !IsStanding ) return;
		KickWobble( dir );
	}

	public void ApplyImpactDamage( int damage, Vector3 dir, int toolTier = Tunables.ValheimImpactToolTier )
	{
		if ( !IsStanding || damage <= 0 ) return;
		if ( toolTier < Tunables.TreeKindMinAxeTier[(int)Kind] ) return;

		ChopsRemaining -= damage;
		if ( ChopsRemaining > 0 )
		{
			KickWobble( dir );
			return;
		}

		StartFell( dir, damage, allowComboPush: false );
	}

	protected override void OnUpdate()
	{
		if ( IsStanding ) TickWobble();
		TickHitFlash();
	}

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
}

// IChoppable + ToolKind live in AxeController.cs.
