namespace TreeChopping;

// Decorative stump that hangs around after a tree is felled. After
// Tunables.TreeRegrowthStumpSeconds it spawns a fresh sapling using
// the current biome's trunk tint and removes itself. Ported from the
// Godot proto's _schedule_regrowth flow in cascade.gd.
public sealed class Stump : Component
{
	[Property] public Vector3 FootPosition { get; set; }
	[Property] public Color TrunkTint { get; set; } = new( 0.42f, 0.28f, 0.18f, 1f );
	[Property] public TreeSpecies Species { get; set; } = TreeSpecies.Beech;
	[Property] public float RegrowSeconds { get; set; } = Tunables.TreeRegrowthStumpSeconds;

	private TimeSince _timeAlive;

	protected override void OnStart()
	{
		_timeAlive = 0f;
	}

	protected override void OnUpdate()
	{
		if ( _timeAlive < RegrowSeconds ) return;
		var biome = BiomeManager.Get( Scene );
		// Preserve the species that produced this stump — the player chopped a
		// crystal tree, the regrowth is another crystal tree. Biome tint is
		// still used as the legacy 3rd-arg even though Tree.SpawnAt now lets
		// the species tint win; passing it keeps the contract intact for any
		// future divergence.
		var tint = biome.IsValid() ? biome.TrunkTintForNewTree() : TrunkTint;
		Tree.SpawnAt( Scene, FootPosition, tint, Species );
		GameObject.Destroy();
	}

	// Optional `species` arg — defaults to Beech so existing callers
	// (SceneStarter or any code that doesn't know the original tree's
	// species) keep compiling. Tree.cs passes it explicitly so the
	// regrowth matches what was felled.
	public static Stump SpawnAt( Scene scene, Vector3 footPosition, Color tint, TreeSpecies species = TreeSpecies.Beech )
	{
		var go = scene.CreateObject();
		go.Name = "Stump";
		go.WorldPosition = footPosition + Vector3.Up * (Tunables.StumpHeight * 0.5f);
		go.Tags.Add( "stump" );

		// Kenney stump .vmdl encodes the correct shape — drop the legacy
		// cube-scaling. Pick a variant by hashing the foot position so each
		// stump is stable across save/load and not all clones of each other.
		go.WorldScale = Vector3.One;

		var darker = new Color( tint.r * 0.7f, tint.g * 0.65f, tint.b * 0.55f, 1f );
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Models.StumpVariant( Math.Abs( footPosition.GetHashCode() ) );
		mr.Tint = darker;

		// Physics envelope stays box-shaped at the legacy size — the player
		// only ever bumps into stumps, never chops them, so cheap collision
		// is fine and the Kenney mesh would over-spec it.
		var col = go.AddComponent<BoxCollider>();
		col.Scale = new Vector3( Tunables.CubeBase );
		col.Static = true;

		var stump = go.AddComponent<Stump>();
		stump.FootPosition = footPosition;
		stump.TrunkTint = tint;
		stump.Species = species;
		return stump;
	}
}
