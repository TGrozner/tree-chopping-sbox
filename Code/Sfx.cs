namespace TreeChopping;

// Tiny try/catch-wrapped Sound.Play. Used across the codebase so failing to
// resolve an audio asset never crashes gameplay — at most we lose a sound.
public static class Sfx
{
	public static void Play( string path, Vector3 pos, float volume = 1f, float pitchMin = 0.95f, float pitchMax = 1.05f )
	{
		try
		{
			var h = Sound.Play( path, pos, volume );
			if ( h is not null && pitchMax > pitchMin )
				h.Pitch = Game.Random.Float( pitchMin, pitchMax );
		}
		catch { /* asset missing or playback failure — no-op */ }
	}
}
