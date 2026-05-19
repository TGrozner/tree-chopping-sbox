namespace TreeChopping;

// Central registry mapping logical names to s&box-native .vmdl paths.
//
// We exclusively reference assets that the current install resolves WITHOUT
// any PackageReference in .sbproj — i.e. content that's globally mounted
// (tested empirically via the editor log on 2026-05-19). Adding more
// packs (`facepunch.sbox_props`, etc) would unlock the wider asset.party
// catalog, but for now this lean set is enough to ditch the cube look:
//   - `pr/...`        : GTA-port tree props auto-mounted
//   - `rock_kit/...`  : 4 rock variants auto-mounted
//   - `models/dead*`  : dead-tree models in the global cache
//
// Anything else falls back to Model.Cube via SafeLoad. When a future
// asset pack is added, just point the relevant lookup at its path and
// the rest of the code keeps working.
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

	// Tree species → real models. Two species share the dead-tree look but
	// with different tints applied downstream (species tint multiplies onto
	// the mesh's vertex colour), so silhouette + tint still distinguishes
	// them at a glance. Beech and Spruce get the two conifer models we have.
	public static Model TreeFor( TreeSpecies species )
	{
		return species switch
		{
			TreeSpecies.Beech => Get( "pr/gta5_prop_tree_cypress_01.vmdl" ),
			TreeSpecies.Spruce => Get( "pr/gta5_prop_tree_cedar_s_01.vmdl" ),
			TreeSpecies.Ironwood => Get( "models/deadtree.vmdl" ),
			TreeSpecies.Crystal => Get( "models/dead_tree_trunk.vmdl" ),
			_ => Get( "pr/gta5_prop_tree_cypress_01.vmdl" ),
		};
	}

	// Rocks — 4 native variants. Caller indexes by per-spawn hash.
	public static readonly string[] RockVariants =
	{
		"rock_kit/rock_1.vmdl",
		"rock_kit/rock_2.vmdl",
		"rock_kit/rock_3.vmdl",
		"rock_kit/rock_4.vmdl",
	};

	public static Model RockVariant( int index )
	{
		return Get( RockVariants[((index % RockVariants.Length) + RockVariants.Length) % RockVariants.Length] );
	}

	public static Model StumpVariant( int index )
	{
		_ = index;
		return Get( "models/dead_tree_trunk.vmdl" );
	}

	public static Model Log => Get( "models/dead_tree_trunk.vmdl" );
	public static Model LogLarge => Get( "models/dead_tree_trunk.vmdl" );

	// Grass / foliage scatter: no good native mesh ships in the loose-mount
	// set. Stays on Model.Cube + tint until a foliage pack is referenced
	// via PackageReferences in tree_chopping.sbproj.
	public static Model GrassVariant( int index )
	{
		_ = index;
		return Model.Cube;
	}

	// Player + tool meshes — no native equivalents in the loose-mount set,
	// stay on cubes until a workshop pack is added.
	public static Model Beaver => Model.Cube;
	public static Model Axe => Model.Cube;
	public static Model Pickaxe => Model.Cube;
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
