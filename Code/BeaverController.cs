namespace TreeChopping;

public sealed class BeaverController : Component
{
	[Property] public GameObject CameraRoot { get; set; }
	[Property] public CameraComponent Camera { get; set; }
	[Property] public float MoveSpeed { get; set; } = Tunables.BeaverMoveSpeed;
	[Property] public float JumpImpulse { get; set; } = Tunables.BeaverJumpImpulse;
	[Property] public float CameraDistance { get; set; } = Tunables.CameraDistance;
	[Property] public float MouseSensitivity { get; set; } = 0.12f;

	private Vector3 _wishVelocity;
	private Angles _cameraAngles;
	private float _swingCooldown;
	private Rigidbody _rb;
	private float _baseFov;
	private float _fovPunch;
	private float _sprintWiden;

	// Walk-bob phase + smoothed amplitude scalar.
	// Phase is in radians; amp 0..1 lerps toward 1 while moving, toward 0 when stopped
	// so there's no visual snap on stop and the sine wave glides out at its current phase.
	private float _walkPhase;
	private float _walkBobAmp;

	// Smoothed camera position so the trace flip-flop between hit/no-hit
	// doesn't jitter the view.
	private Vector3 _lastCamPos;

	// Held tool mesh parented under ToolPivot. Was a pair (axe + pickaxe with
	// visibility toggle) before the bowling pivot — single tool now.
	private GameObject _axeRoot;

	// Sine-profile swing animation on the held tool. Starts at 0 each swing
	// (hit OR miss), sweeps the active tool up and back down in pitch over
	// SwingAnimDuration seconds. Initial value > duration so no animation
	// runs at startup. The animation is purely visual — gameplay timing is
	// driven by _swingCooldown, not by this.
	private TimeSince _swingAnimTime = 999f;
	// Snappier swing arc — duration 0.22→0.18 + pitch 75→92° + slight side roll
	// for diagonal "haymaker" chop feel.
	private const float SwingAnimDuration = 0.18f;
	private const float SwingAnimMaxPitchDeg = 92f;
	private const float SwingAnimSideRollDeg = 22f;

	protected override void OnAwake()
	{
		_rb = Components.Get<Rigidbody>( FindMode.EverythingInSelfAndDescendants );
		_cameraAngles = WorldRotation.Angles();
		// Pitch 15 (compromis entre 12 et 22) : à 22 le beaver tombait sous
		// l'écran. À 15 avec CameraDistance=240 + Height 40, beaver lit dans
		// le tiers-bas, trees devant dans le milieu, sky en haut.
		_cameraAngles.pitch = 15f;
	}

	protected override void OnStart()
	{
		// Capture user-tuned baseline AFTER SceneStarter set FieldOfView.
		if ( Camera.IsValid() ) _baseFov = Camera.FieldOfView;

		// Retroactively swap the cube body that SceneStarter spawned for the
		// Poly Pizza beaver mesh. Done here (not in OnAwake) so we're sure
		// SceneStarter.SpawnBeaver finished AddComponent<ModelRenderer>; the
		// non-uniform parent WorldScale (32,32,72)/CubeBase still applies, so
		// the mesh inherits a mild stretch — accepted vs touching SceneStarter.
		var bodyMr = GameObject.Components.Get<ModelRenderer>();
		if ( bodyMr.IsValid() )
		{
			bodyMr.Model = Models.Beaver;
			// Clear the cube tint so the model's own materials show through.
			bodyMr.Tint = Color.White;
		}

		BuildHeldTools();
		// Always hide the spawn cube + build the procedural critter — the
		// composite reads as a beaver (body + head + snout + eyes + ears + tail)
		// and stays consistent whether or not the Kenney .glb has been compiled.
		// User feedback: Kenney mesh à uniform-scale s'écrase sous le parent
		// non-uniforme (32,32,72)/50 — préférer le proc qui contrôle ses dims.
		if ( bodyMr.IsValid() ) bodyMr.Enabled = false;
		BuildBeaverProps();
	}

	protected override void OnUpdate()
	{
		// Pause gate — all gameplay-affecting inputs (look, move, swing,
		// upgrade, debug spawns) skip while the menu is open. We don't trust
		// Scene.TimeScale=0 alone since OnUpdate keeps ticking on it; the
		// menu also repurposes WASD for slider nav, so reading Input here
		// while paused would steal those presses.
		if ( PauseMenu.Get( Scene )?.IsPaused == true ) return;

		var run = RunManager.Get( Scene );
		bool cinematic = run.IsValid() && run.State != RunState.WaitingForSwing;

		if ( cinematic )
		{
			// Zero out velocity, run only the cinematic camera, skip input.
			if ( _rb.IsValid() )
			{
				var v = _rb.Velocity;
				v.x = 0; v.y = 0;
				_rb.Velocity = v;
			}
			UpdateCinematicCamera( run );
			UpdateSwingAnim();
			return;
		}

		UpdateLook();
		UpdateMovement();
		UpdateCamera();
		UpdateDebugSpawns();
		UpdateSwing();
		UpdateSwingAnim();
	}

