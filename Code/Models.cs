namespace TreeChopping;

// Central registry mapping logical names to s&box-native .vmdl paths.
// All paths target assets that ship pre-compiled in s&box's global cloud
// asset cache (`C:\Program Files (x86)\Steam\steamapps\common\sbox\download\assets\`)
// so they load via `Model.Load` without us authoring any source assets,
// without an editor import step, and without a custom GLB pipeline. This
// is the SOTA s&box approach: lean on what the platform already ships.
//
// Every load is wrapped in SafeLoad → fallback to Model.Cube, so even if
// the cloud cache hasn't fetched a specific asset yet the game still
// renders something instead of the magenta ERROR placeholder.
public static class Models
{
	private static readonly Dictionary<string, Model> _cache = new();

	public static Model Get( string relativeVmdlPath )
	{
		if ( _cache.TryGetValue( relativeVmdlPath, out var cached ) ) return cached;
		var m = SafeLoad( relativeVmdlPath ) ?? Model.Cube;
		_cache[relativeVmdlPath] = m;
		return m;
	}

	// Trees per species — mapped to s&box-cached models that READ like each
	// biome's flavour:
	//   Beech    → leafy deciduous oak (forest)
	//   Spruce   → tall cedar conifer
	//   Ironwood → bare dead tree, evokes autumn / fall
	//   Crystal  → cypress, slim conical (winter / frost stand-in)
	public static Model TreeFor( TreeSpecies species )
	{
		return species switch
		{
			TreeSpecies.Beech => Get( "models/sbox_props/trees/oak/tree_oak_medium_a.vmdl" ),
			TreeSpecies.Spruce => Get( "pr/gta5_prop_tree_cedar_s_01.vmdl" ),
			TreeSpecies.Ironwood => Get( "models/deadtree.vmdl" ),
			TreeSpecies.Crystal => Get( "pr/gta5_prop_tree_cypress_01.vmdl" ),
			_ => Get( "models/sbox_props/trees/oak/tree_oak_medium_a.vmdl" ),
		};
	}

	// Rock variant pool — caller picks an index from a per-spawn rng to vary
	// silhouettes without a per-instance string lookup. `rock_kit` ships 4
	// loose variants pre-compiled in the cloud cache, perfect for our minable
	// pool. Larger boulder silhouettes from the wider asset library back the
	// non-minable border-fence rocks.
	public static readonly string[] RockVariants =
	{
		"rock_kit/rock_1.vmdl",
		"rock_kit/rock_2.vmdl",
		"rock_kit/rock_3.vmdl",
		"rock_kit/rock_4.vmdl",
		"models/rocks/rock_01.vmdl",
		"models/rock_01/rock_01.vmdl",
		"models/rock/rock_05.vmdl",
	};

	public static Model RockVariant( int index )
	{
		return Get( RockVariants[((index % RockVariants.Length) + RockVariants.Length) % RockVariants.Length] );
	}

	// Stumps fall back to the dead-tree-trunk shape — s&box doesn't ship a
	// dedicated stump prop in the global cache. Index parameter is here for
	// symmetry with RockVariant in case a stump pool gets added later.
	public static Model StumpVariant( int index )
	{
		_ = index;
		return Get( "models/dead_tree_trunk.vmdl" );
	}

	public static Model Log => Get( "models/dead_tree_trunk.vmdl" );
	public static Model LogLarge => Get( "models/dead_tree_trunk.vmdl" );

	// Grass / foliage scatter pool spans desert-green to lush forest tones
	// so the bank reads varied without going chromatically wild.
	public static readonly string[] GrassVariants =
	{
		"models/sbox_props/nature/tallgrass/tallgrass_c.vmdl",
		"models/grass_models/grass_medium_a.vmdl",
		"models/desert_grass/desert_grass_green.vmdl",
		"models/forest/grass_3.vmdl",
		"models/rust_nature/bush_willow/bush_willow_a.vmdl",
	};

	public static Model GrassVariant( int index )
	{
		return Get( GrassVariants[((index % GrassVariants.Length) + GrassVariants.Length) % GrassVariants.Length] );
	}

	// No native beaver / axe in the cloud cache — keep cubes for those until
	// we either author them or find a workshop addon. Pick uses a TF2 prop
	// pick model that ships precompiled.
	public static Model Beaver => Model.Cube;
	public static Model Axe => Model.Cube;
	public static Model Pickaxe => Get( "models/props_2fort/pick001.vmdl" );
	public static Model Bird => Model.Cube;

	private static Model SafeLoad( string path )
	{
		try
		{
			var m = Model.Load( path );
			if ( m == null || m == Model.Error ) return null;
			return m;
		}
		catch
		{
			return null;
		}
	}
}
