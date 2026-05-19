namespace TreeChopping;

// Esc-driven pause overlay ported from the Godot proto's settings_menu.gd.
//
// Pause strategy: we set Scene.TimeScale = 0 so the physics tick + Time.Delta
// freeze (ComboTracker also writes Scene.TimeScale, so we re-assert it every
// OnUpdate while paused — last writer wins per-frame and we don't care about
// microframe consistency since OnFixedUpdate samples the value at tick time).
// BeaverController.OnUpdate also early-returns on IsPaused so movement / look /
// swing are belt-and-braces gated even if some future system bypasses TimeScale.
//
// Slider nav: while paused we repurpose WASD — Forward/Backward move focus
// between sliders, Left/Right tweak the focused slider's value. The
// sensitivity slider writes back into BeaverController.MouseSensitivity, FOV
// into Camera.FieldOfView's *baseline* (BeaverController stores _baseFov from
// OnStart; we mutate the live Camera.FieldOfView, and the controller's FOV
// punch / sprint widen layer on top — they don't read _baseFov again so we
// also poke the controller's MouseSensitivity property directly). Volume is
// purely cosmetic for now since there's no audio bus wired up.
public sealed class PauseMenu : Component
{
	public bool IsPaused { get; private set; }

	// Sliders — names + min/max/step mirror Godot's Settings.*_MIN/MAX
	// constants. Keeping them as plain fields (not [Property]) because they
	// shouldn't surface in the inspector — they're owned by the menu, not
	// scene-data.
	private float _sensitivity = 0.12f;
	private float _fov = 70f;
	private float _volume = 1.0f;

	private int _focused; // 0 = Sensitivity, 1 = FOV, 2 = Volume

	private const float SensitivityMin = 0.02f;
	private const float SensitivityMax = 0.40f;
	private const float SensitivityStep = 0.02f;
	private const float FovMin = 50f;
	private const float FovMax = 110f;
	private const float FovStep = 2f;
	private const float VolumeMin = 0f;
	private const float VolumeMax = 1f;
	private const float VolumeStep = 0.05f;

	private BeaverController _beaver;
	private CameraComponent _camera;

	public static PauseMenu Get( Scene scene )
	{
		return scene?.GetAllComponents<PauseMenu>().FirstOrDefault();
	}

	protected override void OnUpdate()
	{
		// Esc toggles. Input.EscapePressed is a frame-edge bool maintained by
		// the engine's InputRouter; "Menu" (Q in Input.config) acts as a
		// fallback so keyboards with a dead Esc still surface the menu.
		bool toggle = Input.EscapePressed || Input.Pressed( "Menu" );
		if ( toggle )
		{
			if ( IsPaused ) Resume();
			else Pause();
		}

		if ( !IsPaused ) return;

		// Re-assert TimeScale every frame — ComboTracker pulls it back to 1f
		// in its OnUpdate when its slowmo window is exhausted, so we'd race
		// against it without this.
		Scene.TimeScale = 0f;

		HandleSliderNav();
		DrawOverlay();
	}

	private void Pause()
	{
		IsPaused = true;
		Scene.TimeScale = 0f;
		// MouseVisibility.Visible = cursor on, free to move; Hidden =
		// locked-to-game. There's no separate Mouse.Locked in current s&box —
		// the Visibility enum supersedes the deprecated Mouse.Visible bool.
		Mouse.Visibility = MouseVisibility.Visible;

		// Snapshot live values so the sliders show what the game is actually
		// using, not stale defaults — mirrors the Godot proto's open() body.
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();
		_camera ??= Scene?.Camera;
		if ( _beaver.IsValid() ) _sensitivity = _beaver.MouseSensitivity;
		if ( _camera.IsValid() ) _fov = _camera.FieldOfView;
	}

	private void Resume()
	{
		IsPaused = false;
		Scene.TimeScale = 1f;
		Mouse.Visibility = MouseVisibility.Hidden;
	}

