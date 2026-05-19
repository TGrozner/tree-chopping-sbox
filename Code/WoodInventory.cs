namespace TreeChopping;

public sealed class WoodInventory : Component
{
	[Property, ReadOnly] public int Wood { get; private set; }

	// Const expression-bodied so hotload picks up future tunings without an
	// editor restart (auto-property defaults are baked at type init).
	public int Cap => Tunables.BackpackCap;
	public bool IsFull => Wood >= Cap;

	public static WoodInventory Get( Scene scene )
	{
		return scene?.GetAllComponents<WoodInventory>().FirstOrDefault();
	}

	// Returns true only when the requested amount was accepted in full — partial
	// fills (cap hit mid-pickup) still bank what fits but report false so callers
	// can surface a "backpack full" cue.
	public bool Add( int amount )
	{
		if ( amount <= 0 ) return false;
		var room = Cap - Wood;
		if ( room <= 0 ) return false;
		var taken = Math.Min( amount, room );
		Wood += taken;
		return taken == amount;
	}
}
