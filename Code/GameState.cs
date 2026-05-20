namespace TreeChopping;

// Persistent progression state — wood balance + current tool tier. Persists
// to disk so closing/reopening the game keeps the player where they were.
// Mow-the-lawn-like : the player never "loses", just accumulates.
//
// MULTIPLAYER TODO : currently a scene-scoped singleton. For 4-player
// co-op, this would need to be (a) per-Player-GameObject as a Component
// owned by each Citizen, with (b) Sandbox [Sync] attributes on the
// persistent fields, and (c) FileSystem.Data persistence keyed by
// SteamId. The shared world (trees, gates, shop totem, terrain) stays
// scene-scoped. The sbproj GameNetworkType=Multiplayer flip enables the
// transport but the per-player partition is still TODO.
public sealed class GameState : Component
{
	[Property, ReadOnly] public int Wood { get; private set; }
	[Property, ReadOnly] public int AxeTier { get; private set; }
	[Property, ReadOnly] public int SpeedTier { get; private set; }
	[Property, ReadOnly] public int LuckTier { get; private set; }
	[Property, ReadOnly] public int PowerTier { get; private set; }
	[Property, ReadOnly] public int Spirits { get; private set; }
	[Property, ReadOnly] public int TotalWoodEarned { get; private set; }
	[Property, ReadOnly] public int GatesBroken { get; private set; }
	[Property, ReadOnly] public int PetTier { get; private set; }

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
		public int Spirits { get; set; }
		public int TotalWoodEarned { get; set; }
		public int GatesBroken { get; set; }
		public int PetTier { get; set; }
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
				Spirits = d.Spirits; TotalWoodEarned = d.TotalWoodEarned;
				GatesBroken = d.GatesBroken; PetTier = d.PetTier;
			}
			else Log.Warning( $"[GameState] {PersistFile} present but unreadable — starting fresh" );
			Log.Info( $"[GameState] Loaded : wood={Wood} axe=T{AxeTier} spd=T{SpeedTier} luk=T{LuckTier} pwr=T{PowerTier} spirits={Spirits}" );
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
				Spirits = Spirits, TotalWoodEarned = TotalWoodEarned,
				GatesBroken = GatesBroken, PetTier = PetTier,
				DateUtc = DateTime.UtcNow.ToString( "yyyy-MM-dd HH:mm" )
			} );
		}
		catch ( System.Exception ex ) { Log.Warning( $"[GameState] Save failed: {ex.Message}" ); }
	}

	public void AddWood( int amount )
	{
		if ( amount <= 0 ) return;
		Wood += amount;
		TotalWoodEarned += amount;
		Save();
	}

	// Prestige : replant the forest. Resets wood + all tiers, awards
	// Sapling Spirits = floor(sqrt(TotalWoodEarned / 50)) on top of any
	// previously earned. Each Spirit gives +1% permanent wood multiplier.
	// Minimum 500 lifetime wood before the first prestige is allowed
	// (yields 3 spirits) — keeps the reset feeling like a real commitment.
	public int SpiritsFromPrestige => (int)MathF.Floor( MathF.Sqrt( TotalWoodEarned / 50f ) );

	public bool CanPrestige() => TotalWoodEarned >= 500 && SpiritsFromPrestige > Spirits;

	public bool TryPrestige()
	{
		if ( !CanPrestige() ) return false;
		Spirits = SpiritsFromPrestige;
		Wood = 0;
		AxeTier = 0;
		SpeedTier = 0;
		LuckTier = 0;
		PowerTier = 0;
		GatesBroken = 0;
		PetTier = 0;
		Save();
		Log.Info( $"[GameState] Prestige : now have {Spirits} Sapling Spirits (+{Spirits}% wood)" );
		return true;
	}

	public void OnGateBroken()
	{
		GatesBroken++;
		Save();
		Log.Info( $"[GameState] Gate broken — outer ring expanded ({GatesBroken} total)" );
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

	public bool TryUpgradePet()
	{
		if ( PetTier >= Tunables.MaxPetTier ) return false;
		int cost = Tunables.PetCosts[PetTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost; PetTier++; Save();
		Log.Info( $"[GameState] Pet upgraded to T{PetTier}" );
		return true;
	}

	// Per-swing chop damage = axe tier base + power bonus from personal stat.
	public int ChopPower => Tunables.AxeTierChopPower[AxeTier] + Tunables.PowerBonus[PowerTier];
	// Wood gain multiplier — axe tier × Spirits permanent boost. Luck is a
	// separate roll-for-double check inside Tree.GiveWoodOnce.
	public float WoodMultiplier => Tunables.AxeTierWoodMul[AxeTier] * (1f + Spirits * 0.01f);
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
		Spirits = 0;
		TotalWoodEarned = 0;
		GatesBroken = 0;
		PetTier = 0;
	}
}