	private TimeSince _cinematicStart;
	private bool _cinematicEntered;
	private Vector3 _cinematicCenter;
	private float _scoredPullT;

	private void UpdateCinematicCamera( RunManager run )
	{
		if ( !Camera.IsValid() ) return;
		if ( !_cinematicEntered )
		{
			_cinematicEntered = true;
			_cinematicStart = 0f;
			_cinematicCenter = ComputeCascadeCenter();
		}
		// Refresh center each tick toward the latest cascade centroid — slowly,
		// so the camera doesn't jitter when trees stop. Smoothed lerp.
		var target = ComputeCascadeCenter();
		_cinematicCenter = Vector3.Lerp( _cinematicCenter, target, Time.Delta * 1.5f );

		// Reset when the run leaves Cascading/Scored back to WaitingForSwing
		// (happens on Regenerate).
		if ( run.State == RunState.WaitingForSwing )
		{
			_cinematicEntered = false;
			return;
		}

		float elapsed = (float)_cinematicStart;
		// Orbit yaw rotates 30°/s clockwise. Radius starts at 350u and pulls
		// back to 850u over the first 3s during cascade. Quand state passe à
		// Scored, push encore plus loin (1100u, height 540u) sur 1.5s pour
		// establishing shot dramatique avec panel score visible devant.
		// Cinematic yaw rate trimmé 30°/s → 18°/s — slower spin = plus cinématographique,
		// joueur peut lire le carnage avant que la cam ait fait demi-tour.
		float yawDeg = -18f * elapsed;
		// Start tight (250u) pour close-up impact pendant 1.5s, puis pull back
		// jusqu'à 850u sur le reste (3s total). Donne "in the action" sensation
		// au tout début de la chain avant de prendre du recul.
		float baseRadius = MathX.Lerp( 250f, 850f, MathX.Clamp( elapsed / 3f, 0f, 1f ) );
		float baseHeight = MathX.Lerp( 180f, 420f, MathX.Clamp( elapsed / 4f, 0f, 1f ) );
		// Scored extra pull-back — _scoredPullT lerps 0→1 sur 1.5s après Scored entry.
		if ( run.State == RunState.Scored )
		{
			_scoredPullT = MathX.Clamp( _scoredPullT + Time.Delta / 1.5f, 0f, 1f );
		}
		else
		{
			_scoredPullT = 0f;
		}
		float radius = MathX.Lerp( baseRadius, 1100f, _scoredPullT );
		float height = MathX.Lerp( baseHeight, 540f, _scoredPullT );

		var rot = Rotation.FromYaw( yawDeg );
		var offset = rot.Forward * -radius + Vector3.Up * height;
		var camPos = _cinematicCenter + offset;
		// LookAt elevation : 60u above center par défaut, +60u extra en Scored
		// pour englober plus de paysage dans le cadre du final shot.
		float lookAtZBoost = MathX.Lerp( 60f, 120f, _scoredPullT );
		var lookAt = _cinematicCenter + Vector3.Up * lookAtZBoost;
		var lookRot = Rotation.LookAt( (lookAt - camPos).Normal, Vector3.Up );

		Camera.WorldPosition = camPos;
		Camera.WorldRotation = lookRot;
		// FOV ramp pendant cascade pour widen dramatique (72 → 95 sur 2.5s),
		// puis return à 72 quand on passe à Scored. Plus dramatic spread
		// que iter17 — extra wide pendant cascade pour englober plus de chaos.
		float fovTarget = run.State == RunState.Cascading
			? MathX.Lerp( 72f, 95f, MathX.Clamp( elapsed / 2.5f, 0f, 1f ) )
			: 72f;
		Camera.FieldOfView = MathX.Lerp( Camera.FieldOfView, fovTarget, Time.Delta * 3f );
	}

	// Centroid of currently-falling + recently-landed trees. Falls back to the
	// beaver position if no tree has motion.
	private Vector3 ComputeCascadeCenter()
	{
		var sum = Vector3.Zero;
		int count = 0;
		foreach ( var t in Scene.GetAllComponents<Tree>() )
		{
			if ( !t.IsValid() ) continue;
			if ( t.Body is null || !t.Body.IsValid() ) continue;
			if ( !t.Body.MotionEnabled ) continue; // standing = ignore
			sum += t.WorldPosition;
			count++;
		}
		return count > 0 ? sum / count : WorldPosition;
	}

