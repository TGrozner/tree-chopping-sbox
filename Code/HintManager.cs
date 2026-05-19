namespace TreeChopping;

// Tutorial hints — short text shown bottom-center, fades in/out once per
// gameplay trigger. Ported from cascade.gd::_tick_hints in the Godot proto.
// HUD draw lives in WoodHud so there's a single overlay layer.
public sealed class HintManager : Component
{
	[Property, ReadOnly] public string CurrentText { get; private set; }
	[Property] public float HoldSeconds { get; set; } = 6f;
	[Property] public float FadeInSeconds { get; set; } = 0.4f;
	[Property] public float FadeOutSeconds { get; set; } = 0.6f;

	private readonly HashSet<int> _shown = new();
	private TimeSince _runStart;
	private TimeSince _hintShown = 1e6f;
	private float _hintDuration;
	private float _sinceSprintEligible;

	private const int HintStart = 0;
	private const int HintLogFell = 1;
	private const int HintPickaxe = 2;
	private const int HintSprint = 3;
	private const int HintDebug = 4;

	public static HintManager Get( Scene scene )
	{
		return scene?.GetAllComponents<HintManager>().FirstOrDefault();
	}

	public float Alpha
	{
		get
		{
			if ( string.IsNullOrEmpty( CurrentText ) ) return 0f;
			float t = _hintShown;
			if ( t < 0f ) return 0f;
			if ( t < FadeInSeconds ) return t / FadeInSeconds;
			float hold = FadeInSeconds + HoldSeconds;
			if ( t < hold ) return 1f;
			float fadeT = (t - hold) / FadeOutSeconds;
			return fadeT < 1f ? 1f - fadeT : 0f;
		}
	}

	protected override void OnStart()
	{
		_runStart = 0f;
		_hintShown = 1e6f;
	}

	protected override void OnUpdate()
	{
		if ( !_shown.Contains( HintStart ) && (float)_runStart > 2f )
		{
			Show( HintStart, "Click to swing the axe. Chop a tree." );
		}

		if ( !_shown.Contains( HintLogFell ) )
		{
			var bm = BiomeManager.Get( Scene );
			if ( bm.IsValid() && bm.TreesCleared > 0 )
			{
				Show( HintLogFell, "Trunk's down — keep swinging to break the log into chunks." );
			}
		}

		if ( !_shown.Contains( HintPickaxe ) )
		{
			var inv = WoodInventory.Get( Scene );
			if ( inv.IsValid() && inv.Wood > 0 )
			{
				Show( HintPickaxe, "Press E to swap to Pickaxe — mine the grey rocks for stone." );
			}
		}

		if ( _shown.Contains( HintPickaxe ) && !_shown.Contains( HintSprint ) )
		{
			_sinceSprintEligible += Time.Delta;
			if ( _sinceSprintEligible > 8f )
			{
				Show( HintSprint, "Hold Shift to sprint — covers the riverside band faster." );
			}
		}

		if ( !_shown.Contains( HintDebug ) && (float)_runStart > 60f )
		{
			Show( HintDebug, "Press F3 for debug info." );
		}
	}

	private void Show( int id, string text )
	{
		_shown.Add( id );
		CurrentText = text;
		_hintShown = 0f;
		_hintDuration = FadeInSeconds + HoldSeconds + FadeOutSeconds;
		Log.Info( $"[Hint] {id}: {text}" );
	}
}
