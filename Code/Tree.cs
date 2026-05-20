namespace TreeChopping;

public sealed class Tree : Component, IChoppable, Component.ICollisionListener
{
	[Property] public Rigidbody Body { get; set; }
	[Property] public Color TrunkTint { get; set; } = new( 0.46f, 0.32f, 0.22f, 1f );
	[Property] public int ChopsRemaining { get; set; } = 3;
	[Property] public TreeSpecies Species { get; set; } = TreeSpecies.Beech;
	[Property] public bool IsMythic { get; set; }

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

	// Visual-only lean accumulated per chop. The standing trunk is kinematic
	// (MotionEnabled=false) so a physics impulse on Body does nothing — we
	// instead tilt the visual mesh away from the chop direction by a few
	// degrees each hit. Reset on StartFell since the rigidbody owns rotation
	// from that point on.
	private Rotation _chopLean = Rotation.Identity;
	// Per-chop lean bumped 7→14° pour que le hit standing lit comme un VRAI
	// hit ("on voit que ça tape"). 14° à 280u de hauteur = ~70u de déplacement
	// du sommet, parfaitement visible à la distance caméra 170u.
	private const float PerChopLeanDeg = 14f;

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
		=> SpawnAt( scene, footPosition, tint, species, mythic: false );

	public static Tree SpawnAt( Scene scene, Vector3 footPosition, Color tint, TreeSpecies species, bool mythic )
	{
		// Species-specific look wins over the biome tint argument — both are
		// passed so callers that want to keep the biome cue can still do so by
		// reading TrunkTint downstream; the trunk model itself uses the
		// species tint because it's the more visible per-tree cue.
		var speciesIdx = (int)species;
		var speciesTint = Tunables.SpeciesTrunkTints[speciesIdx];
		var scaleMul = Tunables.SpeciesScaleMul[speciesIdx];
		var chops = Tunables.SpeciesChopsRequired[speciesIdx];

		// Per-instance scale jitter so each species reads as a population, not clones.
		// Hash the foot XY into a deterministic [0..1] so the layout is stable across
		// runs with the same Seed.
		int hash = footPosition.GetHashCode();
		// Jitter tightened 0.75-1.30 → 0.95-1.25 — pas de tiny anorexique trees
		// (user iter64 feedback: trunks trop fins). Minimum size stays decent.
		float scaleJitter = 0.95f + ((hash & 0xFFFF) / 65535f) * 0.30f;
		scaleMul *= scaleJitter;

		// Tint jitter: nudge each channel ±10% so silhouettes mix even at distance.
		// Different hash bits per channel for independence.
		float tintRJitter = 0.90f + (((hash >> 4) & 0xFF) / 255f) * 0.20f;
		float tintGJitter = 0.90f + (((hash >> 12) & 0xFF) / 255f) * 0.20f;
		float tintBJitter = 0.90f + (((hash >> 20) & 0xFF) / 255f) * 0.20f;
		speciesTint = new Color(
			(speciesTint.r * tintRJitter).Clamp( 0f, 1f ),
			(speciesTint.g * tintGJitter).Clamp( 0f, 1f ),
			(speciesTint.b * tintBJitter).Clamp( 0f, 1f ),
			1f );

		// Per-tree chop variance — big trees of a species need a bit more, small ones less.
		// Tied to the same scaleJitter so visually bigger trees ARE tougher.
		if ( scaleJitter > 1.15f ) chops += 1;
		else if ( scaleJitter < 0.85f ) chops = (int)MathF.Max( 1, chops - 1 );

		// Mythic override: gold tint, bigger scale, same chops (still 1-swing if
		// it's the player's target — but neighbors hitting it cascade as normal).
		if ( mythic )
		{
			scaleMul *= Tunables.MythicScaleMul;
			speciesTint = Tunables.MythicTint;
		}

		var go = scene.CreateObject();
		go.Name = $"Tree.{species}";
		// Procedural composite trees — drop the thin .vmdl and assemble each
		// tree from Model.Cube + Model.Sphere primitives, per-species silhouette.
		// Matches the Godot proto ref's chunky low-poly look (fat coral trunks
		// + saturated teal/cyan/yellow-green canopies + per-species shape).
		go.WorldPosition = footPosition;
		go.Tags.Add( "tree" );
		// Per-species silhouette stretch (X=Y width vs Z height) on top of scaleMul.
		// Gives Spruce tall narrow, Ironwood wide squat, Crystal obelisk —
		// visual variety without needing extra .vmdl assets.
		var stretch = Tunables.SpeciesShapeStretch[speciesIdx];
		go.WorldScale = new Vector3( scaleMul * stretch.x, scaleMul * stretch.y, scaleMul * stretch.z );
		// Random yaw per tree (hashed pour rester deterministe) — sinon tous les
		// arbres pointent +X et la forêt lit "regulière". 360° plein random.
		float yawDeg = ((hash >> 8) & 0xFFFF) / 65535f * 360f;
		go.WorldRotation = Rotation.FromYaw( yawDeg );

		var canopyTint = mythic ? Tunables.MythicTint : Tunables.SpeciesCanopyTints[speciesIdx];
		float canopyTintRJ = 0.92f + (((hash >> 6) & 0xFF) / 255f) * 0.16f;
		float canopyTintGJ = 0.92f + (((hash >> 14) & 0xFF) / 255f) * 0.16f;
		float canopyTintBJ = 0.92f + (((hash >> 22) & 0xFF) / 255f) * 0.16f;
		if ( !mythic )
		{
			canopyTint = new Color(
				(canopyTint.r * canopyTintRJ).Clamp( 0f, 1f ),
				(canopyTint.g * canopyTintGJ).Clamp( 0f, 1f ),
				(canopyTint.b * canopyTintBJ).Clamp( 0f, 1f ),
				1f );
		}

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Models.TreeFor( species );
		mr.Tint = speciesTint;

		// Iter67 user feedback : "ton histoire d'overlay de troncs ça fonctionne pas,
		// colle plus à Valheim". Cube overlay lisait Minecraft, pas Valheim.
		// Pure .vmdl + scale up + tints discrètes = vibe Valheim natural forest.

		// One-time exploration : try to push the .vmdl crown material toward
		// teal via Material.Set("g_vColorTint", ...) — common s&box shader var
		// for tint. Wrapped in try/catch + gated by static bool so failures
		// are silent and we only mutate the shared material ONCE per session.
		// All trees end up with same crown tint (teal), trunk tint stays per-species.
		TryTintCrownMaterialOnce( mr.Model, canopyTint );

		// Iter3 cube canopy overlay removed — tint sur Model.Cube enfant d'un parent
		// scalé non-uniforme rendait grey-cream invisible. Per-species stretch
		// (iter4) gives silhouette variety without extra render cost.

		// Mythic visibility beacon — petit cube gold au-dessus de la canopée pour
		// que les mythics soient repérables dans la masse de forest. Spawn-time
		// only, no per-frame cost. Inheriting parent stretch is fine.
		if ( mythic )
		{
			var beacon = scene.CreateObject();
			beacon.Name = "MythicBeacon";
			beacon.SetParent( go );
			beacon.LocalPosition = new Vector3( 0f, 0f, 350f );
			beacon.LocalScale = new Vector3( 42f, 42f, 42f ) / Tunables.CubeBase;
			beacon.LocalRotation = Rotation.FromYaw( 45f );
			var bmr = beacon.AddComponent<ModelRenderer>();
			bmr.Model = Model.Cube;
			bmr.Tint = Tunables.MythicTint;
		}

		// Collider sized to TreeHeight × TreeRadius² independently of the visual
		// composite. Parent WorldScale is uniform = scaleMul, so divide by scaleMul.
		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3(
			Tunables.TreeRadius * 2f / scaleMul,
			Tunables.TreeRadius * 2f / scaleMul,
			Tunables.TreeHeight / scaleMul );
		col.Center = new Vector3( 0f, 0f, Tunables.TreeHeight * 0.5f / scaleMul );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = Tunables.TreeMass;
		rb.AngularDamping = 1.2f;
		rb.LinearDamping = 0.3f;
		rb.StartAsleep = true;
		// Standing trees must NOT be knocked over by a passing beaver — they're
		// only allowed to move once Chop() has felled them. StartFell flips this
		// back to true; CascadeStrike calls StartFell before its impulse, so the
		// chain-fell path still propagates correctly.
		rb.MotionEnabled = false;

		var tree = go.AddComponent<Tree>();
		tree.Body = rb;
		tree.TrunkTint = speciesTint;
		tree.Species = species;
		tree.ChopsRemaining = chops;
		tree.IsMythic = mythic;
		return tree;
	}