	private void UpdateSwingAnim()
	{
		if ( !_axeRoot.IsValid() ) return;
		if ( _swingAnimTime > SwingAnimDuration )
		{
			_axeRoot.LocalRotation = Rotation.Identity;
			return;
		}

		float t = (float)_swingAnimTime / SwingAnimDuration;
		// Half-sine profile: 0 → peak at t=0.5 → 0 at t=1. Reads as a single
		// overhead arc rather than wind-up + recovery, which keeps the anim
		// short enough to chain at SwingCooldown=0.33s without overlap.
		float arcShape = MathF.Sin( t * MathF.PI );
		float pitchDeg = arcShape * SwingAnimMaxPitchDeg;
		// Side roll component — same arc profile but on Forward axis. Combine
		// les deux pour un diagonal chop au lieu d'un overhead pur.
		float rollDeg = arcShape * SwingAnimSideRollDeg;
		_axeRoot.LocalRotation = Rotation.FromAxis( Vector3.Right, pitchDeg )
			* Rotation.FromAxis( Vector3.Forward, rollDeg );
	}

	private void UpdateDebugSpawns()
	{
		if ( Input.Pressed( "Slot1" ) ) SpawnTestBody( densityFactor: 0.4f, tint: new Color( 0.20f, 0.55f, 0.90f, 1f ) ); // floater
		if ( Input.Pressed( "Slot2" ) ) SpawnTestBody( densityFactor: 2.5f, tint: new Color( 0.85f, 0.20f, 0.20f, 1f ) ); // sinker
		if ( Input.Pressed( "Slot3" ) ) SpawnTestBody( densityFactor: 1.0f, tint: new Color( 0.90f, 0.85f, 0.40f, 1f ) ); // neutral
	}

	private void SpawnTestBody( float densityFactor, Color tint )
	{
		var go = Scene.CreateObject();
		go.Name = "TestBody";
		go.WorldPosition = WorldPosition + Vector3.Up * 120f;
		go.WorldScale = new Vector3( 40f ) / Tunables.CubeBase;
		go.Tags.Add( "test_body" );

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		mr.Tint = tint;

		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );

