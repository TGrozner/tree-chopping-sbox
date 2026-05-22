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
	// Mow-the-lawn-style two-pool economy : BackpackWood is what chopping
	// credits (capped per BackpackTier). The player carries it back to the
	// SELL station, which transfers it to Wood (the wallet). Wood is what
	// gets spent on upgrades. Pivot 2026-05-21 — was wood-instant before.
	[Property, ReadOnly] public int Wood { get; private set; }
	[Property, ReadOnly] public int BackpackWood { get; private set; }
	[Property, ReadOnly] public int AxeTier { get; private set; }
	[Property, ReadOnly] public int SpeedTier { get; private set; }
	[Property, ReadOnly] public int LuckTier { get; private set; }
	[Property, ReadOnly] public int PowerTier { get; private set; }
	[Property, ReadOnly] public int BackpackTier { get; private set; }
	[Property, ReadOnly] public int Spirits { get; private set; }
	[Property, ReadOnly] public int TotalWoodEarned { get; private set; }
	[Property, ReadOnly] public int PetTier { get; private set; }
	[Property, ReadOnly] public int ToolRangeTier { get; private set; }
	[Property, ReadOnly] public int ToolSpeedTier { get; private set; }
	[Property, ReadOnly] public int TreesFelledTotal { get; private set; }
	// Valheim multi-wood-type wallets — Wood (Beech-derived, basic crafts),
	// Finewood (Birch/Oak, advanced), CoreWood (Pine, masts/structures).
	// `Wood` field above = legacy single currency (= alias for WoodByType[0]).
	// Pour preserve les saves existants, Wood / BackpackWood RESTENT le slot 0
	// du nouveau système. Finewood + CoreWood s'accumulent en plus.
	[Property, ReadOnly] public int Finewood { get; private set; }
	[Property, ReadOnly] public int BackpackFinewood { get; private set; }
	[Property, ReadOnly] public int CoreWood { get; private set; }
	[Property, ReadOnly] public int BackpackCoreWood { get; private set; }
	// Valheim Game.IncrementPlayerStat(PlayerStatType.TreeChops) — each chop
	// increments this counter (whether tree fells or not). Tracking only
	// (display/achievements/stats), pas de gameplay impact.
	[Property, ReadOnly] public int TotalChops { get; private set; }
	// Per-axe-tier fell counter — Valheim Game.IncrementPlayerStat(PlayerStatType.TreeTierN).
	// Indexed by Tunables.TreeKindMinAxeTier[Kind] (0=hands, 1=stone, ..., 6=chainsaw).
	// Survives prestige (lifetime stat).
	[Property, ReadOnly] public int[] TreesFelledByTier { get; private set; } = new int[7];

	public int BackpackCapacity => Tunables.BackpackCaps[BackpackTier];
	// BackpackFull check sur le TOTAL (Wood + Finewood + CoreWood) — Valheim
	// inventory partage les slots entre tous les item types.
	public int BackpackTotal => BackpackWood + BackpackFinewood + BackpackCoreWood;
	public bool BackpackFull => BackpackTotal >= BackpackCapacity;

	public static GameState Get( Scene scene )
		=> scene?.GetAllComponents<GameState>().FirstOrDefault();

	// Solo persistence. The per-Steam-id variant (phase6k) bounced between
	// "progress.json" and "progress_{steamId}.json" depending on whether
	// Connection.Local was resolved at Load/Save time — different files
	// each session, partial loads. TODO when real MP is wired : resolve
	// the SteamId-keyed path ONCE at OnStart and cache it on the instance,
	// then use that same path for the entire session.
	private const string PersistFile = "progress.json";

	private class SaveData
	{
		public int Wood { get; set; }
		public int BackpackWood { get; set; }
		public int AxeTier { get; set; }
		public int SpeedTier { get; set; }
		public int LuckTier { get; set; }
		public int PowerTier { get; set; }
		public int BackpackTier { get; set; }
		public int Spirits { get; set; }
		public int TotalWoodEarned { get; set; }
		public int PetTier { get; set; }
		public int ToolRangeTier { get; set; }
		public int ToolSpeedTier { get; set; }
		public int TreesFelledTotal { get; set; }
		public int TotalChops { get; set; }
		public int[] TreesFelledByTier { get; set; }
		public int Finewood { get; set; }
		public int BackpackFinewood { get; set; }
		public int CoreWood { get; set; }
		public int BackpackCoreWood { get; set; }
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
				Wood = d.Wood; BackpackWood = d.BackpackWood; AxeTier = d.AxeTier;
				SpeedTier = d.SpeedTier; LuckTier = d.LuckTier; PowerTier = d.PowerTier;
				BackpackTier = d.BackpackTier;
				Spirits = d.Spirits; TotalWoodEarned = d.TotalWoodEarned;
				PetTier = d.PetTier;
				ToolRangeTier = d.ToolRangeTier; ToolSpeedTier = d.ToolSpeedTier;
				TreesFelledTotal = d.TreesFelledTotal;
				TotalChops = d.TotalChops;
				if ( d.TreesFelledByTier != null && d.TreesFelledByTier.Length == TreesFelledByTier.Length )
					TreesFelledByTier = d.TreesFelledByTier;
				Finewood = d.Finewood;
				BackpackFinewood = d.BackpackFinewood;
				CoreWood = d.CoreWood;
				BackpackCoreWood = d.BackpackCoreWood;
			}
			else Log.Warning( $"[GameState] {PersistFile} present but unreadable — starting fresh" );
			Log.Info( $"[GameState] Loaded : wood={Wood} bag={BackpackWood}/{BackpackCapacity} axe=T{AxeTier} spd=T{SpeedTier} luk=T{LuckTier} pwr=T{PowerTier} spirits={Spirits}" );
		}
		catch ( System.Exception ex ) { Log.Warning( $"[GameState] Load failed: {ex.Message}" ); }
	}

	private void Save()
	{
		// Skip persistence when the headless selftest is running — its
		// AddWood / ResetForTest calls would otherwise clobber the user's
		// real progress.json (the FileSystem.Data dir is shared between
		// selftest and human play). Same gate for FilmStrip when it boots
		// active via +tc_filmstrip — the visual capture scenario resets
		// state and shouldn't touch the player's save either.
		if ( SelfTest.IsActiveRequest() || FilmStrip.IsAnyActive( Scene ) ) return;
		try
		{
			FileSystem.Data.WriteJson( PersistFile, new SaveData
			{
				Wood = Wood, BackpackWood = BackpackWood, AxeTier = AxeTier,
				SpeedTier = SpeedTier, LuckTier = LuckTier, PowerTier = PowerTier,
				BackpackTier = BackpackTier,
				Spirits = Spirits, TotalWoodEarned = TotalWoodEarned,
				PetTier = PetTier,
				ToolRangeTier = ToolRangeTier, ToolSpeedTier = ToolSpeedTier,
				TreesFelledTotal = TreesFelledTotal,
				TotalChops = TotalChops,
				TreesFelledByTier = TreesFelledByTier,
				Finewood = Finewood,
				BackpackFinewood = BackpackFinewood,
				CoreWood = CoreWood,
				BackpackCoreWood = BackpackCoreWood,
				DateUtc = DateTime.UtcNow.ToString( "yyyy-MM-dd HH:mm" )
			} );
		}
		catch ( System.Exception ex ) { Log.Warning( $"[GameState] Save failed: {ex.Message}" ); }
	}

	// Direct-to-wallet credit — used by SelfTest pumping and the SELL
	// station's transfer. Gameplay chopping goes through AddBackpack instead.
	public void AddWood( int amount )
	{
		if ( amount <= 0 ) return;
		Wood += amount;
		TotalWoodEarned += amount;
		TreesFelledTotal++;
		Save();
	}

	// Valheim Game.IncrementPlayerStat(PlayerStatType.TreeChops) — called sur
	// CHAQUE chop réussi (axe-tier-gate-passed et damage applied). Pas de
	// Save() ici car appelé fréquemment ; le Save next AddWood/AddBackpack
	// snapshot le compteur. Returns true si un level up vient d'happenner.
	public bool IncrementTreeChops()
	{
		int oldLevel = WoodCuttingLevel;
		TotalChops++;
		return WoodCuttingLevel > oldLevel;
	}

	// Cosmetic WoodCutting "skill" derived from TotalChops (= Valheim Skills.RaiseSkill
	// display flavor sans gameplay impact). Tier-based shop reste le vrai système.
	// Formula : level = floor(sqrt(TotalChops / 5)). Thresholds : 5 chops=Lv1, 20=Lv2,
	// 45=Lv3, 80=Lv4, 125=Lv5, 180=Lv6, 245=Lv7, etc. Pseudo-Valheim feel.
	public int WoodCuttingLevel => (int)MathF.Floor( MathF.Sqrt( TotalChops / 5f ) );

	// Valheim Game.IncrementPlayerStat(PlayerStatType.TreeTierN) — appelé au
	// fell, indexé par le tier d'axe min requis pour ce kind.
	public void IncrementTreeFelledByTier( int tier )
	{
		if ( tier < 0 || tier >= TreesFelledByTier.Length ) return;
		TreesFelledByTier[tier]++;
		// Pas de Save() ici non plus — sera persisté au prochain Save() autre.
	}

	// Tree chops credit here ; capped by BackpackCapacity (total across all
	// types). Returns how much was actually banked (may be < amount when the
	// backpack overflows ; the caller can use the delta to fire a "FULL" UI hint).
	// Default type = Wood (legacy callers compatibility).
	public int AddBackpack( int amount ) => AddBackpack( amount, WoodType.Wood );

	// Valheim multi-type backpack — items per type stockés séparément, mais
	// la cap totale (Wood + Finewood + CoreWood) reste BackpackCapacity.
	public int AddBackpack( int amount, WoodType type )
	{
		if ( amount <= 0 ) return 0;
		int room = BackpackCapacity - BackpackTotal;
		int banked = Math.Min( amount, Math.Max( 0, room ) );
		if ( banked > 0 )
		{
			switch ( type )
			{
				case WoodType.Wood: BackpackWood += banked; break;
				case WoodType.Finewood: BackpackFinewood += banked; break;
				case WoodType.CoreWood: BackpackCoreWood += banked; break;
			}
			TreesFelledTotal++;
			Save();
		}
		return banked;
	}

	// SELL station entry point — flushes ALL backpack types into their wallets,
	// counts toward lifetime earned (drives prestige threshold based on Wood-equivalent
	// total). Returns the total transferred (0 if all empty). Valheim Trader pattern :
	// you sell par type into separate currencies.
	public int TrySell()
	{
		int total = BackpackTotal;
		if ( total <= 0 ) return 0;
		Wood += BackpackWood;
		Finewood += BackpackFinewood;
		CoreWood += BackpackCoreWood;
		TotalWoodEarned += total;
		BackpackWood = 0;
		BackpackFinewood = 0;
		BackpackCoreWood = 0;
		Save();
		return total;
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
		BackpackWood = 0;
		Finewood = 0;
		BackpackFinewood = 0;
		CoreWood = 0;
		BackpackCoreWood = 0;
		AxeTier = 0;
		SpeedTier = 0;
		LuckTier = 0;
		PowerTier = 0;
		BackpackTier = 0;
		PetTier = 0;
		Save();
		Log.Info( $"[GameState] Prestige : now have {Spirits} Sapling Spirits (+{Spirits}% wood)" );
		return true;
	}

	public bool TryUpgradeAxe()
	{
		if ( AxeTier >= Tunables.MaxAxeTier ) return false;
		// Valheim recipe : multi-resource cost. Vérifier qu'on a Wood + Finewood
		// + CoreWood requis. Si OK, tout débite.
		var recipe = Tunables.AxeTierCostsByType[AxeTier + 1];
		if ( Wood < recipe[0] || Finewood < recipe[1] || CoreWood < recipe[2] ) return false;
		Wood -= recipe[0];
		Finewood -= recipe[1];
		CoreWood -= recipe[2];
		AxeTier++;
		Save();
		Log.Info( $"[GameState] Axe upgraded to T{AxeTier} (cost {recipe[0]}W + {recipe[1]}FW + {recipe[2]}CW)" );
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

	public bool TryUpgradeBackpack()
	{
		if ( BackpackTier >= Tunables.MaxBackpackTier ) return false;
		int cost = Tunables.BackpackCosts[BackpackTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost; BackpackTier++; Save();
		Log.Info( $"[GameState] Backpack upgraded to T{BackpackTier} (cap {BackpackCapacity})" );
		return true;
	}

	public bool TryUpgradeToolRange()
	{
		if ( ToolRangeTier >= Tunables.MaxToolStatTier ) return false;
		int cost = Tunables.ToolRangeCosts[ToolRangeTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost; ToolRangeTier++; Save();
		Log.Info( $"[GameState] Tool range upgraded to T{ToolRangeTier} (×{Tunables.ToolRangeMul[ToolRangeTier]:0.00})" );
		return true;
	}

	public bool TryUpgradeToolSpeed()
	{
		if ( ToolSpeedTier >= Tunables.MaxToolStatTier ) return false;
		int cost = Tunables.ToolSpeedCosts[ToolSpeedTier + 1];
		if ( Wood < cost ) return false;
		Wood -= cost; ToolSpeedTier++; Save();
		Log.Info( $"[GameState] Tool speed upgraded to T{ToolSpeedTier} (recover ×{Tunables.ToolSpeedMul[ToolSpeedTier]:0.00})" );
		return true;
	}

	// Per-swing chop damage = axe tier base + power bonus from personal stat.
	public int ChopPower => Tunables.AxeTierChopPower[AxeTier] + Tunables.PowerBonus[PowerTier];
	// Wood gain multiplier — axe tier × Spirits permanent boost. Luck is a
	// separate roll-for-double check inside Tree.GiveWoodOnce.
	public float WoodMultiplier => Tunables.AxeTierWoodMul[AxeTier] * (1f + Spirits * 0.01f);
	// Player walk speed multiplier — applied by AxeController to the
	// underlying PlayerController.WalkSpeed.
	public float SpeedMultiplier => Tunables.SpeedMul[SpeedTier];
	// Chance per chop to double the wood drop (0..1).
	public float LuckChance => Tunables.LuckChance[LuckTier];
	// Per-tool sub-stats applied to AxeController swing path.
	public float SwingRangeMultiplier => Tunables.ToolRangeMul[ToolRangeTier];
	public float SwingSpeedMultiplier => Tunables.ToolSpeedMul[ToolSpeedTier];

	public void ResetForTest()
	{
		Wood = 0;
		BackpackWood = 0;
		Finewood = 0;
		BackpackFinewood = 0;
		CoreWood = 0;
		BackpackCoreWood = 0;
		AxeTier = 0;
		SpeedTier = 0;
		LuckTier = 0;
		PowerTier = 0;
		BackpackTier = 0;
		ToolRangeTier = 0;
		ToolSpeedTier = 0;
		Spirits = 0;
		TotalWoodEarned = 0;
		PetTier = 0;
		TreesFelledTotal = 0;
		TotalChops = 0;
		for ( int i = 0; i < TreesFelledByTier.Length; i++ ) TreesFelledByTier[i] = 0;
	}
}
