namespace TreeChopping;

// Valheim HitData port for the tree slice. We keep only the fields that change
// tree/log feel: direction, point, damage channels, push force, tool tier.
public struct HitData
{
	public Vector3 Direction;
	public Vector3 HitPoint;
	public int ChopPower;
	public DamageTypes Damage;
	public float PushForce;
	public int ToolTier;
	public WoodCuttingSkill Skill;

	public static HitData Make( Vector3 direction, int chopPower, Vector3 hitPoint, int toolTier = 0, float pushForce = -1f )
	{
		return new HitData
		{
			Direction = direction,
			ChopPower = chopPower,
			Damage = DamageTypes.ChopOnly( chopPower ),
			PushForce = pushForce >= 0f ? pushForce : Tunables.LandedLogKickImpulse,
			HitPoint = hitPoint,
			ToolTier = toolTier,
			Skill = WoodCuttingSkill.None,
		};
	}

	public static HitData MakeImpact( Vector3 direction, Vector3 hitPoint, int toolTier, float scale, float damageMul = 1f )
	{
		scale = scale.Clamp( 0f, 1f );
		float mul = MathF.Max( 0f, damageMul );
		var damage = new DamageTypes
		{
			Chop = Tunables.ImpactChopDamage * scale * mul,
			Blunt = Tunables.ImpactBluntDamage * scale * mul,
		};
		return new HitData
		{
			Direction = direction,
			HitPoint = hitPoint,
			ToolTier = toolTier,
			Damage = damage,
			ChopPower = damage.GetTotalDamage( TreeLogDamageModifiers ),
			PushForce = 0f,
			Skill = WoodCuttingSkill.None,
		};
	}

	public int GetTreeDamage() => Damage.GetTotalDamage( TreeDamageModifiers );
	public int GetTreeLogDamage() => Damage.GetTotalDamage( TreeLogDamageModifiers );

	public static readonly DamageModifiers TreeDamageModifiers = DamageModifiers.TreeLike();
	public static readonly DamageModifiers TreeLogDamageModifiers = DamageModifiers.TreeLike();
}

public struct DamageTypes
{
	public float Chop;
	public float Blunt;

	public static DamageTypes ChopOnly( float amount ) => new() { Chop = amount };

	public bool HaveDamage => Chop > 0f || Blunt > 0f;

	public int GetTotalDamage( DamageModifiers modifiers )
	{
		float total =
			ApplyModifier( Chop, modifiers.Chop ) +
			ApplyModifier( Blunt, modifiers.Blunt );
		if ( total <= 0f ) return 0;
		return Math.Max( 1, (int)MathF.Ceiling( total ) );
	}

	private static float ApplyModifier( float damage, DamageModifier modifier )
	{
		if ( damage <= 0f ) return 0f;
		return modifier switch
		{
			DamageModifier.Immune => 0f,
			DamageModifier.Resistant => damage * 0.5f,
			DamageModifier.Weak => damage * 1.5f,
			_ => damage,
		};
	}
}

public struct DamageModifiers
{
	public DamageModifier Chop;
	public DamageModifier Blunt;

	public static DamageModifiers TreeLike()
	{
		return new DamageModifiers
		{
			Chop = DamageModifier.Normal,
			Blunt = DamageModifier.Immune,
		};
	}
}

public enum DamageModifier { Normal, Resistant, Weak, Immune }

public enum WoodCuttingSkill { None, WoodCutting }
