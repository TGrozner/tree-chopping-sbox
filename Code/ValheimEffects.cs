namespace TreeChopping;

public enum ValheimEffectListId
{
	AttackStart,
	AxeHit,
	TooHard,
	TreeDestroyed,
	TreeBreakYield,
	TreeGroan,
	TreeWhoosh,
	LeafShed,
	LogImpact,
	LogLandingHard,
	LogDestroyed,
	SmallerLogsSpawned,
	DropChip,
}

public static class ValheimEffects
{
	public static bool HasList( ValheimEffectListId id )
		=> id >= ValheimEffectListId.AttackStart && id <= ValheimEffectListId.DropChip;

	public static void AttackStart( int chainLevel )
	{
		float swingPitchMul = 1f + 0.06f * chainLevel;
		Sfx.PlayLocal( "sounds/swing.sound",
			volume: 0.92f, pitchMin: 1.08f * swingPitchMul, pitchMax: 1.28f * swingPitchMul );
	}

	public static void AxeHit( Scene scene, Vector3 point, Vector3 dir, Color? tint, int chipCount, float pitchMul, float logPitch, float volume, bool heavy )
	{
		ChipBurst.Spawn( scene, point, dir, chipCount, tint );
		float hitVol = volume;
		float biteVol = volume * 0.42f;
		Sfx.PlayLocal( "sounds/axe_hit_wood.sound", volume: hitVol, pitchMin: 0.88f * pitchMul * logPitch, pitchMax: 1.02f * pitchMul * logPitch );
		Sfx.PlayLocal( "sounds/chop_wood.sound", volume: biteVol, pitchMin: 0.95f * pitchMul * logPitch, pitchMax: 1.18f * pitchMul * logPitch );
		Sfx.Play( "sounds/axe_hit_wood.sound", point, volume: hitVol * 0.20f, pitchMin: 0.88f * pitchMul * logPitch, pitchMax: 1.02f * pitchMul * logPitch );
		if ( !heavy ) return;
		Sfx.PlayLocal( "sounds/log_break.sound", volume: volume * 0.13f, pitchMin: 1.18f * pitchMul, pitchMax: 1.34f * pitchMul );
		Sfx.Play( "sounds/log_break.sound", point, volume: volume * 0.10f, pitchMin: 1.18f * pitchMul, pitchMax: 1.34f * pitchMul );
	}

	public static void TooHard( Scene scene, Vector3 point, Vector3 dir )
	{
		ChipBurst.Spawn( scene, point, dir, Tunables.ChipBurstCount / 2, new Color( 0.55f, 0.50f, 0.42f, 1f ) );
		Sfx.PlayLocal( "sounds/axe_too_weak.sound", volume: 0.72f, pitchMin: 0.75f, pitchMax: 0.95f );
		Sfx.PlayLocal( "sounds/axe_hit_wood.sound", volume: 0.34f, pitchMin: 0.55f, pitchMax: 0.70f );
		Sfx.Play( "sounds/axe_too_weak.sound", point, volume: 0.22f, pitchMin: 0.75f, pitchMax: 0.95f );
	}

	public static void TreeDestroyed( Scene scene, Vector3 canopyPos, Vector3 dir, Color canopyTint )
	{
		ChipBurst.SpawnLeaves( scene, canopyPos, dir, 52, canopyTint );
		ChipBurst.SpawnLeaves( scene, canopyPos, Vector3.Up, 34, canopyTint );
		ChipBurst.SpawnLeaves( scene, canopyPos, -dir, 24, canopyTint );
	}

	public static void TreeBreakYield( Scene scene, Vector3 point, Vector3 dir, Color trunkTint, TreeKind kind )
	{
		ChipBurst.Spawn( scene, point, dir, Tunables.ChipBurstCount + Tunables.ChipBurstCount / 2, trunkTint );
		ChipBurst.SpawnLeaves( scene, point + Vector3.Up * 12f, dir, 10, new Color( 0.52f, 0.40f, 0.28f, 1f ) );
		float pitchMul = Tunables.TreeKindGroanPitchMul[(int)kind];
		Sfx.Play( "sounds/log_break.sound", point,
			volume: 0.28f, pitchMin: 0.92f * pitchMul, pitchMax: 1.10f * pitchMul );
	}

