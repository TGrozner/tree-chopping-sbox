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
		if ( Input.Pressed( "Use" ) ) bought = BuyCheapest();
		else if ( Input.Pressed( "Slot1" ) ) bought = _state.TryUpgradeAxe();
		else if ( Input.Pressed( "Slot2" ) ) bought = _state.TryUpgradeSpeed();
		else if ( Input.Pressed( "Slot3" ) ) bought = _state.TryUpgradeLuck();
		else if ( Input.Pressed( "Slot4" ) ) bought = _state.TryUpgradePower();
		else if ( Input.Pressed( "Slot5" ) ) bought = _state.TryUpgradePet();
		else if ( Input.Pressed( "Slot6" ) )
		{
			int before = _state.Spirits;
			bought = _state.TryPrestige();
			if ( bought ) FirePrestigeBurst( _state.Spirits - before );
		}
		if ( bought ) Sfx.Play( "sounds/log_break.sound", WorldPosition, volume: 0.7f, pitchMin: 1.2f, pitchMax: 1.4f );
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

	private bool BuyCheapest()
	{
		// Pick the cheapest affordable next-tier purchase across all 4 axes,
		// in price order. Returns false if nothing is affordable.
		int cAxe   = _state.AxeTier   < Tunables.MaxAxeTier  ? Tunables.AxeTierCosts[_state.AxeTier + 1]   : int.MaxValue;
		int cSpeed = _state.SpeedTier < Tunables.MaxStatTier ? Tunables.SpeedCosts[_state.SpeedTier + 1]   : int.MaxValue;
		int cLuck  = _state.LuckTier  < Tunables.MaxStatTier ? Tunables.LuckCosts[_state.LuckTier + 1]     : int.MaxValue;
		int cPower = _state.PowerTier < Tunables.MaxStatTier ? Tunables.PowerCosts[_state.PowerTier + 1]   : int.MaxValue;
		int cPet   = _state.PetTier   < Tunables.MaxPetTier  ? Tunables.PetCosts[_state.PetTier + 1]       : int.MaxValue;
		int cheapest = Math.Min( Math.Min( Math.Min( cAxe, cSpeed ), Math.Min( cLuck, cPower ) ), cPet );
		if ( cheapest == int.MaxValue || _state.Wood < cheapest ) return false;
		if ( cheapest == cAxe ) return _state.TryUpgradeAxe();
		if ( cheapest == cSpeed ) return _state.TryUpgradeSpeed();
		if ( cheapest == cLuck ) return _state.TryUpgradeLuck();
		if ( cheapest == cPower ) return _state.TryUpgradePower();
		return _state.TryUpgradePet();
	}
}
