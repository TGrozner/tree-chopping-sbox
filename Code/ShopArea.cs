namespace TreeChopping;

// Marker GameObject placed at the player spawn. Detects the player inside
// its radius each tick and exposes PlayerInside for the HUD. The "E" press
// inside triggers an axe upgrade purchase via GameState.
public sealed class ShopArea : Component
{
	[Property] public float Radius { get; set; } = 250f;
	[Property, ReadOnly] public bool PlayerInside { get; private set; }

	private GameState _state;
	private BeaverController _beaver;

	protected override void OnUpdate()
	{
		_state ??= GameState.Get( Scene );
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();
		if ( !_beaver.IsValid() ) { PlayerInside = false; return; }

		float dxy = (_beaver.WorldPosition.WithZ( 0f ) - WorldPosition.WithZ( 0f )).Length;
		PlayerInside = dxy < Radius;

		if ( PlayerInside && Input.Pressed( "Use" ) && _state.IsValid() )
		{
			if ( _state.TryUpgradeAxe() )
				Sfx.Play( "sounds/log_break.sound", WorldPosition, volume: 0.7f, pitchMin: 1.2f, pitchMax: 1.4f );
		}
	}
}
