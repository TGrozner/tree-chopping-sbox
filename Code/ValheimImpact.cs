namespace TreeChopping;

internal static class ValheimImpact
{
	public static float LerpStep( float low, float high, float value )
	{
		float span = high - low;
		if ( MathF.Abs( span ) < 0.0001f ) return value >= high ? 1f : 0f;
		return ((value - low) / span).Clamp( 0f, 1f );
	}

	public static float ScaleFromSpeed( float speed )
	{
		return LerpStep( Tunables.ImpactMinSpeed, Tunables.ImpactMaxSpeed, speed );
	}

	public static int DamageFromSpeed( float speed, float damageMul = 1f )
	{
		if ( speed < Tunables.ImpactMinSpeed ) return 0;
		float scaledDamage = Tunables.ImpactBaseDamage * ScaleFromSpeed( speed ) * damageMul;
		if ( scaledDamage <= 0f ) return 0;
		return Math.Max( 1, (int)MathF.Ceiling( scaledDamage ) );
	}
}
