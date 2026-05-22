namespace TreeChopping;

// Small wrapper around Sound.Play. Audio assets should never crash gameplay;
// failed playback becomes a no-op. DebugLog is used by FilmStrip to print every
// Sfx.Play event while auditing the chop feel.
public static class Sfx
{
	public static bool DebugLog { get; set; }
	private const string LogFile = "audio_log.txt";

	public static void Play( string path, Vector3 pos, float volume = 1f, float pitchMin = 0.95f, float pitchMax = 1.05f )
	{
		try
		{
			var h = Sound.Play( path, pos, volume );
			float actualPitch = 1f;
			if ( h is not null && pitchMax > pitchMin )
			{
				actualPitch = Game.Random.Float( pitchMin, pitchMax );
				h.Pitch = actualPitch;
			}
			if ( DebugLog )
			{
				string line = $"{Time.Now:F3}\t{path}\tvol={volume:F2}\tpitch=[{pitchMin:F2}..{pitchMax:F2}]->{actualPitch:F2}\tpos={pos.x:F0},{pos.y:F0},{pos.z:F0}";
				Log.Info( $"[TC_SFX] {line}" );
				try
				{
					string existing = FileSystem.Data.FileExists( LogFile )
						? FileSystem.Data.ReadAllText( LogFile )
						: "# audio_log - Sfx.Play events\n";
					FileSystem.Data.WriteAllText( LogFile, existing + line + "\n" );
				}
				catch { }
			}
		}
		catch { }
	}

	public static void ClearAudioLog()
	{
		try { FileSystem.Data.WriteAllText( LogFile, "# audio_log - Sfx.Play events\n" ); }
		catch { }
	}
}
