namespace TreeChopping;

// Mow-the-lawn HUD : crosshair + wood balance + axe tier badge + shop hint
// when standing inside the shop area. No run state — continuous play.
public sealed class WoodHud : Component
{
	[Property] public Color TextColor { get; set; } = new( 1f, 0.86f, 0.42f, 1f );
	[Property] public Color HotColor { get; set; } = new( 1f, 0.45f, 0.20f, 1f );
	[Property] public Color PanelColor { get; set; } = new( 0.07f, 0.09f, 0.11f, 0.78f );
	[Property] public float FontSize { get; set; } = 24f;

	public static bool DebugVisible { get; private set; }

	private GameState _state;
	private ShopArea _shop;
	private int _lastShownWood;
	private bool _woodSynced;
	private TimeSince _woodChangedTime = 999f;

	protected override void OnUpdate()
	{
		_state ??= GameState.Get( Scene );
		_shop ??= Scene?.GetAllComponents<ShopArea>().FirstOrDefault();
		if ( Input.Pressed( "DebugToggle" ) ) DebugVisible = !DebugVisible;

		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		DrawCrosshair( hud );
		DrawWoodPanel( hud );
		DrawTierBadge( hud );
		DrawShopHint( hud );
		DrawTeleportHint( hud );

		if ( DebugVisible ) DrawDebugBlock( hud );
	}

	private void DrawCrosshair( Sandbox.Rendering.HudPainter hud )
	{
		// Small "+" with a gap in the middle. Reads as a reticle, not a fly
		// stuck on the screen. Stays subtle (alpha 0.55) so it doesn't fight
		// the chop preview highlight on the targeted tree.
		float cx = Screen.Width * 0.5f;
		float cy = Screen.Height * 0.5f;
		var tint = new Color( 0.95f, 0.95f, 0.95f, 0.55f );
		const float armLen = 7f;
		const float armThick = 2f;
		const float gap = 3f;
		hud.DrawRect( new Rect( cx - armLen - gap, cy - armThick * 0.5f, armLen, armThick ), tint );
		hud.DrawRect( new Rect( cx + gap, cy - armThick * 0.5f, armLen, armThick ), tint );
		hud.DrawRect( new Rect( cx - armThick * 0.5f, cy - armLen - gap, armThick, armLen ), tint );
		hud.DrawRect( new Rect( cx - armThick * 0.5f, cy + gap, armThick, armLen ), tint );
	}

	private void DrawWoodPanel( Sandbox.Rendering.HudPainter hud )
	{
		if ( _state is null ) return;
		// First sync after GameState loads : initialise without firing the
		// gain pulse (Wood may be non-zero from progress.json).
		if ( !_woodSynced ) { _lastShownWood = _state.Wood; _woodSynced = true; }
		if ( _state.Wood != _lastShownWood )
		{
			_lastShownWood = _state.Wood;
			_woodChangedTime = 0f;
		}
		const float PulseDuration = 0.35f;
		float pulseT = (float)_woodChangedTime / PulseDuration;
		float scale = pulseT < 1f ? MathX.Lerp( 1.25f, 1.0f, pulseT * pulseT ) : 1f;

		float padL = 36f, padT = 28f;
		float labelSize = 14f;
		float valueSize = 36f * scale;
		float boxW = 180f;
		var labelRect = new Rect( padL, padT, boxW, labelSize * 1.4f );
		hud.DrawText( new TextRendering.Scope( "WOOD", TextColor.WithAlpha( 0.55f ), labelSize ),
			labelRect, TextFlag.LeftCenter );
		var valueRect = new Rect( padL, padT + labelSize, boxW, valueSize * 1.3f );
		hud.DrawText( new TextRendering.Scope( _state.Wood.ToString(), TextColor, valueSize ),
			valueRect, TextFlag.LeftCenter );
	}

