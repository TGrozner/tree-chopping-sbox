namespace TreeChopping;

public enum WeatherState
{
	Clear,
	Cloudy,
	Rain,
}

public sealed class Weather : Component
{
	[Property] public DirectionalLight Sun { get; set; }
	[Property] public SkyBox2D Sky { get; set; }
	[Property] public float StateDurationMin { get; set; } = 25f;
	[Property] public float StateDurationMax { get; set; } = 55f;
	[Property] public int Seed { get; set; } = 0x6EA7;
	[Property, ReadOnly] public WeatherState State { get; private set; } = WeatherState.Clear;

	private TimeSince _timeInState;
	private float _currentDuration;
	private Random _rng;

	public static Weather Get( Scene scene )
	{
		return scene?.GetAllComponents<Weather>().FirstOrDefault();
	}

	protected override void OnStart()
	{
		_rng = new Random( Seed );
		Sun ??= Scene?.GetAllComponents<DirectionalLight>().FirstOrDefault();
		Sky ??= Scene?.GetAllComponents<SkyBox2D>().FirstOrDefault();
		PickNextDuration();
		ApplyState();
	}

	protected override void OnUpdate()
	{
		if ( _timeInState >= _currentDuration )
		{
			AdvanceState();
		}
	}

	private void AdvanceState()
	{
		State = State switch
		{
			WeatherState.Clear => _rng.NextDouble() < 0.55 ? WeatherState.Cloudy : WeatherState.Clear,
			WeatherState.Cloudy => _rng.NextDouble() < 0.55 ? WeatherState.Rain : WeatherState.Clear,
			WeatherState.Rain => _rng.NextDouble() < 0.7 ? WeatherState.Cloudy : WeatherState.Clear,
			_ => WeatherState.Clear,
		};
		_timeInState = 0f;
		PickNextDuration();
		ApplyState();
		Log.Info( $"[Weather] -> {State} (for {_currentDuration:F1}s)" );
	}

	private void PickNextDuration()
	{
		_currentDuration = MathX.Lerp( StateDurationMin, StateDurationMax, (float)_rng.NextDouble() );
	}

	private void ApplyState()
	{
		if ( !Sun.IsValid() ) return;

		switch ( State )
		{
			case WeatherState.Clear:
				Sun.LightColor = new Color( 1f, 0.94f, 0.85f, 1f );
				Sun.SkyColor = new Color( 0.36f, 0.45f, 0.55f, 1f );
				break;
			case WeatherState.Cloudy:
				Sun.LightColor = new Color( 0.62f, 0.65f, 0.70f, 1f );
				Sun.SkyColor = new Color( 0.30f, 0.34f, 0.38f, 1f );
				break;
			case WeatherState.Rain:
				Sun.LightColor = new Color( 0.40f, 0.43f, 0.50f, 1f );
				Sun.SkyColor = new Color( 0.18f, 0.21f, 0.26f, 1f );
				break;
		}
	}
}
