namespace TreeChopping;

// Persistent stump left at the foot of a felled tree (Valheim feel : "tree
// was here"). Spawned by Tree.StartFell at the original _spawnFootPos, lives
// outside the tree's GameObject hierarchy so it survives the GameObject.Destroy
// triggered by SplitIntoLogs. Owns the respawn timer — at TreeKindRespawnDelay
// it spawns a fresh Tree at the same foot pos and destroys itself.
public sealed class TreeStump : Component
{
	[Property] public TreeKind Kind { get; set; }
	[Property] public float BiomeDifficulty { get; set; }
	[Property] public Vector3 SpawnFootPos { get; set; }
	[Property] public bool IsMythicMemory { get; set; }

	private TimeSince _timeSinceFelled;
	// Valheim Pickable.m_respawnTimeInitMin/Max — chaque instance a un random
	// jitter sur son respawn delay pour que les souches voisines ne respawnent
	// pas toutes en lockstep. ±25% de la base. Picked au SpawnAt.
	private float _respawnJitterMul = 1f;
	// Test hook : expose pour TestRespawnJitterRange.
	internal float DebugRespawnJitterMul => _respawnJitterMul;
	private ModelRenderer _baseMr;
	private ModelRenderer _capMr;
	private Color _baseTint;
	private Color _capTint;
	// État de la phase grow : pendant cette phase, _growingTree existe à scale
	// croissant, le stump attend que l'animation finisse avant de se destroy.
	private Tree _growingTree;
	private TimeSince _timeSinceGrowStarted;

	public static TreeStump SpawnAt( Scene scene, Vector3 footPos, float trunkWidth, Color trunkTint, TreeKind kind, float biomeDifficulty, bool isMythic )
	{
		var go = scene.CreateObject();
		go.Name = "TreeStump";
		go.WorldPosition = footPos;

		float h = MathF.Max( 26f, trunkWidth * 1.10f );

		var baseGo = scene.CreateObject();
		baseGo.Name = "TreeStump.Base";
		baseGo.SetParent( go );
		baseGo.LocalPosition = new Vector3( 0f, 0f, h * 0.5f );
		baseGo.LocalScale = new Vector3( trunkWidth * 1.35f, trunkWidth * 1.35f, h ) / Tunables.CubeBase;
		var baseTint = new Color(
			trunkTint.r * 0.72f,
			trunkTint.g * 0.62f,
			trunkTint.b * 0.52f,
			1f );
		var baseMr = Mat.AddTintedCube( baseGo, baseTint );

		// Fresh-cut cap : light tan disc on top, clearly visible as "freshly
		// chopped wood". Slightly narrower than the base so the bark wraps it.
		var capGo = scene.CreateObject();
		capGo.Name = "TreeStump.Cap";
		capGo.SetParent( go );
		capGo.LocalPosition = new Vector3( 0f, 0f, h + 1.5f );
		capGo.LocalScale = new Vector3( trunkWidth * 1.10f, trunkWidth * 1.10f, 3.5f ) / Tunables.CubeBase;
		var capTint = isMythic
			? new Color( 1.00f, 0.86f, 0.42f, 1f )
			: new Color( 0.92f, 0.80f, 0.58f, 1f );
		var capMr = Mat.AddTintedCube( capGo, capTint );

		var stump = go.AddComponent<TreeStump>();
		stump.Kind = kind;
		stump.BiomeDifficulty = biomeDifficulty;
		stump.SpawnFootPos = footPos;
		stump.IsMythicMemory = isMythic;
		stump._baseMr = baseMr;
		stump._capMr = capMr;
		stump._baseTint = baseTint;
		stump._capTint = capTint;
		// TimeSince default = Time.Now (struct interne t=0). Reset explicit
		// pour tracker le délai depuis fell. Sans ça, t = boot time, et le
		// respawn fire immédiatement au lieu d'attendre TreeKindRespawnDelay.
		stump._timeSinceFelled = 0f;
		// ±25% random jitter sur le respawn delay (Valheim Pickable.m_respawnTimeInitMin/Max
		// donne ce genre de variance). Évite que toutes les souches respawnent
		// en lockstep et donne une forêt qui "régénère" naturellement.
		stump._respawnJitterMul = Game.Random.Float( 0.75f, 1.25f );
		return stump;
	}

	protected override void OnUpdate()
	{
		// Phase grow : la nouvelle Tree est spawnée à scale 0, on l'anime vers
		// 1 linéairement (Valheim TreeBase.GrowAnimation). Quand done, destroy
		// la souche — la nouvelle tree prend sa place. Le m_respawnEffect
		// Valheim fire à la fin de la coroutine grow (line 88), pas au start.
		if ( _growingTree.IsValid() )
		{
			float k = ((float)_timeSinceGrowStarted / Tunables.TreeGrowDuration).Clamp( 0f, 1f );
			_growingTree.GameObject.WorldScale = Vector3.One * k;
			if ( k >= 1f )
			{
				// m_respawnEffect Valheim — fire à la fin de la grow animation.
				ChipBurst.SpawnLeaves( Scene, SpawnFootPos + Vector3.Up * 30f, Vector3.Up, 8,
					new Color( 0.62f, 0.82f, 0.40f, 1f ) );
				Sfx.Play( "sounds/stump_respawn.sound", SpawnFootPos + Vector3.Up * 30f, volume: 0.55f, pitchMin: 0.85f, pitchMax: 1.15f );
				GameObject?.Destroy();
			}
			return;
		}

		float respawnDelay = Tunables.TreeKindRespawnDelay[(int)Kind] * _respawnJitterMul;
		if ( IsMythicMemory ) respawnDelay += Tunables.MythicRespawnExtra;
		float t = (float)_timeSinceFelled;

		// Fade vers une couleur de terre les 2s avant respawn, donne l'illusion
		// d'une souche qui se désagrège dans le sol au lieu d'un pop-out.
		const float fadeWindow = 2f;
		float fadeStart = respawnDelay - fadeWindow;
		if ( t > fadeStart )
		{
			float k = ((t - fadeStart) / fadeWindow).Clamp( 0f, 1f );
			var dirt = new Color( 0.32f, 0.26f, 0.20f, 1f );
			if ( _baseMr.IsValid() ) _baseMr.Tint = Color.Lerp( _baseTint, dirt, k );
			if ( _capMr.IsValid() ) _capMr.Tint = Color.Lerp( _capTint, dirt, k );
		}

		if ( t > respawnDelay )
		{
			StartGrowAnimation();
		}
	}

	private void StartGrowAnimation()
	{
		// Spawn la nouvelle Tree à scale ~0 et déclenche la phase grow. La
		// souche reste pendant l'animation, m_respawnEffect fire à la FIN
		// (Valheim TreeBase.GrowAnimation coroutine, après la boucle for).
		_growingTree = Tree.SpawnAt( Scene, SpawnFootPos, BiomeDifficulty );
		_growingTree.GameObject.WorldScale = Vector3.One * 0.01f;
		_timeSinceGrowStarted = 0f;
	}

	// Test hook : force respawn immédiat sans attendre TreeKindRespawnDelay.
	// SelfTest TestStumpRespawn s'en sert pour valider la chaîne stump → Tree
	// + grow animation sans devoir attendre 30s+ in-game.
	internal void TestForceRespawn()
	{
		if ( _growingTree.IsValid() ) return;
		StartGrowAnimation();
	}
}
