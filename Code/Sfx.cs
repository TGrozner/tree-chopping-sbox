namespace TreeChopping;

// Small wrapper around Sound.Play. Audio assets should never crash gameplay;
// failed playback becomes a no-op. DebugLog is used by FilmStrip to print every
// Sfx.Play event while auditing the chop feel.
public static class Sfx
{
	public static bool DebugLog { get; set; }
	private const string LogFile = "audio_log.txt";
	private static readonly List<string> DebugPaths = new();
	private static readonly List<string> DebugLocalPaths = new();
	private static readonly List<string> DebugWorldPaths = new();

	public static int DebugCount( string path ) => DebugPaths.Count( p => p == path );
	public static int DebugCountLocal( string path ) => DebugLocalPaths.Count( p => p == path );
	public static int DebugCountWorld( string path ) => DebugWorldPaths.Count( p => p == path );
	public static string DebugSummary() => string.Join( ", ", DebugPaths );
	public static string DebugLocalSummary() => string.Join( ", ", DebugLocalPaths );
	public static string DebugWorldSummary() => string.Join( ", ", DebugWorldPaths );

	public static void Play( string path, Vector3 pos, float volume = 1f, float pitchMin = 0.95f, float pitchMax = 1.05f )
	{
		try
		{
			float mixedVolume = volume * Tunables.SfxMasterVolume;
			float actualPitch = pitchMax > pitchMin ? Game.Random.Float( pitchMin, pitchMax ) : 1f;
			RecordDebugEvent( path, mixedVolume, volume, pitchMin, pitchMax, actualPitch, pos, false );
			var h = Sound.Play( path, pos, mixedVolume );
			if ( h is not null && pitchMax > pitchMin ) h.Pitch = actualPitch;
		}
		catch { }
	}

	public static void PlayLocal( string path, float volume = 1f, float pitchMin = 0.95f, float pitchMax = 1.05f )
	{
		try
		{
			var h = Sound.Play( path );
			float mixedVolume = volume * Tunables.SfxMasterVolume;
			float actualPitch = pitchMax > pitchMin ? Game.Random.Float( pitchMin, pitchMax ) : 1f;
			RecordDebugEvent( path, mixedVolume, volume, pitchMin, pitchMax, actualPitch, default, true );
			if ( h is not null )
			{
				h.Volume = mixedVolume;
				if ( pitchMax > pitchMin ) h.Pitch = actualPitch;
			}
		}
		catch { }
	}

	public static void ClearAudioLog()
	{
		DebugPaths.Clear();
		DebugLocalPaths.Clear();
		DebugWorldPaths.Clear();
		try { FileSystem.Data.WriteAllText( LogFile, "# audio_log - Sfx.Play events\n" ); }
		catch { }
	}

	private static void RecordDebugEvent( string path, float mixedVolume, float rawVolume, float pitchMin, float pitchMax, float actualPitch, Vector3 pos, bool local )
	{
		if ( !DebugLog ) return;
		DebugPaths.Add( path );
		if ( local ) DebugLocalPaths.Add( path );
		else DebugWorldPaths.Add( path );
		string line = local
			? $"{Time.Now:F3}\t{path}\tLOCAL\tvol={mixedVolume:F2} raw={rawVolume:F2}\tpitch=[{pitchMin:F2}..{pitchMax:F2}]->{actualPitch:F2}"
			: $"{Time.Now:F3}\t{path}\tvol={mixedVolume:F2} raw={rawVolume:F2}\tpitch=[{pitchMin:F2}..{pitchMax:F2}]->{actualPitch:F2}\tpos={pos.x:F0},{pos.y:F0},{pos.z:F0}";
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
