namespace TreeChopping;

// Marker GameObject placed at the player spawn. Detects the player inside
// its radius each tick and exposes PlayerInside for the HUD. The "E" press
// inside triggers an axe upgrade purchase via GameState.
public sealed class ShopArea : Component
{
	[Property] public float Radius { get; set; } = 250f;
	[Property, ReadOnly] public bool PlayerInside { get; private set; }

	private GameState _state;
	private BeaverController _beaver;

	protected override void OnUpdate()
	{
		_state ??= GameState.Get( Scene );
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();
		if ( !_beaver.IsValid() ) { PlayerInside = false; return; }

		float dxy = (_beaver.WorldPosition.WithZ( 0f ) - WorldPosition.WithZ( 0f )).Length;
		PlayerInside = dxy < Radius;

		if ( !PlayerInside || !_state.IsValid() ) return;

		// E (Use) buys the cheapest available upgrade — quick "auto-spend"
		// for casual play. Slot1..Slot4 target specific upgrades for
		// players who want to allocate manually.
		bool bought = false;
		string banner = null;
		// Per-upgrade SFX pitch lets the player distinguish what they just
		// bought without looking at the HUD.
		float pitchMin = 1.2f, pitchMax = 1.4f;
		string sfx = "sounds/log_break.sound";
		if ( Input.Pressed( "Use" ) ) bought = BuyCheapest( out banner );
		else if ( Input.Pressed( "Slot1" ) ) { if ( _state.TryUpgradeAxe() )   { bought = true; banner = $"AXE → {Tunables.AxeTierName[_state.AxeTier].ToUpper()}"; sfx = "sounds/chop_wood.sound"; pitchMin = 0.95f; pitchMax = 1.10f; } }
		else if ( Input.Pressed( "Slot2" ) ) { if ( _state.TryUpgradeSpeed() ) { bought = true; banner = $"SPEED → T{_state.SpeedTier}"; sfx = "sounds/swing.sound"; pitchMin = 1.30f; pitchMax = 1.55f; } }
		else if ( Input.Pressed( "Slot3" ) ) { if ( _state.TryUpgradeLuck() )  { bought = true; banner = $"LUCK → T{_state.LuckTier}"; pitchMin = 1.50f; pitchMax = 1.70f; } }
		else if ( Input.Pressed( "Slot4" ) ) { if ( _state.TryUpgradePower() ) { bought = true; banner = $"POWER → T{_state.PowerTier}"; pitchMin = 0.70f; pitchMax = 0.90f; } }
		else if ( Input.Pressed( "Slot5" ) ) { if ( _state.TryUpgradePet() )   { bought = true; banner = $"PET → T{_state.PetTier}"; pitchMin = 1.80f; pitchMax = 2.10f; } }
		else if ( Input.Pressed( "Slot6" ) )
		{
			int before = _state.Spirits;
			bought = _state.TryPrestige();
			if ( bought ) FirePrestigeBurst( _state.Spirits - before );
		}
		if ( bought )
		{
			Sfx.Play( sfx, WorldPosition, volume: 0.75f, pitchMin: pitchMin, pitchMax: pitchMax );
			if ( !string.IsNullOrEmpty( banner ) )
			{
				var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
				if ( hud.IsValid() ) hud.ShowUpgradeBanner( banner );
			}
		}
	}

	// Public for AutoPlay's bridge-driven shopping loop. Takes prestige
	// first when available (long-term win), then falls back to the cheapest
	// affordable next-tier purchase.
	public bool BuyCheapestAffordable()
	{
		if ( _state.IsValid() && _state.CanPrestige() )
		{
			int before = _state.Spirits;
			if ( _state.TryPrestige() )
			{
				FirePrestigeBurst( _state.Spirits - before );
				return true;
			}
		}
		return BuyCheapest();
	}

	// Cookie-Clicker / AdVenture-Capitalist-style "you just prestiged" cue :
	// big golden leaf burst at the player + a louder triple log_break pitch
	// stack + a 2.5s HUD banner so the reset reads as a celebration instead
	// of a silent wipe.
	private void FirePrestigeBurst( int spiritsGained )
	{
		var beaver = Scene?.GetAllComponents<BeaverController>().FirstOrDefault();
		var pos = beaver.IsValid()
			? beaver.WorldPosition + Vector3.Up * (Tunables.BeaverEyeHeight * 0.5f)
			: WorldPosition;
		ChipBurst.SpawnLeaves( Scene, pos, Vector3.Forward,  28, Tunables.MythicCanopyTint );
		ChipBurst.SpawnLeaves( Scene, pos, Vector3.Backward, 24, Tunables.MythicTrunkTint );
		ChipBurst.SpawnLeaves( Scene, pos, Vector3.Up,       20, Tunables.MythicCanopyTint );
		Sfx.Play( "sounds/log_break.sound", pos, volume: 1.10f, pitchMin: 0.65f, pitchMax: 0.85f );
		Sfx.Play( "sounds/log_break.sound", pos, volume: 0.90f, pitchMin: 1.30f, pitchMax: 1.50f );
		var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
		if ( hud.IsValid() ) hud.ShowPrestigeBanner( spiritsGained );
	}

	private bool BuyCheapest() => BuyCheapest( out _ );

	private bool BuyCheapest( out string banner )
	{
		banner = null;
		int cAxe   = _state.AxeTier   < Tunables.MaxAxeTier  ? Tunables.AxeTierCosts[_state.AxeTier + 1]   : int.MaxValue;
		int cSpeed = _state.SpeedTier < Tunables.MaxStatTier ? Tunables.SpeedCosts[_state.SpeedTier + 1]   : int.MaxValue;
		int cLuck  = _state.LuckTier  < Tunables.MaxStatTier ? Tunables.LuckCosts[_state.LuckTier + 1]     : int.MaxValue;
		int cPower = _state.PowerTier < Tunables.MaxStatTier ? Tunables.PowerCosts[_state.PowerTier + 1]   : int.MaxValue;
		int cPet   = _state.PetTier   < Tunables.MaxPetTier  ? Tunables.PetCosts[_state.PetTier + 1]       : int.MaxValue;
		int cheapest = Math.Min( Math.Min( Math.Min( cAxe, cSpeed ), Math.Min( cLuck, cPower ) ), cPet );
		if ( cheapest == int.MaxValue || _state.Wood < cheapest ) return false;
		if ( cheapest == cAxe   && _state.TryUpgradeAxe()   ) { banner = $"AXE → {Tunables.AxeTierName[_state.AxeTier].ToUpper()}"; return true; }
		if ( cheapest == cSpeed && _state.TryUpgradeSpeed() ) { banner = $"SPEED → T{_state.SpeedTier}"; return true; }
		if ( cheapest == cLuck  && _state.TryUpgradeLuck()  ) { banner = $"LUCK → T{_state.LuckTier}"; return true; }
		if ( cheapest == cPower && _state.TryUpgradePower() ) { banner = $"POWER → T{_state.PowerTier}"; return true; }
		if ( cheapest == cPet   && _state.TryUpgradePet()   ) { banner = $"PET → T{_state.PetTier}"; return true; }
		return false;
	}
}
