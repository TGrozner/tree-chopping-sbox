namespace TreeChopping;

// Autonomous 360° camera yaw rotation for headless / autonomous Play
// validation. SetCursorPos in the Play viewport does NOT generate HID-level
// mouse delta events on this install (see [[sbox-setcursorpos-no-input]]),
// so we drive BeaverController.DebugSetYaw directly each fixed tick.
//
// Activation: launch with "+tc_autospin 1". Spawned by SceneStarter when the
// ConVar is on. Spins at 90°/s for 4s (covers 360°), then logs "[AutoSpin]
// DONE" and stops touching yaw — Play stays running so an external harness
// can keep capturing if it wants.
public sealed class AutoSpin : Component
{
	[ConVar( "tc_autospin", Help = "Spawn AutoSpin to drive a deterministic 360° yaw rotation in Play." )]
	public static bool Enable { get; set; }

	public static bool IsActiveRequest() => Enable;

	private const float YawDegreesPerSecond = 90f;
	private const float SpinDurationSeconds = 4f;

	private BeaverController _beaver;
	private float _startYaw;
	private TimeSince _start;
	private bool _done;

	protected override void OnAwake()
	{
		Log.Info( "[AutoSpin] component awake — driving DebugSetYaw at 90°/s for 4s" );
	}

	protected override void OnFixedUpdate()
	{
		if ( _done ) return;
		_beaver ??= Scene.GetAllComponents<BeaverController>().FirstOrDefault();
		if ( !_beaver.IsValid() ) return;

		if ( (float)_start == 0f )
		{
			_startYaw = _beaver.DebugCameraAngles.yaw;
			_start = 0f;
			Log.Info( $"[AutoSpin] start yaw={_startYaw:F1}" );
		}

		float elapsed = _start;
		if ( elapsed >= SpinDurationSeconds )
		{
			_done = true;
			Log.Info( $"[AutoSpin] DONE after {elapsed:F2}s, total yaw delta={elapsed * YawDegreesPerSecond:F1}°" );
			return;
		}

		float yaw = _startYaw + elapsed * YawDegreesPerSecond;
		_beaver.DebugSetYaw( yaw );
	}
}
