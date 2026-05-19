namespace TreeChopping;

public sealed class StoneInventory : Component
{
	[Property, ReadOnly] public int Stone { get; private set; }

	public int Cap => Tunables.BackpackCap;
	public bool IsFull => Stone >= Cap;

	public static StoneInventory Get( Scene scene )
	{
		return scene?.GetAllComponents<StoneInventory>().FirstOrDefault();
	}

	public bool Add( int amount )
	{
		if ( amount <= 0 ) return false;
		var room = Cap - Stone;
		if ( room <= 0 ) return false;
		var taken = Math.Min( amount, room );
		Stone += taken;
		return taken == amount;
	}
}
