namespace TreeChopping;

public sealed class StoneInventory : Component
{
	[Property, ReadOnly] public int Stone { get; private set; }

	public static StoneInventory Get( Scene scene )
	{
		return scene?.GetAllComponents<StoneInventory>().FirstOrDefault();
	}

	public void Add( int amount )
	{
		if ( amount <= 0 ) return;
		Stone += amount;
	}
}
