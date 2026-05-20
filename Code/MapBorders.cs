namespace TreeChopping;

// Ring of tall static cubes at the edge of the playable area. Two jobs :
// visually they read as distant mountains surrounding the playground ;
// physically they catch rogue logs and stop them flying off into the void.
//
// Tagged "border" (not "ground") so the tree-spawn raycast skips them ; a
// hit on a wall top would otherwise be treated as terrain and let a tree
// spawn on top of the wall.
public static class MapBorders
{
	public static int Spawn( Scene scene, Vector3 centerXY )
	{
		if ( scene is null ) return 0;
		int placed = 0;
		float angleStep = MathF.Tau / Tunables.BorderSegments;
		// Tangent-arc length per segment so the wall reads as a continuous
		// ring rather than visible gaps between cubes.
		float segmentArc = MathF.Tau * Tunables.BorderRadius / Tunables.BorderSegments;
		float overlap = 1.06f; // slight overlap kills any tiny gap

		for ( int i = 0; i < Tunables.BorderSegments; i++ )
		{
			float angle = i * angleStep;
			float x = centerXY.x + MathF.Cos( angle ) * Tunables.BorderRadius;
			float y = centerXY.y + MathF.Sin( angle ) * Tunables.BorderRadius;

			var go = scene.CreateObject();
			go.Name = "BorderMountain";
			go.WorldPosition = new Vector3( x, y, Tunables.BorderWallHeight * 0.5f );
			// Cube's local +X faces radially OUTWARD (depth axis). Yaw aligns it.
			float yawDeg = angle * 180f / MathF.PI;
			go.WorldRotation = Rotation.FromYaw( yawDeg );
			go.WorldScale = new Vector3( Tunables.BorderWallDepth, segmentArc * overlap, Tunables.BorderWallHeight ) / Tunables.CubeBase;
			// Intentionally NOT tagged "ground" — otherwise the tree-spawn
			// raycast hits the wall top and a tree sprouts on the parapet.
			// The collider still catches logs ; that's the only job.
			go.Tags.Add( "border" );

			// Slight per-segment tint variance so the ring doesn't read as a
			// single flat band — small mountain silhouette flavor.
			float n = ((MathF.Sin( angle * 3.3f ) * 0.5f) + 0.5f) * 0.6f
				+ ((MathF.Cos( angle * 5.1f ) * 0.5f) + 0.5f) * 0.4f;
			var tint = Color.Lerp( Tunables.BorderTintLow, Tunables.BorderTintHigh, n );
			Mat.AddTintedCube( go, tint );

			var col = go.AddComponent<BoxCollider>();
			col.Scale = new Vector3( Tunables.CubeBase );
			col.Static = true;
			placed++;
		}
		return placed;
	}
}
