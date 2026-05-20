namespace TreeChopping;

// Low-poly nature pack (titanovsky.low_poly_tree + titanovsky.low_poly_rock).
// Subscribed via PackageReferences in tree_chopping.sbproj; sbox-dev DLs the
// .vmdl_c on project open. Paths confirmed empirically 2026-05-19:
//   models/low_poly_tree.vmdl  → bounds 114×124×227u  (~5.75m tall)
//   models/low_poly_rock.vmdl  → bounds 41×32×45u    (~1m cube)
// SafeLoad detects unresolved assets (engine returns a placeholder with
// bounds (10,100,25)) and falls back to Model.Cube so the game stays
// playable if the pack is unsubscribed.
public static class Models
{
	private static readonly Dictionary<string, Model> _cache = new();
	private static readonly Vector3 _placeholderBounds = new( 10, 100, 25 );

	public static Model TreeFor( TreeSpecies species ) => Get( "models/low_poly_tree.vmdl" );
	public static Model RockVariant( int index ) => Get( "models/low_poly_rock.vmdl" );

	public static Model StumpVariant( int index ) => Model.Cube;
	public static Model LogTrunk => Model.Cube;
	public static Model LogLarge => Model.Cube;
	public static Model GrassVariant( int index ) => Model.Cube;
	// Beaver body silhouette — Kenney Cube Pets animal-beaver.glb (CC0). Sits
	// in Assets/models/, compiled by sbox-dev to models/animal-beaver.vmdl on
	// first project open. `Get` falls back to Model.Cube via SafeLoad if the
	// editor hasn't compiled the .glb yet — re-open the project once after
	// dropping the file in.
	public static Model Beaver => Get( "models/animal-beaver.vmdl" );
	public static Model Axe => Model.Cube;
	public static Model Bird => Model.Cube;

	private static Model Get( string path )
	{
		if ( _cache.TryGetValue( path, out var cached ) ) return cached;
		var m = SafeLoad( path ) ?? Model.Cube;
		_cache[path] = m;
		return m;
	}

	private static Model SafeLoad( string path )
	{
		try
		{
			var m = Model.Load( path );
			if ( m is null || m == Model.Error ) return null;
			var sz = m.Bounds.Size;
			if ( sz.Length <= 1f || (sz - _placeholderBounds).Length < 0.5f )
			{
				Log.Warning( $"[Models] FAIL {path} (placeholder bounds={sz}, falling back to Model.Cube)" );
				return null;
			}
			return m;
		}
		catch ( System.Exception ex )
		{
			Log.Warning( $"[Models] EXC {path}: {ex.Message}" );
			return null;
		}
	}

	public static void AuditCandidatePaths()
	{
		string[] paths =
		{
			"models/low_poly_tree.vmdl",
			"models/low_poly_rock.vmdl",
		};
		int ok = 0, fail = 0;
		foreach ( var p in paths )
		{
			Model m = null;
			try { m = Model.Load( p ); } catch { }
			var sz = m?.Bounds.Size ?? Vector3.Zero;
			bool looksPlaceholder = (sz - _placeholderBounds).Length < 0.5f;
			bool resolved = m != null && m != Model.Error && sz.Length > 1f && !looksPlaceholder;
			if ( resolved ) { ok++; Log.Info( $"[ModelAudit] OK   {p} bounds={sz}" ); }
			else { fail++; Log.Info( $"[ModelAudit] FAIL {p} bounds={sz} placeholder={looksPlaceholder}" ); }
		}
		Log.Info( $"[ModelAudit] summary: {ok} ok, {fail} fail, {paths.Length} total" );
	}
}