	private void DrawTierBadge( Sandbox.Rendering.HudPainter hud )
	{
		if ( _state is null ) return;
		float padR = 36f, padT = 28f;
		float labelSize = 14f;
		float valueSize = 30f;
		float boxW = 220f;
		float x = Screen.Width - padR - boxW;
		var labelRect = new Rect( x, padT, boxW, labelSize * 1.4f );
		string tierName = _state.AxeTier >= 0 && _state.AxeTier < Tunables.AxeTierName.Length
			? Tunables.AxeTierName[_state.AxeTier]
			: "Unknown";
		hud.DrawText( new TextRendering.Scope( $"AXE — {tierName.ToUpper()}", TextColor.WithAlpha( 0.55f ), labelSize ),
			labelRect, TextFlag.RightCenter );
		var valueRect = new Rect( x, padT + labelSize, boxW, valueSize * 1.3f );
		hud.DrawText( new TextRendering.Scope( $"T{_state.AxeTier}", TextColor, valueSize ),
			valueRect, TextFlag.RightCenter );

		// Pip row : ● for each unlocked tier, ○ for each remaining. T0 = ●○○○.
		float pipSize = 8f;
		float pipGap = 4f;
		float pipsY = padT + labelSize + valueSize * 1.4f;
		int total = Tunables.MaxAxeTier + 1;
		float pipsW = total * pipSize + (total - 1) * pipGap;
		float pipsX = Screen.Width - padR - pipsW;
		for ( int i = 0; i < total; i++ )
		{
			var pipColor = i <= _state.AxeTier ? TextColor : TextColor.WithAlpha( 0.20f );
			hud.DrawRect( new Rect( pipsX + i * (pipSize + pipGap), pipsY, pipSize, pipSize ), pipColor );
		}
	}

	private void DrawShopHint( Sandbox.Rendering.HudPainter hud )
	{
		if ( !_shop.IsValid() || !_shop.PlayerInside ) return;
		if ( _state is null ) return;

		float w = Screen.Width;
		float h = Screen.Height;
		float fontSize = 18f;
		float lineH = fontSize * 1.6f;
		float backW = MathF.Min( 780f, w * 0.60f );
		float backH = lineH * 7f + 12f;
		float backX = (w - backW) * 0.5f;
		float backY = h * 0.52f;
		hud.DrawRect( new Rect( backX, backY, backW, backH ), new Color( 0f, 0f, 0f, 0.62f ) );

		string header = _state.Spirits > 0
			? $"SHOP — [E] auto-buy cheapest    ✦ {_state.Spirits} Spirits (+{_state.Spirits}% wood)"
			: "SHOP — [E] auto-buy cheapest";
		hud.DrawText( new TextRendering.Scope( header,
			TextColor.WithAlpha( 0.75f ), fontSize * 1.05f ),
			new Rect( backX, backY + 4f, backW, lineH ), TextFlag.Center );

		DrawShopLine( hud, backX, backY + lineH + 2f, backW, lineH, fontSize, "1",
			$"Axe T{_state.AxeTier} ({Tunables.AxeTierName[_state.AxeTier]})", AxeNextCost(),
			_state.AxeTier < Tunables.MaxAxeTier ? $"→ {Tunables.AxeTierName[_state.AxeTier + 1]}" : "+chop/swing" );
		DrawShopLine( hud, backX, backY + 2 * lineH + 2f, backW, lineH, fontSize, "2",
			$"Speed T{_state.SpeedTier}", SpeedNextCost(), $"×{Tunables.SpeedMul[_state.SpeedTier]:0.00} walk" );
		DrawShopLine( hud, backX, backY + 3 * lineH + 2f, backW, lineH, fontSize, "3",
			$"Luck T{_state.LuckTier}", LuckNextCost(), $"{(Tunables.LuckChance[_state.LuckTier] * 100):0}% × 2 chance" );
		DrawShopLine( hud, backX, backY + 4 * lineH + 2f, backW, lineH, fontSize, "4",
			$"Power T{_state.PowerTier}", PowerNextCost(), $"+{Tunables.PowerBonus[_state.PowerTier]} chop power" );
		DrawShopLine( hud, backX, backY + 5 * lineH + 2f, backW, lineH, fontSize, "5",
			$"Pet T{_state.PetTier}", PetNextCost(), "cosmetic companion" );
		DrawPrestigeLine( hud, backX, backY + 6 * lineH + 2f, backW, lineH, fontSize );
	}

	private void DrawPrestigeLine( Sandbox.Rendering.HudPainter hud, float x, float y, float w, float h, float font )
	{
		bool can = _state.CanPrestige();
		int wouldGet = _state.SpiritsFromPrestige - _state.Spirits;
		string line = can
			? $"  [6]  REPLANT FOREST  ·  gain {wouldGet} Sapling Spirits (+{wouldGet}% wood, perma)"
			: $"  [6]  Replant locked  ·  need 500 lifetime wood (have {_state.TotalWoodEarned})";
		var tint = can ? HotColor : TextColor.WithAlpha( 0.40f );
		hud.DrawText( new TextRendering.Scope( line, tint, font ),
			new Rect( x, y, w, h ), TextFlag.LeftCenter );
	}

