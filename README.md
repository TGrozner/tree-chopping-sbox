# Tree Chopping — s&box

Mow-the-lawn-like (style Plants vs. Zombies' Lawn) avec mécanique d'arbres qui tombent à la Valheim. Tu chop, le bois s'accumule, tu upgrades ta hache au shop sur le sommet, tu redescends, tu chop plus gros.

Multiplayer s&box (Source 2 + C#/.NET, up to 4 players). Pas de score, pas de runs : continuous play.

## Loop

1. Tu spawn sur un plateau au sommet d'une montagne. Le totem doré + le shop disk sont sous tes pieds.
2. Tu descends la pente. Forêt biome-biased autour : saplings (1 chop) près du shop, veterans (8 chops) au bord.
3. Click gauche = swing. Chop multiple selon le tier (T0=1 → T6=20 chops par swing).
4. Tree fells, le trunk roule, devient un landed log, puis se rechoppe pour split en WoodItems pickables. Le backpack se dépose ensuite au WOOD DEPOT.
5. R = téléport au shop. Stations visibles : Tools / Depot / Upgrades / Prestige. DEPOT flush le backpack vers le stockpile.
6. La première couronne de saplings est dense façon mow-the-lawn ; plus loin, la forêt devient biome-biased avec saplings proches et veterans au bord.
7. Long-term : 500 lifetime wood → Replant prestige. Gain `√(woodEarned/50)` Sapling Spirits = +1% wood perma chacun. Reset des tiers de run.
8. Wood + wood types + tiers + spirits persistent dans `progress.json` (`FileSystem.Data`, local profile actuel).

Trees tombent en physique, mais les voisins debout restent kinematic jusqu'à être wake par un ImpactEffect scripté façon Valheim : vitesse d'impact → damage → fell/split si HP tombe à 0.

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
| `Code/SceneStarter.cs` | Bootstrap : singletons, terrain procédural, mountain borders, player spawn, forêt biome-biased, hub/shop stations |
| `Code/Tree.cs` | Multi-chop + StartFell + landed-log split + wood item drops. Biome-biased Kind picker (Easy/Hard weight blend by distance to spawn) |
| `Code/AxeController.cs` | Swing state machine (Idle → WindUp → Recovery), hit-stop, FOV punch, axe wired to hand_R |
| `Code/GameState.cs` | Persistence (FileSystem.Data/progress.json) : Wood/Finewood/CoreWood stockpiles + backpack, AxeTier 0..6, Speed/Luck/Power tiers 0..5, PetTier 0..5, Spirits + TotalWoodEarned (prestige). Derived : ChopPower, WoodMultiplier, SpeedMultiplier, LuckChance |
| `Code/ShopStation.cs` | Stations Tools / Depot / Upgrades / Prestige autour du hub |
| `Code/WoodItem.cs` | Items bois pickables, magnet de proximité, backpack |
| `Code/WoodHud.cs` | HUD immediate-mode (crosshair, wood balance pulse, axe tier badge, shop hint, teleport hint) |
| `Code/AutoPlay.cs` | Autonomous chop-loop in-forest driver (Active=true via MCP bridge). Teleporte le player vers le tree le plus proche, swing until fell, repeat |
| `Code/PerfProbe.cs` | Rolling-window FPS + renderer/tree counts via `[Property, ReadOnly]`. Lisible par MCP bridge sans toucher au HUD debug |
| `Code/SelfTest.cs` | Headless harness phases : player swing path, spawn distribution, stump/respawn, split, pickup/deposit, cascade, too-hard, stats, prestige. Wait-on-condition, pas time-based |
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
| E ("Use") | Station Depot : déposer le backpack |
| 1-7 ("Slot1".."Slot7") | Actions de la station active (tools/upgrades/prestige) |
| R ("Reload") | Téléport au shop |
| B ("DebugToggle") | FPS + tree counts HUD overlay |

## Workflow Codex

Patterns + non-negotiables dans `AGENTS.md` (root) et `Code/AGENTS.md` (chargé quand on touche `Code/`). Hook automation :

- **PostToolUse hook** : `dotnet build` après chaque edit sur `Code/**/*.cs`. Échec → blocking message.
- **Stop hook** : `tools/selftest.ps1` si `Tree.cs` / `SceneStarter.cs` / `AxeController.cs` / `GameState.cs` / `WoodItem.cs` / `ShopStation.cs` modifiés. Échec → bloque le stop.

Hooks dans `.codex/hooks.json` (et `.claude/settings.json` en compat legacy), scripts `tools/hooks/`.

## Source 2 / s&box gotchas

- Z is up. Unités = inches (1 m ≈ 39.37 u).
- `Model.Cube` ignore `ModelRenderer.Tint` sauf `MaterialOverride = materials/default.vmat`.
- `Scene.GetAllComponents<Component>()` retourne 0 — query par type concret ou interface (`IChoppable`).
- Standing tree Rigidbody **doit** être `MotionEnabled = false` (sinon player les fait tomber juste en marchant dedans).
- `System.Environment.*` est sur le deny-list du compiler — flags via `[ConVar]` + `sbox-server.exe +name value`.

Voir `Code/AGENTS.md` pour la liste complète des patterns.
