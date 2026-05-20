namespace TreeChopping;

// Score + run-state HUD for the bowling-with-trees loop. Kept named WoodHud so
// SceneStarter.EnsureHud and parallel-agent code in flight don't have to chase
// a rename — content is fully run-aware now.
//
// Top-left panel: live Score during a run, Best across the session.
// Center-screen banner: state-driven prompt (pick your shot / cascade in progress /
// final score + restart). Cleaner than dumping everything into corner stack.
public sealed class WoodHud : Component
{
	[Property] public Color PanelColor { get; set; } = new( 0.07f, 0.09f, 0.11f, 0.78f );
	[Property] public Color TextColor { get; set; } = new( 1f, 0.86f, 0.42f, 1f );
	[Property] public Color HotColor { get; set; } = new( 1f, 0.45f, 0.20f, 1f );
	[Property] public Color BannerColor { get; set; } = new( 0.05f, 0.07f, 0.10f, 0.88f );
	// Bumped 32→36 — corner panel needs visual presence sans dominer le frame.
	[Property] public float FontSize { get; set; } = 36f;

	private RunManager _run;
	private BeaverController _beaver;
	private int _lastShownScore = 0;
	private TimeSince _scoreChangedTime = 999f;

	// Toggled by the "DebugToggle" input (key bound in Input.config). Static so any
	// other component can also check the flag without a back-ref.
	public static bool DebugVisible { get; private set; }

	protected override void OnUpdate()
	{
		_run ??= RunManager.Get( Scene );
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();

		if ( Input.Pressed( "DebugToggle" ) ) DebugVisible = !DebugVisible;

		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		DrawMilestoneFlash( hud );
		DrawCascadeStartFlash( hud );
		DrawCornerPanel( hud );
		DrawAimReticle( hud );
		DrawStateBanner( hud );
		DrawMilestonePopup( hud );

		if ( DebugVisible )
		{
			DrawDebugBlock( hud );
		}
	}

	private void DrawCornerPanel( Sandbox.Rendering.HudPainter hud )
	{
		var pad = 14f;
		var width = 280f;
		var height = FontSize + pad * 1.4f;
		var x = 32f;
		var y = 32f;

		int score = _run?.Score ?? 0;
		int best = _run?.BestScore ?? 0;
		int initial = _run?.InitialTreeCount ?? 0;
		var scoreTint = score > 0 ? HotColor : TextColor;

		if ( score != _lastShownScore )
		{
			_lastShownScore = score;
			_scoreChangedTime = 0f;
		}
		const float ScorePulseDuration = 0.4f;
		float pulseT = (float)_scoreChangedTime / ScorePulseDuration;
		float pulseScale = pulseT < 1f ? 1f + (1f - pulseT) * 0.45f : 1f;

		// "x2" — current Heavy multiplier (dial-back depuis x3). Affichage Score
		// = score brut (sans "/initial") parce qu'avec Heavy ×2 + Mythic bonus le
		// score peut dépasser le nombre d'arbres, ce qui rendait "/1000" confus.
		var heavySuffix = (_run?.ActiveModifier == RunModifier.Heavy) ? " ×2" : "";
		DrawScoreLine( hud, x, ref y, width, height * pulseScale, pad, $"Score : {score}{heavySuffix}", scoreTint, FontSize * pulseScale );
		DrawLine( hud, x, ref y, width, height, pad, $"Best  : {best}", TextColor );
		var today = DateTime.UtcNow.ToString( "yyyy-MM-dd" );
		DrawLine( hud, x, ref y, width, height, pad, $"Daily : {today}", TextColor.WithAlpha( 0.7f ) );

		var weather = Weather.Get( Scene );
		if ( weather.IsValid() )
		{
			DrawLine( hud, x, ref y, width, height, pad, $"Sky   : {weather.State}", TextColor.WithAlpha( 0.75f ) );
		}

		// Biome line — joueur sait quel zone il chop (Forest = Beech/Spruce vert,
		// Autumn = Ironwood orange, Frost = Crystal cyan). Tinted par biome
		// palette pour cue visuelle immédiate.
		var biome = BiomeManager.Get( Scene );
		if ( biome.IsValid() )
		{
			Color biomeTint = biome.Current switch
			{
				BiomeKind.Autumn => new Color( 0.95f, 0.55f, 0.20f, 0.90f ),
				BiomeKind.Frost => new Color( 0.65f, 0.85f, 1.0f, 0.90f ),
				_ => new Color( 0.50f, 0.78f, 0.45f, 0.90f ), // Forest
			};
			DrawLine( hud, x, ref y, width, height, pad, $"Biome : {biome.Current}", biomeTint );
		}
	}

