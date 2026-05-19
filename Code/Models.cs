namespace TreeChopping;

// Central registry mapping logical names to .vmdl paths for the imported
// Kenney Nature Kit + Poly Pizza assets. Every load is wrapped in a safe
// fallback to Model.Cube so the game keeps running even when the editor
// hasn't compiled the .glb → .vmdl_c pair yet (e.g. fresh clone before
// first editor open, or headless validation in sbox-server). See
// Assets/models/ATTRIBUTION.md for licenses.
public static class Models
{
	public const string KenneyDir = "models/kenney_nature_kit/";
	public const string ToolsDir = "models/poly_pizza_tools/";

	// Cached so we don't go through the asset system on every spawn —
	// SafeLoad's exception-handling path is expensive in tight loops.
	private static readonly Dictionary<string, Model> _cache = new();

	public static Model Get( string relativeVmdlPath )
	{
		if ( _cache.TryGetValue( relativeVmdlPath, out var cached ) ) return cached;
		var m = SafeLoad( relativeVmdlPath ) ?? Model.Cube;
		_cache[relativeVmdlPath] = m;
		return m;
	}

	public static Model TreeFor( TreeSpecies species )
	{
		return species switch
		{
			TreeSpecies.Beech => Get( KenneyDir + "tree_oak.vmdl" ),
			TreeSpecies.Spruce => Get( KenneyDir + "tree_pineDefaultA.vmdl" ),
			TreeSpecies.Ironwood => Get( KenneyDir + "tree_oak_fall.vmdl" ),
			TreeSpecies.Crystal => Get( KenneyDir + "tree_pineRoundC.vmdl" ),
			_ => Get( KenneyDir + "tree_oak.vmdl" ),
		};
	}

	// Rock variant pool — caller picks an index based on a per-spawn rng to
	// vary silhouettes across the bank without a per-instance string lookup.
	public static readonly string[] RockVariants =
	{
		KenneyDir + "rock_smallA.vmdl",
		KenneyDir + "rock_smallB.vmdl",
		KenneyDir + "rock_smallC.vmdl",
		KenneyDir + "rock_smallD.vmdl",
		KenneyDir + "rock_smallE.vmdl",
		KenneyDir + "rock_smallF.vmdl",
		KenneyDir + "rock_smallG.vmdl",
	};

	public static Model RockVariant( int index )
	{
		return Get( RockVariants[((index % RockVariants.Length) + RockVariants.Length) % RockVariants.Length] );
	}

	public static readonly string[] StumpVariants =
	{
		KenneyDir + "stump_round.vmdl",
		KenneyDir + "stump_old.vmdl",
		KenneyDir + "stump_square.vmdl",
	};

	public static Model StumpVariant( int index )
	{
		return Get( StumpVariants[((index % StumpVariants.Length) + StumpVariants.Length) % StumpVariants.Length] );
	}

	public static Model Log => Get( KenneyDir + "log.vmdl" );
	public static Model LogLarge => Get( KenneyDir + "log_large.vmdl" );

	public static readonly string[] GrassVariants =
	{
		KenneyDir + "grass.vmdl",
		KenneyDir + "grass_large.vmdl",
		KenneyDir + "grass_leafs.vmdl",
		KenneyDir + "plant_bush.vmdl",
		KenneyDir + "plant_bushSmall.vmdl",
	};

	public static Model GrassVariant( int index )
	{
		return Get( GrassVariants[((index % GrassVariants.Length) + GrassVariants.Length) % GrassVariants.Length] );
	}

	public static Model Beaver => Get( ToolsDir + "beaver_googlepoly.vmdl" );
	public static Model Axe => Get( ToolsDir + "axe_kenney.vmdl" );
	public static Model Pickaxe => Get( ToolsDir + "pickaxe_creativetrio.vmdl" );
	public static Model Bird => Get( ToolsDir + "bird_pigeon.vmdl" );

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
