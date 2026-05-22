namespace TreeChopping;

public enum StationKind { Tools, Sell, Upgrades, Prestige }

// Mow-the-lawn-style physical shop : one station per category, each a stone
// pillar with a colored interaction disc + worldspace label. Player walks
// onto the disc → station-specific input listened, station-specific HUD
// mini-menu shown. Stations don't overlap so the same input (e.g. Slot1)
// means different things depending on which station you're standing on.
//
// Replaces the older single ShopArea + 6-line plein-écran menu pattern.
public sealed class ShopStation : Component
{
	[Property] public StationKind Kind { get; set; }
	[Property] public float Radius { get; set; } = 160f;
	[Property, ReadOnly] public bool PlayerInside { get; private set; }

	private GameState _state;
	private BeaverController _beaver;
	private bool _wasInside;
	private bool _justEntered;

	public string Label => Kind switch
	{
		StationKind.Tools    => "TOOLS",
		StationKind.Sell     => "SELL WOOD",
		StationKind.Upgrades => "UPGRADES",
		StationKind.Prestige => "PRESTIGE",
		_ => "?",
	};

	// Ring + cap tint per station, also drives the worldspace label color.
	// Echoes the Mow-the-lawn palette : tools=cyan, sell=green, upgrades=
	// orange, prestige=gold. Already-saturated values, no HDR multiplier —
	// bumping past 1.0 just blows out the tonemapper to pink/white.
	public Color RingTint => Kind switch
	{
		StationKind.Tools    => new( 0.30f, 0.78f, 1.00f, 1f ),
		StationKind.Sell     => new( 0.20f, 0.95f, 0.30f, 1f ),
		StationKind.Upgrades => new( 1.00f, 0.55f, 0.10f, 1f ),
		StationKind.Prestige => new( 1.00f, 0.85f, 0.15f, 1f ),
		_ => Color.White,
	};

	// No visible geometry — Thomas 2026-05-21 dropped the stone pillar too.
	// The worldspace label IS the station marker. PlayerInside still uses
	// the Radius proximity check, just there's nothing to render at the spot.
	public static ShopStation SpawnAt( Scene scene, Vector3 pos, StationKind kind, string name = null )
	{
		var go = scene.CreateObject();
		go.Name = name ?? $"ShopStation.{kind}";
		go.WorldPosition = pos;
		var station = go.AddComponent<ShopStation>();
		station.Kind = kind;
		return station;
	}

	protected override void OnUpdate()
	{
		_state ??= GameState.Get( Scene );
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();
		if ( !_beaver.IsValid() ) { PlayerInside = false; return; }

		float dxy = (_beaver.WorldPosition.WithZ( 0f ) - WorldPosition.WithZ( 0f )).Length;
		bool newInside = dxy < Radius;
		_justEntered = newInside && !_wasInside;
		_wasInside = newInside;
		PlayerInside = newInside;

		DrawWorldLabel();

		if ( !PlayerInside || !_state.IsValid() ) return;

		switch ( Kind )
		{
			case StationKind.Tools:    HandleTools();    break;
			case StationKind.Sell:     HandleSell();     break;
			case StationKind.Upgrades: HandleUpgrades(); break;
			case StationKind.Prestige: HandlePrestige(); break;
		}
	}

	private void HandleSell()
	{
		// Sell station = transfer BackpackWood → Wood. Auto-sells once on
		// disc entry (false→true PlayerInside transition) so the loop reads
		// as "fill bag → run home → it just sells", and again on [E]/[1]
		// after that (in case the player carried more wood back).
		if ( _justEntered ) TryAutoSell();
		if ( Input.Pressed( "Use" ) || Input.Pressed( "Slot1" ) ) TryAutoSell();
	}

	private void TryAutoSell()
	{
		int sold = _state.TrySell();
		if ( sold <= 0 ) return;
		var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
		if ( hud.IsValid() ) hud.ShowSellFlash( sold );
		Sfx.Play( "sounds/log_break.sound", WorldPosition, volume: 0.85f, pitchMin: 1.10f, pitchMax: 1.35f );
	}

