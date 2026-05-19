namespace TreeChopping;

// Decorative remnant left behind when a rock shatters. After RegrowSeconds
// it spawns a fresh rock at the same spot using the original rock tint and
// removes itself. Mirrors Stump.cs (tree regrowth) but for rocks. No Rock
// component: the stump is not minable, just a low-profile collision pad the
// beaver can walk on or past.
public sealed class RockStump : Component
{
	// Inline rather than in Tunables.cs because a parallel agent owns that file
	// this commit. Slightly longer than tree regrowth (30s) so rocks feel more
	// scarce/precious than wood.
	public const float RegrowSeconds = 45f;

	[Property] public Vector3 FootPosition { get; set; }
	[Property] public Color RockTint { get; set; } = new( 0.55f, 0.55f, 0.58f, 1f );

	private TimeSince _timeAlive;

	protected override void OnStart()
	{
		_timeAlive = 0f;
	}

	protected override void OnUpdate()
	{
		if ( _timeAlive < RegrowSeconds ) return;
		Rock.SpawnAt( Scene, FootPosition, RockTint );
		GameObject.Destroy();
	}

	public static RockStump SpawnAt( Scene scene, Vector3 footPosition, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = "RockStump";
		// Half rock height = squat pad; sits with its base on the foot plane.
		var stumpHeight = Tunables.RockHeight * 0.5f;
		go.WorldPosition = footPosition + Vector3.Up * (stumpHeight * 0.5f);
		go.Tags.Add( "rock_stump" );

		// Half the live-rock scale so the stump reads as a smaller remnant of
		// the parent without picking a different silhouette family.
		var seed = footPosition.GetHashCode();
		var stumpScale = Rock.RockModelScale * 0.5f;
		go.WorldScale = new Vector3( stumpScale );
		go.WorldRotation = Rotation.FromYaw( (seed & 0xFFFF) * 0.0055f );

		// Smaller variant pool (A/B/C) keeps the stump visually coherent with
		// the parent rock — same hue family, calmer silhouette.
		var darker = new Color( tint.r * 0.6f, tint.g * 0.6f, tint.b * 0.6f, 1f );
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Models.RockVariant( Math.Abs( seed ) % 3 );
		mr.Tint = darker;

		// Solid collider (not a trigger) — the player can stand on it; it does
		// not respond to pickaxe because there's no Rock/IChoppable component.
		// Collider scale undoes WorldScale so the final box matches the squat
		// stump footprint regardless of the model's authored size.
		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.RockRadius * 2f, Tunables.RockRadius * 2f, stumpHeight ) / stumpScale;
		col.Static = true;

		var stump = go.AddComponent<RockStump>();
		stump.FootPosition = footPosition;
		stump.RockTint = tint;
		return stump;
	}
}
