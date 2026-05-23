namespace TreeChopping;

// Valheim HitData (HitData.cs lignes 651-700) port simplifié. Encapsule tous
// les params d'un hit dans une struct unique — direction, hit point, damage,
// push force, tool tier, skill, attacker. Sert d'interop
// universel pour IChoppable.Damage(HitData) (= Valheim IDestructible.Damage).
//
// Valheim fields portés :
//   - m_dir       → Direction
//   - m_point     → HitPoint
//   - m_damage    → ChopPower (single chop damage channel)
//   - m_pushForce → PushForce (impulse only; kept separate from damage)
//   - m_toolTier  → ToolTier (CheckToolTier gate)
//   - m_skill     → Skill (raise XP)
//
// Fields Valheim non-portés (n/a notre game) :
//   - per-type DamageTypes blunt/slash/pierce/chop/fire — single chop channel
//   - m_backstabBonus, m_staggerMultiplier — no combat staggers
//   - m_hitType (EnemyHit/PlayerHit/Fall/Impact) — context implicit
//   - m_attacker (ZDOID) — singleplayer, no network
public struct HitData
{
	public Vector3 Direction;
	public Vector3 HitPoint;
	public int ChopPower;
	public float PushForce;
	public int ToolTier;
	public WoodCuttingSkill Skill;

	// Factory pour les call sites qui ont déjà séparé les params (legacy
	// pattern) — évite rewrites massifs.
	public static HitData Make( Vector3 direction, int chopPower, Vector3 hitPoint, int toolTier = 0, float pushForce = -1f )
	{
		return new HitData
		{
			Direction = direction,
			ChopPower = chopPower,
			PushForce = pushForce >= 0f ? pushForce : Tunables.LandedLogKickImpulse,
			HitPoint = hitPoint,
			ToolTier = toolTier,
			Skill = WoodCuttingSkill.None,
		};
	}
}

// Valheim Skills.SkillType — pour l'instant, seul WoodCutting nous concerne.
// Pourrait s'étendre (Axes, Pickaxes, etc.) si on ajoute des outils variés.
public enum WoodCuttingSkill { None, WoodCutting }
