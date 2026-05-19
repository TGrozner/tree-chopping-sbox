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

	public static DayNightCycle Get( Scene scene )
	{
		return scene?.GetAllComponents<DayNightCycle>().FirstOrDefault();
	}

	protected override void OnStart()
	{
		Sun ??= Scene?.GetAllComponents<DirectionalLight>().FirstOrDefault();
		Sky ??= Scene?.GetAllComponents<SkyBox2D>().FirstOrDefault();
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
	}
}
