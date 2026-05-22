namespace TreeChopping;

// Procedurally-generated Sandbox.Terrain — replaces the flat scene plane
// with smooth rolling hills + valleys. Heightmap = 3-octave FBM value noise,
// smoothstepped so peaks read rounded and valleys deepen ; no right-angle
// cube-bump staircase artefacts. Same seed → same terrain.
//
// Coverage : TerrainSize=6000 centered on origin → ±3000u. Arena radius is
// 2500 so the player + entire forest sit comfortably inside.
public static class TerrainHeightmap
{
	// Picks one of the NoiseStyle variants based on the seed (deterministic
	// — same seed always picks the same mountain shape).
	public static Tunables.NoiseStyle PickStyle( int seed )
	{
		int values = Enum.GetValues( typeof( Tunables.NoiseStyle ) ).Length;
		return (Tunables.NoiseStyle)((uint)(seed * 2654435761u) % (uint)values);
	}

	public static Terrain Spawn( Scene scene, int seed, Vector3 spawnPeakXY )
	{
		var style = PickStyle( seed );
		Log.Info( $"[TerrainHeightmap] Mountain style : {style}" );
		return Spawn( scene, seed, spawnPeakXY, style );
	}

	public static Terrain Spawn( Scene scene, int seed, Vector3 spawnPeakXY, Tunables.NoiseStyle style )
	{
		if ( scene is null ) return null;

		var go = scene.CreateObject();
		go.Name = "TerrainHeightmap";
		// Terrain is anchored at one CORNER (not centered), so push it back
		// by half the size to center the playable area on world origin.
		var terrainOrigin = new Vector3( -Tunables.TerrainSize * 0.5f, -Tunables.TerrainSize * 0.5f, 0f );
		go.WorldPosition = terrainOrigin;
		go.Tags.Add( "ground" );

		var storage = new TerrainStorage();
		storage.SetResolution( Tunables.TerrainResolution );
		storage.TerrainSize = Tunables.TerrainSize;
		storage.TerrainHeight = Tunables.TerrainMaxHeight;

		int res = Tunables.TerrainResolution;
		var heights = new ushort[res * res];

		// Per-octave random offsets so we don't sample noise from origin
		// (which would always give the same seed-pattern visible at center).
		var rng = new Random( seed );
		float ox1 = (float)(rng.NextDouble() * 1000f), oy1 = (float)(rng.NextDouble() * 1000f);
		float ox2 = (float)(rng.NextDouble() * 1000f), oy2 = (float)(rng.NextDouble() * 1000f);
		float ox3 = (float)(rng.NextDouble() * 1000f), oy3 = (float)(rng.NextDouble() * 1000f);

		// Spawn-peak grid coords + radii in grid units.
		float worldToGrid = (res - 1) / Tunables.TerrainSize;
		float peakGridX = (spawnPeakXY.x - terrainOrigin.x) * worldToGrid;
		float peakGridY = (spawnPeakXY.y - terrainOrigin.y) * worldToGrid;
		float plateauR = Tunables.MountainPlateauRadius * worldToGrid;
		float baseR = Tunables.MountainBaseRadius * worldToGrid;

		// TwinPeaks : second peak placed 1500u downhill of the spawn in +X
		// so the player can see + reach it. Smaller than the main one.
		var twinXY = new Vector3( spawnPeakXY.x + 1500f, spawnPeakXY.y + 200f, 0f );
		float twinGridX = (twinXY.x - terrainOrigin.x) * worldToGrid;
		float twinGridY = (twinXY.y - terrainOrigin.y) * worldToGrid;
		float twinBaseR = baseR * 0.65f;
		float twinPlateauR = plateauR * 1.5f;

		for ( int j = 0; j < res; j++ )
		{
			for ( int i = 0; i < res; i++ )
			{
				float u = i / (float)(res - 1);
				float v = j / (float)(res - 1);

				// 3-octave FBM in [0,1]. Used as small bumps ON the mountain.
				float n = 0f;
				n += Noise( u * Tunables.TerrainNoiseFreqLow + ox1, v * Tunables.TerrainNoiseFreqLow + oy1, seed ) * 0.55f;
				n += Noise( u * Tunables.TerrainNoiseFreqMid + ox2, v * Tunables.TerrainNoiseFreqMid + oy2, seed + 1 ) * 0.30f;
				n += Noise( u * Tunables.TerrainNoiseFreqHigh + ox3, v * Tunables.TerrainNoiseFreqHigh + oy3, seed + 2 ) * 0.15f;

				// Radial cone height calc — used by all styles as the base.
				float dx = i - peakGridX;
				float dy = j - peakGridY;
				float dist = MathF.Sqrt( dx * dx + dy * dy );
				float cone = ConeHeight( dist, plateauR, baseR );

				// Style-specific reshape on top of the base cone.
				switch ( style )
				{
					case Tunables.NoiseStyle.Ridges:
					{
						// Ridge pattern : 1 - |2N - 1| forms sharp ridges
						// (peaks at 1 where the noise crosses 0.5). Mix
						// it with the cone so the mountain becomes a
						// ridged spine rather than a smooth dome.
						float ridge = Noise( u * 5f + ox1 + 17f, v * 5f + oy1 + 23f, seed );
						float ridgeFactor = 1f - MathF.Abs( 2f * ridge - 1f );
						cone *= 0.55f + 0.45f * ridgeFactor;
						break;
					}
					case Tunables.NoiseStyle.TwinPeaks:
					{
						float tdx = i - twinGridX;
						float tdy = j - twinGridY;
						float tdist = MathF.Sqrt( tdx * tdx + tdy * tdy );
						float twin = ConeHeight( tdist, twinPlateauR, twinBaseR ) * 0.85f;
						cone = MathF.Max( cone, twin );
						break;
					}
					case Tunables.NoiseStyle.Plateau:
					{
						// Sharper falloff — flatter top + steeper drop ; reads
						// like a mesa rather than a dome.
						if ( dist > plateauR && dist < baseR )
						{
							float t = (baseR - dist) / (baseR - plateauR);
							cone = 1f - MathF.Pow( 1f - t, 4f );
						}
						break;
					}
				}

				// Blend : mostly cone, FBM scaled by the cone weight so noise
				// fades out with the slope (no high-frequency bumps at the
				// flat base — keeps the silhouette clean from a distance).
				float h = cone * Tunables.MountainConeWeight + n * Tunables.MountainNoiseWeight * cone;

				heights[j * res + i] = (ushort)(h.Clamp( 0f, 1f ) * 65535f);
			}
		}
		storage.HeightMap = heights;

		var terrain = go.AddComponent<Terrain>();
		terrain.Storage = storage;
		terrain.TerrainSize = Tunables.TerrainSize;
		terrain.TerrainHeight = Tunables.TerrainMaxHeight;
		terrain.EnableCollision = true;
		terrain.Static = true;
		// DO NOT touch this — Thomas locked the ground to the dev checker
		// 2026-05-21. Uniform tinted materials hide the heightmap relief and
		// kill scale readability ; the checker is the source of truth visual.
		// Engine default = dev checker, so no MaterialOverride.
		terrain.Create();

		Log.Info( $"[TerrainHeightmap] Built terrain {res}×{res} @ {Tunables.TerrainSize}u, max height {Tunables.TerrainMaxHeight}u" );
		return terrain;
	}

