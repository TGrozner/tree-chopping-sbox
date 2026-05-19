namespace TreeChopping;

// Top-right HUD compass — shows the beaver's facing relative to the world.
// The arrow points toward downstream (-Y), so it matches the Godot proto's
// "carry wood downstream" convention. As the beaver turns, the N/E/S/W
// labels stay rooted to the world; the arrow rotates in screen-space.
//
// Drawing strategy: Hud.DrawRect is axis-aligned, so the arrow is approximated
// by a fan of small rectangles laid end-to-end along the screen-space angle.
// Cheap, allocation-free per frame (only stack-locals).
public sealed class HudCompass : Component
{
	[Property] public Color PanelColor { get; set; } = new( 0.07f, 0.09f, 0.11f, 0.78f );
	[Property] public Color RimColor { get; set; } = new( 1f, 0.86f, 0.42f, 0.5f );
	[Property] public Color LabelColor { get; set; } = new( 0.95f, 0.93f, 0.85f, 1f );
	[Property] public Color ArrowColor { get; set; } = new( 0.35f, 0.85f, 1f, 1f );
	[Property] public float Size { get; set; } = 120f;
	[Property] public float Margin { get; set; } = 24f;

	private BeaverController _beaver;

	protected override void OnUpdate()
	{
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();

		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		// Beaver yaw in degrees. Source 2 convention: yaw 0 = +X (forward),
		// yaw 90 = +Y. Downstream is -Y → yaw 270 (or -90).
		float beaverYaw = _beaver.IsValid() ? _beaver.WorldRotation.Yaw() : 0f;

		float size = Size;
		float x = Screen.Width - size - Margin;
		float y = Margin;
		float cx = x + size * 0.5f;
		float cy = y + size * 0.5f;

		// Backing panel + rim — visual style matches WoodHud panels.
		hud.DrawRect( new Rect( x, y, size, size ), PanelColor );
		hud.DrawRect( new Rect( x, y, size, 2f ), RimColor );
		hud.DrawRect( new Rect( x, y + size - 2f, size, 2f ), RimColor );
		hud.DrawRect( new Rect( x, y, 2f, size ), RimColor );
		hud.DrawRect( new Rect( x + size - 2f, y, 2f, size ), RimColor );

		// Cardinal labels — rotated around the dial by the negative beaver
		// yaw so "world North" stays world-fixed: when the beaver faces +Y
		// (yaw 90°), "E" should drift to the top of the dial in screen-space.
		// Screen-space angle convention here: 0 = up (north visual), CW.
		float ring = size * 0.5f - 14f;
		float labelFont = 18f;
		DrawLabel( hud, cx, cy, ring, -beaverYaw, "N", labelFont );
		DrawLabel( hud, cx, cy, ring, -beaverYaw + 90f, "E", labelFont );
		DrawLabel( hud, cx, cy, ring, -beaverYaw + 180f, "S", labelFont );
		DrawLabel( hud, cx, cy, ring, -beaverYaw + 270f, "W", labelFont );

		// Downstream arrow — world -Y. World yaw of "-Y direction" is 270°.
		// Screen-space "up" maps to the world direction the beaver is facing
		// (yaw = beaverYaw), so the screen-angle of -Y = 270 - beaverYaw,
		// measured from up, CW.
		float arrowScreenDeg = 270f - beaverYaw;
		DrawArrow( hud, cx, cy, size * 0.40f, arrowScreenDeg, ArrowColor );

		// Tip tag — short label near where the arrow points so the player
		// learns what the arrow means without needing a tooltip.
		float tipR = size * 0.40f + 6f;
		float a = MathX.DegreeToRadian( arrowScreenDeg );
		float tx = cx + MathF.Sin( a ) * tipR;
		float ty = cy - MathF.Cos( a ) * tipR;
		var tagScope = new TextRendering.Scope( "DOWNSTREAM", ArrowColor, 11f );
		var tagRect = new Rect( tx - 60f, ty - 8f, 120f, 16f );
		hud.DrawText( tagScope, tagRect, TextFlag.Center );
	}

	private void DrawLabel( Sandbox.Rendering.HudPainter hud, float cx, float cy, float radius, float screenDeg, string text, float fontSize )
	{
		float a = MathX.DegreeToRadian( screenDeg );
		// Screen-space: x = sin(a), y = -cos(a) so 0° = up, 90° = right.
		float px = cx + MathF.Sin( a ) * radius;
		float py = cy - MathF.Cos( a ) * radius;
		var rect = new Rect( px - 12f, py - 10f, 24f, 20f );
		var scope = new TextRendering.Scope( text, LabelColor, fontSize );
		hud.DrawText( scope, rect, TextFlag.Center );
	}

	private void DrawArrow( Sandbox.Rendering.HudPainter hud, float cx, float cy, float length, float screenDeg, Color color )
	{
		float a = MathX.DegreeToRadian( screenDeg );
		float dx = MathF.Sin( a );
		float dy = -MathF.Cos( a );

		// Shaft: chain of small 4x4 squares stepping from center to tip.
		// Approximates a rotated thin rect using only axis-aligned DrawRect.
		const int segments = 12;
		float seg = 3f;
		for ( int i = 0; i < segments; i++ )
		{
			float t = (i + 0.5f) / segments;
			float px = cx + dx * (length * t);
			float py = cy + dy * (length * t);
			hud.DrawRect( new Rect( px - seg * 0.5f, py - seg * 0.5f, seg, seg ), color );
		}

		// Tip — slightly larger square at the end so the arrow reads
		// directional rather than as a plain line of dots.
		float tipX = cx + dx * length;
		float tipY = cy + dy * length;
		float tip = 7f;
		hud.DrawRect( new Rect( tipX - tip * 0.5f, tipY - tip * 0.5f, tip, tip ), color );

		// Hub at the dial centre — anchors the eye and reinforces "this is
		// a rotation indicator", not just a free-floating arrow.
		float hub = 6f;
		hud.DrawRect( new Rect( cx - hub * 0.5f, cy - hub * 0.5f, hub, hub ), color.WithAlpha( 0.7f ) );
	}
}
