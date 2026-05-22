namespace TreeChopping;

// Hub furniture — log pile near the Tools station, "TIMBER CO." sign near
// the spawn, and 3 torches lighting each station. Pure decoration : no
// gameplay, no colliders (the player can walk through these — they're
// scale anchors, not obstacles). Following the Mow-the-lawn pattern where
// the hub reads as a small workplace with personality, not an empty pad.
public static class HubProps
{
	public static void Spawn( Scene scene, Vector3 spawn, float stationArcRadius )
	{
		if ( scene is null ) return;

		// 3 torches : one per station angle (-60° / 0° / +60°) sitting at
		// the disc edge so they frame each station like lampposts.
		SpawnTorch( scene, AtArc( spawn, -60f, stationArcRadius + 50f ), new Color( 1.00f, 0.65f, 0.32f, 1f ) );
		SpawnTorch( scene, AtArc( spawn,   0f, stationArcRadius + 50f ), new Color( 1.00f, 0.65f, 0.32f, 1f ) );
		SpawnTorch( scene, AtArc( spawn, +60f, stationArcRadius + 50f ), new Color( 1.00f, 0.65f, 0.32f, 1f ) );

		// Log pile : 6 brown logs stacked in a 3-2-1 pyramid, dropped near
		// the Tools station so the trio reads as "where the wood goes".
		// Tools station angle = -60° → offset the pile to the OUTSIDE of it
		// so the player can still walk onto the disc.
		SpawnLogPile( scene, AtArc( spawn, -70f, stationArcRadius + 130f ), -70f );

		// "TIMBER CO." sign at the back of the spawn (-X) so a player who
		// turns around sees the workplace nameplate. Faces +X.
		SpawnSign( scene, spawn + new Vector3( -300f, 0f, 0f ), "TIMBER CO." );
	}

	private static Vector3 AtArc( Vector3 spawn, float angleDeg, float radius )
	{
		float rad = angleDeg * MathF.PI / 180f;
		return spawn + new Vector3( MathF.Cos( rad ) * radius, MathF.Sin( rad ) * radius, 0f );
	}

	private static void SpawnTorch( Scene scene, Vector3 footPos, Color flameTint )
	{
		// Drop foot to terrain so the torch base sits on the ground rather
		// than at the spawn's plateau Z (which differs once the player walks
		// down the slope past the hub).
		if ( TryGetGroundZ( scene, footPos.x, footPos.y, out float groundZ ) )
			footPos.z = groundZ;

		var go = scene.CreateObject();
		go.Name = "HubTorch";
		go.WorldPosition = footPos;

		// Wood pole.
		var pole = scene.CreateObject();
		pole.Name = "HubTorch.Pole";
		pole.SetParent( go );
		pole.LocalPosition = new Vector3( 0f, 0f, 110f );
		pole.LocalScale = new Vector3( 18f, 18f, 220f ) / Tunables.CubeBase;
		Mat.AddTintedCube( pole, new Color( 0.42f, 0.28f, 0.18f, 1f ) );

		// Glowing flame cap.
		var flame = scene.CreateObject();
		flame.Name = "HubTorch.Flame";
		flame.SetParent( go );
		flame.LocalPosition = new Vector3( 0f, 0f, 240f );
		flame.LocalScale = new Vector3( 38f, 38f, 50f ) / Tunables.CubeBase;
		Mat.AddTintedCube( flame, flameTint );

		// Real point light so the torch actually lights the disc + pillar.
		// Range is implicit (engine default ~250u — close-range fill light).
		var light = flame.AddComponent<PointLight>();
		light.LightColor = flameTint * 3f;
	}

	private static void SpawnLogPile( Scene scene, Vector3 pos, float yawDeg )
	{
		if ( TryGetGroundZ( scene, pos.x, pos.y, out float groundZ ) )
			pos.z = groundZ;

		var go = scene.CreateObject();
		go.Name = "HubLogPile";
		go.WorldPosition = pos;
		go.WorldRotation = Rotation.FromYaw( yawDeg );

		// 3-2-1 pyramid. Logs along the local Y axis, stacked along Z.
		// Each log = 240u long, 38u thick.
		const float logLen = 240f;
		const float logThick = 38f;
		var bark = new Color( 0.50f, 0.34f, 0.20f, 1f );
		var barkLight = new Color( 0.58f, 0.40f, 0.24f, 1f );

		// Bottom row (3 logs).
		float[] rowYs = { -logThick * 1.05f, 0f, +logThick * 1.05f };
		float z0 = logThick * 0.5f;
		foreach ( var ry in rowYs ) SpawnLog( go, new Vector3( 0f, ry, z0 ), logLen, logThick, bark );

		// Middle row (2 logs).
		float z1 = z0 + logThick * 0.92f;
		SpawnLog( go, new Vector3( 0f, -logThick * 0.52f, z1 ), logLen, logThick, barkLight );
		SpawnLog( go, new Vector3( 0f, +logThick * 0.52f, z1 ), logLen, logThick, barkLight );

		// Top log.
		float z2 = z1 + logThick * 0.92f;
		SpawnLog( go, new Vector3( 0f, 0f, z2 ), logLen, logThick, bark );
	}

