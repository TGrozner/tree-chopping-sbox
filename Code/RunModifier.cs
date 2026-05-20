namespace TreeChopping;

public enum RunModifier
{
	Standard,
	Explosive,
	Frozen,
	Heavy,
	ChainLightning,
}

public static class RunModifierExt
{
	public static string DisplayName( this RunModifier m ) => m switch
	{
		RunModifier.Standard => "Standard",
		RunModifier.Explosive => "EXPLOSIVE AXE",
		RunModifier.Frozen => "FROZEN BLAST",
		RunModifier.Heavy => "HEAVY STRIKE",
		RunModifier.ChainLightning => "CHAIN LIGHTNING",
		_ => "—",
	};

	public static Color Tint( this RunModifier m ) => m switch
	{
		RunModifier.Standard => new Color( 0.95f, 0.95f, 0.95f, 1f ),
		RunModifier.Explosive => new Color( 1.0f, 0.55f, 0.18f, 1f ),
		RunModifier.Frozen => new Color( 0.42f, 0.82f, 1.0f, 1f ),
		RunModifier.Heavy => new Color( 0.80f, 0.40f, 1.0f, 1f ),
		RunModifier.ChainLightning => new Color( 1.0f, 0.95f, 0.30f, 1f ),
		_ => Color.White,
	};

	public static string ShortHint( this RunModifier m ) => m switch
	{
		RunModifier.Standard => "Aim for clusters.",
		RunModifier.Explosive => "AOE blast on first chop.",
		RunModifier.Frozen => "Cascade radiates 360°.",
		RunModifier.Heavy => "Each felled tree × 3 score.",
		RunModifier.ChainLightning => "Cascade jumps to far trees.",
		_ => "",
	};
}
