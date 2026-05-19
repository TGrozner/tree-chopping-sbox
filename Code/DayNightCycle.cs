namespace TreeChopping;

// 90-second day/night loop. Ported from the Godot proto's ambiance.gd:
// sun rotates around the X axis (Z-up), tinted warm at dawn/dusk and cool
// at night. Writes are done in OnPreRender so they always win over any
// other system that touched the sun earlier in the frame (Weather).
public sealed class DayNightCycle : Component
{
	[Property] public DirectionalLight Sun { get; set; }
	[Property] public SkyBox2D Sky { get; set; }
	[Property] public float DayLength { get; set; } = Tunables.DayLengthSeconds;
	[Property, Range( 0f, 1f )] public float DayPhase { get; set; } = Tunables.DayPhaseStart;
	[Property] public Color DaySunColor { get; set; } = new( 1f, 0.94f, 0.85f, 1f );
	[Property] public Color DuskSunColor { get; set; } = new( 1f, 0.55f, 0.30f, 1f );
	[Property] public Color NightSunColor { get; set; } = new( 0.40f, 0.50f, 0.70f, 1f );
	[Property] public Color DaySkyTint { get; set; } = new( 1f, 1f, 1f, 1f );
	[Property] public Color DuskSkyTint { get; set; } = new( 0.95f, 0.65f, 0.40f, 1f );
	[Property] public Color NightSkyTint { get; set; } = new( 0.10f, 0.12f, 0.22f, 1f );

	public float SunHeight { get; private set; }
	public float DawnDuskAmount { get; private set; }
	public float NightAmount { get; private set; }

	// Cached at OnStart so OnPreRender doesn't rescan the scene every frame.
	// Per-star base tint stored alongside so we can multiply by NightAmount
	// without losing the slight colour jitter each star was born with.
	private readonly List<(ModelRenderer Renderer, Color BaseTint)> _stars = new();

	public static DayNightCycle Get( Scene scene )
	{
		return scene?.GetAllComponents<DayNightCycle>().FirstOrDefault();
	}

	protected override void OnStart()
	{
		Sun ??= Scene?.GetAllComponents<DirectionalLight>().FirstOrDefault();
		Sky ??= Scene?.GetAllComponents<SkyBox2D>().FirstOrDefault();

		SpawnStarfield();
	}

	private void SpawnStarfield()
	{
		const int StarCount = 200;
		const float StarHemisphereRadius = 5000f;
		const float StarSize = 8f;
		const int StarSeed = 0x57A45; // deterministic so star pattern is stable across reloads
		var StarHemisphereCenter = new Vector3( 0f, 0f, 800f );

		// Single parent so the editor scene tree stays tidy and we can
		// disable/move the whole field by toggling the root.
		var root = Scene.CreateObject();
		root.Name = "_stars";

		var rng = new Random( StarSeed );

		for ( int i = 0; i < StarCount; i++ )
		{
			// Uniform-ish sample on a hemisphere shell: cosine-weighted theta
			// would clump near the pole; using sqrt on the z component keeps
			// distribution roughly even when projected to the dome above us.
			float u = (float)rng.NextDouble();           // azimuth 0..1
			float v = (float)rng.NextDouble();           // upper-half elevation 0..1
			float azimuth = u * MathF.Tau;
			float z = v;                                 // 0=horizon, 1=zenith
			float horiz = MathF.Sqrt( 1f - z * z );      // shell radius at this z
			var offset = new Vector3(
				MathF.Cos( azimuth ) * horiz,
				MathF.Sin( azimuth ) * horiz,
				z
			) * StarHemisphereRadius;

			var go = root.Scene.CreateObject();
			go.Name = $"Star_{i}";
			go.Parent = root;
			go.WorldPosition = StarHemisphereCenter + offset;
			// Model.Cube is 50u native; divide to land at StarSize.
			go.WorldScale = new Vector3( StarSize / Tunables.CubeBase );
			go.Tags.Add( "star" );

			var mr = go.AddComponent<ModelRenderer>();
			mr.Model = Model.Cube;
			// Pure white with a small per-star bias toward warm yellow or
			// cool blue so the field doesn't read as a uniform speckle.
			float jitter = (float)rng.NextDouble();
			Color baseTint;
			if ( jitter < 0.5f )
			{
				float t = jitter / 0.5f * 0.10f; // 0..0.10 toward yellow
				baseTint = new Color( 1f, 1f, 1f - t, 1f );
			}
			else
			{
				float t = (jitter - 0.5f) / 0.5f * 0.10f; // 0..0.10 toward blue
				baseTint = new Color( 1f - t, 1f - t * 0.5f, 1f, 1f );
			}
			mr.Tint = baseTint;

			_stars.Add( (mr, baseTint) );
		}
	}

	protected override void OnUpdate()
	{
		DayPhase = (DayPhase + Time.Delta / DayLength) % 1f;
	}

	protected override void OnPreRender()
	{
		float sunAngleRad = (DayPhase - 0.5f) * MathF.PI;
		SunHeight = MathF.Cos( sunAngleRad );
		DawnDuskAmount = (1f - SunHeight * 1.2f).Clamp( 0f, 1f );
		NightAmount = (-SunHeight * 1.5f).Clamp( 0f, 1f );

		if ( Sun.IsValid() )
		{
			// Pitch: -90° at noon (light straight down), 0° at horizon (dawn/dusk),
			// flips to +90° underground (midnight). Yaw stays constant — sun moves
			// along an arc with a fixed compass bearing for visual readability.
			float pitchDeg = -SunHeight * 90f;
			Sun.GameObject.WorldRotation = Rotation.From( new Angles( pitchDeg, Tunables.SunYawDegrees, 0f ) );

			float energy = (SunHeight * Tunables.SunMaxEnergyMul + 0.1f).Clamp( Tunables.SunMinEnergyMul, Tunables.SunMaxEnergyMul );
			var hue = Color.Lerp( DaySunColor, DuskSunColor, DawnDuskAmount );
			hue = Color.Lerp( hue, NightSunColor, NightAmount );
			Sun.LightColor = new Color( hue.r * energy, hue.g * energy, hue.b * energy, 1f );
		}

		if ( Sky.IsValid() )
		{
			var tint = Color.Lerp( DaySkyTint, DuskSkyTint, DawnDuskAmount );
			tint = Color.Lerp( tint, NightSkyTint, NightAmount );
			Sky.Tint = tint;
		}

		// Stars: tint*NightAmount darkens the unlit cube during the day to
		// match the sky (effectively invisible), then ramps back to the
		// per-star base colour at midnight. Disable the GameObject entirely
		// during the day so we skip the renderer entirely — 200 hidden draws
		// would otherwise still hit the broadphase.
		bool visible = NightAmount > 0.05f;
		for ( int i = 0; i < _stars.Count; i++ )
		{
			var (mr, baseTint) = _stars[i];
			if ( !mr.IsValid() ) continue;

			if ( mr.GameObject.Enabled != visible )
				mr.GameObject.Enabled = visible;

			if ( visible )
			{
				mr.Tint = new Color(
					baseTint.r * NightAmount,
					baseTint.g * NightAmount,
					baseTint.b * NightAmount,
					1f
				);
			}
		}
	}
}
