namespace TreeChopping;

public sealed class PlayerAxeView : Component
{
	private const string AxeModelPath = "models/props/trim_sheets/tools/woodaxe.vmdl";

	[Property] public SkinnedModelRenderer PlayerRenderer { get; set; }
	[Property, ReadOnly] public ModelRenderer AxeRenderer { get; private set; }

	protected override void OnStart()
	{
		PlayerRenderer ??= Components.Get<SkinnedModelRenderer>( FindMode.EverythingInSelfAndDescendants );
		var hold = PlayerRenderer.IsValid() ? PlayerRenderer.GetBoneObject( "hold_R" ) : null;
		hold = hold.IsValid() ? hold : PlayerRenderer?.GetBoneObject( "hand_R" );
		if ( !hold.IsValid() ) return;

		var axe = Scene.CreateObject();
		axe.Name = "HeldAxe";
		axe.SetParent( hold );
		axe.LocalPosition = new Vector3( 0f, 0f, 0f );
		axe.LocalRotation = Rotation.From( 0f, 0f, 0f );
		axe.LocalScale = Vector3.One * 1.35f;

		AxeRenderer = axe.AddComponent<ModelRenderer>();
		AxeRenderer.Model = Model.Load( AxeModelPath );
	}
}
