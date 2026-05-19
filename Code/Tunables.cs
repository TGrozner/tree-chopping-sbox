namespace TreeChopping;

public static class Tunables
{
	public const float UnitsPerMeter = 39.37f;

	public const float BeaverMoveSpeed = 240f;
	public const float BeaverSprintMultiplier = 1.75f;
	public const float BeaverJumpImpulse = 320f;
	public const float BeaverEyeHeight = 56f;
	public const float CameraDistance = 200f;
	public const float CameraHeightAboveBeaver = 90f;
	public const float CameraMinPitch = -75f;
	public const float CameraMaxPitch = 55f;

	public const float TreeHeight = 280f;
	public const float TreeRadius = 16f;
	public const float TreeMass = 240f;
	public const float TreeMaxChops = 3;
	public const float FellTorque = 90000f;
	public const float FellPush = 2400f;
	public const float SlowTipInitialFrac = 0.05f;
	public const float SlowTipRampFrac = 0.30f;
	public const float SlowTipDuration = 0.45f;
	public const float TreeAngularDampLanded = 1.5f;
	public const float TreeLinearDampLanded = 0.6f;

	public const float SwingRange = 90f;
	public const float SwingConeDot = 0.45f;
	public const float SwingCooldown = 0.33f;

	public const float LogBreakHits = 2;
	public const float LogPieceHeight = 120f;
	public const float LogPieceRadius = 14f;
	public const float LogPieceMass = 40f;
	public const float ChunkHeight = 18f;
	public const float ChunkRadius = 7f;
	public const float ChunkMass = 5f;
	public const int ChunksPerLogPiece = 4;

	public const float PickupRadius = 48f;
	public const float PickupLerpSpeed = 16f;

	public const float TreeFallenUpDotMax = 0.6f;
}
