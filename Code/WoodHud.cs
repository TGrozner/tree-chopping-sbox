namespace TreeChopping;


public sealed class WoodHud : Component
{
	[Property] public WoodInventory Inventory { get; set; }
	[Property] public Color PanelColor { get; set; } = new( 0.07f, 0.09f, 0.11f, 0.78f );
	[Property] public Color TextColor { get; set; } = new( 1f, 0.86f, 0.42f, 1f );
	[Property] public Color ChainHotColor { get; set; } = new( 1f, 0.45f, 0.20f, 1f );
	[Property] public float FontSize { get; set; } = 32f;

	private ComboTracker _combo;
	private BeaverController _beaver;

	protected override void OnUpdate()
	{
		Inventory ??= WoodInventory.Get( Scene );
		_combo ??= ComboTracker.Get( Scene );
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();

		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		var pad = 14f;
		var width = 260f;
		var height = FontSize + pad * 1.4f;
		var x = 32f;
		var y = 32f;

		DrawLine( hud, x, ref y, width, height, pad, $"Wood : {Inventory?.Wood ?? 0}", TextColor );

		if ( _beaver.IsValid() )
		{
			DrawLine( hud, x, ref y, width, height, pad, $"Tool : {_beaver.CurrentTool}", TextColor );
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
