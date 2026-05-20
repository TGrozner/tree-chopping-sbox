namespace TreeChopping;

public sealed class WoodChunk : Component
{
	[Property] public int WoodValue { get; set; } = 1;

	private bool _attractToBeaver;
	private BeaverController _beaver;
	private bool _collected;

	protected override void OnUpdate()
	{
		if ( _collected ) return;

		_beaver ??= Scene.GetAllComponents<BeaverController>().FirstOrDefault();
		if ( _beaver is null ) return;

		var to = _beaver.WorldPosition + Vector3.Up * 20f - WorldPosition;
		var dist = to.Length;

		if ( !_attractToBeaver && dist < Tunables.PickupRadius )
		{
			_attractToBeaver = true;
			var rb = Components.Get<Rigidbody>();
			if ( rb.IsValid() ) rb.MotionEnabled = false;
			// Attract spark — bright golden burst quand chunk entre dans le
			// pickup radius. Donne sensation "récolté" même avant le collect.
			ChopParticles.Burst( Scene, WorldPosition, Vector3.Up, new Color( 1.0f, 0.85f, 0.40f, 1f ), 4, 140f );
		}

		if ( _attractToBeaver )
		{
			WorldPosition = Vector3.Lerp( WorldPosition, _beaver.WorldPosition + Vector3.Up * 25f, Time.Delta * Tunables.PickupLerpSpeed );
			if ( dist < 12f )
			{
				Collect();
			}
		}
	}

	private void Collect()
	{
		_collected = true;
		AudioBank.PlayPickupWood( Scene, WorldPosition );
		var inv = WoodInventory.Get( Scene );
		inv?.Add( WoodValue );
		ComboTracker.Get( Scene )?.Beat();
		GameObject.Destroy();
	}
}
