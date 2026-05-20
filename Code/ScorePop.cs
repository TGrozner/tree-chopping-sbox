namespace TreeChopping;

// Floating world-space "+N" text that drifts up + fades out. Spawned by
// Tree.StartFell so every cascade kill leaves a popping number. Pure visual
// — no collider, no rb, self-destructs after lifetime.
public sealed class ScorePop : Component
{
	[Property] public string Text { get; set; } = "+1";
	[Property] public Color Tint { get; set; } = new( 1f, 0.92f, 0.55f, 1f );
	[Property] public float Lifetime { get; set; } = 1.4f;
	[Property] public float RiseSpeed { get; set; } = 75f;
	[Property] public float FontSize { get; set; } = 48f;

	private TimeSince _born;
	private Vector3 _spawnPos;

	public static void Spawn( Scene scene, Vector3 worldPos, string text, Color tint )
	{
		var go = scene.CreateObject();
		go.Name = "ScorePop";
		go.WorldPosition = worldPos;
		var pop = go.AddComponent<ScorePop>();
		pop.Text = text;
		pop.Tint = tint;
		// Plus le nombre est gros, plus le pop est gros — +1 reste discret,
		// +50 lit comme un milestone, +200 explose. Length proxy : +N a 2 chars,
		// +50 a 3, +500 a 4. Scale lineaire 42 → 96 sur 5 chars max.
		int extraChars = Math.Clamp( text.Length - 2, 0, 4 );
		pop.FontSize = 42f + extraChars * 13.5f;
		pop.Lifetime = 1.2f + extraChars * 0.25f; // gros pops restent visibles plus longtemps
	}

	protected override void OnStart()
	{
		_born = 0f;
		_spawnPos = WorldPosition;
	}

	protected override void OnUpdate()
	{
		if ( _born > Lifetime ) { GameObject.Destroy(); return; }

		// Float up + drift slightly.
		WorldPosition = _spawnPos + Vector3.Up * RiseSpeed * (float)_born;

		// Project to screen space + draw via HudPainter.
		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var hud = camera.Hud;

		float u = (float)_born / Lifetime;
		// Fade out in the last 35%, pop-in scale in the first 15%.
		float alpha = u > 0.65f ? (1f - u) / 0.35f : 1f;
		float scale = u < 0.15f ? 1.0f + (1.0f - u / 0.15f) * 0.7f : 1.0f;

		// Reject points behind the camera before projecting (PointToScreenPixels
		// would otherwise project them to weird pixel coords with no depth info).
		var camFwd = camera.WorldRotation.Forward;
		if ( camFwd.Dot( WorldPosition - camera.WorldPosition ) <= 0f ) return;

		var screenPos = camera.PointToScreenPixels( WorldPosition );

		var color = Tint.WithAlpha( alpha );
		var size = FontSize * scale;
		var rect = new Rect( screenPos.x - 80f, screenPos.y - size * 0.5f, 160f, size );
		var scope = new TextRendering.Scope( Text, color, size );
		hud.DrawText( scope, rect, TextFlag.Center );
	}
}