	private void DrawStateBanner( Sandbox.Rendering.HudPainter hud )
	{
		if ( _run is null ) return;

		if ( _run.State == RunState.Scored )
		{
			DrawScoredResults( hud );
			return;
		}

		string text;
		Color tint;
		switch ( _run.State )
		{
			case RunState.WaitingForSwing:
				if ( !_run.HasSwungEver )
				{
					text = "Aim at a dense cluster. ONE SWING — let the cascade do the rest. (Move with WASD, look with mouse, left-click to swing.)";
					tint = TextColor;
				}
				else
				{
					text = $"{_run.ActiveModifier.DisplayName()} — {_run.ActiveModifier.ShortHint()} Click to swing.";
					tint = _run.ActiveModifier.Tint();
				}
				break;
			case RunState.Cascading:
				// Skip ce banner — DrawCascadeCounter draws le big chain counter
				// au centre haut à la place, plus impactful.
				DrawCascadeCounter( hud );
				return;
			default:
				return;
		}

		float screenW = Screen.Width;
		float screenH = Screen.Height;
		float bannerWidth = MathF.Min( 820f, screenW * 0.75f );
		float fontSize = 28f;
		float vpad = 20f;
		float h = fontSize + vpad * 1.6f;
		float x = (screenW - bannerWidth) * 0.5f;
		float y = screenH * 0.80f;

		hud.DrawRect( new Rect( x, y, bannerWidth, h ), BannerColor );
		hud.DrawRect( new Rect( x, y, bannerWidth, 2f ), tint.WithAlpha( 0.6f ) );
		hud.DrawRect( new Rect( x, y + h - 2f, bannerWidth, 2f ), tint.WithAlpha( 0.6f ) );
		var rect = new Rect( x, y, bannerWidth, h );
		var scope = new TextRendering.Scope( text, tint, fontSize );
		hud.DrawText( scope, rect, TextFlag.Center );
	}

	// Giant chain counter pendant Cascading — "x47" centré haut, gros et tinted
	// par milestone tier en cours. Pulse à chaque tree fell (via _scoreChangedTime).
	private void DrawCascadeCounter( Sandbox.Rendering.HudPainter hud )
	{
		if ( _run is null ) return;
		int score = _run.Score;
		float screenW = Screen.Width;
		float screenH = Screen.Height;

		// Tint par milestone tier (Spark, Chain Reaction, Lumberjack, Domino King, Forest Killer, TIMBER SHOCK)
		var thresholds = Tunables.ScoreMilestones;
		var colors = Tunables.ScoreMilestoneColors;
		Color tint = TextColor;
		for ( int i = thresholds.Length - 1; i >= 0; i-- )
		{
			if ( score >= thresholds[i] ) { tint = colors[i]; break; }
		}

		// Pulse scale sur _scoreChangedTime (déjà animé par DrawStatPanel).
		const float PulseDuration = 0.25f;
		float pulseT = (float)_scoreChangedTime / PulseDuration;
		float pulseScale = pulseT < 1f ? MathX.Lerp( 1.6f, 1.0f, pulseT * pulseT ) : 1f;

		float fontSize = 120f * pulseScale;
		string text = $"x{score}";
		// Subtle dark backdrop pour que le big number ne se perde pas sur background
		// clair. Centered, soft (alpha 0.32), width matches text approximate width.
		float backdropW = MathF.Min( 480f, screenW * 0.45f );
		float backdropH = fontSize * 1.4f;
		hud.DrawRect( new Rect( (screenW - backdropW) * 0.5f, screenH * 0.115f, backdropW, backdropH ), new Color( 0f, 0f, 0f, 0.32f ) );
		var rect = new Rect( 0, screenH * 0.12f, screenW, fontSize * 1.2f );
		var scope = new TextRendering.Scope( text, tint, fontSize );
		hud.DrawText( scope, rect, TextFlag.Center );

		// Petit "TREES FELLED" subtitle pour clarté.
		float subSize = 22f;
		var subRect = new Rect( 0, screenH * 0.12f + fontSize * 1.15f, screenW, subSize * 1.3f );
		var subScope = new TextRendering.Scope( "TREES FELLED", tint.WithAlpha( 0.65f ), subSize );
		hud.DrawText( subScope, subRect, TextFlag.Center );

		// Run timer pendant cascade — petit chrono sous le compteur, donne sense
		// du time-to-resolve. Format SS.S secondes.
		float elapsed = (float)_run.StateEntered;
		string timer = $"{elapsed:F1}s";
		float timerSize = 26f;
		var timerRect = new Rect( 0, screenH * 0.12f + fontSize * 1.15f + subSize * 1.6f, screenW, timerSize * 1.3f );
		var timerScope = new TextRendering.Scope( timer, tint.WithAlpha( 0.45f ), timerSize );
		hud.DrawText( timerScope, timerRect, TextFlag.Center );
	}

