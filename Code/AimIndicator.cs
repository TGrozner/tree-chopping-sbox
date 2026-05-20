namespace TreeChopping;

// Visualizes the player's chop angle + the tree that would be hit during
// WaitingForSwing. Uses Gizmo.Draw for the aim line + a runtime ModelRenderer
// tint pulse on the candidate target. The aim path is driven by the SAME
// camera-raycast helper as BeaverController.UpdateSwing (PickCameraAimTarget),
// so the highlight is ground-truth: tinted tree == tree that swing will hit.
public sealed class AimIndicator : Component
{
	[Property] public Color LineColor { get; set; } = new( 1f, 0.85f, 0.35f, 0.8f );
	[Property] public Color TargetTint { get; set; } = new( 1f, 0.7f, 0.25f, 1f );

	private BeaverController _beaver;
	private RunManager _run;
	private IChoppable _lastTarget;
	private List<(ModelRenderer mr, Color original)> _lastTargetTints = new();

	public static AimIndicator Get( Scene scene )
		=> scene?.GetAllComponents<AimIndicator>().FirstOrDefault();

	// Exposed so WoodHud can show a reticle that matches the live aim state.
	public bool HasLockedTarget { get; private set; }
	public Vector3 LastHitPosition { get; private set; }
	public Color CurrentTint { get; private set; }

	protected override void OnUpdate()
	{
		_beaver ??= Scene?.GetAllComponents<BeaverController>().FirstOrDefault();
		_run ??= RunManager.Get( Scene );

		if ( _run is null || _run.State != RunState.WaitingForSwing || !_beaver.IsValid() )
		{
			RestoreLastTarget();
			HasLockedTarget = false;
			return;
		}

		// Single source of truth for both swing and highlight.
		var target = _beaver.PickCameraAimTarget( out var hitPos );
		HasLockedTarget = target != null;
		LastHitPosition = hitPos;

		// Tint suit le modifier actif : Explosive=orange, Frozen=cyan, Heavy=violet,
		// ChainLightning=jaune. Donne feedback visuel immédiat du buff du run.
		var modTint = _run.ActiveModifier.Tint();
		var aimColor = Color.Lerp( LineColor, modTint, 0.55f );
		CurrentTint = aimColor;

		if ( target != _lastTarget )
		{
			RestoreLastTarget();
			if ( target is Component tc && tc.IsValid() )
			{
				var renderers = tc.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants );
				foreach ( var mr in renderers )
				{
					if ( !mr.IsValid() ) continue;
					_lastTargetTints.Add( (mr, mr.Tint) );
					mr.Tint = Color.Lerp( mr.Tint, TargetTint, 0.55f );
				}
				_lastTarget = target;
			}
		}

		// Marker visuel au pied de la cible — vertical beam + ground ring.
		// Beaucoup plus lisible en 3rd-person que la skim-line de iter précédent.
		if ( target is Component targetC && targetC.IsValid() )
		{
			float pulse = 0.75f + 0.25f * MathF.Sin( Time.Now * 5.5f );
			Gizmo.Draw.Color = aimColor.WithAlpha( aimColor.a * pulse );
			// Vertical beam from tree foot upward.
			var foot = targetC.WorldPosition;
			Gizmo.Draw.LineThickness = 4f;
			Gizmo.Draw.Line( foot, foot + Vector3.Up * (Tunables.TreeHeight * 0.6f) );
			// Ground ring at tree foot.
			int seg = 24;
			float ringR = 38f;
			for ( int i = 0; i < seg; i++ )
			{
				float a0 = (i / (float)seg) * MathF.Tau;
				float a1 = ((i + 1) / (float)seg) * MathF.Tau;
				var p0 = foot + new Vector3( MathF.Cos( a0 ) * ringR, MathF.Sin( a0 ) * ringR, 2f );
				var p1 = foot + new Vector3( MathF.Cos( a1 ) * ringR, MathF.Sin( a1 ) * ringR, 2f );
				Gizmo.Draw.Line( p0, p1 );
			}
			// Tick-mark from hit point along the impact normal.
			if ( hitPos != default )
			{
				Gizmo.Draw.LineThickness = 2.5f;
				Gizmo.Draw.Line( hitPos, hitPos + Vector3.Up * 20f );
			}
		}
		else
		{
			// Pas de cible — petit indicateur "no lock" très discret au pied du beaver.
			float pulse = 0.55f + 0.15f * MathF.Sin( Time.Now * 4f );
			Gizmo.Draw.Color = aimColor.WithAlpha( 0.35f * pulse );
			var foot = _beaver.WorldPosition + Vector3.Down * 30f;
			var fwd = _beaver.DebugCameraAngles.WithPitch( 0f ).ToRotation().Forward;
			Gizmo.Draw.LineThickness = 2f;
			Gizmo.Draw.Line( foot, foot + fwd * 80f );
		}
	}

	private void RestoreLastTarget()
	{
		foreach ( var (mr, original) in _lastTargetTints )
		{
			if ( mr.IsValid() ) mr.Tint = original;
		}
		_lastTargetTints.Clear();
		_lastTarget = null;
	}

	protected override void OnDisabled()
	{
		RestoreLastTarget();
		HasLockedTarget = false;
	}
}