	// Project the label position to screen via manual world→screen math.
	// Cleanup 2026-05-21 : the prior BBoxToScreenPixels approach drew labels
	// off-screen sometimes (small bboxes returned garbage centers). Manual
	// camera-relative projection + explicit on-screen + range gating means
	// labels only render when the station is actually a meaningful target.
	private void DrawWorldLabel()
	{
		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var labelWorld = WorldPosition + Vector3.Up * 360f;
		if ( !TryWorldToScreen( camera, labelWorld, out var screen, out float dist ) ) return;
		if ( dist > 2400f ) return;
		if ( screen.x < 0f || screen.x > Screen.Width || screen.y < 0f || screen.y > Screen.Height ) return;
		float size = (3200f / MathF.Max( dist, 200f )).Clamp( 16f, 36f );
		float bgW = Label.Length * size * 0.58f + size * 0.6f;
		float bgH = size * 1.4f;
		var bgRect = new Rect( screen.x - bgW * 0.5f, screen.y - bgH * 0.5f, bgW, bgH );
		var bgAlpha = PlayerInside ? 0.85f : 0.65f;
		camera.Hud.DrawRect( bgRect, new Color( 0f, 0f, 0f, bgAlpha ) );
		var textTint = PlayerInside ? RingTint : RingTint.WithAlpha( 0.95f );
		camera.Hud.DrawText( new TextRendering.Scope( Label, textTint, size ),
			bgRect, TextFlag.Center );
	}

	// Manual perspective projection : dot the world-from-camera vector against
	// the camera basis to get forward/right/up scalars, then divide by forward
	// distance + camera FOV to land in NDC, then map to screen pixels. Returns
	// false if the point is behind the camera.
	public static bool TryWorldToScreen( CameraComponent cam, Vector3 worldPos, out Vector2 screen, out float distance )
	{
		screen = default;
		distance = 0f;
		var rel = worldPos - cam.WorldPosition;
		var fwd = Vector3.Dot( cam.WorldRotation.Forward, rel );
		if ( fwd <= 0.5f ) return false;
		distance = rel.Length;
		float right = Vector3.Dot( cam.WorldRotation.Right, rel );
		float up = Vector3.Dot( cam.WorldRotation.Up, rel );
		float fovRad = cam.FieldOfView * MathF.PI / 180f;
		float aspect = Screen.Width / MathF.Max( Screen.Height, 1f );
		float halfH = MathF.Tan( fovRad * 0.5f );
		float halfW = halfH * aspect;
		float ndcX = (right / fwd) / halfW;
		float ndcY = (up / fwd) / halfH;
		screen = new Vector2( (ndcX + 1f) * 0.5f * Screen.Width, (1f - (ndcY + 1f) * 0.5f) * Screen.Height );
		return true;
	}

	private void HandleTools()
	{
		// Tools station — phase C : axe tier + 2 per-tool sub-stats (range,
		// speed-recover). [E] auto-buys the cheapest. [1] axe, [2] range,
		// [3] speed.
		bool bought = false; string banner = null; string sfx = "sounds/chop_wood.sound"; float pmin = 0.95f, pmax = 1.10f;
		if ( Input.Pressed( "Use" ) )
		{
			(bought, banner, sfx, pmin, pmax) = BuyCheapestTool();
		}
		else if ( Input.Pressed( "Slot1" ) ) { if ( _state.TryUpgradeAxe()       ) { bought = true; banner = $"AXE → {Tunables.AxeTierName[_state.AxeTier].ToUpper()}"; } }
		else if ( Input.Pressed( "Slot2" ) ) { if ( _state.TryUpgradeToolRange() ) { bought = true; banner = $"RANGE → T{_state.ToolRangeTier} (×{Tunables.ToolRangeMul[_state.ToolRangeTier]:0.00})"; sfx = "sounds/swing.sound"; pmin = 1.45f; pmax = 1.60f; } }
		else if ( Input.Pressed( "Slot3" ) ) { if ( _state.TryUpgradeToolSpeed() ) { bought = true; banner = $"SWING SPEED → T{_state.ToolSpeedTier}"; sfx = "sounds/swing.sound"; pmin = 1.80f; pmax = 2.10f; } }

		if ( bought ) AnnounceBuy( banner, sfx, pmin, pmax );
	}

	private (bool bought, string banner, string sfx, float pmin, float pmax) BuyCheapestTool()
	{
		int cAxe   = _state.AxeTier       < Tunables.MaxAxeTier       ? Tunables.AxeTierCosts[_state.AxeTier + 1]       : int.MaxValue;
		int cRange = _state.ToolRangeTier < Tunables.MaxToolStatTier  ? Tunables.ToolRangeCosts[_state.ToolRangeTier + 1] : int.MaxValue;
		int cSpd   = _state.ToolSpeedTier < Tunables.MaxToolStatTier  ? Tunables.ToolSpeedCosts[_state.ToolSpeedTier + 1] : int.MaxValue;
		int cheapest = Math.Min( cAxe, Math.Min( cRange, cSpd ) );
		if ( cheapest == int.MaxValue || _state.Wood < cheapest ) return (false, null, null, 0f, 0f);
		if ( cheapest == cAxe   && _state.TryUpgradeAxe()       ) return (true, $"AXE → {Tunables.AxeTierName[_state.AxeTier].ToUpper()}", "sounds/chop_wood.sound", 0.95f, 1.10f);
		if ( cheapest == cRange && _state.TryUpgradeToolRange() ) return (true, $"RANGE → T{_state.ToolRangeTier} (×{Tunables.ToolRangeMul[_state.ToolRangeTier]:0.00})", "sounds/swing.sound", 1.45f, 1.60f);
		if ( cheapest == cSpd   && _state.TryUpgradeToolSpeed() ) return (true, $"SWING SPEED → T{_state.ToolSpeedTier}", "sounds/swing.sound", 1.80f, 2.10f);
		return (false, null, null, 0f, 0f);
	}