	private void DrawScoredResults( Sandbox.Rendering.HudPainter hud )
	{
		// Multi-line end-of-run stats card. Vertical stack centered horizontally,
		// each line stepping down by its own fontSize × 1.4 so larger lines get
		// proportional breathing room. Background panel sized to the full stack.
		int finalScore = _run.Score;

		// Reveal animation : fade panel in over 0.55s, tick score from 0→finalScore over 0.9s
		// with ease-out so the number lands smoothly instead of snapping.
		float scoredT = (float)_run.StateEntered;
		const float FadeInDuration = 0.55f;
		const float ScoreRevealDuration = 0.9f;
		float panelAlpha = MathF.Min( 1f, scoredT / FadeInDuration );
		// Smoothstep so the alpha eases (cubic-like).
		panelAlpha = panelAlpha * panelAlpha * (3f - 2f * panelAlpha);
		float scoreT = MathF.Min( 1f, scoredT / ScoreRevealDuration );
		// Ease-out cubic for the score counter.
		float scoreEased = 1f - (1f - scoreT) * (1f - scoreT) * (1f - scoreT);
		int displayedScore = (int)MathF.Round( finalScore * scoreEased );

		bool newBest = finalScore > _run.BestScoreBeforeRun && finalScore > 0;
		int bestDelta = finalScore - _run.BestScoreBeforeRun;
		string ratingPrefix;
		if ( finalScore >= Tunables.ScoreMasterTarget )
			ratingPrefix = "*** PERFECT *** ";
		else if ( finalScore >= Tunables.ScoreGoodRunTarget )
			ratingPrefix = "GOOD RUN — ";
		else
			ratingPrefix = "";

		string line1 = newBest
			? $"NEW BEST — {ratingPrefix}{displayedScore} trees felled"
			: $"{ratingPrefix}{displayedScore} trees felled";
		Color line1Tint = newBest || finalScore >= Tunables.ScoreMasterTarget ? HotColor : TextColor;

		string line2 = $"Cascade lasted {_run.LastCascadeDuration:F1}s · Modifier: {_run.ActiveModifier.DisplayName()}";
		Color line2Tint = TextColor.WithAlpha( 0.85f );

		bool hasMythics = _run.MythicsFelled > 0;
		string line3 = hasMythics
			? $"✨ {_run.MythicsFelled} mythic{(_run.MythicsFelled > 1 ? "s" : "")}"
			: null;
		Color line3Tint = Tunables.MythicTint.WithAlpha( 0.95f );

		// New-best delta line — only shown when player beat their previous high,
		// and only after the score reveal animation has finished (score must read
		// at its final value before the "+N" reveal lands).
		bool showBestDelta = newBest && scoredT > ScoreRevealDuration;
		string lineDelta = showBestDelta ? $"+{bestDelta} over previous best" : null;
		Color lineDeltaTint = HotColor.WithAlpha( 0.95f );

		string today = DateTime.UtcNow.ToString( "yyyy-MM-dd" );
		string line4 = $"Best: {_run.BestScore}  ·  Daily seed: {today}";
		Color line4Tint = TextColor.WithAlpha( 0.65f );

		string line5 = "Press R for a new run";
		// Pulse the prompt alpha so the player notices the call to action.
		float promptPulse = 0.65f + 0.35f * MathF.Sin( Time.Now * 3f );
		Color line5Tint = (!_run.HasSwungEver ? HotColor : TextColor.WithAlpha( 0.75f )).WithAlpha( promptPulse );

		const float Font1 = 64f;
		const float Font2 = 30f;
		const float Font3 = 28f;
		const float FontDelta = 30f;
		const float Font4 = 22f;
		const float Font5 = 26f;
		const float Lh = 1.4f;

		float screenW = Screen.Width;
		float screenH = Screen.Height;
		float bannerWidth = MathF.Min( 960f, screenW * 0.82f );

		float h1 = Font1 * Lh;
		float h2 = Font2 * Lh;
		float h3 = hasMythics ? Font3 * Lh : 0f;
		float hDelta = showBestDelta ? FontDelta * Lh : 0f;
		float h4 = Font4 * Lh;
		float h5 = Font5 * Lh;
		float totalH = h1 + h2 + h3 + hDelta + h4 + h5 + 32f;

		float x = (screenW - bannerWidth) * 0.5f;
		float y = screenH * 0.30f;

		// Apply panel fade-in to all tints + bg via WithAlpha multiplier.
		var bgWithFade = BannerColor.WithAlpha( BannerColor.a * panelAlpha );
		hud.DrawRect( new Rect( x, y, bannerWidth, totalH ), bgWithFade );
		hud.DrawRect( new Rect( x, y, bannerWidth, 2f ), line1Tint.WithAlpha( 0.65f * panelAlpha ) );
		hud.DrawRect( new Rect( x, y + totalH - 2f, bannerWidth, 2f ), line1Tint.WithAlpha( 0.65f * panelAlpha ) );

		float cursorY = y + 16f;

		hud.DrawText( new TextRendering.Scope( line1, line1Tint.WithAlpha( panelAlpha ), Font1 ),
			new Rect( x, cursorY, bannerWidth, h1 ), TextFlag.Center );
		cursorY += h1;

		hud.DrawText( new TextRendering.Scope( line2, line2Tint.WithAlpha( line2Tint.a * panelAlpha ), Font2 ),
			new Rect( x, cursorY, bannerWidth, h2 ), TextFlag.Center );
		cursorY += h2;

		if ( hasMythics )
		{
			hud.DrawText( new TextRendering.Scope( line3, line3Tint.WithAlpha( line3Tint.a * panelAlpha ), Font3 ),
				new Rect( x, cursorY, bannerWidth, h3 ), TextFlag.Center );
			cursorY += h3;
		}

		if ( showBestDelta )
		{
			// Delta gets its own pop-in after the score reveal ends. Re-derive
			// local fade from time-since-reveal-end so the "+N" snaps in fresh.
			float deltaT = MathF.Max( 0f, scoredT - ScoreRevealDuration );
			float deltaAlpha = MathF.Min( 1f, deltaT / 0.3f );
			deltaAlpha = deltaAlpha * deltaAlpha * (3f - 2f * deltaAlpha);
			hud.DrawText( new TextRendering.Scope( lineDelta, lineDeltaTint.WithAlpha( lineDeltaTint.a * deltaAlpha ), FontDelta ),
				new Rect( x, cursorY, bannerWidth, hDelta ), TextFlag.Center );
			cursorY += hDelta;
		}

		hud.DrawText( new TextRendering.Scope( line4, line4Tint.WithAlpha( line4Tint.a * panelAlpha ), Font4 ),
			new Rect( x, cursorY, bannerWidth, h4 ), TextFlag.Center );
		cursorY += h4;

		hud.DrawText( new TextRendering.Scope( line5, line5Tint.WithAlpha( line5Tint.a * panelAlpha ), Font5 ),
			new Rect( x, cursorY, bannerWidth, h5 ), TextFlag.Center );
	}

