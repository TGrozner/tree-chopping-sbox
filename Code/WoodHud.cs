namespace TreeChopping;


public sealed class WoodHud : Component
{
	[Property] public WoodInventory Inventory { get; set; }
	[Property] public StoneInventory Stones { get; set; }
	[Property] public Color PanelColor { get; set; } = new( 0.07f, 0.09f, 0.11f, 0.78f );
	[Property] public Color TextColor { get; set; } = new( 1f, 0.86f, 0.42f, 1f );
	[Property] public Color StoneColor { get; set; } = new( 0.78f, 0.82f, 0.88f, 1f );
	[Property] public Color ChainHotColor { get; set; } = new( 1f, 0.45f, 0.20f, 1f );
	[Property] public float FontSize { get; set; } = 32f;

	private ComboTracker _combo;
	private BeaverController _beaver;

	// Toggled by F3 (Input.config "DebugToggle"). Static so any other
	// component can also check the flag without a back-ref.
	public static bool DebugVisible { get; private set; }

	protected override void OnUpdate()
	{
		Inventory ??= WoodInventory.Get( Scene );
		Stones ??= StoneInventory.Get( Scene );
		_combo ??= ComboTracker.Get( Scene );
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();

		if ( Input.Pressed( "DebugToggle" ) ) DebugVisible = !DebugVisible;

		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		var pad = 14f;
		var width = 260f;
		var height = FontSize + pad * 1.4f;
		var x = 32f;
		var y = 32f;

		// Cap pulled from the live component so hotload-tuned values surface
		// immediately; fallback to Tunables when an inventory isn't ready yet.
		var woodCap = Inventory?.Cap ?? Tunables.BackpackCap;
		var stoneCap = Stones?.Cap ?? Tunables.BackpackCap;
		var woodCount = Inventory?.Wood ?? 0;
		var stoneCount = Stones?.Stone ?? 0;
		var woodTint = (Inventory?.IsFull ?? false) ? ChainHotColor : TextColor;
		var stoneTint = (Stones?.IsFull ?? false) ? ChainHotColor : StoneColor;
		DrawLine( hud, x, ref y, width, height, pad, $"Wood : {woodCount}/{woodCap}", woodTint );
		DrawLine( hud, x, ref y, width, height, pad, $"Stone : {stoneCount}/{stoneCap}", stoneTint );

		if ( _beaver.IsValid() )
		{
			DrawLine( hud, x, ref y, width, height, pad, $"Tool : {_beaver.CurrentTool}", TextColor );

			// Tier line — current/max + a hot tint when the player can afford the
			// next upgrade so the R-key prompt reads at a glance. Pickaxe tier is
			// shown but never paintable hot (no spend path yet).
			var isAxe = _beaver.CurrentTool == ToolKind.Axe;
			var tier = isAxe ? _beaver.AxeTier : _beaver.PickaxeTier;
			var nextTier = tier + 1;
			bool canAfford = false;
			if ( isAxe && nextTier <= Tunables.MaxAxeTier )
			{
				var cost = Tunables.AxeTierCosts[nextTier];
				canAfford = woodCount >= cost;
			}
			var tierTint = canAfford ? ChainHotColor : TextColor;
			DrawLine( hud, x, ref y, width, height, pad, $"Tier : {tier}/{Tunables.MaxAxeTier}", tierTint );
		}

		if ( _combo.IsValid() && _combo.Chain > 0 )
		{
			var hot = _combo.Chain >= Tunables.ComboSlowmoChain ? ChainHotColor : TextColor;
			DrawLine( hud, x, ref y, width, height, pad, $"Chain x{_combo.Chain}", hot );
		}

		var weather = Weather.Get( Scene );
		if ( weather.IsValid() )
		{
			DrawLine( hud, x, ref y, width, height, pad, $"Sky : {weather.State}", TextColor );
		}

		if ( DebugVisible )
		{
			DrawDebugLines( hud, x, ref y, width, height, pad );
		}

		var hints = HintManager.Get( Scene );
		if ( hints.IsValid() && hints.Alpha > 0.01f && !string.IsNullOrEmpty( hints.CurrentText ) )
		{
			DrawHint( hud, hints.CurrentText, hints.Alpha );
		}
	}

	private void DrawHint( Sandbox.Rendering.HudPainter hud, string text, float alpha )
	{
		float screenW = Screen.Width;
		float screenH = Screen.Height;
		float hintWidth = MathF.Min( 760f, screenW * 0.7f );
		float fontSize = 26f;
		float pad = 18f;
		float h = fontSize + pad * 1.5f;
		float x = (screenW - hintWidth) * 0.5f;
		float y = screenH * 0.78f;

		var bg = PanelColor.WithAlpha( PanelColor.a * alpha );
		var rim = TextColor.WithAlpha( 0.45f * alpha );
		var fg = new Color( 0.95f, 0.93f, 0.85f, alpha );

		hud.DrawRect( new Rect( x, y, hintWidth, h ), bg );
		hud.DrawRect( new Rect( x, y, hintWidth, 2f ), rim );
		hud.DrawRect( new Rect( x, y + h - 2f, hintWidth, 2f ), rim );
		var rect = new Rect( x, y, hintWidth, h );
		var scope = new TextRendering.Scope( text, fg, fontSize );
		hud.DrawText( scope, rect, TextFlag.Center );
	}

	private void DrawDebugLines( Sandbox.Rendering.HudPainter hud, float x, ref float y, float width, float height, float pad )
	{
		var dim = new Color( 0.65f, 0.92f, 1f, 1f );
		var fps = 1f / Time.Delta.Clamp( 1e-4f, 1f );
		DrawLine( hud, x, ref y, width, height, pad, $"FPS : {fps:0}", dim );
		if ( _beaver.IsValid() )
		{
			var p = _beaver.WorldPosition;
			DrawLine( hud, x, ref y, width, height, pad, $"Pos : {p.x:0} {p.y:0} {p.z:0}", dim );
		}
		var trees = Scene?.GetAllComponents<Tree>().Count() ?? 0;
		var pieces = Scene?.GetAllComponents<LogPiece>().Count() ?? 0;
		var chunks = Scene?.GetAllComponents<WoodChunk>().Count() ?? 0;
		DrawLine( hud, x, ref y, width, height, pad, $"World : T{trees} L{pieces} C{chunks}", dim );
	}

	private void DrawLine( Sandbox.Rendering.HudPainter hud, float x, ref float y, float width, float height, float pad, string label, Color tint )
	{
		hud.DrawRect( new Rect( x, y, width, height ), PanelColor );
		hud.DrawRect( new Rect( x, y, width, 2f ), tint.WithAlpha( 0.5f ) );
		hud.DrawRect( new Rect( x, y + height - 2f, width, 2f ), tint.WithAlpha( 0.5f ) );
		var textPos = new Vector2( x + pad, y + height * 0.5f );
		var scope = new TextRendering.Scope( label, tint, FontSize );
		hud.DrawText( scope, textPos );
		y += height + 6f;
	}
}
