namespace TreeChopping;

public enum BiomeKind
{
	Forest,
	Autumn,
	Frost,
}

public sealed class BiomeManager : Component
{
	[Property] public int TreesPerBiome { get; set; } = 30;
	[Property] public float PaletteTweenSeconds { get; set; } = 1.5f;
	[Property, ReadOnly] public BiomeKind Current { get; private set; } = BiomeKind.Forest;
	[Property, ReadOnly] public int TreesCleared { get; private set; }
	[Property, ReadOnly] public float TweenProgress { get; private set; } = 1f;

	private BiomeKind _previous;
	private Color _fromBank, _toBank;
	private Color _fromTrunk, _toTrunk;

	public static BiomeManager Get( Scene scene )
	{
		return scene?.GetAllComponents<BiomeManager>().FirstOrDefault();
	}

	public void NotifyTreeCleared()
	{
		TreesCleared++;
		if ( TreesCleared >= TreesPerBiome )
		{
			AdvanceBiome();
		}
	}

	public Color TrunkTintForNewTree()
	{
		return PaletteFor( Current ).Trunk;
	}

	// Weighted species roll for a new tree in the current biome. Caller owns
	// the Random so spawn-time determinism (seeded forest layouts, tests)
	// stays in the caller's hands. Weights:
	//   Forest = 60% Beech / 40% Spruce
	//   Autumn = 70% Ironwood / 20% Beech / 10% Spruce
	//   Frost  = 70% Crystal / 30% Spruce
	// All 4 species remain in the code; the bias just makes each biome read.
	public TreeSpecies SpeciesForNewTree( Random rng )
	{
		var roll = rng.NextDouble();
		switch ( Current )
		{
			case BiomeKind.Forest:
				return roll < 0.60 ? TreeSpecies.Beech : TreeSpecies.Spruce;
			case BiomeKind.Autumn:
				if ( roll < 0.70 ) return TreeSpecies.Ironwood;
				if ( roll < 0.90 ) return TreeSpecies.Beech;
				return TreeSpecies.Spruce;
			case BiomeKind.Frost:
				return roll < 0.70 ? TreeSpecies.Crystal : TreeSpecies.Spruce;
			default:
				return TreeSpecies.Beech;
		}
	}

	protected override void OnStart()
	{
		ApplyImmediate( Current );
	}

	protected override void OnUpdate()
	{
		if ( TweenProgress >= 1f ) return;
		TweenProgress = MathF.Min( 1f, TweenProgress + Time.Delta / PaletteTweenSeconds );
		var bank = Color.Lerp( _fromBank, _toBank, TweenProgress );
		ApplyBankTint( bank );
	}

	public void AdvanceBiome()
	{
		_previous = Current;
		Current = Current switch
		{
			BiomeKind.Forest => BiomeKind.Autumn,
			BiomeKind.Autumn => BiomeKind.Frost,
			BiomeKind.Frost => BiomeKind.Forest,
			_ => BiomeKind.Forest,
		};
		TreesCleared = 0;
		_fromBank = PaletteFor( _previous ).Bank;
		_toBank = PaletteFor( Current ).Bank;
		_fromTrunk = PaletteFor( _previous ).Trunk;
		_toTrunk = PaletteFor( Current ).Trunk;
		TweenProgress = 0f;
		Log.Info( $"[Biome] -> {Current}" );
	}

	public void ForceBiome( BiomeKind kind )
	{
		if ( kind == Current ) return;
		_previous = Current;
		Current = kind;
		TreesCleared = 0;
		_fromBank = PaletteFor( _previous ).Bank;
		_toBank = PaletteFor( Current ).Bank;
		_fromTrunk = PaletteFor( _previous ).Trunk;
		_toTrunk = PaletteFor( Current ).Trunk;
		TweenProgress = 0f;
		Log.Info( $"[Biome] forced -> {Current}" );
	}

	private void ApplyImmediate( BiomeKind kind )
	{
		ApplyBankTint( PaletteFor( kind ).Bank );
	}

	private void ApplyBankTint( Color tint )
	{
		var trunk = PaletteFor( Current ).Trunk;
		foreach ( var go in Scene.GetAllObjects( true ) )
		{
			if ( go.Tags.Has( "bank" ) )
			{
				var mr = go.Components.Get<ModelRenderer>();
				if ( mr.IsValid() ) mr.Tint = tint;
			}
			else if ( go.Tags.Has( "tree" ) || go.Tags.Has( "logpiece" ) )
			{
				var mr = go.Components.Get<ModelRenderer>();
				if ( mr.IsValid() ) mr.Tint = trunk;
			}
		}
	}

	private struct Palette
	{
		public Color Bank;
		public Color Trunk;
	}

	private static Palette PaletteFor( BiomeKind kind ) => kind switch
	{
		BiomeKind.Forest => new Palette { Bank = new Color( 0.35f, 0.49f, 0.27f, 1f ), Trunk = new Color( 0.42f, 0.30f, 0.18f, 1f ) },
		BiomeKind.Autumn => new Palette { Bank = new Color( 0.54f, 0.38f, 0.18f, 1f ), Trunk = new Color( 0.55f, 0.27f, 0.13f, 1f ) },
		BiomeKind.Frost  => new Palette { Bank = new Color( 0.78f, 0.86f, 0.92f, 1f ), Trunk = new Color( 0.50f, 0.42f, 0.36f, 1f ) },
		_ => new Palette { Bank = Color.Gray, Trunk = Color.Gray },
	};
}
