namespace TreeChopping;

// Global wind system — Valheim EnvMan.GetWindForce / GetWindDir / GetWindIntensity.
// Pour notre projet : pas de shader vertex animation (Valheim use shaders for
// foliage sway). On expose juste la direction + intensité courantes que les
// Components consomment côté CPU (Tree.TickWobble sway, leaves drift, etc.).
//
// Valheim setup réel : m_windDir1/m_windDir2 lerp entre états de wind, transitions
// pilotées par biome / time-of-day / random events. Notre simplification : un sinus
// continu en direction (rotation lente sur WindCycle) + une intensité qui oscille
// (gusts) avec WindGustCycle. Tout dérivé de Time.Now → pas de state, pas de sync.
public static class EnvWind
{
	// Wind direction = horizontal vector qui rotate slowly. Z=0.
	public static Vector3 GetWindDir()
	{
		float t = Time.Now / Tunables.WindRotationCycle;
		float ang = t * MathF.Tau;
		return new Vector3( MathF.Cos( ang ), MathF.Sin( ang ), 0f );
	}

	// Wind intensity [0..1] qui pulse en gusts. 0.5 baseline + 0.5 sine.
	public static float GetWindIntensity()
	{
		float t = Time.Now / Tunables.WindGustCycle;
		return 0.5f + 0.5f * MathF.Sin( t * MathF.Tau );
	}

	// Combined : direction × intensity (= Valheim GetWindForce).
	public static Vector3 GetWindForce() => GetWindDir() * GetWindIntensity();
}
