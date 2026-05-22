namespace TreeChopping;

// Tiny try/catch-wrapped Sound.Play. Used across the codebase so failing to
// resolve an audio asset never crashes gameplay — at most we lose a sound.
// Audit hook : DebugLog=true → chaque Play écrit une ligne dans
// FileSystem.Data/audio_log.txt (path + pos + volume + pitch range + time).
// Used pour audit Valheim feel alignment côté audio.
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
				try
				{
					string line = $"{Time.Now:F3}\t{path}\tvol={volume:F2}\tpitch=[{pitchMin:F2}..{pitchMax:F2}]→{actualPitch:F2}\tpos={pos.x:F0},{pos.y:F0},{pos.z:F0}\n";
					string existing = FileSystem.Data.FileExists( LogFile )
						? FileSystem.Data.ReadAllText( LogFile )
						: "# audio_log — Sfx.Play events\n";
					FileSystem.Data.WriteAllText( LogFile, existing + line );
				}
				catch { }
			}
		}
		catch { /* asset missing or playback failure — no-op */ }
	}

	// Pre-cycle reset — clear le log avant un cycle filmstrip pour avoir un
	// fichier propre à analyser ensuite.
	public static void ClearAudioLog()
	{
		try { FileSystem.Data.WriteAllText( LogFile, "# audio_log — Sfx.Play events\n" ); }
		catch { }
	}
}
