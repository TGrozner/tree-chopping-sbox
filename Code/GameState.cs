namespace TreeChopping;

// Persistent progression state — wood balance + current tool tier. Persists
// to disk so closing/reopening the game keeps the player where they were.
// Mow-the-lawn-like : the player never "loses", just accumulates.
public sealed class GameState : Component
{
	[Property, ReadOnly] public int Wood { get; private set; }
	[Property, ReadOnly] public int AxeTier { get; private set; }
	[Property, ReadOnly] public int SpeedTier { get; private set; }
	[Property, ReadOnly] public int LuckTier { get; private set; }
	[Property, ReadOnly] public int PowerTier { get; private set; }

	public static GameState Get( Scene scene )
		=> scene?.GetAllComponents<GameState>().FirstOrDefault();

	private const string PersistFile = "progress.json";

	private class SaveData
	{
		public int Wood { get; set; }
		public int AxeTier { get; set; }
		public int SpeedTier { get; set; }
		public int LuckTier { get; set; }
		public int PowerTier { get; set; }
		public string DateUtc { get; set; }
	}

	protected override void OnStart() => Load();

	private void Load()
	{
		try
		{
			if ( !FileSystem.Data.FileExists( PersistFile ) ) return;
			var d = FileSystem.Data.ReadJsonOrDefault<SaveData>( PersistFile, null );
			if ( d != null )
			{
				Wood = d.Wood; AxeTier = d.AxeTier;
				SpeedTier = d.SpeedTier; LuckTier = d.LuckTier; PowerTier = d.PowerTier;
			}
			else Log.Warning( $"[GameState] {PersistFile} present but unreadable — starting fresh" );
			Log.Info( $"[GameState] Loaded : wood={Wood} axe=T{AxeTier} spd=T{SpeedTier} luk=T{LuckTier} pwr=T{PowerTier}" );
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
				Wood = Wood, AxeTier = AxeTier,
				SpeedTier = SpeedTier, LuckTier = LuckTier, PowerTier = PowerTier,
				DateUtc = DateTime.UtcNow.ToString( "yyyy-MM-dd HH:mm" )
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

	public bool TryUpgradeSpeed()
	{
		if ( SpeedTier >= Tunables.MaxStatTier ) return false;
		int cost = Tunables.SpeedCosts[SpeedTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost; SpeedTier++; Save();
		Log.Info( $"[GameState] Speed upgraded to T{SpeedTier}" );
		return true;
	}

	public bool TryUpgradeLuck()
	{
		if ( LuckTier >= Tunables.MaxStatTier ) return false;
		int cost = Tunables.LuckCosts[LuckTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost; LuckTier++; Save();
		Log.Info( $"[GameState] Luck upgraded to T{LuckTier}" );
		return true;
	}

	public bool TryUpgradePower()
	{
		if ( PowerTier >= Tunables.MaxStatTier ) return false;
		int cost = Tunables.PowerCosts[PowerTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost; PowerTier++; Save();
		Log.Info( $"[GameState] Power upgraded to T{PowerTier}" );
		return true;
	}

	// Per-swing chop damage = axe tier base + power bonus from personal stat.
	public int ChopPower => Tunables.AxeTierChopPower[AxeTier] + Tunables.PowerBonus[PowerTier];
	// Wood gain multiplier — axe tier only ; the Luck stat is a separate
	// roll-for-double check inside Tree.GiveWoodOnce.
	public float WoodMultiplier => Tunables.AxeTierWoodMul[AxeTier];
	// Player walk speed multiplier — applied by BeaverController to the
	// underlying PlayerController.WalkSpeed.
	public float SpeedMultiplier => Tunables.SpeedMul[SpeedTier];
	// Chance per chop to double the wood drop (0..1).
	public float LuckChance => Tunables.LuckChance[LuckTier];

	public void ResetForTest()
	{
		Wood = 0;
		AxeTier = 0;
		SpeedTier = 0;
		LuckTier = 0;
		PowerTier = 0;
	}
}
