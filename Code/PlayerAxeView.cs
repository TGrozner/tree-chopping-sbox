namespace TreeChopping;

public sealed class PlayerAxeView : Component
{
	private const string AxeModelPath = "models/props/trim_sheets/tools/woodaxe.vmdl";

	[Property] public SkinnedModelRenderer PlayerRenderer { get; set; }
	[Property, ReadOnly] public ModelRenderer AxeRenderer { get; private set; }

	private AxeController _axeController;
	private GameObject _axeObject;
	private Vector3 _basePosition;
	private Rotation _baseRotation;
	private Vector3 _prevWorldPosition;
	private float _walkBobWeight;
	private float _bobbleSeed;

	protected override void OnStart()
	{
		PlayerRenderer ??= Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		_axeController ??= Components.Get<AxeController>( FindMode.InAncestors );
		var hold = PlayerRenderer.IsValid() ? PlayerRenderer.GetBoneObject( "hold_R" ) : null;
		hold = hold.IsValid() ? hold : PlayerRenderer?.GetBoneObject( "hand_R" );
		if ( !hold.IsValid() ) return;

		var axe = Scene.CreateObject();
		axe.Name = "HeldAxe";
		axe.SetParent( hold );
		_basePosition = new Vector3( 2f, -2f, 1f );
		_baseRotation = Rotation.From( -8f, 18f, 92f );
		axe.LocalPosition = _basePosition;
		axe.LocalRotation = _baseRotation;
		axe.LocalScale = Vector3.One * 1.55f;
		_axeObject = axe;
		_prevWorldPosition = WorldPosition;

		AxeRenderer = axe.AddComponent<ModelRenderer>();
		AxeRenderer.Model = Model.Load( AxeModelPath );
	}

	protected override void OnUpdate()
	{
		if ( !_axeObject.IsValid() ) return;
		_axeController ??= Components.Get<AxeController>( FindMode.InAncestors );

		_bobbleSeed += Time.Delta;
		float speed = Time.Delta > 0f ? (WorldPosition - _prevWorldPosition).WithZ( 0f ).Length / Time.Delta : 0f;
		_prevWorldPosition = WorldPosition;
		float targetBob = (speed / 130f).Clamp( 0f, 1f );
		_walkBobWeight = MathX.Lerp( _walkBobWeight, targetBob, 12f * Time.Delta );
		float walkBob = MathF.Sin( _bobbleSeed * 8.0f ) * 0.75f * _walkBobWeight;
		float idleBob = MathF.Sin( _bobbleSeed * 2.2f ) * 0.4f;
		var pos = _basePosition + new Vector3( 0f, idleBob, walkBob );
		var rot = _baseRotation;

		if ( _axeController.IsValid() && _axeController.IsSwinging )
		{
			float p = _axeController.SwingViewProgress;
			float combo = 1f + 0.10f * _axeController.ChainLevel;
			if ( p <= 1f )
			{
				float wind = 1f - MathF.Pow( 1f - p, 3f );
				pos += new Vector3( -5.5f * wind, -3.6f * wind, 8.5f * wind ) * combo;
				rot *= Rotation.From( -38f * wind * combo, 12f * wind, -50f * wind * combo );
			}
			else
			{
				float r = (1f - (p - 1f)).Clamp( 0f, 1f );
				float snap = r * r;
				pos += new Vector3( 8f * snap, 2.6f * snap, -7f * snap ) * combo;
				rot *= Rotation.From( 30f * snap * combo, -11f * snap, 40f * snap * combo );
			}
		}
		if ( _axeController.IsValid() )
		{
			float hit = _axeController.ViewImpactKick;
			if ( hit > 0f )
			{
				float buzz = MathF.Sin( Time.Now * 90f ) * hit;
				pos += new Vector3( 5f * hit, 1.5f * buzz, -4f * hit );
				rot *= Rotation.From( 18f * hit, -7f * buzz, 24f * hit );
			}
		}

		_axeObject.LocalPosition = pos;
		_axeObject.LocalRotation = rot;
	}
}
