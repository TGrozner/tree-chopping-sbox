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
		else if ( Input.Pressed( "Slot5" ) ) bought = _state.TryPrestige();
		if ( bought ) Sfx.Play( "sounds/log_break.sound", WorldPosition, volume: 0.7f, pitchMin: 1.2f, pitchMax: 1.4f );
	}

	// Public for AutoPlay's bridge-driven shopping loop.
	public bool BuyCheapestAffordable() => BuyCheapest();

	private bool BuyCheapest()
	{
		// Pick the cheapest affordable next-tier purchase across all 4 axes,
		// in price order. Returns false if nothing is affordable.
		int cAxe   = _state.AxeTier   < Tunables.MaxAxeTier  ? Tunables.AxeTierCosts[_state.AxeTier + 1]   : int.MaxValue;
		int cSpeed = _state.SpeedTier < Tunables.MaxStatTier ? Tunables.SpeedCosts[_state.SpeedTier + 1]   : int.MaxValue;
		int cLuck  = _state.LuckTier  < Tunables.MaxStatTier ? Tunables.LuckCosts[_state.LuckTier + 1]     : int.MaxValue;
		int cPower = _state.PowerTier < Tunables.MaxStatTier ? Tunables.PowerCosts[_state.PowerTier + 1]   : int.MaxValue;
		int cheapest = Math.Min( Math.Min( cAxe, cSpeed ), Math.Min( cLuck, cPower ) );
		if ( cheapest == int.MaxValue || _state.Wood < cheapest ) return false;
		if ( cheapest == cAxe ) return _state.TryUpgradeAxe();
		if ( cheapest == cSpeed ) return _state.TryUpgradeSpeed();
		if ( cheapest == cLuck ) return _state.TryUpgradeLuck();
		return _state.TryUpgradePower();
	}
}
