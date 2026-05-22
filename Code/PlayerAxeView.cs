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

		AxeRenderer = axe.AddComponent<ModelRenderer>();
		AxeRenderer.Model = Model.Load( AxeModelPath );
	}

	protected override void OnUpdate()
	{
		if ( !_axeObject.IsValid() ) return;
		_axeController ??= Components.Get<AxeController>( FindMode.InAncestors );

		_bobbleSeed += Time.Delta;
		float walkBob = MathF.Sin( _bobbleSeed * 8.0f ) * 0.8f;
		float idleBob = MathF.Sin( _bobbleSeed * 2.2f ) * 0.4f;
		var pos = _basePosition + new Vector3( 0f, idleBob, walkBob );
		var rot = _baseRotation;

		if ( _axeController.IsValid() && _axeController.IsSwinging )
		{
			float p = _axeController.SwingViewProgress;
			if ( p <= 1f )
			{
				float wind = MathF.Sin( p * MathF.PI * 0.5f );
				pos += new Vector3( -4f * wind, -3f * wind, 7f * wind );
				rot *= Rotation.From( -28f * wind, 10f * wind, -38f * wind );
			}
			else
			{
				float r = 1f - (p - 1f);
				float snap = r * r;
				pos += new Vector3( 7f * snap, 2f * snap, -5f * snap );
				rot *= Rotation.From( 20f * snap, -8f * snap, 28f * snap );
			}
		}

		_axeObject.LocalPosition = pos;
		_axeObject.LocalRotation = rot;
	}
}