	public static void TreeGroan( Vector3 point, TreeKind kind )
	{
		float pitchMul = Tunables.TreeKindGroanPitchMul[(int)kind];
		Sfx.Play( "sounds/tree_groan.sound", point,
			volume: 0.52f, pitchMin: 0.55f * pitchMul, pitchMax: 0.72f * pitchMul );
	}

	public static void TreeWhoosh( Vector3 point, TreeKind kind )
	{
		float pitchMul = Tunables.TreeKindChopPitchMul[(int)kind];
		Sfx.Play( "sounds/tree_fall_whoosh.sound", point,
			volume: 0.55f, pitchMin: 0.55f * pitchMul, pitchMax: 0.75f * pitchMul );
	}

	public static void LeafShed( Scene scene, Vector3 point, Vector3 dir, int count, Color tint )
	{
		ChipBurst.SpawnLeaves( scene, point, dir, count, tint );
		float rustleVol = (count / 6f).Clamp( 0.025f, 0.065f );
		Sfx.Play( "sounds/leaves_rustle.sound", point, volume: rustleVol, pitchMin: 0.85f, pitchMax: 1.15f );
	}

	public static void LogImpact( Scene scene, Vector3 point, Vector3 preCollisionVelocity, Color trunkTint, float softScale, float damageScale )
	{
		bool violent = damageScale >= Tunables.ImpactViolentScale;
		bool hard = damageScale >= Tunables.ImpactHardScale;
		float vol = hard ? 0.70f + damageScale * 0.45f : 0.32f + softScale * 0.35f;
		Sfx.Play( hard ? "sounds/log_break.sound" : "sounds/axe_hit_wood.sound", point,
			volume: vol,
			pitchMin: violent ? 0.50f : (hard ? 0.62f : 0.72f),
			pitchMax: violent ? 0.68f : (hard ? 0.84f : 0.95f) );
		int dustCount = hard ? 8 + (int)(damageScale * 18f) : 2 + (int)(softScale * 5f);
		var dustTint = hard
			? new Color( 0.62f, 0.48f, 0.35f, 1f )
			: new Color( 0.48f, 0.42f, 0.34f, 1f );
		ChipBurst.SpawnLeaves( scene, point, Vector3.Up, dustCount, dustTint );
		if ( !violent ) return;
		var sideDir = preCollisionVelocity.WithZ( 0f );
		if ( sideDir.LengthSquared > 0.01f )
			ChipBurst.SpawnLeaves( scene, point, sideDir.Normal, dustCount / 2, trunkTint );
	}

	public static void LogLandingHard( Scene scene, Vector3 point, float damageScale )
	{
		var dustTint = new Color( 0.62f, 0.48f, 0.35f, 1f );
		int dustCount = (int)(10 + damageScale * 16f);
		ChipBurst.SpawnLeaves( scene, point + Vector3.Up * 8f, Vector3.Up, dustCount, dustTint );
	}

	public static void LogDestroyed( Scene scene, Vector3 point, Color? leafTint = null )
	{
		Sfx.Play( "sounds/log_break.sound", point, volume: 1.0f, pitchMin: 0.62f, pitchMax: 0.82f );
		if ( leafTint.HasValue )
			ChipBurst.SpawnLeaves( scene, point, Vector3.Up, 18, leafTint.Value );
	}

	public static void SmallerLogsSpawned( Vector3 point )
	{
		Sfx.Play( "sounds/log_break.sound", point, volume: 0.95f, pitchMin: 0.58f, pitchMax: 0.78f );
	}

	public static void DropChip( Scene scene, Vector3 point, Vector3 dir, Color tint, int count = 2 )
	{
		ChipBurst.Spawn( scene, point, dir, count, tint );
	}
}
