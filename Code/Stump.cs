namespace TreeChopping;

// Decorative stump that hangs around after a tree is felled. Used to regrow
// into a fresh tree after TreeRegrowthStumpSeconds in the wood-gathering era;
// the bowling pivot scrapped per-tree regrowth (the whole arena rebuilds on
// "R" restart via RunManager + SceneStarter.RegenerateForest), so stumps now
// stay permanent debris until the next regenerate.
public sealed class Stump : Component
{
	[Property] public Vector3 FootPosition { get; set; }
	[Property] public Color TrunkTint { get; set; } = new( 0.42f, 0.28f, 0.18f, 1f );
	[Property] public TreeSpecies Species { get; set; } = TreeSpecies.Beech;

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

		go.WorldScale = new Vector3( Tunables.StumpRadius * 2f, Tunables.StumpRadius * 2f, Tunables.StumpHeight ) / Tunables.CubeBase;

		var darker = new Color( tint.r * 0.7f, tint.g * 0.65f, tint.b * 0.55f, 1f );
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Models.StumpVariant( Math.Abs( footPosition.GetHashCode() ) );
		mr.Tint = darker;

		// Cut-line ring on top — légèrement plus foncé pour suggest "tronc coupé
		// vu d'au-dessus" silhouette. Child cube qui pose au sommet du stump.
		var cutLineGo = scene.CreateObject();
		cutLineGo.Name = "StumpCutLine";
		cutLineGo.SetParent( go );
		cutLineGo.LocalPosition = new Vector3( 0f, 0f, 0.55f );
		cutLineGo.LocalScale = new Vector3( 0.92f, 0.92f, 0.08f );
		var cutMr = cutLineGo.AddComponent<ModelRenderer>();
		cutMr.Model = Model.Cube;
		cutMr.Tint = new Color( tint.r * 0.55f, tint.g * 0.50f, tint.b * 0.42f, 1f );

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
