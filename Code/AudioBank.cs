namespace TreeChopping;

// Centralized lookup of gameplay-event → sound.  Ports the Godot proto's
// AudioBank singleton, but uses string sound names instead of procedurally
// generated WAVs (s&box ships .sound assets — we'll author them later).
//
// Each Play* method swallows exceptions because the .sound files are NOT
// shipped yet: this lets us wire all the call sites today and have the
// engine emit "couldn't resolve" warnings without crashing headless
// validation (sbox-server) or the editor.  When the assets are authored
// the try/catch keeps the same safety net for typos and renames.
public static class AudioBank
{
	// Resource paths the eventual .sound assets must live under.  Keeping
	// them as const-string is intentional: we log the set once at startup so
	// the user has a checklist of what still needs to be authored.
	private const string SwingSound = "sounds/swing.sound";
	private const string ChopWoodSound = "sounds/chop_wood.sound";
	private const string ChopStoneSound = "sounds/chop_stone.sound";
	private const string LogBreakSound = "sounds/log_break.sound";
	private const string PickupWoodSound = "sounds/pickup_wood.sound";
	private const string PickupStoneSound = "sounds/pickup_stone.sound";

	// HashSet dedupes the "asset path expected" startup notice — without
	// this we'd spam the log once per call site.
	private static readonly HashSet<string> _announced = new();

	// Volume / pitch parameters per event, mirrored from the Godot proto's
	// randf_range() ranges so the audio feel survives the port.  All volumes
	// here are LINEAR (0..1+); Godot uses dB, so we convert mentally:
	// 0 dB ≈ 1.0, -2 dB ≈ 0.79, -4 dB ≈ 0.63, +1 dB ≈ 1.12, +4 dB ≈ 1.58.
	public static void PlaySwing( Scene scene, Vector3 position )
		=> PlayInternal( SwingSound, scene, position, volume: 0.85f, pitchMin: 0.92f, pitchMax: 1.10f );

	public static void PlayChopWood( Scene scene, Vector3 position )
		=> PlayInternal( ChopWoodSound, scene, position, volume: 0.85f, pitchMin: 0.88f, pitchMax: 1.10f );

	public static void PlayChopStone( Scene scene, Vector3 position )
		=> PlayInternal( ChopStoneSound, scene, position, volume: 0.90f, pitchMin: 0.85f, pitchMax: 1.08f );

	// Loud thunk on tree / log shatter.  Higher base volume = the Godot
	// proto layered crack(+4 dB) + rumble(-4 dB) for the same beat.
	public static void PlayLogBreak( Scene scene, Vector3 position )
		=> PlayInternal( LogBreakSound, scene, position, volume: 1.20f, pitchMin: 0.85f, pitchMax: 1.05f );

	public static void PlayPickupWood( Scene scene, Vector3 position )
		=> PlayInternal( PickupWoodSound, scene, position, volume: 0.80f, pitchMin: 1.05f, pitchMax: 1.25f );

	public static void PlayPickupStone( Scene scene, Vector3 position )
		=> PlayInternal( PickupStoneSound, scene, position, volume: 0.80f, pitchMin: 1.00f, pitchMax: 1.18f );

	private static void PlayInternal( string path, Scene scene, Vector3 position, float volume, float pitchMin, float pitchMax )
	{
		if ( _announced.Add( path ) )
		{
			// First-encounter log so the user knows what assets to author.
			Log.Info( $"[AudioBank] expects sound asset: {path}" );
		}

		// Sound.Play returns a SoundHandle whose Pitch we tweak.  Wrapped
		// in try/catch because the .sound files aren't shipped yet —
		// without this the engine throws when resolving the asset.
		try
		{
			var handle = Sound.Play( path, position, volume );
			if ( handle is not null )
			{
				handle.Pitch = Game.Random.Float( pitchMin, pitchMax );
			}
		}
		catch
		{
			// Swallow: asset missing or playback failure should never
			// crash gameplay logic.
		}
	}
}