		var rb = go.AddComponent<Rigidbody>();
		rb.MassOverride = 8f * densityFactor;
		rb.LinearDamping = 0.5f;
		rb.AngularDamping = 0.8f;
	}

	// Spawns the ToolPivot + Axe child once on start. Bowling pivot collapsed
	// the prior axe+pickaxe toggle to a single tool — only the axe is held.
	// Parent beaver has non-uniform WorldScale (32,32,72)/CubeBase; local
	// transforms inherit that stretch and we accept the visual squash for now.
	private void BuildHeldTools()
	{
		// Pivot offset in *local* units (parent's WorldScale stretches X/Y/Z 0.64/0.64/1.44).
		// Chest height + a touch forward so the tool reads as held in front of the beaver.
		const float PivotForwardLocal = 0.55f;     // ~28u forward after X-stretch
		const float PivotUpLocal = 0.25f;          // ~18u up after Z-stretch (mid-chest)

		var pivot = Scene.CreateObject();
		pivot.Name = "ToolPivot";
		pivot.Parent = GameObject;
		pivot.LocalPosition = new Vector3( PivotForwardLocal, 0f, PivotUpLocal );
		pivot.LocalScale = Vector3.One;

		_axeRoot = BuildAxe( pivot );
	}

	// Procedural beaver silhouette — head + snout + tail cube children parented
	// under the beaver GO so they ride along with movement. No colliders / no
	// rigidbodies — purely visual. Inherits the parent's (0.64, 0.64, 1.44)
	// non-uniform stretch; LocalScale below pre-compensates so the children
	// render at the intended world sizes.
	// Composite beaver silhouette (fallback when Kenney glb isn't compiled).
	// Spheres for body+head, cubes for snout/tail/legs/ears — reads as a critter
	// way better than a single cube. Tints stay in the warm-brown range.
	private void BuildBeaverProps()
	{
		// Stylized critter composite — body + head + snout + 2 eyes + 2 ears + tail.
		// Palette saturée chocolat clair (pas muddy brown) pour matcher l'ambiance
		// stylized Godot. LocalPos is in PRE-parent-scale u (parent is 32,32,72)/50;
		// scale en local = ratio cube de base.
		var bodyColor = new Color( 0.58f, 0.36f, 0.20f, 1f );  // warm chocolate
		var headColor = new Color( 0.66f, 0.42f, 0.24f, 1f );  // slightly lighter
		var snoutColor = new Color( 0.30f, 0.18f, 0.10f, 1f ); // dark
		var earColor = new Color( 0.45f, 0.28f, 0.15f, 1f );   // mid
		var tailColor = new Color( 0.30f, 0.20f, 0.12f, 1f );  // dark brown
		var eyeColor = new Color( 0.05f, 0.04f, 0.03f, 1f );   // black

		// All-cube composite — Model.Sphere tinting is unreliable in s&box (renders
		// near-white regardless of mr.Tint). Cubes take tint cleanly.
		BuildBeaverProp( "BeaverBody", Vector3.Zero,
			new Vector3( 0.90f, 0.90f, 0.42f ), bodyColor, Model.Cube );

		BuildBeaverProp( "BeaverHead", new Vector3( 22f, 0f, 14f ),
			new Vector3( 0.46f, 0.46f, 0.30f ), headColor, Model.Cube );

		BuildBeaverProp( "BeaverSnout", new Vector3( 34f, 0f, 10f ),
			new Vector3( 0.22f, 0.18f, 0.12f ), snoutColor, Model.Cube );

		BuildBeaverProp( "BeaverEyeL", new Vector3( 28f, 8f, 20f ),
			new Vector3( 0.08f, 0.04f, 0.08f ), eyeColor, Model.Cube );
		BuildBeaverProp( "BeaverEyeR", new Vector3( 28f, -8f, 20f ),
			new Vector3( 0.08f, 0.04f, 0.08f ), eyeColor, Model.Cube );

		BuildBeaverProp( "BeaverEarL", new Vector3( 18f, 10f, 24f ),
			new Vector3( 0.12f, 0.08f, 0.10f ), earColor, Model.Cube );
		BuildBeaverProp( "BeaverEarR", new Vector3( 18f, -10f, 24f ),
			new Vector3( 0.12f, 0.08f, 0.10f ), earColor, Model.Cube );

		BuildBeaverProp( "BeaverTail", new Vector3( -28f, 0f, -8f ),
			new Vector3( 0.55f, 0.22f, 0.14f ), tailColor, Model.Cube );

		// 4 small legs at corners under body — pushes the silhouette from
		// "floating stack of cubes" to "ankle'd critter". Front legs slightly
		// offset forward for posture.
		var legColor = new Color( 0.40f, 0.24f, 0.14f, 1f );
		BuildBeaverProp( "LegFL", new Vector3( 10f, 14f, -22f ),
			new Vector3( 0.12f, 0.12f, 0.20f ), legColor, Model.Cube );
		BuildBeaverProp( "LegFR", new Vector3( 10f, -14f, -22f ),
			new Vector3( 0.12f, 0.12f, 0.20f ), legColor, Model.Cube );
		BuildBeaverProp( "LegBL", new Vector3( -14f, 14f, -22f ),
			new Vector3( 0.13f, 0.13f, 0.22f ), legColor, Model.Cube );
		BuildBeaverProp( "LegBR", new Vector3( -14f, -14f, -22f ),
			new Vector3( 0.13f, 0.13f, 0.22f ), legColor, Model.Cube );
	}

	private void BuildBeaverProp( string name, Vector3 localPos, Vector3 localScale, Color tint, Model model )
	{
		var go = Scene.CreateObject();
		go.Name = name;
		go.Parent = GameObject;
		go.LocalPosition = localPos;
		go.LocalScale = localScale;
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = model;
		mr.Tint = tint;
	}

	// Kenney axe held in hand. LocalScale=One — the parent's non-uniform
	// (32,32,72)/CubeBase squash propagates and is accepted (mentioned in
	// commit body). Model.Tint stays untouched so the GLB's own materials
	// read; only fall back to a brown cube if Models.Axe failed to load.
	private GameObject BuildAxe( GameObject pivot )
	{
		var go = Scene.CreateObject();
		go.Name = "Axe";
		go.Parent = pivot;
		go.LocalPosition = new Vector3( 0f, 0f, 0f );
		// Échelle compensée pour parent WorldScale non-uniforme (0.64, 0.64, 1.44) :
		// vise un axe world ~8×8×22u (avant c'était 32×32×72 — un cube géant
		// blanc qui couvrait le beaver). Local = world / 50 / parent_scale.
		go.LocalScale = new Vector3( 8f / 50f / 0.64f, 8f / 50f / 0.64f, 22f / 50f / 1.44f );
		go.Tags.Add( "beaver_tool" );

		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Models.Axe;
		// Tint dark wood brown — sinon Model.Cube est blanc par défaut et lit
		// comme un cube de debug collé sur le beaver.
		mr.Tint = new Color( 0.30f, 0.20f, 0.13f, 1f );
		return go;
	}

	private void UpdateLook()
	{
		var look = Input.AnalogLook;
		_cameraAngles.pitch += look.pitch;
		_cameraAngles.yaw += look.yaw;
		_cameraAngles.pitch = _cameraAngles.pitch.Clamp( Tunables.CameraMinPitch, Tunables.CameraMaxPitch );
		_cameraAngles.roll = 0f;
	}

	private void UpdateMovement()
	{
		var move = Input.AnalogMove;
		var yawOnly = new Angles( 0f, _cameraAngles.yaw, 0f ).ToRotation();
		var wish = yawOnly * new Vector3( move.x, move.y, 0f );
		var speed = MoveSpeed * (Input.Down( "Run" ) ? Tunables.BeaverSprintMultiplier : 1f);
		_wishVelocity = wish.Normal * speed;

		if ( _rb.IsValid() )
		{
			var v = _rb.Velocity;
			v.x = _wishVelocity.x;
			v.y = _wishVelocity.y;
			_rb.Velocity = v;

			if ( Input.Pressed( "Jump" ) && IsGrounded() )
			{
				_rb.ApplyImpulse( Vector3.Up * JumpImpulse * _rb.PhysicsBody.Mass );
			}
		}
		else
		{
			WorldPosition += _wishVelocity * Time.Delta;
		}

		var horiz = wish.Normal;
		if ( horiz.LengthSquared > 0.01f )
		{
			var targetYaw = Rotation.LookAt( horiz, Vector3.Up );
			WorldRotation = Rotation.Slerp( WorldRotation, targetYaw, Time.Delta * 12f );
		}
	}

	private bool IsGrounded()
	{
		var down = Scene.Trace
			.Ray( WorldPosition + Vector3.Up * 4f, WorldPosition + Vector3.Down * 8f )
			.IgnoreGameObjectHierarchy( GameObject )
			.Run();
		return down.Hit;
	}

	private void UpdateCamera()
	{
		if ( !Camera.IsValid() ) return;
		var pivot = WorldPosition + Vector3.Up * Tunables.CameraHeightAboveBeaver;
		var rot = _cameraAngles.ToRotation();
		// Shoulder offset — pulls the cam off-center to the right so the beaver
		// silhouette occupies the lower-left third of frame instead of dead-center.
		// Matches modern 3rd-person framing (Witcher, Dark Souls).
		const float ShoulderOffset = 24f;
		var desired = pivot - rot.Forward * CameraDistance + rot.Right * ShoulderOffset;

		// Ignore arbres + log pieces + ambient + chips quand on cast la cam :
		// avec 1000 trees autour du beaver, sans ça le trace clip dans le 1er
		// tronc derrière et la cam finit DANS un arbre = beaver invisible.
		// Radius 16 (était 10) = plus large = clip plus tôt sur des obstacles
		// non-tree (ground only). Offset 16u (était 4u) = pull-back marqué.
		var trace = Scene.Trace
			.Ray( pivot, desired )
			.Radius( 16f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "tree", "logpiece", "ambient_leaf", "grass_tuft", "stump",
				"beaver_tool", "wood_chunk" )
			.Run();
		var targetCamPos = trace.Hit ? trace.EndPosition + trace.Normal * 16f : desired;

		// Smoothing — au lieu de snap chaque frame, lerp vers la position cible.
		// Évite les jitters quand le trace flip-flop entre hit/no-hit, et donne
		// un suivi cinématographique propre. Réinit si distance >800u (téléport
		// sur Regenerate ne devrait pas glisser).
		if ( (_lastCamPos - targetCamPos).LengthSquared > 800f * 800f ) _lastCamPos = targetCamPos;
		_lastCamPos = Vector3.Lerp( _lastCamPos, targetCamPos, Time.Delta * 12f );
		var camPos = _lastCamPos;

		// Squirrel Eiserloh trauma shake: amplitude = trauma^2, scaled to screen-space-ish.
		var combo = ComboTracker.Get( Scene );
		if ( combo.IsValid() && combo.TraumaAmount > 0.01f )
		{
			float t = combo.TraumaAmount * combo.TraumaAmount;
			var rng = (uint)(Time.Now * 1000.0);
			float jx = (HashFloat( rng, 0u ) - 0.5f) * 2f;
			float jy = (HashFloat( rng, 1u ) - 0.5f) * 2f;
			float jz = (HashFloat( rng, 2u ) - 0.5f) * 2f;
			camPos += new Vector3( jx, jy, jz ) * t * Tunables.CameraTraumaScale;
			rot *= Rotation.FromAxis( Vector3.Up, (HashFloat( rng, 3u ) - 0.5f) * 4f * t );
		}

		Camera.WorldPosition = camPos;
		Camera.WorldRotation = rot;

		// Walk-bob — ported from Godot proto's visual-mesh bob (beaver.gd `_animate_visual`
		// used sin(_move_phase) * 0.04 m vertical + sin(_move_phase*0.5) * 0.05 roll).
		// Here we apply it to the *camera position* instead of the beaver mesh: vertical
		// from sin(phase), lateral from cos(phase*0.5) along the camera's right vector so
		// it composes with the trauma jiggle that just wrote camPos. Adding AFTER the
		// shake means the shake jitters the bobbed position, not the other way around.
		const float BobFrequencyHz = 1.6f;        // ~steps per second at base walk
		const float BobAmplitudeUp = 2.4f;        // SU (~0.06 m, slightly tighter than Godot's 0.04 m on body)
		const float BobAmplitudeSide = 1.4f;      // SU sway peak
		const float BobSpeedThreshold = 30f;      // below this horiz speed we decay to neutral
		const float BobSprintFreqMul = 1.5f;      // sprint cadence bump (matches Godot ~1.5x boost feel)
		const float BobSprintAmpMul = 1.25f;
		const float BobDecayRate = 6f;            // amp lerp rate when stopped (and when ramping back up)
		const float Tau = MathF.PI * 2f;

		var horizSpeed = _rb.IsValid() ? _rb.Velocity.WithZ( 0f ).Length : _wishVelocity.WithZ( 0f ).Length;
		bool moving = horizSpeed > BobSpeedThreshold;
		bool sprintingBob = Input.Down( "Run" );

		// Phase advances proportional to speed so it doesn't tick while idle-creeping;
		// freq * Tau converts Hz → rad/s. Sprint bumps cadence to read as a faster gait.
		float speedFactor = MathX.Clamp( horizSpeed / (Tunables.BeaverMoveSpeed + 0.001f), 0f, 2f );
		float freq = BobFrequencyHz * (sprintingBob ? BobSprintFreqMul : 1f);
		_walkPhase += Time.Delta * speedFactor * freq * Tau;
		if ( _walkPhase > Tau * 1024f ) _walkPhase -= Tau * 1024f; // keep from blowing up over hours

		// Smooth amp toward 0 when stopped (no snap) or toward target when moving.
		float targetAmp = moving ? 1f : 0f;
		_walkBobAmp = MathX.Lerp( _walkBobAmp, targetAmp, MathX.Clamp( Time.Delta * BobDecayRate, 0f, 1f ) );

		float ampMul = (sprintingBob ? BobSprintAmpMul : 1f) * _walkBobAmp;
		float bobUp = MathF.Sin( _walkPhase ) * BobAmplitudeUp * ampMul;
		float bobSide = MathF.Cos( _walkPhase * 0.5f ) * BobAmplitudeSide * ampMul;
		Camera.WorldPosition += Vector3.Up * bobUp + rot.Right * bobSide;

		// Subtle camera roll sur sprint pour speed feel. ±1.5° à la cadence du
		// walk-phase. Only when sprinting + moving — pas de roll au stop.
		if ( sprintingBob && _walkBobAmp > 0.5f )
		{
			float rollDeg = MathF.Sin( _walkPhase * 0.5f ) * 1.5f * _walkBobAmp;
			Camera.WorldRotation = Camera.WorldRotation * Rotation.FromAxis( Vector3.Forward, rollDeg );
		}

		UpdateFov();
	}

	private void UpdateFov()
	{
		if ( _baseFov <= 0f ) _baseFov = Camera.FieldOfView;

		// Sprint widen only counts when actually moving — pure key-hold shouldn't drift FOV.
		bool sprinting = Input.Down( "Run" ) && _wishVelocity.WithZ( 0f ).LengthSquared > 1f;
		float target = sprinting ? Tunables.FovSprintWiden : 0f;
		_sprintWiden = MathX.Lerp( _sprintWiden, target, Time.Delta * Tunables.FovSprintLerpRate );

		_fovPunch = MathF.Max( 0f, _fovPunch - Time.Delta * Tunables.FovPunchDecay );

		Camera.FieldOfView = _baseFov + _sprintWiden + _fovPunch;
	}

	private static float HashFloat( uint a, uint b )
	{
		uint h = a * 374761393u + b * 668265263u;
		h = (h ^ (h >> 13)) * 1274126177u;
		h ^= h >> 16;
		return (h & 0xFFFFFF) / (float)0x1000000;
	}

	private void UpdateSwing()
	{
		_swingCooldown = MathF.Max( 0f, _swingCooldown - Time.Delta );
		if ( _swingCooldown > 0f ) return;
		if ( TitleScreen.ShouldBlockInput ) return;
		if ( !Input.Pressed( "attack1" ) ) return;

		// One-shot bowling premise: RunManager locks further swings after the
		// first hit. We early-out here so the animation + audio don't fire on
		// a swing that wouldn't connect anyway.
		var run = RunManager.Get( Scene );
		if ( run.IsValid() && !run.CanSwing ) return;

		_swingCooldown = Tunables.SwingCooldown;

		// Fire the visual swing arc + swing-whoosh audio BEFORE hit resolution:
		// a miss should still feel like the player swung. AudioBank.PlaySwing
		// is silent today (no .sound asset shipped) but the wiring is in place
		// for when one is authored.
		_swingAnimTime = 0f;
		AudioBank.PlaySwing( Scene, WorldPosition );
		// Small FOV punch on every swing (even miss) — physical "I committed
		// to the swing" feel. +2.5° on top of any hit punch (+4° = +6.5° total).
		_fovPunch += 2.5f;

		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var forward = (_cameraAngles.WithPitch( 0f )).ToRotation().Forward;
		// Swing whoosh trail — small fast-moving white particle arc along the
		// swing direction. Donne visual cue même sur un miss, joueur sait que
		// son swing a fired et dans quelle direction.
		var swingTrailPos = origin + forward * 18f;
		ChopParticles.Burst( Scene, swingTrailPos, forward, new Color( 0.92f, 0.95f, 1.0f, 1f ), 5, 280f );
		// WYSIWYG aim : sphere-sweep depuis la caméra dans la direction du crosshair.
		// L'arbre que le joueur voit au centre de l'écran est celui qui est touché.
		// Fallback yaw-cone si pas de Camera (jamais en jeu, juste sécurité).
		var hit = PickCameraAimTarget( out _ ) ?? ChooseSwingTarget( origin, forward );

		if ( hit is null ) return;

		hit.Chop( forward );
		var runForBlast = RunManager.Get( Scene );
		if ( runForBlast.IsValid() && runForBlast.ActiveModifier == RunModifier.Explosive )
		{
			ApplyExplosiveBlast( hit.WorldPosition, 300f );
		}
		_fovPunch += Tunables.FovChopPunch;
		ComboTracker.Get( Scene )?.Beat();
		// Lock the run regardless of which IChoppable got hit — the player
		// committed their single shot. Cascade tracking begins now.
		RunManager.Get( Scene )?.OnSwingFired();
	}

	private void ApplyExplosiveBlast( Vector3 center, float radius )
	{
		float radiusSq = radius * radius;
		foreach ( var t in Scene.GetAllComponents<Tree>().ToList() )
		{
			if ( !t.IsValid() ) continue;
			if ( !t.IsStanding ) continue;
			var to = t.WorldPosition - center;
			if ( to.LengthSquared > radiusSq ) continue;
			// Radial impulse outward + slight up.
			var dir = (to.WithZ( 0f ).Normal + Vector3.Up * 0.3f).Normal;
			t.CascadeStrike( dir, t.WorldPosition + Vector3.Up * (Tunables.TreeHeight * 0.5f), dir * 6000f );
		}
		// Visual : orange chip burst at center.
		ChopParticles.Burst( Scene, center, Vector3.Up, new Color( 1f, 0.55f, 0.18f, 1f ), 60, 450f );
		ComboTracker.Get( Scene )?.AddTrauma( 0.6f );
	}

	// Test hook for [[sbox-selftest-pattern]] — exercises the real UpdateSwing
	// chain (ChooseSwingTarget + Chop) without going through Input.Pressed or
	// the cooldown. Lets the headless SelfTest probe cone/range/tool gating so
	// a "Chop() direct works but swinging in-game doesn't" regression is caught.
	//
	// Returns the IChoppable that got hit, or null when nothing was in range.
	// Reads CurrentTool and the current yaw — set them via DebugSetYaw + the
	// existing Use input (or DebugSetTool) before calling.
	public IChoppable DebugSwing()
	{
		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var forward = (_cameraAngles.WithPitch( 0f )).ToRotation().Forward;
		var hit = ChooseSwingTarget( origin, forward );
		if ( hit is null ) return null;
		hit.Chop( forward );
		ComboTracker.Get( Scene )?.Beat();
		return hit;
	}

	// Verbose version for the headless test — same selection logic but logs
	// every candidate's filter result so a "tree should be hittable but isn't"
	// regression can be diagnosed straight from the harness output.
	public IChoppable DebugSwingVerbose()
	{
		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var forward = (_cameraAngles.WithPitch( 0f )).ToRotation().Forward;
		Log.Info( $"[TC_TEST] DebugSwingVerbose origin={origin} forward={forward}" );

		var all = Scene.GetAllComponents<IChoppable>().ToList();
		int considered = 0, droppedValid = 0, droppedTool = 0, droppedRange = 0, droppedCone = 0;
		IChoppable best = null;
		var bestScore = float.NegativeInfinity;
		foreach ( var c in all )
		{
			considered++;
			if ( !c.IsValid() ) { droppedValid++; continue; }
			if ( !c.AcceptsTool( ToolKind.Axe ) ) { droppedTool++; continue; }
			var to = c.WorldPosition - origin;
			to.z = 0f;
			var dist = to.Length;
			if ( dist > Tunables.SwingRange ) { droppedRange++; continue; }
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) { droppedCone++; continue; }
			var score = dot - dist * 0.005f;
			Log.Info( $"[TC_TEST]   candidate {c.GetType().Name} pos={c.WorldPosition} dist={dist:F1} dot={dot:F2} score={score:F2}" );
			if ( score > bestScore )
			{
				bestScore = score;
				best = c;
			}
		}
		Log.Info( $"[TC_TEST] DebugSwingVerbose considered={considered} droppedValid={droppedValid} droppedTool={droppedTool} droppedRange={droppedRange} droppedCone={droppedCone} best={(best == null ? "null" : best.GetType().Name)}" );
		if ( best is null ) return null;
		best.Chop( forward );
		ComboTracker.Get( Scene )?.Beat();
		return best;
	}

	// Yaw setter for tests — the SelfTest can aim the beaver at a known target
	// (tree, rock) before calling DebugSwing.
	public void DebugSetYaw( float yawDegrees )
	{
		_cameraAngles.yaw = yawDegrees;
		_cameraAngles.pitch = 0f;
		_cameraAngles.roll = 0f;
	}

	public Angles DebugCameraAngles => _cameraAngles;

	// Appelé par RunManager.Regenerate pour repointer la cam vers downhill
	// (+X) au début d'un nouveau run, sinon le joueur restart avec le yaw où il
	// avait laissé sa souris sur le run précédent.
	public void ResetLookForNewRun()
	{
		_cameraAngles = Angles.Zero;
		_cameraAngles.pitch = 15f;
	}

	// Kept as a no-op so SelfTest can keep its DebugSetTool(ToolKind.Axe) call
	// site without conditional compilation. Only Axe exists after the bowling
	// pivot — there's nothing to swap.
	public void DebugSetTool( ToolKind tool ) { }

	private IChoppable ChooseSwingTarget( Vector3 origin, Vector3 forward )
	{
		// Scene.GetAllComponents<T> with the base Component type returns nothing —
		// the engine matches by exact T (or T = interface) and there are no raw
		// Component instances. Querying the interface directly is the supported
		// path (Sandbox.Engine.xml says: "This can include interfaces.").
		var candidates = Scene.GetAllComponents<IChoppable>()
			.Where( c => c.IsValid() && c.AcceptsTool( ToolKind.Axe ) )
			.ToList();

		IChoppable best = null;
		var bestScore = float.NegativeInfinity;
		foreach ( var c in candidates )
		{
			var to = c.WorldPosition - origin;
			to.z = 0f;
			var dist = to.Length;
			if ( dist > Tunables.SwingRange ) continue;
			var dot = forward.Dot( to.Normal );
			if ( dot < Tunables.SwingConeDot ) continue;
			var score = dot - dist * 0.005f;
			if ( score > bestScore )
			{
				bestScore = score;
				best = c;
			}
		}
		return best;
	}

	// WYSIWYG swing target : sphere-sweep depuis la position de la caméra dans
	// la direction du crosshair (centre de l'écran). Retourne l'IChoppable touché
	// s'il est dans SwingRange du beaver, sinon null. Donne au joueur un feedback
	// d'aim cohérent (l'arbre qu'il regarde est celui qu'il touche), au lieu du
	// yaw-cone qui divergeait du pitch caméra en 3rd-person.
	//
	// Aussi utilisé par AimIndicator pour le tint highlight + marker au sol.
	public IChoppable PickCameraAimTarget( out Vector3 hitPos )
	{
		hitPos = default;
		if ( !Camera.IsValid() ) return null;
		var origin = WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f);
		var ray = Camera.ScreenNormalToRay( new Vector3( 0.5f, 0.5f, 0f ) );
		// Long sweep — la cam est ~280u derrière le beaver, on couvre CameraDistance
		// + SwingRange devant. La range réelle est validée plus bas (beaver→hit).
		float sweepLen = Tunables.CameraDistance + Tunables.SwingRange + 200f;
		var trace = Scene.Trace
			.Sphere( Tunables.SwingAimSweepRadius, ray.Position, ray.Position + ray.Forward * sweepLen )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "ambient_leaf", "grass_tuft", "wood_chunk", "beaver_tool" )
			.Run();
		if ( !trace.Hit ) return null;
		var go = trace.GameObject;
		if ( !go.IsValid() ) return null;

		// L'arbre peut avoir des ModelRenderers enfants — remonter au root IChoppable.
		var ic = (go.Components.Get<Tree>() as IChoppable)
			?? (go.Components.Get<LogPiece>() as IChoppable)
			?? (go.Components.Get<Tree>( FindMode.InAncestors ) as IChoppable)
			?? (go.Components.Get<LogPiece>( FindMode.InAncestors ) as IChoppable);
		if ( ic is null || !ic.IsValid() || !ic.AcceptsTool( ToolKind.Axe ) ) return null;

		// Range gate : distance beaver(eye)→hit doit rester < SwingRange, sinon
		// le joueur sniperait à travers la moitié de l'arène.
		var hp = trace.EndPosition;
		var beaverToHit = (hp - origin).WithZ( 0f );
		if ( beaverToHit.Length > Tunables.SwingRange ) return null;

		hitPos = hp;
		return ic;
	}
}

public interface IChoppable
{
	Vector3 WorldPosition { get; }
	bool IsValid();
	void Chop( Vector3 direction );
	bool AcceptsTool( ToolKind tool );
}

public enum ToolKind
{
	Axe,
}