	private void HandleUpgrades()
	{
		// Upgrades station = stats + pet. [E] auto-buys cheapest stat.
		// [1] speed, [2] luck, [3] power, [4] pet — matches the order of
		// the worldspace mini-menu drawn by WoodHud.
		bool bought = false;
		string banner = null;
		string sfx = "sounds/log_break.sound";
		float pmin = 1.2f, pmax = 1.4f;

		if ( Input.Pressed( "Use" ) )
		{
			(bought, banner, sfx, pmin, pmax) = BuyCheapestStat();
		}
		else if ( Input.Pressed( "Slot1" ) ) { if ( _state.TryUpgradeSpeed() )    { bought = true; banner = $"SPEED → T{_state.SpeedTier}"; sfx = "sounds/swing.sound"; pmin = 1.30f; pmax = 1.55f; } }
		else if ( Input.Pressed( "Slot2" ) ) { if ( _state.TryUpgradeLuck()  )    { bought = true; banner = $"LUCK → T{_state.LuckTier}"; pmin = 1.50f; pmax = 1.70f; } }
		else if ( Input.Pressed( "Slot3" ) ) { if ( _state.TryUpgradePower() )    { bought = true; banner = $"POWER → T{_state.PowerTier}"; pmin = 0.70f; pmax = 0.90f; } }
		else if ( Input.Pressed( "Slot4" ) ) { if ( _state.TryUpgradeBackpack() ) { bought = true; banner = $"BACKPACK → T{_state.BackpackTier} ({_state.BackpackCapacity} cap)"; pmin = 1.10f; pmax = 1.30f; } }
		else if ( Input.Pressed( "Slot5" ) ) { if ( _state.TryUpgradePet()   )    { bought = true; banner = $"PET → T{_state.PetTier}"; pmin = 1.80f; pmax = 2.10f; } }

		if ( bought ) AnnounceBuy( banner, sfx, pmin, pmax );
	}

	private void HandlePrestige()
	{
		// Prestige station = the "REPLANT FOREST" altar. [E] / [1] confirm.
		if ( !Input.Pressed( "Use" ) && !Input.Pressed( "Slot1" ) ) return;
		int before = _state.Spirits;
		if ( _state.TryPrestige() ) FirePrestigeBurst( _state.Spirits - before );
	}

	private void AnnounceBuy( string banner, string sfx, float pitchMin, float pitchMax )
	{
		Sfx.Play( sfx, WorldPosition, volume: 0.75f, pitchMin: pitchMin, pitchMax: pitchMax );
		if ( !string.IsNullOrEmpty( banner ) )
		{
			var hud = Scene?.GetAllComponents<WoodHud>().FirstOrDefault();
			if ( hud.IsValid() ) hud.ShowUpgradeBanner( banner );
		}
	}