	private void DrawMilestonePopup( Sandbox.Rendering.HudPainter hud )
	{
		if ( _run is null || string.IsNullOrEmpty( _run.LastMilestoneName ) ) return;
		float t = (float)_run.MilestoneShownTime;
		if ( t > Tunables.MilestonePopupDuration ) return;

		// Smooth alpha curve: fade in over first 15%, hold middle, fade out last 25%.
		float fadeIn = 0.15f;
		float fadeOut = 0.25f;
		float u = t / Tunables.MilestonePopupDuration;
		float alpha = u < fadeIn ? u / fadeIn
			: u > (1f - fadeOut) ? (1f - u) / fadeOut
			: 1f;

		// Scale pulses big->normal in the first 25% then stays.
		float scale = u < 0.25f ? 1.0f + (1.0f - u / 0.25f) * 0.6f : 1.0f;

		float screenW = Screen.Width;
		float screenH = Screen.Height;
		float fontSize = 56f * scale;
		int idx = Math.Clamp( _run.LastMilestoneIndex, 0, Tunables.ScoreMilestoneColors.Length - 1 );
		var color = Tunables.ScoreMilestoneColors[idx].WithAlpha( alpha );
		var text = _run.LastMilestoneName.ToUpper();
		// Subtle dark backdrop pour le milestone popup, alpha follows fade.
		float backdropW = MathF.Min( 700f, screenW * 0.55f );
		float backdropH = fontSize * 1.6f;
		hud.DrawRect( new Rect( (screenW - backdropW) * 0.5f, screenH * 0.175f, backdropW, backdropH ),
			new Color( 0f, 0f, 0f, 0.40f * alpha ) );
		var rect = new Rect( 0, screenH * 0.18f, screenW, fontSize * 1.4f );
		var scope = new TextRendering.Scope( text, color, fontSize );
		hud.DrawText( scope, rect, TextFlag.Center );
	}

