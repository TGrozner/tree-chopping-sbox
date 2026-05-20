namespace TreeChopping;

// Always-on rolling-window FPS probe. Exposes FpsAvg / FpsMin / Renderers /
// Trees as [Property, ReadOnly] so the MCP bridge can poll them without
// touching the DebugToggle HUD path. Used to diagnose the impact of the
// 2-tone canopy / tapered trunk changes that doubled the cube count.
public sealed class PerfProbe : Component
{
	[Property, ReadOnly] public float FpsAvg { get; private set; }
	[Property, ReadOnly] public float FpsMin { get; private set; }
	[Property, ReadOnly] public int Renderers { get; private set; }
	[Property, ReadOnly] public int Trees { get; private set; }

	private const int WindowFrames = 120;
	private readonly float[] _dt = new float[WindowFrames];
	private int _idx;
	private TimeSince _sinceCountScan = 999f;

	protected override void OnUpdate()
	{
		_dt[_idx % WindowFrames] = Time.Delta;
		_idx++;
		int n = _idx < WindowFrames ? _idx : WindowFrames;
		float sum = 0f, mx = 0f;
		for ( int i = 0; i < n; i++ )
		{
			var dt = _dt[i];
			sum += dt;
			if ( dt > mx ) mx = dt;
		}
		float avg = sum / (n > 0 ? n : 1);
		FpsAvg = 1f / avg.Clamp( 1e-4f, 1f );
		FpsMin = 1f / mx.Clamp( 1e-4f, 1f );

		// Component scans are heavier (linear in scene size) — refresh once
		// per second instead of every frame.
		if ( (float)_sinceCountScan > 1f )
		{
			Renderers = Scene?.GetAllComponents<ModelRenderer>().Count() ?? 0;
			Trees = Scene?.GetAllComponents<Tree>().Count() ?? 0;
			_sinceCountScan = 0f;
		}
	}
}
