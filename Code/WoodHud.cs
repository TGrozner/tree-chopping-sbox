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
	private AxeController _axe;
	private ShopStation _activeStation;
	private int _lastShownWood;
	private bool _woodSynced;
	private TimeSince _woodChangedTime = 999f;
	private bool _woodWasSpent;
	private TimeSince _prestigeBannerTime = 999f;
	private int _prestigeBannerSpirits;
	private TimeSince _upgradeBannerTime = 999f;
	private string _upgradeBannerText = "";
	private TimeSince _backpackFullTime = 999f;
	private TimeSince _depositFlashTime = 999f;
	private int _lastDepositAmount;

	// Called after a successful TryPrestige() so the player gets
	// a clear "you just earned N Spirits" beat on top of the chip burst.
	public void ShowPrestigeBanner( int spiritsGained )
	{
		_prestigeBannerTime = 0f;
		_prestigeBannerSpirits = spiritsGained;
	}

	// Small toast for every shop purchase — "AXE → IRON" / "SPEED +1" /
	// "PET → YELLOW FINCH" / etc. Lower-priority than the prestige banner
	// so it can co-exist without fighting for the same eye-space.
	public void ShowUpgradeBanner( string text )
	{
		_upgradeBannerTime = 0f;
		_upgradeBannerText = text;
	}

	// Fired by Tree.GiveWoodOnce when the backpack overflowed (extra wood
	// dropped on the ground = wasted). HUD shows a "BACKPACK FULL" pulse.
	public void ShowBackpackFullHint() => _backpackFullTime = 0f;

	// Fired by Tree.Chop when the player's axe tier can't fell this kind.
	// Phase E progression gate : "buy a better axe to chop Normals/Veterans".
	public void ShowAxeTooWeakHint( TreeKind kind, int neededTier )
	{
		_axeTooWeakTime = 0f;
		_axeTooWeakKind = kind;
		_axeTooWeakNeededTier = neededTier;
	}

	private TimeSince _axeTooWeakTime = 999f;
	private TreeKind _axeTooWeakKind;
	private int _axeTooWeakNeededTier;

	// Fired by the depot after a TryDeposit — shows the amount transferred
	// as a brief floating "+N" so the action reads.
	public void ShowDepositFlash( int amount )
	{
		_depositFlashTime = 0f;
		_lastDepositAmount = amount;
	}

	// Fired by WoodItem.OnPickup. Stack-merge Valheim MessageHud pattern (lignes
	// 210-224 MessageHud.cs) : si le dernier toast est récent (< MergeWindow s)
	// ET du même type, bump son Amount/Count. Sinon nouveau toast.
	public void ShowWoodPickupToast( int amount, WoodType type = WoodType.Wood )
	{
		const float MergeWindow = 1.0f; // Valheim use 4s ; raccourci à 1s pour notre rythme arcade rapide
		if ( _pickupToasts.Count > 0 )
		{
			int last = _pickupToasts.Count - 1;
			var lastToast = _pickupToasts[last];
			if ( lastToast.Type == type && (float)lastToast.Time < MergeWindow )
			{
				lastToast.Amount += amount;
				lastToast.Count += 1;
				lastToast.Time = 0f; // refresh fade window
				_pickupToasts[last] = lastToast;
				return;
			}
		}
		_pickupToasts.Add( new PickupToast { Time = 0f, Amount = amount, Count = 1, Type = type } );
		// Cap so a mass pickup doesn't grow the list unboundedly.
		if ( _pickupToasts.Count > 12 ) _pickupToasts.RemoveAt( 0 );
	}

	private struct PickupToast { public TimeSince Time; public int Amount; public int Count; public WoodType Type; }
	private readonly List<PickupToast> _pickupToasts = new();

	// Test hook : exposer le count de toasts actifs pour TestPickupStackMerge.
	internal int GetPickupToastDebugCount() => _pickupToasts.Count;
	internal void ClearPickupToastsForTest() => _pickupToasts.Clear();

	// Damage popups Valheim-style — port direct de DamageText.AddInworldText.
	// Float-up text au hit point + offset random, rise lent, cubic alpha decay,
	// distance cull 30m, font size switch à 10m. Cap 200 popups (= Valheim).
	private struct DamagePopup
	{
		public TimeSince Time;
		public Vector3 WorldPos;
		public string Text;
		public Color Tint;
		public bool IsBonus; // Bonus = +50% size, 3s duration
	}
	private readonly List<DamagePopup> _damagePopups = new();

	// Distance cull match `m_maxTextDistance = 30f` Valheim (30m).
	// En sbox units (inches) 30m = ~1180u.
	const float DamageTextMaxDistance = 1180f;
	// Switch font small/large à 10m Valheim (≈ 400u sbox).
	const float DamageTextSmallFontDistance = 400f;
	const float DamageTextLifetime = 1.5f;
	const float DamageTextBonusLifetime = 3f;

	// Valheim colors verbatim (DamageText.AddInworldText switch type).
	public static readonly Color DamageTextNormal     = new( 1f, 1f, 1f, 1f );
	public static readonly Color DamageTextResistant  = new( 0.6f, 0.6f, 0.6f, 1f );
	public static readonly Color DamageTextWeak       = new( 1f, 1f, 0f, 1f );
	public static readonly Color DamageTextTooHard    = new( 0.8f, 0.7f, 0.7f, 1f );
	public static readonly Color DamageTextBonus      = new( 1f, 0.63f, 0.24f, 1f );
	public static readonly Color DamageTextHeal       = new( 0.5f, 1f, 0.5f, 0.7f );

	public void ShowDamageText( string text, Vector3 worldPos, Color tint, bool isBonus = false )
	{
		// Random offset Valheim : `pos + Random.insideUnitSphere * 0.5f` — 0.5m
		// sphere. En sbox units (inches) ≈ 20u.
		var jittered = worldPos + new Vector3(
			Game.Random.Float( -20f, 20f ),
			Game.Random.Float( -20f, 20f ),
			Game.Random.Float( -20f, 20f ) );
		_damagePopups.Add( new DamagePopup
		{
			Time = 0f,
			WorldPos = jittered,
			Text = text,
			Tint = tint,
			IsBonus = isBonus,
		} );
		if ( _damagePopups.Count > 200 ) _damagePopups.RemoveAt( 0 );
	}

	private bool _welcomeShown;
	protected override void OnUpdate()
	{
		_state ??= GameState.Get( Scene );
		_activeStation = Scene?.GetAllComponents<ShopStation>()
			.FirstOrDefault( s => s.IsValid() && s.PlayerInside );
		if ( Input.Pressed( "DebugToggle" ) ) DebugVisible = !DebugVisible;
		if ( !_welcomeShown && _state.IsValid() )
		{
			_welcomeShown = true;
			if ( _state.TreesFelledTotal > 0 )
				ShowUpgradeBanner( $"WELCOME BACK  ·  {_state.TreesFelledTotal} trees · {_state.Spirits} spirits" );
		}

		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		DrawCrosshair( hud );
		DrawComboPips( hud );
		DrawContextHint( hud );
		DrawWoodPanel( hud );
		DrawBackpackPanel( hud );
		DrawTierBadge( hud );
		DrawShopHint( hud );
		DrawTeleportHint( hud );
		DrawPrestigeBanner( hud );
		DrawUpgradeBanner( hud );
		DrawBackpackFullWarning( hud );
		DrawDepositFlash( hud );
		DrawAxeTooWeakHint( hud );
		DrawPickupToasts( hud );
		DrawDamagePopups( hud, camera );

		if ( DebugVisible ) DrawDebugBlock( hud );
	}

	// Combo pips sous le crosshair — 3 dots qui s'allument selon ChainLevel.
	// Le dernier pip (final hit) flashe en orange quand chain est au max.
	// Match Valheim's combo indicator pattern (chain feedback visuel pour que
	// le joueur perçoive qu'il combo).
	private void DrawComboPips( Sandbox.Rendering.HudPainter hud )
	{
		_axe ??= Scene?.GetAllComponents<AxeController>().FirstOrDefault();
		if ( !_axe.IsValid() ) return;
		int chain = _axe.ChainLevel;
		int maxChain = Tunables.ChopComboMaxLevels;

		float cx = Screen.Width * 0.5f;
		float cy = Screen.Height * 0.5f;
		float pipSize = 6f;
		float pipGap = 8f;
		float totalW = maxChain * pipSize + (maxChain - 1) * pipGap;
		float startX = cx - totalW * 0.5f;
		float y = cy + 26f; // sous le crosshair

		for ( int i = 0; i < maxChain; i++ )
		{
			float x = startX + i * (pipSize + pipGap);
			bool lit = i <= chain;
			bool isFinal = i == maxChain - 1;
			Color tint;
			if ( !lit )
			{
				tint = new Color( 0.95f, 0.95f, 0.95f, 0.25f );
			}
			else if ( isFinal && chain == maxChain - 1 )
			{
				// Final hit pip — orange flash (Valheim DamageText.Bonus color).
				tint = DamageTextBonus.WithAlpha( 1f );
			}
			else
			{
				tint = HotColor.WithAlpha( 0.85f );
			}
			hud.DrawRect( new Rect( x, y, pipSize, pipSize ), tint );
		}
	}

	private void DrawCrosshair( Sandbox.Rendering.HudPainter hud )
	{
		// Small "+" with a gap in the middle. Idle = subtle white alpha 0.55.
		// Target locked (HasAimTarget = tree under reticle inside SwingRange) =
		// HotColor + opaque + larger gap + thicker arms. Reads as a meaty
		// "this swing will connect" signal without crowding the screen when
		// the player is just walking around.
		_axe ??= Scene?.GetAllComponents<AxeController>().FirstOrDefault();
		bool locked = _axe.IsValid() && _axe.HasAimTarget;

		float cx = Screen.Width * 0.5f;
		float cy = Screen.Height * 0.5f;
		var tint = locked
			? HotColor.WithAlpha( 0.95f )
			: new Color( 0.95f, 0.95f, 0.95f, 0.55f );
		float armLen = locked ? 9f : 7f;
		float armThick = locked ? 3f : 2f;
		float gap = locked ? 5f : 3f;
		hud.DrawRect( new Rect( cx - armLen - gap, cy - armThick * 0.5f, armLen, armThick ), tint );
		hud.DrawRect( new Rect( cx + gap, cy - armThick * 0.5f, armLen, armThick ), tint );
		hud.DrawRect( new Rect( cx - armThick * 0.5f, cy - armLen - gap, armThick, armLen ), tint );
		hud.DrawRect( new Rect( cx - armThick * 0.5f, cy + gap, armThick, armLen ), tint );
	}

	private void DrawContextHint( Sandbox.Rendering.HudPainter hud )
	{
		_axe ??= Scene?.GetAllComponents<AxeController>().FirstOrDefault();
		if ( !_axe.IsValid() || _state is null ) return;

		string label = "";
		Color tint = TextColor.WithAlpha( 0.72f );
		if ( _axe.HasAimTarget )
		{
			label = _axe.AimTargetLabel;
			tint = _axe.AimTargetTooHard ? HotColor.WithAlpha( 0.92f ) : TextColor.WithAlpha( 0.84f );
		}
		else if ( _activeStation.IsValid() && _activeStation.Kind == StationKind.Deposit )
		{
			label = _state.BackpackTotal > 0 ? "DEPOSIT WOOD" : "BACKPACK EMPTY";
			tint = _state.BackpackTotal > 0 ? TextColor.WithAlpha( 0.82f ) : TextColor.WithAlpha( 0.42f );
		}
		else if ( _state.BackpackFull )
		{
			label = "BACKPACK FULL";
			tint = HotColor.WithAlpha( 0.85f );
		}
		else if ( TryGetNearestWoodItem( out var nearestWood, out float distance ) )
		{
			string typeName = Tunables.WoodTypeNames[(int)nearestWood.Type].ToUpper();
			label = distance < Tunables.WoodItemMagnetRange ? $"PICKUP {typeName}" : $"{typeName} NEARBY";
			tint = Color.Lerp( TextColor, Tunables.WoodTypeTints[(int)nearestWood.Type], 0.65f ).WithAlpha( 0.78f );
		}

		if ( string.IsNullOrEmpty( label ) ) return;

		float fontSize = 14f;
		float cx = Screen.Width * 0.5f;
		float cy = Screen.Height * 0.5f + 44f;
		float w = MathF.Min( 240f, Screen.Width * 0.42f );
		var rect = new Rect( cx - w * 0.5f, cy, w, fontSize * 1.5f );
		hud.DrawRect( rect, new Color( 0f, 0f, 0f, 0.26f ) );
		hud.DrawText( new TextRendering.Scope( label, tint, fontSize ), rect, TextFlag.Center );
	}

	private bool TryGetNearestWoodItem( out WoodItem nearest, out float nearestDistance )
	{
		nearest = null;
		nearestDistance = float.MaxValue;
		var playerPos = _axe.WorldPosition;
		foreach ( var item in Scene?.GetAllComponents<WoodItem>() ?? Enumerable.Empty<WoodItem>() )
		{
			if ( !item.IsValid() ) continue;
			float d = item.WorldPosition.Distance( playerPos );
			if ( d >= Tunables.WoodItemHintRange || d >= nearestDistance ) continue;
			nearest = item;
			nearestDistance = d;
		}
		return nearest.IsValid();
	}

	private void DrawWoodPanel( Sandbox.Rendering.HudPainter hud )
	{
		if ( _state is null ) return;
		// First sync after GameState loads : initialise without firing the
		// gain pulse (Wood may be non-zero from progress.json).
		if ( !_woodSynced ) { _lastShownWood = _state.Wood; _woodSynced = true; }
		if ( _state.Wood != _lastShownWood )
		{
			_woodWasSpent = _state.Wood < _lastShownWood;
			_lastShownWood = _state.Wood;
			_woodChangedTime = 0f;
		}
		const float PulseDuration = 0.35f;
		float pulseT = (float)_woodChangedTime / PulseDuration;
		float scale = pulseT < 1f ? MathX.Lerp( 1.25f, 1.0f, pulseT * pulseT ) : 1f;
		var pulseColor = pulseT < 1f && _woodWasSpent ? HotColor : TextColor;

		float padL = 36f, padT = 28f;
		float labelSize = 14f;
		float valueSize = 36f * scale;
		float boxW = 180f;
		var labelRect = new Rect( padL, padT, boxW, labelSize * 1.4f );
		hud.DrawText( new TextRendering.Scope( "WOOD", TextColor.WithAlpha( 0.55f ), labelSize ),
			labelRect, TextFlag.LeftCenter );
		var valueRect = new Rect( padL, padT + labelSize, boxW, valueSize * 1.3f );
		hud.DrawText( new TextRendering.Scope( _state.Wood.ToString(), pulseColor, valueSize ),
			valueRect, TextFlag.LeftCenter );
	}

	private void DrawBackpackPanel( Sandbox.Rendering.HudPainter hud )
	{
		if ( _state is null ) return;
		float padL = 36f;
		// Sit just under the WOOD value (which spans ~ padT + label + value ~ 90px).
		float y = 110f;
		float labelSize = 12f;
		float valueSize = 22f;
		float boxW = 220f;
		var col = _state.BackpackFull ? HotColor : TextColor.WithAlpha( 0.80f );
		hud.DrawText( new TextRendering.Scope( "BACKPACK", TextColor.WithAlpha( 0.45f ), labelSize ),
			new Rect( padL, y, boxW, labelSize * 1.4f ), TextFlag.LeftCenter );
		var str = $"{_state.BackpackTotal} / {_state.BackpackCapacity}";
		hud.DrawText( new TextRendering.Scope( str, col, valueSize ),
			new Rect( padL, y + labelSize, boxW, valueSize * 1.3f ), TextFlag.LeftCenter );
		if ( _state.BackpackFinewood > 0 || _state.BackpackCoreWood > 0 )
		{
			string detail = $"W {_state.BackpackWood}  F {_state.BackpackFinewood}  C {_state.BackpackCoreWood}";
			hud.DrawText( new TextRendering.Scope( detail, TextColor.WithAlpha( 0.55f ), 12f ),
				new Rect( padL, y + labelSize + valueSize * 1.12f, boxW, 18f ), TextFlag.LeftCenter );
		}
	}

	private void DrawBackpackFullWarning( Sandbox.Rendering.HudPainter hud )
	{
		const float duration = 1.0f;
		float t = (float)_backpackFullTime / duration;
		if ( t >= 1f ) return;
		float alpha = (1f - t).Clamp( 0f, 1f );
		float size = 30f;
		var rect = new Rect( 0f, Screen.Height * 0.18f, Screen.Width, size * 1.6f );
		hud.DrawText( new TextRendering.Scope( "BACKPACK FULL - RETURN TO DEPOT", HotColor.WithAlpha( alpha ), size ),
			rect, TextFlag.Center );
	}

	private void DrawDamagePopups( Sandbox.Rendering.HudPainter hud, CameraComponent camera )
	{
		var camPos = camera.WorldPosition;
		// Cull expired (lifetime = bonus 3s / normal 1.5s match Valheim m_textDuration).
		for ( int i = _damagePopups.Count - 1; i >= 0; i-- )
		{
			float life = _damagePopups[i].IsBonus ? DamageTextBonusLifetime : DamageTextLifetime;
			if ( (float)_damagePopups[i].Time > life ) _damagePopups.RemoveAt( i );
		}

		foreach ( var p in _damagePopups )
		{
			float life = p.IsBonus ? DamageTextBonusLifetime : DamageTextLifetime;
			float t = ((float)p.Time / life).Clamp( 0f, 1f );

			// Distance cull à 30m Valheim (1180u sbox).
			float distance = camPos.Distance( p.WorldPos );
			if ( distance > DamageTextMaxDistance ) continue;

			// Rise lent : Valheim `worldPos.y += dt` → 1u/s en mètres. En inches
			// c'est ~40u/s. Sur 1.5s lifetime = ~60u de rise total.
			var risingPos = p.WorldPos + Vector3.Up * (t * life * 40f);

			// Projection via BBox dégénérée (sbox n'expose pas de WorldToScreen direct).
			var bbox = new BBox( risingPos - new Vector3( 0.5f ), risingPos + new Vector3( 0.5f ) );
			var rect = camera.BBoxToScreenPixels( bbox, out var behind );
			if ( behind ) continue;
			float cx = rect.Left + rect.Width * 0.5f;
			float cy = rect.Top + rect.Height * 0.5f;

			// Cubic alpha decay match Valheim : `color.a = 1 - pow(t, 3)`. Reste
			// opaque la majorité du temps, fade hard à la fin.
			float alpha = 1f - MathF.Pow( t, 3f );

			// Distance-based font size : Valheim large=16 / small=8 split à 10m.
			// On scale large=28 / small=18 dans nos unités HUD.
			float baseSize = distance > DamageTextSmallFontDistance ? 18f : 28f;
			if ( p.IsBonus ) baseSize *= 1.5f;

			var drawRect = new Rect( cx - 70f, cy - baseSize * 0.5f, 140f, baseSize * 1.4f );
			hud.DrawText( new TextRendering.Scope( p.Text, p.Tint.WithAlpha( alpha ), baseSize ),
				drawRect, TextFlag.Center );
		}
	}

	private void DrawPickupToasts( Sandbox.Rendering.HudPainter hud )
	{
		const float duration = 1.6f;
		// Cull expired entries from the head of the list (insertion order
		// preserves the visual stack from top = oldest to bottom = newest).
		while ( _pickupToasts.Count > 0 && (float)_pickupToasts[0].Time > duration )
			_pickupToasts.RemoveAt( 0 );
		float padL = 36f;
		float baseY = 200f;
		float lineH = 26f;
		float size = 18f;
		for ( int i = 0; i < _pickupToasts.Count; i++ )
		{
			var t = (float)_pickupToasts[i].Time / duration;
			float alpha = t < 0.10f ? (t / 0.10f) : (1f - (t - 0.10f) / 0.90f);
			alpha = alpha.Clamp( 0f, 1f );
			float y = baseY + i * lineH;
			// Valheim MessageHud lignes 220-224 : "text x{amount}" si amount > 1.
			// Notre Count = nombre de pickups stackés. Amount = somme. Nom du type
			// Valheim verbatim ("Wood" / "Finewood" / "Core Wood").
			string typeName = Tunables.WoodTypeNames[(int)_pickupToasts[i].Type];
			string label = _pickupToasts[i].Count > 1
				? $"{typeName} +{_pickupToasts[i].Amount} x{_pickupToasts[i].Count}"
				: $"{typeName} +{_pickupToasts[i].Amount}";
			// Tint match le type Wood/Finewood/CoreWood pour identification rapide.
			var typeColor = Color.Lerp( TextColor, Tunables.WoodTypeTints[(int)_pickupToasts[i].Type], 0.6f );
			hud.DrawText(
				new TextRendering.Scope( label, typeColor.WithAlpha( alpha ), size ),
				new Rect( padL, y, 260f, lineH ), TextFlag.LeftCenter );
		}
	}

	private void DrawAxeTooWeakHint( Sandbox.Rendering.HudPainter hud )
	{
		const float duration = 1.2f;
		float t = (float)_axeTooWeakTime / duration;
		if ( t >= 1f ) return;
		float alpha = (1f - t).Clamp( 0f, 1f );
		string need = _axeTooWeakNeededTier >= 0 && _axeTooWeakNeededTier < Tunables.AxeTierName.Length
			? Tunables.AxeTierName[_axeTooWeakNeededTier].ToUpper()
			: $"T{_axeTooWeakNeededTier}";
		string msg = $"AXE TOO WEAK — {_axeTooWeakKind.ToString().ToUpper()} NEEDS {need}";
		float size = 26f;
		var rect = new Rect( 0f, Screen.Height * 0.28f, Screen.Width, size * 1.6f );
		hud.DrawText( new TextRendering.Scope( msg, HotColor.WithAlpha( alpha ), size ), rect, TextFlag.Center );
	}

	private void DrawDepositFlash( Sandbox.Rendering.HudPainter hud )
	{
		const float duration = 1.2f;
		float t = (float)_depositFlashTime / duration;
		if ( t >= 1f || _lastDepositAmount <= 0 ) return;
		float alpha = t < 0.10f ? (t / 0.10f) : (1f - (t - 0.10f) / 0.90f);
		alpha = alpha.Clamp( 0f, 1f );
		// Float upward from the stockpile position.
		float padL = 36f;
		float y0 = 70f;
		float yOff = -50f * t;
		float size = 32f;
		var rect = new Rect( padL, y0 + yOff, 240f, size * 1.4f );
		hud.DrawText( new TextRendering.Scope( $"+{_lastDepositAmount}", TextColor.WithAlpha( alpha ), size ),
			rect, TextFlag.LeftCenter );
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
		if ( !_activeStation.IsValid() || _state is null ) return;
		switch ( _activeStation.Kind )
		{
			case StationKind.Tools:    DrawToolsHint( hud );    break;
			case StationKind.Deposit:     DrawDepositHint( hud );     break;
			case StationKind.Upgrades: DrawUpgradesHint( hud ); break;
			case StationKind.Prestige: DrawPrestigeHint( hud ); break;
		}
	}

	private void DrawDepositHint( Sandbox.Rendering.HudPainter hud )
	{
		string header = _state.BackpackTotal > 0
			? $"DEPOT - auto deposit on entry · [E] / [1] to flush again ({_state.BackpackTotal} carried)"
			: "DEPOT - backpack empty, go chop";
		DrawStationHintFrame( hud, 0, header,
			out _, out _, out _, out _, out _ );
	}

	private void DrawStationHintFrame( Sandbox.Rendering.HudPainter hud, int lineCount, string header, out float backX, out float backY, out float backW, out float lineH, out float fontSize )
	{
		float w = Screen.Width;
		float h = Screen.Height;
		fontSize = 18f;
		lineH = fontSize * 1.6f;
		backW = MathF.Min( 780f, w * 0.60f );
		float backH = lineH * (lineCount + 1) + 12f;
		backX = (w - backW) * 0.5f;
		backY = h * 0.52f;
		hud.DrawRect( new Rect( backX, backY, backW, backH ), new Color( 0f, 0f, 0f, 0.62f ) );
		hud.DrawText( new TextRendering.Scope( header, TextColor.WithAlpha( 0.75f ), fontSize * 1.05f ),
			new Rect( backX, backY + 4f, backW, lineH ), TextFlag.Center );
	}

	private void DrawToolsHint( Sandbox.Rendering.HudPainter hud )
	{
		DrawStationHintFrame( hud, 3, "TOOLS — [E] auto-buy cheapest",
			out float x, out float y, out float w, out float lh, out float fs );
		DrawShopLine( hud, x, y + 1 * lh + 2f, w, lh, fs, "1",
			$"Axe T{_state.AxeTier} ({Tunables.AxeTierName[_state.AxeTier]})", AxeNextCostText(), _state.CanAffordNextAxe(), _state.AxeTier >= Tunables.MaxAxeTier,
			_state.AxeTier < Tunables.MaxAxeTier ? $"→ {Tunables.AxeTierName[_state.AxeTier + 1]}" : "+chop/swing" );
		DrawShopLine( hud, x, y + 2 * lh + 2f, w, lh, fs, "2",
			$"Range T{_state.ToolRangeTier}", ToolRangeNextCost(),
			$"×{Tunables.ToolRangeMul[_state.ToolRangeTier]:0.00} swing reach" );
		DrawShopLine( hud, x, y + 3 * lh + 2f, w, lh, fs, "3",
			$"Swing Speed T{_state.ToolSpeedTier}", ToolSpeedNextCost(),
			$"recover ×{Tunables.ToolSpeedMul[_state.ToolSpeedTier]:0.00}" );
	}

	private int ToolRangeNextCost() => _state.ToolRangeTier < Tunables.MaxToolStatTier ? Tunables.ToolRangeCosts[_state.ToolRangeTier + 1] : -1;
	private int ToolSpeedNextCost() => _state.ToolSpeedTier < Tunables.MaxToolStatTier ? Tunables.ToolSpeedCosts[_state.ToolSpeedTier + 1] : -1;

	private void DrawUpgradesHint( Sandbox.Rendering.HudPainter hud )
	{
		string header = _state.Spirits > 0
			? $"UPGRADES — [E] auto-buy cheapest    ✦ {_state.Spirits} Spirits (+{_state.Spirits}% wood)"
			: "UPGRADES — [E] auto-buy cheapest";
		DrawStationHintFrame( hud, 5, header,
			out float x, out float y, out float w, out float lh, out float fs );
		DrawShopLine( hud, x, y + 1 * lh + 2f, w, lh, fs, "1",
			$"Speed T{_state.SpeedTier}", SpeedNextCost(), $"×{Tunables.SpeedMul[_state.SpeedTier]:0.00} walk" );
		DrawShopLine( hud, x, y + 2 * lh + 2f, w, lh, fs, "2",
			$"Luck T{_state.LuckTier}", LuckNextCost(), $"{(Tunables.LuckChance[_state.LuckTier] * 100):0}% × 2 chance" );
		DrawShopLine( hud, x, y + 3 * lh + 2f, w, lh, fs, "3",
			$"Power T{_state.PowerTier}", PowerNextCost(), $"+{Tunables.PowerBonus[_state.PowerTier]} chop power" );
		DrawShopLine( hud, x, y + 4 * lh + 2f, w, lh, fs, "4",
			$"Backpack T{_state.BackpackTier}", BackpackNextCost(), $"{_state.BackpackCapacity} cap" );
		DrawShopLine( hud, x, y + 5 * lh + 2f, w, lh, fs, "5",
			$"Pet T{_state.PetTier}", PetNextCost(), "cosmetic companion" );
	}

	private int BackpackNextCost() => _state.BackpackTier < Tunables.MaxBackpackTier ? Tunables.BackpackCosts[_state.BackpackTier + 1] : -1;

	private void DrawPrestigeHint( Sandbox.Rendering.HudPainter hud )
	{
		DrawStationHintFrame( hud, 1, "PRESTIGE — [E] / [1] replant the forest",
			out float x, out float y, out float w, out float lh, out float fs );
		DrawPrestigeLine( hud, x, y + lh + 2f, w, lh, fs );
	}

	private void DrawPrestigeLine( Sandbox.Rendering.HudPainter hud, float x, float y, float w, float h, float font )
	{
		bool can = _state.CanPrestige();
		int wouldGet = _state.SpiritsFromPrestige - _state.Spirits;
		string line = can
			? $"  [1]  REPLANT FOREST  ·  gain {wouldGet} Sapling Spirits (+{wouldGet}% wood, perma)"
			: $"  [1]  Replant locked  ·  need 500 lifetime wood (have {_state.TotalWoodEarned})";
		var tint = can ? HotColor : TextColor.WithAlpha( 0.40f );
		hud.DrawText( new TextRendering.Scope( line, tint, font ),
			new Rect( x, y, w, h ), TextFlag.LeftCenter );
	}

	private string AxeNextCostText()
	{
		if ( _state.AxeTier >= Tunables.MaxAxeTier ) return "MAX";
		var recipe = Tunables.AxeTierCostsByType[_state.AxeTier + 1];
		var parts = new List<string>();
		if ( recipe[0] > 0 ) parts.Add( $"{recipe[0]}W" );
		if ( recipe[1] > 0 ) parts.Add( $"{recipe[1]}FW" );
		if ( recipe[2] > 0 ) parts.Add( $"{recipe[2]}CW" );
		return string.Join( " + ", parts );
	}
	private int SpeedNextCost() => _state.SpeedTier < Tunables.MaxStatTier ? Tunables.SpeedCosts[_state.SpeedTier + 1] : -1;
	private int LuckNextCost() => _state.LuckTier < Tunables.MaxStatTier ? Tunables.LuckCosts[_state.LuckTier + 1] : -1;
	private int PowerNextCost() => _state.PowerTier < Tunables.MaxStatTier ? Tunables.PowerCosts[_state.PowerTier + 1] : -1;
	private int PetNextCost() => _state.PetTier < Tunables.MaxPetTier ? Tunables.PetCosts[_state.PetTier + 1] : -1;

	private void DrawShopLine( Sandbox.Rendering.HudPainter hud, float x, float y, float w, float h, float font,
		string key, string label, string costStr, bool affordable, bool maxed, string effect )
	{
		var labelColor = maxed ? TextColor.WithAlpha( 0.40f ) : (affordable ? HotColor : TextColor.WithAlpha( 0.85f ));
		string line = $"  [{key}]  {label}  Â·  {costStr}  Â·  {effect}";
		hud.DrawText( new TextRendering.Scope( line, labelColor, font ),
			new Rect( x, y, w, h ), TextFlag.LeftCenter );
	}

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

	private void DrawPrestigeBanner( Sandbox.Rendering.HudPainter hud )
	{
		const float duration = 2.5f;
		float t = (float)_prestigeBannerTime / duration;
		if ( t >= 1f ) return;
		float w = Screen.Width;
		float h = Screen.Height;
		// Fade in fast, hold, fade out smoothly.
		float alpha = t < 0.15f ? (t / 0.15f) : (1f - (t - 0.15f) / 0.85f);
		alpha = alpha.Clamp( 0f, 1f );
		float fontSize = 64f;
		var title = $"REPLANTED  ·  +{_prestigeBannerSpirits} SAPLING SPIRITS";
		// Black backdrop strip behind the text.
		hud.DrawRect( new Rect( 0, h * 0.30f, w, fontSize * 1.8f ),
			new Color( 0f, 0f, 0f, 0.55f * alpha ) );
		hud.DrawText( new TextRendering.Scope( title, Tunables.MythicCanopyTint.WithAlpha( alpha ), fontSize ),
			new Rect( 0, h * 0.30f, w, fontSize * 1.8f ), TextFlag.Center );
	}

	private void DrawUpgradeBanner( Sandbox.Rendering.HudPainter hud )
	{
		const float duration = 1.4f;
		float t = (float)_upgradeBannerTime / duration;
		if ( t >= 1f || string.IsNullOrEmpty( _upgradeBannerText ) ) return;
		float w = Screen.Width;
		float h = Screen.Height;
		float alpha = t < 0.10f ? (t / 0.10f) : (1f - (t - 0.10f) / 0.90f);
		alpha = alpha.Clamp( 0f, 1f );
		float fontSize = 28f;
		hud.DrawRect( new Rect( 0, h * 0.74f, w, fontSize * 1.6f ),
			new Color( 0f, 0f, 0f, 0.45f * alpha ) );
		hud.DrawText( new TextRendering.Scope( _upgradeBannerText, TextColor.WithAlpha( alpha ), fontSize ),
			new Rect( 0, h * 0.74f, w, fontSize * 1.6f ), TextFlag.Center );
	}

	private void DrawTeleportHint( Sandbox.Rendering.HudPainter hud )
	{
		// Hide the hint when player is inside any shop station (the station
		// hint is more important and would overlap).
		if ( _activeStation.IsValid() ) return;
		float fontSize = 16f;
		string text = "[R] teleport to shop";
		var tint = TextColor.WithAlpha( 0.40f );
		if ( _state is not null && _state.BackpackTotal > 0 )
		{
			if ( _state.AxeTier < Tunables.MaxAxeTier )
			{
				var recipe = Tunables.AxeTierCostsByType[_state.AxeTier + 1];
				bool nextAxeReady =
					_state.Wood + _state.BackpackWood >= recipe[0]
					&& _state.Finewood + _state.BackpackFinewood >= recipe[1]
					&& _state.CoreWood + _state.BackpackCoreWood >= recipe[2];
				if ( nextAxeReady )
				{
					text = $"[R] return to depot - {Tunables.AxeTierName[_state.AxeTier + 1].ToUpper()} ready";
					tint = HotColor.WithAlpha( 0.72f );
				}
			}
			if ( _state.BackpackFull )
			{
				text = "[R] return to depot - backpack full";
				tint = HotColor.WithAlpha( 0.72f );
			}
		}
		var rect = new Rect( 0, Screen.Height * 0.92f, Screen.Width, fontSize * 1.4f );
		hud.DrawText( new TextRendering.Scope( text, tint, fontSize ), rect, TextFlag.Center );
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
		var logs = Scene?.GetAllComponents<FallenLog>().ToList() ?? new List<FallenLog>();
		int standing = 0, falling = 0, landed = 0;
		foreach ( var t in trees )
		{
			if ( !t.IsValid() ) continue;
			if ( t.IsStanding ) standing++;
			else if ( t.IsFalling ) falling++;
			else landed++;
		}
		foreach ( var l in logs )
		{
			if ( !l.IsValid() ) continue;
			if ( l.IsFalling ) falling++;
			else if ( l.IsFallenLog ) landed++;
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