	private int AxeNextCost() => _state.AxeTier < Tunables.MaxAxeTier ? Tunables.AxeTierCosts[_state.AxeTier + 1] : -1;
	private int SpeedNextCost() => _state.SpeedTier < Tunables.MaxStatTier ? Tunables.SpeedCosts[_state.SpeedTier + 1] : -1;
	private int LuckNextCost() => _state.LuckTier < Tunables.MaxStatTier ? Tunables.LuckCosts[_state.LuckTier + 1] : -1;
	private int PowerNextCost() => _state.PowerTier < Tunables.MaxStatTier ? Tunables.PowerCosts[_state.PowerTier + 1] : -1;
	private int PetNextCost() => _state.PetTier < Tunables.MaxPetTier ? Tunables.PetCosts[_state.PetTier + 1] : -1;

	private void DrawShopLine( Sandbox.Rendering.HudPainter hud, float x, float y, float w, float h, float font,
		string key, string label, int cost, string effect )
	{
		bool maxed = cost < 0;
		bool affordable = !maxed && _state.Wood >= cost;
		var labelColor = maxed ? TextColor.WithAlpha( 0.40f ) : (affordable ? HotColor : TextColor.WithAlpha( 0.85f ));
		string costStr = maxed ? "MAX" : $"{cost} wood";
		string line = $"  [{key}]  {label}  ·  {costStr}  ·  {effect}";
		hud.DrawText( new TextRendering.Scope( line, labelColor, font ),
			new Rect( x, y, w, h ), TextFlag.LeftCenter );
	}

	private void DrawTeleportHint( Sandbox.Rendering.HudPainter hud )
	{
		// Hide the hint when player is inside shop (shop hint is more important).
		if ( _shop.IsValid() && _shop.PlayerInside ) return;
		float fontSize = 16f;
		var tint = TextColor.WithAlpha( 0.40f );
		var rect = new Rect( 0, Screen.Height * 0.92f, Screen.Width, fontSize * 1.4f );
		hud.DrawText( new TextRendering.Scope( "[R] teleport to shop", tint, fontSize ), rect, TextFlag.Center );
	}

	private float[] _frameTimes = new float[120];
	private int _frameIdx;

	private void DrawDebugBlock( Sandbox.Rendering.HudPainter hud )
	{
		var pad = 14f;
		var width = 360f;
		var height = FontSize + pad * 1.4f;
		var x = 32f;
		var y = 180f;
		var dim = new Color( 0.65f, 0.92f, 1f, 1f );
		var hot = new Color( 1f, 0.50f, 0.35f, 1f );

		_frameTimes[_frameIdx % _frameTimes.Length] = Time.Delta;
		_frameIdx++;
		float dtAvg = 0f, dtMax = 0f;
		int n = _frameIdx < _frameTimes.Length ? _frameIdx : _frameTimes.Length;
		for ( int i = 0; i < n; i++ )
		{
			var dt = _frameTimes[i];
			dtAvg += dt;
			if ( dt > dtMax ) dtMax = dt;
		}
		dtAvg /= (n > 0 ? n : 1);
		float fpsAvg = 1f / dtAvg.Clamp( 1e-4f, 1f );
		float fpsMin = 1f / dtMax.Clamp( 1e-4f, 1f );
		var fpsTint = fpsMin < 30f ? hot : dim;
		DrawLine( hud, x, ref y, width, height, pad, $"FPS : {fpsAvg:0} avg / {fpsMin:0} min", fpsTint );

		var trees = Scene?.GetAllComponents<Tree>().ToList() ?? new List<Tree>();
		int standing = 0, falling = 0, landed = 0;
		foreach ( var t in trees )
		{
			if ( !t.IsValid() ) continue;
			if ( t.IsStanding ) standing++;
			else if ( t.IsFalling ) falling++;
			else landed++;
		}
		DrawLine( hud, x, ref y, width, height, pad, $"Trees: {standing}s {falling}f {landed}l", dim );
	}

	private void DrawLine( Sandbox.Rendering.HudPainter hud, float x, ref float y, float width, float height, float pad, string label, Color tint )
	{
		hud.DrawRect( new Rect( x, y, width, height ), PanelColor );
		hud.DrawRect( new Rect( x, y, width, 2f ), tint.WithAlpha( 0.5f ) );
		hud.DrawRect( new Rect( x, y + height - 2f, width, 2f ), tint.WithAlpha( 0.5f ) );
		var textPos = new Vector2( x + pad, y + height * 0.5f );
		hud.DrawText( new TextRendering.Scope( label, tint, FontSize ), textPos );
		y += height + 6f;
	}
}
