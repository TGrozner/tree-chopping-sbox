namespace TreeChopping;

public sealed class WoodInventory : Component
{
	[Property, ReadOnly] public int Wood { get; private set; }

	public static WoodInventory Get( Scene scene )
	{
		return scene?.GetAllComponents<WoodInventory>().FirstOrDefault();
	}

	public void Add( int amount )
	{
		if ( amount <= 0 ) return;
		Wood += amount;
	}
}
