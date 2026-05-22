namespace TreeChopping;

// Cosmetic pet — a small tinted orb that orbits around the player. No
// gameplay effect, just sells the "you have a companion" vibe à la
// Mow-The-Lawn / Forager. Auto-syncs its visibility + size + tint from
// GameState.PetTier each tick.
public sealed class Pet : Component
{
	[Property] public AxeController FollowTarget { get; set; }

	private float _phase;
	private ModelRenderer _renderer;
	private int _lastTier = -1;

	protected override void OnStart()
	{
		_renderer = Components.Get<ModelRenderer>() ?? AddComponent<ModelRenderer>();
		_renderer.Model = Model.Cube;
		_renderer.MaterialOverride = Mat.Default;
		ApplyTier( 0 );
	}

	protected override void OnUpdate()
	{
		FollowTarget ??= Scene?.GetAllComponents<AxeController>().FirstOrDefault();
		if ( !FollowTarget.IsValid() ) return;

		var gs = GameState.Get( Scene );
		int tier = gs.IsValid() ? gs.PetTier : 0;
		if ( tier != _lastTier ) ApplyTier( tier );

		// Hide when un-purchased.
		if ( _renderer.IsValid() ) _renderer.Enabled = tier > 0;
		if ( tier <= 0 ) return;

		_phase += Time.Delta * 1.4f;
		float radius = 60f + tier * 8f;
		float bob = MathF.Sin( _phase * 2.0f ) * 4f;
		var anchor = FollowTarget.WorldPosition + Vector3.Up * (Tunables.PlayerEyeHeight + 12f + bob);
		WorldPosition = anchor + new Vector3( MathF.Cos( _phase ) * radius, MathF.Sin( _phase ) * radius, 0f );
		WorldRotation *= Rotation.FromAxis( Vector3.Up, 90f * Time.Delta );
	}

	private void ApplyTier( int tier )
	{
		_lastTier = tier;
		if ( !_renderer.IsValid() ) return;
		if ( tier <= 0 ) { _renderer.Enabled = false; return; }
		_renderer.Enabled = true;
		_renderer.Tint = Tunables.PetTints[tier];
		float size = Tunables.PetSizes[tier];
		WorldScale = new Vector3( size ) / Tunables.CubeBase;
	}
}
