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
			if ( FileSystem.Data.FileExists( PersistFile ) )
			{
				var d = FileSystem.Data.ReadJsonOrDefault<SaveData>( PersistFile, null );
				if ( d != null ) { Wood = d.Wood; AxeTier = d.AxeTier; }
				Log.Info( $"[GameState] Loaded : wood={Wood} tier={AxeTier}" );
			}
		}
		catch ( System.Exception ex ) { Log.Warning( $"[GameState] Load failed: {ex.Message}" ); }
	}

	private void Save()
	{
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

	public bool TrySpendWood( int amount )
	{
		if ( Wood < amount ) return false;
		Wood -= amount;
		Save();
		return true;
	}

	public bool TryUpgradeAxe()
	{
		if ( AxeTier >= Tunables.MaxAxeTier ) return false;
		int cost = Tunables.AxeTierCosts[AxeTier + 1];
		if ( !TrySpendWood( cost ) ) return false;
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

	// Test-only : wipe back to defaults.
	public void ResetForTest()
	{
		Wood = 0;
		AxeTier = 0;
		Save();
	}
}