	private void HandleSliderNav()
	{
		// Focus move: W/S step through sliders. Wrap so the list feels like a
		// circular menu rather than dead-ending at the bottom.
		if ( Input.Pressed( "Forward" ) ) _focused = (_focused + 2) % 3; // -1 mod 3
		if ( Input.Pressed( "Backward" ) ) _focused = (_focused + 1) % 3;

		bool dec = Input.Pressed( "Left" );
		bool inc = Input.Pressed( "Right" );
		if ( !dec && !inc ) return;

		float sign = inc ? 1f : -1f;
		switch ( _focused )
		{
			case 0:
				_sensitivity = MathX.Clamp( _sensitivity + sign * SensitivityStep, SensitivityMin, SensitivityMax );
				if ( _beaver.IsValid() ) _beaver.MouseSensitivity = _sensitivity;
				break;
			case 1:
				_fov = MathX.Clamp( _fov + sign * FovStep, FovMin, FovMax );
				// Write the camera FOV directly; BeaverController.UpdateFov
				// adds sprint widen + punch ON TOP of Camera.FieldOfView each
				// frame, so this value is the new baseline.
				if ( _camera.IsValid() ) _camera.FieldOfView = _fov;
				break;
			case 2:
				_volume = MathX.Clamp( _volume + sign * VolumeStep, VolumeMin, VolumeMax );
				break;
		}
	}

	private void DrawOverlay()
	{
		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		float screenW = Screen.Width;
		float screenH = Screen.Height;

		// Full-screen dim. Alpha lifted from the Godot proto's _BG_COLOR
		// (0.06,0.07,0.10,0.82) so the look matches.
		hud.DrawRect( new Rect( 0f, 0f, screenW, screenH ), new Color( 0.06f, 0.07f, 0.10f, 0.82f ) );

		// Centered card. Dimensions roughly mirror the Godot panel's
		// offset_left/right/top/bottom = 220/200 ⇒ 440x400.
		const float cardW = 440f;
		const float cardH = 320f;
		float cardX = (screenW - cardW) * 0.5f;
		float cardY = (screenH - cardH) * 0.5f;

		var panel = new Color( 0.14f, 0.16f, 0.22f, 0.96f );
		var accent = new Color( 0.55f, 0.85f, 0.65f, 1f );
		var label = new Color( 0.92f, 0.95f, 0.98f, 1f );
		var focusedTint = new Color( 1f, 0.86f, 0.42f, 1f );

		hud.DrawRect( new Rect( cardX, cardY, cardW, cardH ), panel );
		// 2px accent border to read as a framed card without needing a
		// proper styled rect — same trick the wood-HUD lines use.
		hud.DrawRect( new Rect( cardX, cardY, cardW, 2f ), accent );
		hud.DrawRect( new Rect( cardX, cardY + cardH - 2f, cardW, 2f ), accent );
		hud.DrawRect( new Rect( cardX, cardY, 2f, cardH ), accent );
		hud.DrawRect( new Rect( cardX + cardW - 2f, cardY, 2f, cardH ), accent );

		const float titleSize = 36f;
		const float lineSize = 24f;
		const float footSize = 14f;

		float y = cardY + 28f;
		var titleRect = new Rect( cardX, y, cardW, titleSize + 8f );
		hud.DrawText( new TextRendering.Scope( "PAUSED", label, titleSize ), titleRect, TextFlag.Center );
		y += titleSize + 24f;

		DrawSliderLine( hud, cardX, ref y, cardW, lineSize, "Sensitivity", $"{_sensitivity:0.00}", _focused == 0, label, focusedTint );
		DrawSliderLine( hud, cardX, ref y, cardW, lineSize, "FOV", $"{_fov:0}", _focused == 1, label, focusedTint );
		DrawSliderLine( hud, cardX, ref y, cardW, lineSize, "Master volume", $"{(_volume * 100f):0}%", _focused == 2, label, focusedTint );

		y = cardY + cardH - 28f - footSize;
		var footRect = new Rect( cardX, y, cardW, footSize + 4f );
		hud.DrawText(
			new TextRendering.Scope( "WASD to navigate / adjust  -  Esc to resume", new Color( 0.6f, 0.65f, 0.75f, 1f ), footSize ),
			footRect, TextFlag.Center );
	}

	private void DrawSliderLine(
		Sandbox.Rendering.HudPainter hud,
		float cardX, ref float y, float cardW, float lineSize,
		string name, string value, bool focused, Color baseTint, Color focusTint )
	{
		var tint = focused ? focusTint : baseTint;
		float padX = 28f;
		float lineH = lineSize + 8f;
		// Caret on the focused row reads at a glance which slider Left/Right
		// will touch — keep it minimal (no full row highlight needed).
		string prefix = focused ? "> " : "  ";
		var leftRect = new Rect( cardX + padX, y, cardW * 0.5f, lineH );
		var rightRect = new Rect( cardX + cardW * 0.5f, y, cardW * 0.5f - padX, lineH );
		hud.DrawText( new TextRendering.Scope( prefix + name, tint, lineSize ), leftRect, TextFlag.LeftCenter );
		hud.DrawText( new TextRendering.Scope( value, tint, lineSize ), rightRect, TextFlag.RightCenter );
		y += lineH + 8f;
	}
}
