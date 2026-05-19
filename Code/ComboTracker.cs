namespace TreeChopping;

public sealed class ComboTracker : Component
{
	[Property, ReadOnly] public int Chain { get; private set; }
	[Property, ReadOnly] public float TimeLeft { get; private set; }
	[Property, ReadOnly] public float SlowmoElapsed { get; private set; }
	[Property, ReadOnly] public float TraumaAmount { get; private set; }

	private TimeSince _timeSinceLastBeat;

	public static ComboTracker Get( Scene scene )
	{
		return scene?.GetAllComponents<ComboTracker>().FirstOrDefault();
	}

	public void Beat()
	{
		_timeSinceLastBeat = 0f;
		Chain++;
		AddTrauma( 0.18f );

		if ( Chain == Tunables.ComboSlowmoChain )
		{
			SlowmoElapsed = 0f;
		}
		if ( Chain == Tunables.ComboFlashChain )
		{
			AddTrauma( 0.45f );
		}
	}

	public void AddTrauma( float amount )
	{
		TraumaAmount = MathF.Min( 1f, TraumaAmount + amount );
	}

	protected override void OnUpdate()
	{
		TimeLeft = MathF.Max( 0f, Tunables.ComboIdleTimeout - _timeSinceLastBeat );
		if ( _timeSinceLastBeat > Tunables.ComboIdleTimeout && Chain != 0 )
		{
			Chain = 0;
		}

		TraumaAmount = MathF.Max( 0f, TraumaAmount - Time.Delta * Tunables.ComboTraumaDecay );

		// Slowmo ramp + recover
		if ( SlowmoElapsed >= 0f && SlowmoElapsed < Tunables.ComboSlowmoDuration )
		{
			SlowmoElapsed += Time.Delta;
			float t = SlowmoElapsed / Tunables.ComboSlowmoDuration;
			float scale = MathX.Lerp( Tunables.ComboSlowmoScale, 1f, MathF.Pow( t, 2f ) );
			Scene.TimeScale = scale;
		}
		else if ( Scene.TimeScale < 0.999f )
		{
			Scene.TimeScale = 1f;
		}
	}
}