	// Brief screen flash quand on entre en Cascading state — punch visuel pour
	// marquer le commitment du swing. Hot orange-amber tint, courte (0.35s),
	// fade out exponentiel pour pas masquer le carnage qui commence.
	private void DrawCascadeStartFlash( Sandbox.Rendering.HudPainter hud )
	{
		if ( _run is null || _run.State != RunState.Cascading ) return;
		float t = (float)_run.StateEntered;
		const float FlashDuration = 0.35f;
		if ( t > FlashDuration ) return;
		float u = t / FlashDuration;
		float alpha = MathF.Pow( 1f - u, 2.5f ) * 0.32f;
		var flash = new Color( 1.0f, 0.65f, 0.20f, alpha );
		hud.DrawRect( new Rect( 0, 0, Screen.Width, Screen.Height ), flash );
	}

	private void DrawMilestoneFlash( Sandbox.Rendering.HudPainter hud )
	{
		if ( _run is null || string.IsNullOrEmpty( _run.LastMilestoneName ) ) return;
		float t = (float)_run.MilestoneShownTime;
		// Flash only in the very first 0.4s of the popup — peak brightness at 0,
		// exponentially fading. Higher milestone tiers flash brighter.
		const float FlashDuration = 0.4f;
		if ( t > FlashDuration ) return;

		float u = t / FlashDuration;
		float alpha = MathF.Pow( 1f - u, 2.5f );
		int tierMul = _run.LastMilestoneIndex + 1;  // tier 0 = 1x, tier 5 = 6x
		float strength = MathF.Min( 0.65f, 0.18f + 0.10f * tierMul );
		var color = Tunables.ScoreMilestoneColors[
			Math.Clamp( _run.LastMilestoneIndex, 0, Tunables.ScoreMilestoneColors.Length - 1 )];
		var flash = color.WithAlpha( alpha * strength );

		hud.DrawRect( new Rect( 0, 0, Screen.Width, Screen.Height ), flash );
	}