	private static bool _crownTintAttempted;
	// Mutate the .vmdl crown material's tint param once per session. Tries common
	// s&box shader var names (g_vColorTint, ColorTint, g_flTintColor). Logs results
	// so we can confirm via grep what worked. Side-effect : ALL trees share crown
	// material, so only first call's tint sticks — picks Beech canopy as global teal.
	private static void TryTintCrownMaterialOnce( Model model, Color tint )
	{
		if ( _crownTintAttempted ) return;
		_crownTintAttempted = true;
		try
		{
			var v4 = new Vector4( tint.r, tint.g, tint.b, tint.a );
			foreach ( var mat in model.Materials )
			{
				if ( mat?.Name == null ) continue;
				if ( !mat.Name.Contains( "crown" ) ) continue;
				Log.Info( $"[Tree] crown material found: {mat.Name} — trying tint sets (Color + Vector4)" );
				// Try Color overload + Vector4 overload across common s&box shader vars.
				try { mat.Set( "g_vColorTint", tint ); } catch ( System.Exception e ) { Log.Info( $"  g_vColorTint(Color) failed: {e.Message}" ); }
				try { mat.Set( "g_vColorTint", v4 ); } catch ( System.Exception e ) { Log.Info( $"  g_vColorTint(V4) failed: {e.Message}" ); }
				try { mat.Set( "ColorTint", tint ); } catch ( System.Exception e ) { Log.Info( $"  ColorTint failed: {e.Message}" ); }
				try { mat.Set( "g_flTintColor", tint ); } catch ( System.Exception e ) { Log.Info( $"  g_flTintColor failed: {e.Message}" ); }
				try { mat.Set( "Color", tint ); } catch ( System.Exception e ) { Log.Info( $"  Color failed: {e.Message}" ); }
				try { mat.Set( "TintColor", tint ); } catch ( System.Exception e ) { Log.Info( $"  TintColor failed: {e.Message}" ); }
				break;
			}
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Tree] TryTintCrownMaterial outer fail: {ex.Message}" );
		}
	}

	// Build a stylized procedural tree composite (trunk + per-species canopy)
	// as children of `parent`. All sizes are in unscaled local u; the parent's
	// uniform scaleMul propagates. Hash provides deterministic variation per tree.
	private static void BuildTreeVisual( Scene scene, GameObject parent, TreeSpecies species,
		Color trunkTint, Color canopyTint, int hash, bool mythic )
	{
		switch ( species )
		{
			case TreeSpecies.Spruce:
				BuildSpruce( scene, parent, trunkTint, canopyTint, hash );
				break;
			case TreeSpecies.Ironwood:
				BuildIronwood( scene, parent, trunkTint, canopyTint, hash );
				break;
			case TreeSpecies.Crystal:
				BuildCrystal( scene, parent, trunkTint, canopyTint, hash );
				break;
			default: // Beech — broadleaf rounded
				BuildBeech( scene, parent, trunkTint, canopyTint, hash );
				break;
		}
	}

	// Beech : tall coral trunk + 2-stack rotated cubes (chunky hex broadleaf).
	private static void BuildBeech( Scene scene, GameObject parent, Color trunkTint, Color canopyTint, int hash )
	{
		AddTrunk( scene, parent, 22f, 22f, 145f, trunkTint );
		float baseYaw = ((hash >> 9) & 0xFF) / 255f * 360f;
		AddCube( scene, parent, new Vector3( 0f, 0f, 175f ), new Vector3( 115f, 115f, 95f ),
			canopyTint, baseYaw + 45f );
		var cap = ShiftTint( canopyTint, 1.15f );
		AddCube( scene, parent, new Vector3( 5f, -5f, 235f ), new Vector3( 70f, 70f, 60f ),
			cap, baseYaw + 15f );
	}

	// Spruce : tan trunk + 3-stack rotated cubes (cone-like silhouette).
	private static void BuildSpruce( Scene scene, GameObject parent, Color trunkTint, Color canopyTint, int hash )
	{
		AddTrunk( scene, parent, 18f, 18f, 165f, trunkTint );
		float baseYaw = ((hash >> 11) & 0xFF) / 255f * 360f;
		var darker = ShiftTint( canopyTint, 0.85f );
		AddCube( scene, parent, new Vector3( 0f, 0f, 175f ), new Vector3( 105f, 105f, 60f ), darker, baseYaw + 45f );
		AddCube( scene, parent, new Vector3( 0f, 0f, 220f ), new Vector3( 75f, 75f, 50f ), canopyTint, baseYaw + 30f );
		var brighter = ShiftTint( canopyTint, 1.18f );
		AddCube( scene, parent, new Vector3( 0f, 0f, 255f ), new Vector3( 42f, 42f, 42f ), brighter, baseYaw + 60f );
	}

	// Ironwood : fat short trunk + wide flat hexagonal canopy + smaller cap.
	private static void BuildIronwood( Scene scene, GameObject parent, Color trunkTint, Color canopyTint, int hash )
	{
		AddTrunk( scene, parent, 28f, 28f, 115f, trunkTint );
		float baseYaw = ((hash >> 13) & 0xFF) / 255f * 360f;
		AddCube( scene, parent, new Vector3( 0f, 0f, 150f ), new Vector3( 140f, 140f, 70f ),
			canopyTint, baseYaw + 45f );
		var darker = ShiftTint( canopyTint, 0.85f );
		AddCube( scene, parent, new Vector3( -8f, 6f, 200f ), new Vector3( 85f, 85f, 45f ), darker, baseYaw + 15f );
	}

	// Crystal : thin pale-blue trunk + tall stacked obelisk spire (cyan crystal vibe).
	private static void BuildCrystal( Scene scene, GameObject parent, Color trunkTint, Color canopyTint, int hash )
	{
		AddTrunk( scene, parent, 15f, 15f, 195f, trunkTint );
		float baseYaw = ((hash >> 5) & 0xFF) / 255f * 360f;
		AddCube( scene, parent, new Vector3( 0f, 0f, 215f ), new Vector3( 55f, 55f, 90f ),
			canopyTint, baseYaw + 30f );
		var brighter = ShiftTint( canopyTint, 1.20f );
		AddCube( scene, parent, new Vector3( 0f, 0f, 290f ), new Vector3( 32f, 32f, 70f ),
			brighter, baseYaw + 60f );
	}

	private static void AddTrunk( Scene scene, GameObject parent, float w, float d, float h, Color tint )
	{
		AddCube( scene, parent, new Vector3( 0f, 0f, h * 0.5f ), new Vector3( w, d, h ), tint, 0f );
	}

	private static GameObject AddCube( Scene scene, GameObject parent, Vector3 localPosUnscaled,
		Vector3 dimsUnscaled, Color tint, float yawDeg )
	{
		var go = scene.CreateObject();
		go.Name = "TreePart";
		go.SetParent( parent );
		go.LocalPosition = localPosUnscaled;
		go.LocalScale = dimsUnscaled / Tunables.CubeBase;
		go.LocalRotation = Rotation.FromYaw( yawDeg );
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		mr.Tint = tint;
		return go;
	}

	private static Color ShiftTint( Color c, float factor )
		=> new(
			(c.r * factor).Clamp( 0f, 1f ),
			(c.g * factor).Clamp( 0f, 1f ),
			(c.b * factor).Clamp( 0f, 1f ),
			c.a );

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
		var hitDir = direction.WithZ( 0f );
		hitDir = hitDir.LengthSquared > 0.0001f ? hitDir.Normal : Vector3.Forward;

		// Accumulate a visual lean AWAY from the chop direction. Each hit tilts
		// the top a few degrees further so the player sees the cumulative chop
		// damage before the trunk goes kinematic-off and physics takes over.
		var leanAxis = Vector3.Up.Cross( hitDir );
		if ( leanAxis.LengthSquared > 0.001f )
		{
			_chopLean = Rotation.FromAxis( leanAxis.Normal, PerChopLeanDeg ) * _chopLean;
		}

		var chipPos = WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.15f) + hitDir * Tunables.TreeRadius;
		ChopParticles.Burst( Scene, chipPos, hitDir, TrunkTint, Tunables.ChipBurstCountWood, Tunables.ChipSpeedWood );
		// Bark splinters en plus des copeaux — double burst = THE hit lit.
		ChopParticles.SplinterBurst( Scene, chipPos, hitDir, TrunkTint, 8, 260f );
		AudioBank.PlayChopWood( Scene, chipPos );

		// Camera shake bumped 0.12→0.22 — user feedback "aucun indicateur visuel"
		// au chop. Trauma plus visible secoue la cam au contact.
		var combo = ComboTracker.Get( Scene );
		combo?.AddTrauma( 0.22f );
		// Hit-stop : freeze ~55ms au moment du chop. Ajoute du poids visuel
		// "tank-stop on contact" comme Dark Souls / Hollow Knight.
		combo?.TriggerHitStop();
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

		// Hand off the visual lean to the rigidbody by baking it into the body's
		// rotation, then clear our visual lean so TickWindSway stops fighting
		// the falling physics. Without this the tree visually snaps back to
		// vertical on the frame StartFell fires.
		WorldRotation = _chopLean * _baseRotation;
		_chopLean = Rotation.Identity;

		// Big punch when the tree starts going down — this is the moment the
		// player sees their work pay off. ~0.35 trauma reads as a clear "wow"
		// shake without crossing into nausea (trauma is squared in the shake).
		ComboTracker.Get( Scene )?.AddTrauma( 0.35f );

		// Canopy burst — green-yellow leaves shake loose when the trunk starts going
		// over. Spawned at the canopy height (top of TreeHeight), aimed up + outward
		// in the fell direction so they read as scattering, not bunched at the trunk.
		var canopyPos = WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.85f);
		var leafTint = new Color( 0.45f, 0.68f, 0.22f, 1f );
		ChopParticles.Burst( Scene, canopyPos, _fellDir, leafTint, 22, 240f );

		// Premier arbre du run = signal de "ça commence" — petit boost de trauma
		// pour marquer le départ de la chaîne. Lit le score AVANT OnTreeFell.
		if ( RunManager.Get( Scene )?.Score == 0 )
		{
			ComboTracker.Get( Scene )?.AddTrauma( 0.15f );
		}

		// Score one for the run. Cascade-struck trees route through StartFell
		// too (via CascadeStrike), so this single call covers both the player's
		// chosen target and every domino that follows. Mythic gold trees award
		// an additional bonus on top of the +1 — Noita-style "scolagi" pop.
		var runMgr = RunManager.Get( Scene );
		runMgr?.OnTreeFell();
		var runMgr2 = RunManager.Get( Scene );
		if ( runMgr2.IsValid() )
		{
			if ( runMgr2.ActiveModifier == RunModifier.ChainLightning )
			{
				TryChainLightning();
			}
			else if ( runMgr2.ActiveModifier == RunModifier.Frozen )
			{
				TryFrozenRadial();
			}
		}
		if ( IsMythic )
		{
			runMgr?.OnMythicFell();
			for ( int i = 0; i < Tunables.MythicScoreBonus; i++ ) runMgr?.OnTreeFell();
			ComboTracker.Get( Scene )?.AddTrauma( 0.50f );
			// Gold leaf canopy burst.
			var goldPos = WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.7f);
			ChopParticles.Burst( Scene, goldPos, _fellDir, Tunables.MythicTint, 40, 320f );
		}

		// World-space +N popup so the player sees each chain kill register visually.
		var run = RunManager.Get( Scene );
		if ( run.IsValid() )
		{
			var popPos = WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.6f);
			// Color escalates with the current milestone tier — gold base, hotter tints
			// as the chain gets bigger.
			var popTint = run.LastMilestoneIndex >= 0 && run.LastMilestoneIndex < Tunables.ScoreMilestoneColors.Length
				? Tunables.ScoreMilestoneColors[run.LastMilestoneIndex]
				: new Color( 1f, 0.85f, 0.40f, 1f );
			ScorePop.Spawn( Scene, popPos, $"+{run.Score}", popTint );
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

		// Cascade-impact debris — gated by impulse magnitude pour pas spammer
		// les contacts faibles. Seuil 150 = seul un VRAI hit chain-fait éjecte
		// des splinters. Évite de saturer le physics tick à 50+ trees-en-chute.
		if ( impulse.LengthSquared > 22500f ) // 150²
		{
			ChopParticles.SplinterBurst( Scene, contactWorld, fellDir, TrunkTint, 5, 260f );
		}
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

	private void TryChainLightning()
	{
		// Find next-nearest standing tree within 350u + cascade-strike it.
		const float Range = 350f;
		float rangeSq = Range * Range;
		Tree nearest = null;
		float nearestSq = rangeSq;
		foreach ( var t in Scene.GetAllComponents<Tree>() )
		{
			if ( !t.IsValid() || t == this ) continue;
			if ( !t.IsStanding ) continue;
			var d = (t.WorldPosition - WorldPosition).WithZ( 0f );
			var dSq = d.LengthSquared;
			if ( dSq < nearestSq ) { nearestSq = dSq; nearest = t; }
		}
		if ( nearest is null ) return;
		var dir = (nearest.WorldPosition - WorldPosition).WithZ( 0f ).Normal;
		nearest.CascadeStrike( dir, nearest.WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.4f),
			dir * Tunables.TreeMass * 6f );
		// Yellow lightning chip trail.
		ChopParticles.Burst( Scene, WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.5f),
			dir, new Color( 1f, 0.95f, 0.30f, 1f ), 12, 480f );
	}

	private void TryFrozenRadial()
	{
		// Radiate to all standing trees within 250u.
		const float Range = 250f;
		float rangeSq = Range * Range;
		foreach ( var t in Scene.GetAllComponents<Tree>().ToList() )
		{
			if ( !t.IsValid() || t == this ) continue;
			if ( !t.IsStanding ) continue;
			var d = (t.WorldPosition - WorldPosition).WithZ( 0f );
			if ( d.LengthSquared > rangeSq ) continue;
			var dir = d.Normal;
			t.CascadeStrike( dir, t.WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.4f),
				dir * Tunables.TreeMass * 3.5f );
		}
	}

	public void CascadeStrike( Vector3 dir, Vector3 contactWorld, Vector3 impulse )
	{
		if ( _broken || _chopped ) return;
		StartFell( dir );
		if ( Body.IsValid() )
		{
			Body.ApplyImpulseAt( contactWorld, impulse );

			// Twist sur l'axe perpendiculaire à la chute → l'arbre cascadé roule
			// visuellement au lieu de tomber tout droit. Feel "branches qui
			// s'accrochent" type Valheim.
			var torqueAxis = Vector3.Up.Cross( dir );
			if ( torqueAxis.LengthSquared > 0.001f )
			{
				Body.ApplyTorque( torqueAxis.Normal * impulse.Length * 0.4f );
			}
		}

		// Cascade contact punch — burst leaves + tiny camera shake at the impact
		// point so chain hits read as distinct events instead of one mushy fall.
		// Leaves are tinted with the canopy palette (varies per species). Trauma
		// pulse is small so chains of 50+ stay under the per-frame cap.
		var leafTint = new Color( 0.50f, 0.72f, 0.25f, 1f );
		ChopParticles.Burst( Scene, contactWorld, dir, leafTint, 8, 220f );
		ComboTracker.Get( Scene )?.AddTrauma( 0.06f );
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
		const float WindAmplitudeDeg = 7.0f;   // bumped 4.5→7 — sway plus visible à distance.
		const float WindFreqHz = 0.85f;        // base sway, légèrement plus rapide.
		const float WindFreq2Hz = 1.9f;        // organic second harmonic so it doesn't feel sinusoidal.
		const float WindFreq2Mul = 0.50f;      // contribution harmonic bumpée pour plus de vie.
		const float WindGustHz = 0.22f;        // gusts plus fréquents.

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
		// _chopLean pre-multiplies (applied in world frame BEFORE sway) so the lean
		// is the dominant pose and sway is a small jiggle on top of the leaned trunk.
		WorldRotation = _chopLean * _baseRotation
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

		// Leaf trail : 2 leaves toutes les ~0.20s pendant la chute, biome-tinted.
		// Avec 50 trees simultanés : 50/0.20 × 2 = 500 leaves/s — toujours OK
		// (cap chip lifetime 4s = ~2k particules max in flight).
		_fallLeafTimer += Time.Delta;
		if ( _fallLeafTimer > 0.20f )
		{
			_fallLeafTimer = 0f;
			var canopy = WorldPosition + WorldRotation.Up * (Tunables.TreeHeight * 0.85f);
			var bm = BiomeManager.Get( Scene );
			Color trailTint = bm?.Current switch
			{
				BiomeKind.Autumn => new Color( 0.92f, 0.58f, 0.22f, 1f ),
				BiomeKind.Frost => new Color( 0.78f, 0.92f, 1.0f, 1f ),
				_ => new Color( 0.46f, 0.66f, 0.24f, 1f ), // Forest
			};
			ChopParticles.LeafTrail( Scene, canopy, trailTint, 2, 80f );
		}

		var upDot = WorldRotation.Up.Dot( Vector3.Up );
		if ( upDot < Tunables.TreeFallenUpDotMax )
		{
			BecomeLandedLog();
		}
	}

	private float _fallLeafTimer;

	private void BecomeLandedLog()
	{
		_landed = true;
		_timeSinceLanded = 0f;
		ChopsRemaining = (int)Tunables.LogBreakHits;
		Tags.Add( "log" );

		// Velocity-at-impact pre-damping, so we scale dust by how fast this
		// tree was actually slamming the ground.
		float impactSpeed = Body.IsValid() ? Body.Velocity.Length : 200f;

		if ( Body.IsValid() )
		{
			Body.AngularDamping = Tunables.TreeAngularDampLanded;
			Body.LinearDamping = Tunables.TreeLinearDampLanded;
		}

		// WHOMP — gros pulse de trauma scaled by impact speed (50% to 100%).
		float speedFrac = MathX.Clamp( impactSpeed / 400f, 0.3f, 1.2f );
		ComboTracker.Get( Scene )?.AddTrauma( 0.18f * speedFrac );

		// Burst de poussière brune côté cime (là où le tronc a frappé le sol).
		// Particle count + speed scalent par impact velocity : un gros chute
		// punch un cloud massif, un léger touch génère juste une wisp.
		const float LandDustBaseSpeed = 180f;
		var landDustColor = new Color( 0.45f, 0.35f, 0.22f, 1f );
		var landDustPos = WorldPosition + WorldRotation.Forward * (Tunables.TreeHeight * 0.4f);
		int dustCount = (int)(Tunables.ChipBurstCountWoodHeavy * speedFrac);
		ChopParticles.Burst( Scene, landDustPos, Vector3.Up, landDustColor, dustCount, LandDustBaseSpeed * speedFrac );
		// Bonus splinter burst horizontal sur les gros impacts (>300 u/s).
		if ( impactSpeed > 300f )
		{
			ChopParticles.SplinterBurst( Scene, landDustPos, WorldRotation.Forward, TrunkTint, 6, 220f );
		}

		// Pas de SFX dédié "tree-land" pour l'instant — log-break est le
		// fallback le plus proche en attendant un asset propre.
		AudioBank.PlayLogBreak( Scene, WorldPosition );

		// Knock-back radial sur les rigidbodies voisins → "le sol tremble" autour
		// de l'impact. Sphère le long du tronc couché.
		const float LandShakeRadius = 80f;
		const float LandShakeMul = 80f;
		var trace = Scene.Trace.Sphere( LandShakeRadius, WorldPosition, WorldPosition ).RunAll();
		foreach ( var hit in trace )
		{
			var otherGo = hit.GameObject;
			if ( !otherGo.IsValid() || otherGo == GameObject ) continue;
			var otherRb = otherGo.Components.Get<Rigidbody>();
			if ( !otherRb.IsValid() ) continue;
			var delta = (otherGo.WorldPosition - WorldPosition).WithZ( 0.2f );
			if ( delta.LengthSquared < 0.001f ) continue;
			otherRb.ApplyImpulse( delta.Normal * otherRb.PhysicsBody.Mass * LandShakeMul );
		}

		// One more burst of leaves at the landing canopy end — the trunk's WorldRotation.Forward
		// is the felled axis, so this spawns where the top of the tree slapped the ground.
		var landedCanopy = WorldPosition + WorldRotation.Forward * (Tunables.TreeHeight * 0.45f) + Vector3.Up * 12f;
		ChopParticles.Burst( Scene, landedCanopy, WorldRotation.Forward, new Color( 0.50f, 0.72f, 0.25f, 1f ), 18, 200f );
	}

	private TimeSince _lastRollDust = 999f;

	private void TickLandedDecay()
	{
		if ( !Body.IsValid() ) return;

		// Roll-dust trail Noita-feel : un landed log qui dévale sur la slope
		// éjecte de la poussière brune toutes les ~0.15s tant qu'il bouge fort.
		// Filtré par speed pour ne pas spammer une fois settled.
		var speedSq = Body.Velocity.LengthSquared;
		if ( speedSq > 600f && (float)_lastRollDust > 0.15f )
		{
			_lastRollDust = 0f;
			var dustPos = WorldPosition + Vector3.Down * 8f + Vector3.Random.WithZ( 0f ) * 12f;
			var dustDir = -Body.Velocity.WithZ( 0f ).Normal;
			if ( dustDir.LengthSquared < 0.001f ) dustDir = Vector3.Forward;
			ChopParticles.Burst( Scene, dustPos, dustDir, new Color( 0.55f, 0.45f, 0.30f, 1f ), 6, 70f );
		}

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
		go.WorldScale = new Vector3( Tunables.LogPieceRadius * 2f, Tunables.LogPieceRadius * 2f, Tunables.LogPieceHeight ) / Tunables.CubeBase;
		go.Tags.Add( "logpiece" );

		var model = go.AddComponent<ModelRenderer>();
		model.Model = Models.LogTrunk;
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