	private static void SpawnLog( GameObject parent, Vector3 localPos, float length, float thick, Color tint )
	{
		var log = parent.Scene.CreateObject();
		log.Name = "HubLog";
		log.SetParent( parent );
		log.LocalPosition = localPos;
		log.LocalScale = new Vector3( length, thick, thick ) / Tunables.CubeBase;
		Mat.AddTintedCube( log, tint );
	}

	private static void SpawnSign( Scene scene, Vector3 pos, string text )
	{
		if ( TryGetGroundZ( scene, pos.x, pos.y, out float groundZ ) )
			pos.z = groundZ;

		var go = scene.CreateObject();
		go.Name = "HubSign";
		go.WorldPosition = pos;

		var woodDark = new Color( 0.40f, 0.26f, 0.16f, 1f );
		var woodLight = new Color( 0.62f, 0.46f, 0.28f, 1f );

		// 2 posts spaced along Y, with a horizontal plank across them.
		const float postH = 220f;
		const float postSpacing = 240f;

		var postL = scene.CreateObject();
		postL.Name = "HubSign.PostL";
		postL.SetParent( go );
		postL.LocalPosition = new Vector3( 0f, -postSpacing * 0.5f, postH * 0.5f );
		postL.LocalScale = new Vector3( 16f, 16f, postH ) / Tunables.CubeBase;
		Mat.AddTintedCube( postL, woodDark );

		var postR = scene.CreateObject();
		postR.Name = "HubSign.PostR";
		postR.SetParent( go );
		postR.LocalPosition = new Vector3( 0f, +postSpacing * 0.5f, postH * 0.5f );
		postR.LocalScale = new Vector3( 16f, 16f, postH ) / Tunables.CubeBase;
		Mat.AddTintedCube( postR, woodDark );

		var plank = scene.CreateObject();
		plank.Name = "HubSign.Plank";
		plank.SetParent( go );
		plank.LocalPosition = new Vector3( 0f, 0f, postH - 30f );
		plank.LocalScale = new Vector3( 14f, postSpacing + 30f, 70f ) / Tunables.CubeBase;
		Mat.AddTintedCube( plank, woodLight );

		// Attach a billboard-style label that draws on top of the plank in HUD.
		var label = go.AddComponent<HubSignLabel>();
		label.Text = text;
		label.LabelOffset = new Vector3( 0f, 0f, postH );
	}

	private static bool TryGetGroundZ( Scene scene, float x, float y, out float groundZ )
	{
		var top = new Vector3( x, y, 2000f );
		var bot = new Vector3( x, y, -2000f );
		var hit = scene.Trace.Ray( top, bot ).WithAnyTags( "ground" ).Run();
		if ( !hit.Hit ) { groundZ = 0f; return false; }
		groundZ = hit.EndPosition.z;
		return true;
	}
}

// Worldspace label drawn on top of the sign's plank. Same projection trick
// as ShopStation.DrawWorldLabel — BBoxToScreenPixels + Hud.DrawText.
public sealed class HubSignLabel : Component
{
	[Property] public string Text { get; set; } = "TIMBER CO.";
	[Property] public Vector3 LabelOffset { get; set; } = new( 0f, 0f, 200f );
	[Property] public Color TextTint { get; set; } = new( 0.95f, 0.82f, 0.42f, 1f );

	protected override void OnUpdate()
	{
		var camera = Scene?.Camera;
		if ( !camera.IsValid() ) return;
		var labelWorld = WorldPosition + LabelOffset;
		if ( !ShopStation.TryWorldToScreen( camera, labelWorld, out var screen, out float dist ) ) return;
		if ( dist > 2400f ) return;
		if ( screen.x < 0f || screen.x > Screen.Width || screen.y < 0f || screen.y > Screen.Height ) return;
		float size = (2400f / MathF.Max( dist, 200f )).Clamp( 12f, 28f );
		camera.Hud.DrawText( new TextRendering.Scope( Text, TextTint, size ), screen );
	}
}