	private void DrawDebugBlock( Sandbox.Rendering.HudPainter hud )
	{
		var pad = 14f;
		var width = 280f;
		var height = FontSize + pad * 1.4f;
		var x = 32f;
		var y = 32f + (height + 6f) * 4f;
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
		DrawLine( hud, x, ref y, width, height, pad, $"World : T{trees} L{pieces}", dim );
		if ( _run is not null )
		{
			DrawLine( hud, x, ref y, width, height, pad, $"Run : {_run.State} idle={_run.CascadeIdleSeconds:F1}s", dim );
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

	private void DrawScoreLine( Sandbox.Rendering.HudPainter hud, float x, ref float y, float width, float height, float pad, string label, Color tint, float fontSize )
	{
		hud.DrawRect( new Rect( x, y, width, height ), PanelColor );
		hud.DrawRect( new Rect( x, y, width, 2f ), tint.WithAlpha( 0.5f ) );
		hud.DrawRect( new Rect( x, y + height - 2f, width, 2f ), tint.WithAlpha( 0.5f ) );
		var textPos = new Vector2( x + pad, y + height * 0.5f );
		var scope = new TextRendering.Scope( label, tint, fontSize );
		hud.DrawText( scope, textPos );
		y += height + 6f;
	}

	// Reticle au centre de l'écran pendant WaitingForSwing. Petit cross +
	// halo coloré quand un arbre est lockable (couleur du modifier actif).
	// État no-lock = cross gris discret. Aide direct au "WYSIWYG aim" :
	// le joueur sait quand son clic va connecter.
	private void DrawAimReticle( Sandbox.Rendering.HudPainter hud )
	{
		if ( _run is null || _run.State != RunState.WaitingForSwing ) return;

		var aim = AimIndicator.Get( Scene );
		bool locked = aim.IsValid() && aim.HasLockedTarget;
		var tint = locked
			? (aim.CurrentTint.a > 0.01f ? aim.CurrentTint : new Color( 1f, 0.7f, 0.25f, 1f ))
			: new Color( 0.85f, 0.85f, 0.85f, 0.55f );

		float cx = Screen.Width * 0.5f;
		float cy = Screen.Height * 0.5f;
		float armLen = locked ? 14f : 10f;
		float armThk = 2f;
		float gap = 5f;

		// 4-arm cross (gap au centre pour pas masquer la cible).
		hud.DrawRect( new Rect( cx - armLen - gap, cy - armThk * 0.5f, armLen, armThk ), tint );
		hud.DrawRect( new Rect( cx + gap, cy - armThk * 0.5f, armLen, armThk ), tint );
		hud.DrawRect( new Rect( cx - armThk * 0.5f, cy - armLen - gap, armThk, armLen ), tint );
		hud.DrawRect( new Rect( cx - armThk * 0.5f, cy + gap, armThk, armLen ), tint );

		// Halo pulse quand locked.
		if ( locked )
		{
			float pulse = 0.65f + 0.35f * MathF.Sin( Time.Now * 6f );
			float ringR = 18f;
			float ringThk = 2f;
			var ringTint = tint.WithAlpha( tint.a * pulse );
			// 4 segments façon target-locker (haut/bas/gauche/droite).
			hud.DrawRect( new Rect( cx - ringR, cy - ringThk * 0.5f, 6f, ringThk ), ringTint );
			hud.DrawRect( new Rect( cx + ringR - 6f, cy - ringThk * 0.5f, 6f, ringThk ), ringTint );
			hud.DrawRect( new Rect( cx - ringThk * 0.5f, cy - ringR, ringThk, 6f ), ringTint );
			hud.DrawRect( new Rect( cx - ringThk * 0.5f, cy + ringR - 6f, ringThk, 6f ), ringTint );
		}
	}
}
