namespace TreeChopping;

// Valheim HitData (HitData.cs lignes 651-700) port simplifié. Encapsule tous
// les params d'un hit dans une struct unique — direction, hit point, push
// force (= chopPower notre cas), tool tier, skill, attacker. Sert d'interop
// universel pour IChoppable.Damage(HitData) (= Valheim IDestructible.Damage).
//
// Valheim fields portés :
//   - m_dir       → Direction
//   - m_point     → HitPoint
//   - m_pushForce → ChopPower (notre version, scale damage + impulse)
//   - m_toolTier  → ToolTier (CheckToolTier gate)
//   - m_skill     → Skill (raise XP)
//
// Fields Valheim non-portés (n/a notre game) :
//   - m_damage (DamageTypes blunt/slash/pierce/chop/fire) — single chop type
//   - m_backstabBonus, m_staggerMultiplier — no combat staggers
//   - m_hitType (EnemyHit/PlayerHit/Fall/Impact) — context implicit
//   - m_attacker (ZDOID) — singleplayer, no network
public struct HitData
{
	public Vector3 Direction;
	public Vector3 HitPoint;
	public int ChopPower;
	public int ToolTier;
	public WoodCuttingSkill Skill;

	// Factory pour les call sites qui ont déjà séparé les params (legacy
	// pattern) — évite rewrites massifs.
	public static HitData Make( Vector3 direction, int chopPower, Vector3 hitPoint, int toolTier = 0 )
	{
		return new HitData
		{
			Direction = direction,
			ChopPower = chopPower,
			HitPoint = hitPoint,
			ToolTier = toolTier,
			Skill = WoodCuttingSkill.None,
		};
	}
}

// Valheim Skills.SkillType — pour l'instant, seul WoodCutting nous concerne.
// Pourrait s'étendre (Axes, Pickaxes, etc.) si on ajoute des outils variés.
public enum WoodCuttingSkill { None, WoodCutting }
