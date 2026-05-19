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

		// Roughly same footprint as the parent rock so the silhouette reads
		// as "this rock got knocked down" rather than a different prop.
		go.WorldScale = new Vector3( Tunables.RockRadius * 2f, Tunables.RockRadius * 2f, stumpHeight ) / Tunables.CubeBase;

		// Dim grey: same hue family as the source rock, pushed darker so it
		// reads as a stump rather than a fresh rock.
		var darker = new Color( tint.r * 0.6f, tint.g * 0.6f, tint.b * 0.6f, 1f );
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		mr.Tint = darker;

		// Solid collider (not a trigger) — the player can stand on it; it does
		// not respond to pickaxe because there's no Rock/IChoppable component.
		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );
		col.Static = true;

		var stump = go.AddComponent<RockStump>();
		stump.FootPosition = footPosition;
		stump.RockTint = tint;
		return stump;
	}
}
