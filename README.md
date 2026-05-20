# Tree Chopping — s&box

Mow-the-lawn-like (style Plants vs. Zombies' Lawn) avec mécanique d'arbres qui tombent à la Valheim. Tu chop, le bois s'accumule, tu upgrades ta hache au shop sur le sommet, tu redescends, tu chop plus gros.

Singleplayer s&box (Source 2 + C#/.NET). Pas de score, pas de runs : continuous play.

## Loop

1. Tu spawn sur un plateau au sommet d'une montagne. Le shop marker est sous tes pieds.
2. Tu descends la pente. Forêt biome-biased autour : saplings (1 chop) près du shop, veterans (8 chops) au bord.
3. Click gauche = swing. Chop multiple selon le tier (T0=1, T1=2, T2=3, T3=5 chops par swing).
4. Tree fells, le trunk roule, paye du bois (Sapling 1, Normal 3, Veteran 8, Brittle 2, Mythic +12).
5. R = téléport au shop. E (quand près) = upgrade axe (coûts 8 / 28 / 80 bois).
6. Wood + tier persistent dans `progress.json` (`FileSystem.Data`).

Trees tombent en physique naturelle — un trunk qui tombe peut bumper un voisin debout (Valheim soft cascade). Pas de CascadeStrike scripté.

## Run

Headless self-test (12s, exit 0/1/3 = PASS/FAIL/TIMEOUT) :
```powershell
tools\selftest.ps1 -Seeds 3
```

Lance le jeu visuel : ouvrir `tree_chopping.sbproj` dans `sbox-dev.exe`, Play (F5) sur `Assets/scenes/main.scene`.

Compiler le C# offline (validation types, sans Steam) :
```powershell
dotnet build Code/tree_chopping.csproj
```

## Architecture

| Fichier | Rôle |
|---|---|
| `Code/SceneStarter.cs` | Bootstrap : singletons (GameState, WoodHud, AutoPlay, PerfProbe, SelfTest), terrain procédural, mountain borders, beaver spawn, forêt biome-biased, shop area |
| `Code/Tree.cs` | Multi-chop + StartFell + force-land timeout + GiveWoodOnce. Biome-biased Kind picker (Easy/Hard weight blend by distance to spawn) |
| `Code/BeaverController.cs` | Swing state machine (Idle → WindUp → Recovery), hit-stop, FOV punch, axe wired to hand_R |
| `Code/GameState.cs` | Wood + AxeTier persistence (FileSystem.Data/progress.json). ChopPower + WoodMultiplier derived from tier |
| `Code/ShopArea.cs` | Player-near-shop detect + E to upgrade |
| `Code/WoodHud.cs` | HUD immediate-mode (crosshair, wood balance pulse, axe tier badge, shop hint, teleport hint) |
| `Code/AutoPlay.cs` | Autonomous chop-loop in-forest driver (Active=true via MCP bridge). Teleporte le castor vers le tree le plus proche, swing until fell, repeat |
| `Code/PerfProbe.cs` | Rolling-window FPS + renderer/tree counts via `[Property, ReadOnly]`. Lisible par MCP bridge sans toucher au HUD debug |
| `Code/SelfTest.cs` | Headless harness phases : Init → Approach → Swing → Verify(Wood>0). Wait-on-condition, pas time-based |
| `Code/TerrainHeightmap.cs` | Procedural cone + 3-octave FBM noise terrain (Sandbox.Terrain), MaterialOverride sur `materials/ground.vmat` |
| `Code/MapBorders.cs` | Ring de mountain segments tagged "border" (pas "ground") au-delà de la forêt |
| `Code/ChipBurst.cs` | Chips/leaves/splinters custom-physics (no Rigidbody — pattern dans memory `sbox-particle-rigidbody-trap`) |
| `Code/Mat.cs` | Helper `AddTintedCube` (Model.Cube ignore Tint sauf si MaterialOverride = `materials/default.vmat`) |
| `Code/Sfx.cs` | Try/catch-wrapped Sound.Play |
| `Code/Tunables.cs` | Toutes les constantes gameplay : axe tier ladder, tree kind weights/scales/tints, fell physics, terrain shape, chip burst, swing feel |
| `Assets/scenes/main.scene` | Sun + Skybox + Fog + Ground (disabled at runtime) + Bootstrap (SceneStarter) + Camera |
| `Assets/materials/ground.vmat` | Tinted grass-green vmat — bypass le `g_vColorTint` alpha=0 de `materials/default.vmat` |

## Bindings (`ProjectSettings/Input.config`)

| Touche | Action |
|---|---|
| WASD | Move (Sandbox.PlayerController) |
| Souris | Look |
| Espace | Jump |
| Shift | Sprint |
| Click gauche | Swing axe |
| E ("Use") | Acheter axe upgrade (dans ShopArea) |
| R ("Reload") | Téléport au shop |
| B ("DebugToggle") | FPS + tree counts HUD overlay |

## Workflow Claude

Patterns + non-negotiables dans `CLAUDE.md` (root) et `Code/CLAUDE.md` (chargé quand on touche `Code/`). Hook automation :

- **PostToolUse hook** : `dotnet build` après chaque edit sur `Code/**/*.cs`. Échec → blocking message.
- **Stop hook** : `tools/selftest.ps1` si `Tree.cs` / `SceneStarter.cs` / `BeaverController.cs` / `GameState.cs` / `ShopArea.cs` modifiés. Échec → bloque le stop.

Hooks dans `.claude/settings.json`, scripts `tools/hooks/`.

## Source 2 / s&box gotchas

- Z is up. Unités = inches (1 m ≈ 39.37 u).
- `Model.Cube` ignore `ModelRenderer.Tint` sauf `MaterialOverride = materials/default.vmat`.
- `Scene.GetAllComponents<Component>()` retourne 0 — query par type concret ou interface (`IChoppable`).
- Standing tree Rigidbody **doit** être `MotionEnabled = false` (sinon player les fait tomber juste en marchant dedans).
- `System.Environment.*` est sur le deny-list du compiler — flags via `[ConVar]` + `sbox-server.exe +name value`.

Voir `Code/CLAUDE.md` pour la liste complète des patterns.