	private (bool bought, string banner, string sfx, float pmin, float pmax) BuyCheapestStat()
	{
		int cSpeed = _state.SpeedTier    < Tunables.MaxStatTier     ? Tunables.SpeedCosts[_state.SpeedTier + 1]       : int.MaxValue;
		int cLuck  = _state.LuckTier     < Tunables.MaxStatTier     ? Tunables.LuckCosts[_state.LuckTier + 1]         : int.MaxValue;
		int cPower = _state.PowerTier    < Tunables.MaxStatTier     ? Tunables.PowerCosts[_state.PowerTier + 1]       : int.MaxValue;
		int cBack  = _state.BackpackTier < Tunables.MaxBackpackTier ? Tunables.BackpackCosts[_state.BackpackTier + 1] : int.MaxValue;
		int cPet   = _state.PetTier      < Tunables.MaxPetTier      ? Tunables.PetCosts[_state.PetTier + 1]           : int.MaxValue;
		int cheapest = Math.Min( Math.Min( Math.Min( cSpeed, cLuck ), Math.Min( cPower, cBack ) ), cPet );
		if ( cheapest == int.MaxValue || _state.Wood < cheapest ) return (false, null, null, 0f, 0f);
		if ( cheapest == cSpeed && _state.TryUpgradeSpeed()    ) return (true, $"SPEED → T{_state.SpeedTier}",                                 "sounds/swing.sound",     1.30f, 1.55f);
		if ( cheapest == cLuck  && _state.TryUpgradeLuck()     ) return (true, $"LUCK → T{_state.LuckTier}",                                   "sounds/log_break.sound", 1.50f, 1.70f);
		if ( cheapest == cPower && _state.TryUpgradePower()    ) return (true, $"POWER → T{_state.PowerTier}",                                 "sounds/log_break.sound", 0.70f, 0.90f);
		if ( cheapest == cBack  && _state.TryUpgradeBackpack() ) return (true, $"BACKPACK → T{_state.BackpackTier} ({_state.BackpackCapacity} cap)", "sounds/log_break.sound", 1.10f, 1.30f);
		if ( cheapest == cPet   && _state.TryUpgradePet()      ) return (true, $"PET → T{_state.PetTier}",                                     "sounds/log_break.sound", 1.80f, 2.10f);
		return (false, null, null, 0f, 0f);
	}

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

	// AutoPlay / bridge entry point — first FLUSHES the backpack if non-zero
	// (post-Phase B : the player has to actually sell to convert wood),
	// then prestiges if available, then buys the cheapest affordable upgrade.
	public static bool TryBuyCheapestAcrossAll( Scene scene )
	{
		var state = GameState.Get( scene );
		if ( !state.IsValid() ) return false;
		if ( state.BackpackWood > 0 )
		{
			state.TrySell();
			return true;
		}
		if ( state.CanPrestige() )
		{
			var prestigeStation = scene.GetAllComponents<ShopStation>()
				.FirstOrDefault( s => s.IsValid() && s.Kind == StationKind.Prestige );
			int before = state.Spirits;
			if ( state.TryPrestige() )
			{
				if ( prestigeStation.IsValid() ) prestigeStation.FirePrestigeBurst( state.Spirits - before );
				return true;
			}
		}
		int cAxe   = state.AxeTier       < Tunables.MaxAxeTier       ? Tunables.AxeTierCosts[state.AxeTier + 1]         : int.MaxValue;
		int cRange = state.ToolRangeTier < Tunables.MaxToolStatTier  ? Tunables.ToolRangeCosts[state.ToolRangeTier + 1] : int.MaxValue;
		int cSpd   = state.ToolSpeedTier < Tunables.MaxToolStatTier  ? Tunables.ToolSpeedCosts[state.ToolSpeedTier + 1] : int.MaxValue;
		int cSpeed = state.SpeedTier     < Tunables.MaxStatTier      ? Tunables.SpeedCosts[state.SpeedTier + 1]         : int.MaxValue;
		int cLuck  = state.LuckTier      < Tunables.MaxStatTier      ? Tunables.LuckCosts[state.LuckTier + 1]           : int.MaxValue;
		int cPower = state.PowerTier     < Tunables.MaxStatTier      ? Tunables.PowerCosts[state.PowerTier + 1]         : int.MaxValue;
		int cBack  = state.BackpackTier  < Tunables.MaxBackpackTier  ? Tunables.BackpackCosts[state.BackpackTier + 1]   : int.MaxValue;
		int cPet   = state.PetTier       < Tunables.MaxPetTier       ? Tunables.PetCosts[state.PetTier + 1]             : int.MaxValue;
		int cheapest = Math.Min( Math.Min( Math.Min( Math.Min( cAxe, cRange ), Math.Min( cSpd, cSpeed ) ),
			Math.Min( cLuck, cPower ) ), Math.Min( cBack, cPet ) );
		if ( cheapest == int.MaxValue || state.Wood < cheapest ) return false;
		if ( cheapest == cAxe   && state.TryUpgradeAxe()       ) return true;
		if ( cheapest == cRange && state.TryUpgradeToolRange() ) return true;
		if ( cheapest == cSpd   && state.TryUpgradeToolSpeed() ) return true;
		if ( cheapest == cSpeed && state.TryUpgradeSpeed()     ) return true;
		if ( cheapest == cLuck  && state.TryUpgradeLuck()      ) return true;
		if ( cheapest == cPower && state.TryUpgradePower()     ) return true;
		if ( cheapest == cBack  && state.TryUpgradeBackpack()  ) return true;
		if ( cheapest == cPet   && state.TryUpgradePet()       ) return true;
		return false;
	}
}
