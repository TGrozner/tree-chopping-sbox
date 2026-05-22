namespace TreeChopping;

// Ring of irregular rocks closing the back + sides of the hub spawn area,
// leaving the front (+X) open to the forest. Matches the Mow-the-lawn
// pattern : you spawn inside a small horseshoe of stone, face the shop
// stations, and walk out the open side to harvest.
//
// Rocks are tagged "border" (NOT "ground") so the tree-spawn raycast skips
// them — like MapBorders, they catch rogue logs without becoming a foliage
// platform.
public static class HubAmphitheatre
{
	public static int Spawn( Scene scene, Vector3 center, int seed )
	{
		if ( scene is null ) return 0;
		int placed = 0;
		var rng = new Random( seed ^ 0x131311 );

		// 10 rocks spread on a 220° arc behind the spawn (front 140° stays
		// open toward the forest). Front = +X = angle 0° ; we skip the front
		// ±70° wedge and place rocks from 70° clockwise around to 290°.
		const int RockCount = 10;
		const float ArcStartDeg = 70f;
		const float ArcEndDeg = 290f;
		const float Radius = 540f;

		for ( int i = 0; i < RockCount; i++ )
		{
			float t = i / (float)(RockCount - 1);
			float angleDeg = MathX.Lerp( ArcStartDeg, ArcEndDeg, t )
				+ (float)(rng.NextDouble() - 0.5) * 12f; // small jitter so spacing isn't perfectly regular
			float rad = angleDeg * MathF.PI / 180f;
			float r = Radius + (float)(rng.NextDouble() - 0.5) * 80f;
			float x = center.x + MathF.Cos( rad ) * r;
			float y = center.y + MathF.Sin( rad ) * r;

			// Drop to the ground for the rock base — terrain is sloped, the
			// amphi sits half-buried so the visible silhouette starts at the
			// player's eye level instead of looking like a floating block.
			if ( !TryGetGroundZ( scene, x, y, out float groundZ ) ) groundZ = center.z;

			// Irregular rock dimensions — wider on the radial axis, deep
			// enough to read as a boulder, height varied so the silhouette
			// isn't a wall.
			float w = 260f + (float)rng.NextDouble() * 220f; // 260..480
			float d = 220f + (float)rng.NextDouble() * 180f; // 220..400
			float h = 380f + (float)rng.NextDouble() * 260f; // 380..640

			var go = scene.CreateObject();
			go.Name = "HubRock";
			// Bury the bottom ~80u so the rock looks anchored, not dropped.
			go.WorldPosition = new Vector3( x, y, groundZ + h * 0.5f - 80f );
			// Rotate the rock so its +X faces inward (toward the spawn),
			// add small pitch + roll so it doesn't read as a perfect cube.
			float yawDeg = angleDeg + 180f;
			float pitchDeg = (float)(rng.NextDouble() - 0.5) * 18f;
			float rollDeg  = (float)(rng.NextDouble() - 0.5) * 14f;
			go.WorldRotation = Rotation.From( pitchDeg, yawDeg, rollDeg );
			go.WorldScale = new Vector3( d, w, h ) / Tunables.CubeBase;
			go.Tags.Add( "border" );

			// Warm gray with mild variance — earth-tone palette matching the
			// existing border mountains so the hub doesn't clash with the
			// distant skyline.
			float g = 0.50f + (float)rng.NextDouble() * 0.18f;
			var tint = new Color( g * 0.98f, g, g * 0.94f, 1f );
			Mat.AddTintedCube( go, tint );

			var col = go.AddComponent<BoxCollider>();
			col.Scale = new Vector3( Tunables.CubeBase );
			col.Static = true;
			placed++;
		}
		return placed;
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
