namespace TreeChopping;

// Phase B helper — Material.Load("materials/default.vmat") respects
// ModelRenderer.Tint, while the baked dev material on Model.Cube/Sphere does
// not (cf. memory sbox-model-cube-ignores-tint). Set MaterialOverride to this
// on any primitive renderer that needs to show its tint color.
//
// Material.Load is internally cached by the engine, so calls are cheap.
public static class Mat
{
	private static Material _default;

	public static Material Default
	{
		get
		{
			if ( _default is null )
			{
				_default = Material.Load( "materials/default.vmat" );
			}
			return _default;
		}
	}

	// Convenience: build a tinted ModelRenderer on the given GameObject.
	public static ModelRenderer AddTintedCube( GameObject go, Color tint )
	{
		var mr = go.AddComponent<ModelRenderer>();
		mr.Model = Model.Cube;
		mr.MaterialOverride = Default;
		mr.Tint = tint;
		return mr;
	}
}
