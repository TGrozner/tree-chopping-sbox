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
	private int _lastShownWood;
	private TimeSince _woodChangedTime = 999f;

	protected override void OnStart()
	{
		DebugVisible = false;
	}

	protected override void OnUpdate()
	{
		_state ??= GameState.Get( Scene );
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
		float cx = Screen.Width * 0.5f;
		float cy = Screen.Height * 0.5f;
		var tint = new Color( 0.92f, 0.92f, 0.92f, 0.65f );
		hud.DrawRect( new Rect( cx - 1.5f, cy - 1.5f, 3f, 3f ), tint );
	}

	// Wood balance top-left. Pulses on each gain.
	private void DrawWoodPanel( Sandbox.Rendering.HudPainter hud )
	{
		if ( _state is null ) return;
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
		float boxW = 160f;
		float x = Screen.Width - padR - boxW;
		var labelRect = new Rect( x, padT, boxW, labelSize * 1.4f );
		hud.DrawText( new TextRendering.Scope( "AXE TIER", TextColor.WithAlpha( 0.55f ), labelSize ),
			labelRect, TextFlag.RightCenter );
		var valueRect = new Rect( x, padT + labelSize, boxW, valueSize * 1.3f );
		hud.DrawText( new TextRendering.Scope( $"T{_state.AxeTier}", TextColor, valueSize ),
			valueRect, TextFlag.RightCenter );
	}

	// Shop hint when the player is inside the shop trigger zone.
	private void DrawShopHint( Sandbox.Rendering.HudPainter hud )
	{
		var shop = Scene?.GetAllComponents<ShopArea>().FirstOrDefault();
		if ( !shop.IsValid() || !shop.PlayerInside ) return;
		if ( _state is null ) return;

		float w = Screen.Width;
		float h = Screen.Height;
		bool canUpgrade = _state.AxeTier < Tunables.MaxAxeTier;
		string line;
		if ( !canUpgrade ) line = "MAX TIER — no more upgrades";
		else
		{
			int cost = Tunables.AxeTierCosts[_state.AxeTier + 1];
			bool afford = _state.Wood >= cost;
			line = afford
				? $"[E] UPGRADE AXE  ·  T{_state.AxeTier} → T{_state.AxeTier + 1}  ·  costs {cost} wood"
				: $"need {cost} wood to upgrade  (have {_state.Wood})";
		}

		float fontSize = 24f;
		float backW = MathF.Min( 780f, w * 0.6f );
		float backH = fontSize * 1.8f;
		var tint = (canUpgrade && _state.Wood >= Tunables.AxeTierCosts[Math.Min( _state.AxeTier + 1, Tunables.MaxAxeTier )])
			? HotColor : TextColor.WithAlpha( 0.85f );
		hud.DrawRect( new Rect( (w - backW) * 0.5f, h * 0.78f, backW, backH ), new Color( 0f, 0f, 0f, 0.55f ) );
		var rect = new Rect( 0, h * 0.79f, w, fontSize * 1.4f );
		hud.DrawText( new TextRendering.Scope( line, tint, fontSize ), rect, TextFlag.Center );
	}

	private void DrawTeleportHint( Sandbox.Rendering.HudPainter hud )
	{
		var shop = Scene?.GetAllComponents<ShopArea>().FirstOrDefault();
		// Hide the hint when player is inside shop (shop hint is more important).
		if ( shop.IsValid() && shop.PlayerInside ) return;
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
