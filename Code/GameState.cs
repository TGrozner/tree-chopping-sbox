namespace TreeChopping;

// Persistent progression state — wood balance + current tool tier. Persists
// to disk so closing/reopening the game keeps the player where they were.
// Mow-the-lawn-like : the player never "loses", just accumulates.
public sealed class GameState : Component
{
	[Property, ReadOnly] public int Wood { get; private set; }
	[Property, ReadOnly] public int AxeTier { get; private set; }

	public static GameState Get( Scene scene )
		=> scene?.GetAllComponents<GameState>().FirstOrDefault();

	private const string PersistFile = "progress.json";

	private class SaveData
	{
		public int Wood { get; set; }
		public int AxeTier { get; set; }
		public string DateUtc { get; set; }
	}

	protected override void OnStart() => Load();

	private void Load()
	{
		try
		{
			if ( !FileSystem.Data.FileExists( PersistFile ) ) return;
			var d = FileSystem.Data.ReadJsonOrDefault<SaveData>( PersistFile, null );
			if ( d != null ) { Wood = d.Wood; AxeTier = d.AxeTier; }
			else Log.Warning( $"[GameState] {PersistFile} present but unreadable — starting fresh (wood=0 tier=0)" );
			Log.Info( $"[GameState] Loaded : wood={Wood} tier={AxeTier}" );
		}
		catch ( System.Exception ex ) { Log.Warning( $"[GameState] Load failed: {ex.Message}" ); }
	}

	private void Save()
	{
		// Skip persistence when the headless selftest is running — its
		// AddWood / ResetForTest calls would otherwise clobber the user's
		// real progress.json (the FileSystem.Data dir is shared between
		// selftest and human play).
		if ( SelfTest.IsActiveRequest() ) return;
		try
		{
			FileSystem.Data.WriteJson( PersistFile, new SaveData
			{
				Wood = Wood, AxeTier = AxeTier, DateUtc = DateTime.UtcNow.ToString( "yyyy-MM-dd HH:mm" )
			} );
		}
		catch ( System.Exception ex ) { Log.Warning( $"[GameState] Save failed: {ex.Message}" ); }
	}

	public void AddWood( int amount )
	{
		if ( amount <= 0 ) return;
		Wood += amount;
		Save();
	}

	public bool TryUpgradeAxe()
	{
		if ( AxeTier >= Tunables.MaxAxeTier ) return false;
		int cost = Tunables.AxeTierCosts[AxeTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost;
		AxeTier++;
		Save();
		Log.Info( $"[GameState] Axe upgraded to T{AxeTier}" );
		return true;
	}

	// Per-tier swing power : how many ChopsRemaining the swing removes per
	// click. T0 = 1 (bare hands feel), T3 = 4 (massive axe).
	public int ChopPower => Tunables.AxeTierChopPower[AxeTier];

	// Wood gain multiplier per tier — better tools harvest more.
	public float WoodMultiplier => Tunables.AxeTierWoodMul[AxeTier];

	// Test-only : wipe back to defaults IN MEMORY ONLY. Do not Save() — that
	// would overwrite the user's real progress.json (FileSystem.Data is the
	// same directory for selftest and human play). The selftest runs for ~12s
	// and exits ; the disk file is untouched, so the next human play loads
	// the real progress.
	public void ResetForTest()
	{
		Wood = 0;
		AxeTier = 0;
	}
}
