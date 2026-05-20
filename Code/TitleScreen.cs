namespace TreeChopping;

// Title screen overlay shown on scene load. Renders title + subtitle + start
// prompt via HudPainter on top of the rendered world. Once Input.Pressed("Jump")
// (SPACE) fires, transitions to gameplay by raising a static flag that other
// components check. After dismissal stays inactive even after Regenerate
// — title is per-session, not per-run.
public sealed class TitleScreen : Component
{
	[Property] public string Title { get; set; } = "TIMBER STRIKE";
	[Property] public string Subtitle { get; set; } = "One swing. Bowling with trees.";
	[Property] public Color TitleColor { get; set; } = new( 1f, 0.85f, 0.40f, 1f );
	[Property] public Color SubColor { get; set; } = new( 0.85f, 0.85f, 0.85f, 1f );

	public static bool Dismissed { get; set; }
	// Still block input on the frame we dismissed, so the same click that
	// dismisses the title does not also fire BeaverController.DebugSwing
	// (player would lose their one swing before seeing the arena).
	public static bool ShouldBlockInput => !Dismissed || Time.Now == _dismissTimeNow;
	private static float _dismissTimeNow = -1f;

	private TimeSince _dismissTime = -1f;
	private bool _dismissed;

	public static TitleScreen Get( Scene scene )
		=> scene?.GetAllComponents<TitleScreen>().FirstOrDefault();

	// Test hook : mirror the real Dismissed-on-this-tick handshake without
	// requiring an actual key press. Used by TestSuite to verify the input
	// guard prevents same-frame click bleed into BeaverController.
	public static void DismissForTest()
	{
		Dismissed = true;
		_dismissTimeNow = Time.Now;
	}

	public static void ResetForTest()
	{
		Dismissed = false;
		_dismissTimeNow = -1f;
	}

	protected override void OnStart()
	{
		// Re-enter (e.g. hotload during dev) — keep dismissed if already done.
		_dismissed = Dismissed;
	}

	protected override void OnUpdate()
	{
		if ( !_dismissed )
		{
			// SPACE or click to dismiss.
			if ( Input.Pressed( "Jump" ) || Input.Pressed( "attack1" ) )
			{
				_dismissed = true;
				Dismissed = true;
				_dismissTimeNow = Time.Now;
				_dismissTime = 0f;
				Log.Info( "[Title] Dismissed — starting game" );
			}
		}

		Render();
	}

	private void Render()
	{
		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		float alpha = 1f;
		const float FadeOutDuration = 0.5f;
		if ( _dismissed )
		{
			float t = (float)_dismissTime;
			if ( t > FadeOutDuration ) return;
			alpha = 1f - t / FadeOutDuration;
		}

		float screenW = Screen.Width;
		float screenH = Screen.Height;

		// Dark overlay
		hud.DrawRect( new Rect( 0, 0, screenW, screenH ), new Color( 0f, 0f, 0f, 0.55f * alpha ) );

		// Title
		var titleFontSize = 96f;
		var titleRect = new Rect( 0, screenH * 0.25f, screenW, titleFontSize * 1.4f );
		var titleScope = new TextRendering.Scope( Title, TitleColor.WithAlpha( alpha ), titleFontSize );
		hud.DrawText( titleScope, titleRect, TextFlag.Center );

		// Subtitle
		var subFontSize = 32f;
		var subRect = new Rect( 0, screenH * 0.40f, screenW, subFontSize * 1.4f );
		var subScope = new TextRendering.Scope( Subtitle, SubColor.WithAlpha( alpha ), subFontSize );
		hud.DrawText( subScope, subRect, TextFlag.Center );

		// Stats line — best + daily
		var run = RunManager.Get( Scene );
		if ( run.IsValid() )
		{
			var stats = $"Best: {run.BestScore}  ·  Daily: {DateTime.UtcNow:yyyy-MM-dd}";
			var statsRect = new Rect( 0, screenH * 0.52f, screenW, 28f );
			var statsScope = new TextRendering.Scope( stats, SubColor.WithAlpha( alpha * 0.7f ), 24f );
			hud.DrawText( statsScope, statsRect, TextFlag.Center );

			// Modifier preview — joueur sait quel buff il aura AVANT de start.
			// Affiché plus gros que les stats, tinted par modifier color.
			var mod = run.ActiveModifier;
			var modLine = $"THIS RUN — {mod.DisplayName()}";
			var modHint = mod.ShortHint();
			var modColor = mod.Tint().WithAlpha( alpha * 0.95f );
			var modRect = new Rect( 0, screenH * 0.58f, screenW, 36f );
			hud.DrawText( new TextRendering.Scope( modLine, modColor, 30f ), modRect, TextFlag.Center );
			var hintRect = new Rect( 0, screenH * 0.63f, screenW, 24f );
			hud.DrawText( new TextRendering.Scope( modHint, SubColor.WithAlpha( alpha * 0.6f ), 22f ), hintRect, TextFlag.Center );
		}

		// Prompt — pulse alpha so it draws the eye.
		float pulse = 0.65f + 0.35f * MathF.Sin( Time.Now * 3f );
		var promptFontSize = 28f;
		var promptRect = new Rect( 0, screenH * 0.70f, screenW, promptFontSize * 1.4f );
		var promptScope = new TextRendering.Scope( "Press SPACE or CLICK to start", TitleColor.WithAlpha( alpha * pulse ), promptFontSize );
		hud.DrawText( promptScope, promptRect, TextFlag.Center );
	}
}
