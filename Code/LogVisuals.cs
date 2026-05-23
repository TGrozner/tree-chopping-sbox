namespace TreeChopping;

public static class LogVisuals
{
	private static Model _logModel;

	public static Model CylinderModel
	{
		get
		{
			if ( _logModel is not null ) return _logModel;

			const int sides = Tunables.LogVisualSides;
			var buffer = new VertexBuffer();
			buffer.Init( false );
			float half = 0.5f;
			float radius = 0.5f;

			for ( int i = 0; i < sides; i++ )
			{
				float a0 = i / (float)sides * MathF.Tau;
				float a1 = (i + 1) / (float)sides * MathF.Tau;
				var p0 = new Vector3( MathF.Cos( a0 ) * radius, MathF.Sin( a0 ) * radius, -half );
				var p1 = new Vector3( MathF.Cos( a1 ) * radius, MathF.Sin( a1 ) * radius, -half );
				var p2 = new Vector3( MathF.Cos( a1 ) * radius, MathF.Sin( a1 ) * radius, half );
				var p3 = new Vector3( MathF.Cos( a0 ) * radius, MathF.Sin( a0 ) * radius, half );
				var normal = new Vector3( MathF.Cos( (a0 + a1) * 0.5f ), MathF.Sin( (a0 + a1) * 0.5f ), 0f ).Normal;
				buffer.Default.Normal = normal;
				buffer.Default.Tangent = new Vector4( Vector3.Up, 1f );
				buffer.AddQuad( p0, p1, p2, p3 );

				buffer.Default.Normal = Vector3.Down;
				buffer.Default.Tangent = new Vector4( Vector3.Right, 1f );
				buffer.Add( new Vector3( 0f, 0f, -half ) );
				buffer.Add( p1 );
				buffer.Add( p0 );

				buffer.Default.Normal = Vector3.Up;
				buffer.Default.Tangent = new Vector4( Vector3.Right, 1f );
				buffer.Add( new Vector3( 0f, 0f, half ) );
				buffer.Add( p3 );
				buffer.Add( p2 );
			}

			var mesh = new Mesh( Mat.Default );
			mesh.CreateBuffers( buffer, true );
			_logModel = new ModelBuilder()
				.WithName( "tc_lowpoly_log" )
				.AddMesh( mesh )
				.Create();
			return _logModel;
		}
	}
}