	// Smoothstep cone height : 1 inside plateau radius, smoothly drops to 0
	// at base radius. Used by all NoiseStyle variants as their starting
	// shape — they reshape it from there.
	private static float ConeHeight( float dist, float plateauR, float baseR )
	{
		if ( dist <= plateauR ) return 1f;
		if ( dist >= baseR ) return 0f;
		float t = (baseR - dist) / (baseR - plateauR);
		return t * t * (3f - 2f * t);
	}

	// Bilinear-blended value noise — copy of SceneStarter.ValueNoise2D so the
	// terrain code doesn't depend on internals of the scene starter.
	private static float Noise( float x, float y, int seed )
	{
		int xi = (int)MathF.Floor( x );
		int yi = (int)MathF.Floor( y );
		float xf = x - xi;
		float yf = y - yi;
		float u = xf * xf * (3f - 2f * xf);
		float v = yf * yf * (3f - 2f * yf);
		float a = Hash( xi, yi, seed );
		float b = Hash( xi + 1, yi, seed );
		float c = Hash( xi, yi + 1, seed );
		float d = Hash( xi + 1, yi + 1, seed );
		return MathX.Lerp( MathX.Lerp( a, b, u ), MathX.Lerp( c, d, u ), v );
	}

	private static float Hash( int x, int y, int seed )
	{
		uint h = (uint)(x * 374761393 ^ y * 668265263 ^ seed * 1274126177);
		h = (h ^ (h >> 13)) * 1274126177u;
		h ^= h >> 16;
		return (h & 0xFFFFFF) / (float)0x1000000;
	}
}
