# CLAUDE.md — Tree Chopping (s&box)

Notes pour les futures sessions Claude qui bossent sur ce projet en autonomie. Ce projet est un proto **s&box** (moteur Source 2 + C#/.NET, par Facepunch). Voir `README.md` pour le pitch gameplay.

## Non-negotiables — lis ça avant de coder

Chaque ligne ici a déjà coûté du debug à une session précédente. Pas d'exception.

1. **s&box API docs → context7 first, NOT WebFetch.** sbox.game est JS-rendered et renvoie littéralement la string "s&box". Utilise le MCP context7 avec `/llmstxt/sbox_game_llms_txt` (le plus dense) ou `/websites/sbox_game_api`. Source of truth locale : `C:\Program Files (x86)\Steam\steamapps\common\sbox\bin\managed\Sandbox.*.xml` — grep dedans quand context7 manque de précision.

2. **Headless est le loop par défaut, pas l'éditeur :**
   - `dotnet build C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj` pour valider les types.
   - `sbox-server.exe +game <sbproj> +maxplayers 1` pour le lifecycle + physique (pas de rendu, pas d'input client). `Log.Info` → stdout.
   - **`tools\selftest.ps1`** lance le mow-the-lawn scenario end-to-end via la ConVar `+tc_selftest 1` → exit 0 = PASS / 1 = FAIL / 3 = TIMEOUT en ~12 s. Phases : Init → Approach → Swing → Verify (wood gained, target tree no longer standing). **À relancer après TOUT changement dans Tree / GameState / SceneStarter.SpawnForest / BeaverController swing path.** Build clean ≠ scénario vert.
   - `sbox.exe` ne marche PAS sur projets locaux (traite le sbproj comme un cloud package et 404). Seuls `sbox-dev.exe` (éditeur) et `sbox-server.exe` (headless) gèrent du dev non shippé.

3. **`System.Environment.*` est sur le deny-list de la whitelist du compiler s&box.** `GetEnvironmentVariable`, `GetCommandLineArgs`, etc. font échouer la compile avec `"is not allowed when whitelist is enabled"`. Pour les flags de lancement → `[ConVar("name")]`, set via `sbox-server.exe ... +name value`.

4. **Component enumeration : query par interface, pas par base type.** `Scene.GetAllComponents<Component>()` renvoie 0. `Scene.GetAllComponents<T>()` accepte un type concret ou une **interface** (la doc XML dit literally *"This can include interfaces."*). Pour scanner par capacité (IChoppable, IInteractable…) query l'interface, pas un `Component` + `OfType<>`.

5. **Pas de F-keys pour les bindings.** L'éditeur s&box intercepte F1-F12 pour ses propres shortcuts et ils passent pas fiablement en Play. Lettres ou combos modifiés seulement. État courant : `DebugToggle=B`, `PlayShortcut=Ctrl+Shift+P`.

6. **Le clavier est AZERTY.** Si tu scripts de l'input via SendInput / `keybd_event` vers la Play view de l'éditeur, envoie `VK_Z` pour Forward (pas `VK_W`), `VK_Q` pour Left. La string `"W"` dans `Input.config` cible le *caractère* W, qui sur AZERTY vient de la touche physique en position Z.

7. **`SetCursorPos` dans le viewport Play envoie de l'`Input.AnalogLook`** — chaque move souris pivote la caméra. Donc "click puis screenshot stable" ne marche pas, le click déplace d'abord le curseur. Privilégier le headless selftest pour valider la logique. GUI screenshot ([[sbox-screenshot-runtime]]) seulement quand il faut des pixels.

8. **Rigidbody arbres debout : `MotionEnabled = false` obligatoire.** Sinon le castor les fait tomber juste en marchant dedans — wood serait gagné sans swing, et l'arène collapserait toute seule. `StartFell` flippe la valeur à `true` quand `ChopsRemaining` tombe à 0 (multi-chop). Pas de cascade explicite : les collisions rigidbody naturelles font le reste.

9. **Un test qui prend un raccourci passe pendant que le chemin réel est cassé.** Le premier SelfTest appelait `Tree.Chop()` direct et a caché 2 bugs prod (`GetAllComponents<Component>` retournant 0, `Tree.IChoppable.IsValid` excluant les landed logs). Chaque phase de selftest DOIT exercer au moins un code path joueur réel — `BeaverController.DebugSwing → ChooseSwingTarget → Chop`, pas `Chop()` direct. **Corollaire TDD : quand tu ajoutes un nouveau pipeline runtime, écris d'abord la phase de selftest qui DOIT échouer sans ton impl, puis ajoute l'impl. Si la phase passe avant que tu codes l'impl, elle ne teste rien.**

10. **Conventions Source 2.** Z is up (pas Y). Unités = inches (`Tunables.UnitsPerMeter = 39.37`). `Vector3.Forward = +X`. Spawn de cubes via `WorldScale = wantedSize / Tunables.CubeBase` parce que `Model.Cube` est le dev cube natif 50u. Quaternions = (x, y, z, w).

11. **Style de code de ce repo.** Tabs + `if ( foo )` (espaces dans les parens). Default à zéro commentaires : un commentaire ne se justifie que quand le *why* est non-évident (contrainte cachée, incident passé, invariant subtil). Pas de wrappers "legacy", pas de stubs pour des besoins hypothétiques. Trois lignes similaires battent une abstraction prématurée. FR OK dans les commits + logs + CLAUDE.md ; XML / docs sbox-facing restent EN.

12. **Agents parallèles éditent parfois les mêmes fichiers.** Re-read avant Edit si tu as `"File has been modified"`, re-applique minimalement. Pas de commit sans `"commit ça"` explicite — l'utilisateur fait ses propres petits commits `phase2X:`.

Workflow type pour un changement runtime :
1. `dotnet build` → types OK.
2. `tools\selftest.ps1` → pipeline end-to-end + anti-collision OK.
3. Si tu touches HUD / particles / camera / rendering / son → dis explicitement que le headless ne valide pas le visuel, demande un Play GUI. Ne réclame jamais "ça marche" sans preuve.

**Harness automation (.claude/settings.json)** :
- **PostToolUse hook** lance `dotnet build Code/tree_chopping.csproj` après chaque Edit/Write/MultiEdit sur `Code/**/*.cs`. Échec → exit 2 + stderr remonté ici comme blocking message. Étape 1 du workflow appliquée automatiquement.
- **Stop hook** lance `tools\selftest.ps1` (1 seed, ~12s) si `Tree.cs` / `GameState.cs` / `SceneStarter.cs` / `BeaverController.cs` / `ShopArea.cs` apparaissent dans `git status`. Échec → bloque le stop une fois ; appel de Stop suivant override (`stop_hook_active` guard). Étape 2 partiellement appliquée — seulement pour les fichiers du chop/wood path.
- **Limitation à connaître :** le Stop hook utilise `git status`, pas le track des fichiers édités cette session. Si la branche a des changements pendants sur un fichier critical avant que tu démarres, le hook se déclenchera quand même. Pour bypass délibéré : commit/stash avant, ou Stop deux fois (la 2e passe).
- Scripts : `tools/hooks/post-edit-build.ps1` et `tools/hooks/stop-selftest.ps1` — modifie-les si la couverture ne te convient pas.

User = Thomas : FR, 10 ans Godot, deux mois s&box. Préfère terse, pas de hand-holding, pas de résumé trailing. Confirmer avant destructive ops (`git reset`, force-push, mass-delete). Quand tu as plusieurs streams search/edit qui ne touchent pas les mêmes fichiers, fan-out en Agent calls parallèles — c'est le default qu'il a demandé. Sur un bug runtime non-trivial (cascade qui ne chaîne pas, timing physique foireux, race conditions OnAwake/OnStart), tu peux écrire "ultrathink" ou "think harder" dans ton prompt à toi-même au moment de planifier — c'est une trigger phrase Anthropic qui bumpe le reasoning budget sans cérémonie `/effort`.

## TL;DR — comment bosser sans l'éditeur

1. **Compiler le code** (validation rapide, n'a pas besoin de Steam/sbox-dev ouvert) :
   ```powershell
   dotnet build C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj
   ```
   - Le `.csproj` est **gitignored** et régénéré automatiquement par sbox-dev. S'il manque, ouvre l'éditeur une fois ou copie le pattern de `Editor\tree_chopping.editor.csproj`.
   - Sortie : `Code\bin\tree_chopping.dll`.

2. **Run headless** (vérifier que ça s'exécute vraiment, pas juste que ça compile) :
   ```powershell
   $exe  = "C:\Program Files (x86)\Steam\steamapps\common\sbox\sbox-server.exe"
   $proj = "C:\dev\tree-chopping-sbox\tree_chopping.sbproj"
   $out  = "$env:TEMP\sbox-srv-out.log"
   Remove-Item $out -ErrorAction SilentlyContinue
   $p = Start-Process $exe -ArgumentList "+game `"$proj`" +maxplayers 1" `
        -PassThru -NoNewWindow `
        -RedirectStandardOutput $out -RedirectStandardError "$env:TEMP\sbox-srv-err.log"
   Start-Sleep -Seconds 8
   if ( -not $p.HasExited ) { Stop-Process -Id $p.Id -Force }
   Get-Content $out | Select-String "SceneStarter|Exception|FATAL"
   ```
   - **sbox-server.exe charge le `.sbproj` singleplayer sans broncher.** Il charge la scène, fire OnAwake/OnStart, run OnFixedUpdate à 50Hz, exécute la physique. `Log.Info(...)` part en stdout. Si ça throw, l'exception apparaît.
   - Validation = **logique, lifecycle, spawn, tick, physique**. Pas le rendu, pas l'input clavier (pas de client connecté).
   - Pour visuel / input / HUD / inspector → sbox-dev.exe GUI (Play F5).
   - **sbox-standalone.exe NON** : il veut `assets/standalone.manifest.json` qui n'existe que pour les jeux shippés.

3. **Tester en jeu (visuel)** : sbox-dev.exe (GUI). Le hotload recompile à la sauvegarde — pas besoin de redémarrer pour un changement de méthode.

4. **Trigger Play en autonomie** (pour screenshots/vidéos sans demander à l'utilisateur) :
   - `Editor/PlayShortcut.cs` ajoute un `[Shortcut(..., "F5")]` mappé sur `EditorScene.TogglePlay()`.
   - Si l'éditeur est ouvert : `SendKeys "{F5}"` sur sa MainWindow toggle Play (Qt SendKeys marche pour les shortcuts enregistrés via attribute).
   - Capture via `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT=3)` — fonctionne même si la fenêtre est occluded par autre chose.
   - Pattern PowerShell complet dans `~/.claude/projects/.../memory/sbox-screenshot-runtime.md`.

### Workflow Claude type

Quand tu changes du runtime code (un OnUpdate, un spawn, un Component) :
1. `dotnet build` — types/signatures.
2. `sbox-server` 6-10s — fire-fait-fail, exception, position des objets spawnés.
3. Reporte ce qui s'est passé en stdout. Pas "ça compile" tout court. Pas "test-le toi-même" pour des trucs vérifiables headless.

Si tu touches à du rendu/input/HUD : prévenir que sbox-server ne validera pas, demander un Play GUI.

## Avec l'éditeur — MCP Claude Bridge

Installé 2026-05-19. Un MCP `sbox` (LouSputthole/Sbox-Claude v1.3.1) expose 99 tools qui drivent l'éditeur s&box vivant : `start_play`/`stop_play`/`is_playing`, `take_screenshot`, `get_scene_hierarchy`, `set_property`, `describe_type`/`get_method_signature` (reflection live), `invoke_button`, etc. Détails complets et catalogue dans [[sbox-claude-bridge-mcp]].

**Prérequis runtime** :
1. `sbox-dev.exe` ouvert sur `tree_chopping.sbproj`.
2. **View → Claude Bridge** — le dock doit rester visible (le `[EditorEvent.Frame]` ne tick que dock-on-screen, sinon silent timeout).
3. Premier check : tool `get_bridge_status` → `connected:true, handlerCount:100`.

**Quand l'utiliser vs headless** : headless reste le default pour types/lifecycle/régression. Bridge brille pour : inspecter l'état runtime live, trigger Play sans SendKeys, screenshot sans PrintWindow, vérifier une API s&box via reflection au lieu de grep XML. Si l'éditeur n'est pas ouvert → bridge KO, retomber sur `sbox-server` + `tools\selftest.ps1`.

**Paths** : repo MCP server à `C:\dev\sbox-claude\`, addon bridge dans `Libraries/claudebridge/` (committable), IPC dans `%TEMP%\sbox-bridge-ipc\`.

## Layout du projet

| Chemin | Rôle |
|---|---|
| `tree_chopping.sbproj` | Manifest s&box (JSON). `StartupScene`, `TickRate`, `GameNetworkType: Singleplayer`. |
| `tree_chopping.slnx` | Solution VS qui pointe vers `Code/`, `Editor/`, et les addons sbox dans `Program Files (x86)\Steam\steamapps\common\sbox\`. |
| `Code/` | Assembly de jeu. Namespace `TreeChopping`. `Assembly.cs` = global usings. **`Code/CLAUDE.md`** = patterns API s&box / Source 2 / hotload, chargé seulement quand tu touches `Code/**/*.cs`. |
| `Editor/` | Assembly **éditeur** séparée (`tree_chopping.editor.csproj`) — code outils, pas sandboxé. Actuellement vide à part les usings. |
| `Assets/scenes/main.scene` | Scène JSON minimale : `Sun` (DirectionalLight, overridden au runtime par SceneStarter.SetupLighting) + `Skybox` (SkyBox2D, warm orange tint) + `Fog` (GradientFog, copper-orange `0.92,0.62,0.42`) + `Ground` (plane initial — désactivé au boot, terrain heightmap prend le relais) + `Bootstrap` (SceneStarter, TreeCount=400, MinSpacing=180, BeaverSpawn=(-1000,0,80), SpawnPadRadius=180) + `Camera` (FieldOfView=72, Tonemapping HableFilmic + Bloom + ColorGrading). Tout le reste (Citizen player + axe, terrain procédural, mountain borders, forêt biome-biased, ShopArea, WoodHud, GameState, AutoPlay) est spawné au runtime par `SceneStarter`. |
| `ProjectSettings/Input.config` | Bindings clavier/gamepad. **Tu lis ces noms** dans `Input.Pressed("Jump")` etc. "Use" (E) achète un upgrade dans ShopArea ; "Reload" (R) téléporte le joueur au spawn shop. |
| `ProjectSettings/Collision.config` | Matrice de collision. |
| `Libraries/` | Libs externes. Contient `claudebridge/` (MCP bridge addon — cf. section "Avec l'éditeur"). `tree_chopping.sbproj.PackageReferences = ["facepunch.woodaxe"]` — sbox-dev DL au project-open, pas de fichier local. |
| `tools/` | `selftest.ps1` (harness mow-the-lawn scenario, exit 0/1/3 = PASS/FAIL/TIMEOUT) + `session-prompt.md` + `hooks/` (scripts appelés par `.claude/settings.json`). |
| `.claude/settings.json` | Hooks Claude Code — voir section "Harness automation" plus haut. PostToolUse = build auto, Stop = selftest auto. |
| `.sbox/` | Cache éditeur — généré, **ne pas commiter** (déjà gitignored). |

Fichiers gitignored à noter : `*.csproj`, `*.slnx`, `*.sln`, `obj/`, `bin/`, `.sbox/`, `*.*_c` (assets compilés sauf `*.shader_c`). Donc **le sln/csproj présents sur disque ne sont pas dans git** — c'est normal.

## Architecture gameplay actuelle

**Pivot 2026-05-20 : mow-the-lawn-like façon Valheim.** Tu spawnes au sommet d'une montagne où vit ton shop. Tu descends, tu chop des arbres, ils tombent à la Valheim (multi-chop selon le tier de hache) et **propagent par physique rigidbody naturelle** — pas de CascadeStrike scripté, juste des collisions. Les arbres droppent du bois quand ils landed. Tu remontes au shop, tu upgrade ton axe (T0→T3), tu redescends. Continuous play, pas de "fin de run".

### Layout des biomes
Anneau de difficulté centré sur le spawn :
- proches du shop = saplings (1 chop, 1 wood) — pratique
- au milieu = trees normaux (3 chops, 3 wood)
- bord d'arène = veterans (8 chops, 8 wood) + brittles éparpillés
Sélection biome-biased par `biomeDifficulty ∈ [0, 1]` calculé `(dist - SpawnPadRadius) / (ArenaRadius - SpawnPadRadius)` à la pose de chaque arbre. `Tree.PickKindFromHash` lerpe entre `TreeKindWeightsEasy` et `TreeKindWeightsHard`. Mythics (gold-tinted, +12 wood) sortent partout, 1/120.

### Pipeline runtime

```
SceneStarter.OnStart()
 ├─ Singletons (créés si absents) : GameState · WoodHud · AutoPlay · PerfProbe
 │   · SelfTest? (gated par tc_selftest)
 ├─ DisableSceneGround() — la plane par défaut s'efface, le terrain heightmap la remplace
 ├─ TerrainHeightmap.Spawn(scene, seed, BeaverSpawn) — cône radial + 3-octave FBM noise,
 │   plateau au sommet pour le shop, tagged "ground"
 ├─ MapBorders.Spawn(scene, Vector3.Zero) — 40 segments en anneau (BorderRadius=2750)
 │   au-delà de la forêt, tagged "border" (pas "ground" pour pas que les trees y spawnent dessus)
 ├─ ResolveBeaverSpawnGround() — raycast au point BeaverSpawn pour poser le castor au sol
 ├─ SpawnBeaver(camera) : Player GameObject + Sandbox.PlayerController (third-person,
 │   camera + input + animator owned) + child SkinnedModelRenderer (Citizen vmdl) +
 │   BeaverController (handles swing only) + axe (facepunch.woodaxe) parenté à hand_R
 ├─ SpawnShop() : ShopArea au ResolvedBeaverSpawn + visual disk wood-amber au sol
 └─ SpawnForest() : N Tree dans un disque (Tunables.ArenaRadius=2500), MinSpacing=180
                    ├─ uniform disc sampling avec √u correction (forêt 360° autour
                    │  du joueur — l'ancien filtre +X-only-of-beaver de l'ère
                    │  bowling a été viré phase5f)
                    ├─ ValueNoise2D + Hash2D gate (densité > ArenaDensityThreshold)
                    ├─ keepout central + SpawnPadRadius autour du shop
                    ├─ raycast au sol pour foot Z (terrain contour)
                    └─ Tree.SpawnAt(scene, pos, biomeDifficulty)

Player loop (Sandbox.PlayerController + BeaverController) :
  PlayerController : WASD + souris + jump + third-person camera. On ne touche pas.
  BeaverController.UpdateSwing :
    ├─ State machine : Idle → WindUp (anim b_attack) → Impact → Recovery → Idle
    ├─ Cooldown via Recovery (Tunables.SwingRecoveryDuration=0.18s)
    ├─ Aim : sphere-sweep depuis le reticle (WYSIWYG), fallback cone+range
    ├─ Tree.Chop(forward, chopPower) ← chopPower vient de GameState.ChopPower
    │   (Tunables.AxeTierChopPower[tier], T0=1, T1=2, T2=3, T3=5)
    └─ Si ChopsRemaining tombe à 0 → Tree.StartFell

  Reload (R) : BeaverController.TeleportTo(SpawnShop) — pour remonter au shop fast.

Tree pipeline (multi-chop + natural cascade) :
  Tree.Chop(direction, chopPower) :
    ├─ ChopsRemaining -= chopPower
    ├─ Si ChopsRemaining > 0 : reste debout, juste chip burst + audio
    └─ Si <= 0 : StartFell(direction)
  Tree.StartFell :
    ├─ MotionEnabled = true (était false debout — gotcha #8 anti-bump)
    ├─ ApplyTorque + ApplyImpulse au haut du trunk (FellTorque/FellPush, slow-tip ramp)
    └─ Leaves burst (canopy "shedding")
  Tree.OnFixedUpdate.TickFall :
    └─ Quand upDot < TreeFallenUpDotMax → BecomeLandedLog
  Tree.BecomeLandedLog :
    ├─ Damping landed (TreeAngularDampLanded / TreeLinearDampLanded)
    ├─ Audio "log break"
    └─ GiveWoodOnce → GameState.AddWood(reward × tier multiplier)
  **Cascade** : pas de code dédié. Les collisions rigidbody natives propagent l'énergie.
    Un trunk qui tombe peut bumper un voisin debout — si l'impulsion est assez forte
    physiquement (mass × velocity), le voisin tombe aussi. Pas de torque scripté,
    pas de ricochet. Choix 2026-05-20 ("Naturelle uniquement") pour éviter le pinball.

Shop / progression (GameState + ShopArea) :
  GameState (singleton, persistant via FileSystem.Data/progress.json) :
    ├─ Wood : int (gain via Tree.GiveWoodOnce)
    ├─ AxeTier : int (0..MaxAxeTier=3)
    ├─ TryUpgradeAxe() : pay AxeTierCosts[tier+1] wood → tier++
    └─ ChopPower / WoodMultiplier dérivés du tier
  ShopArea (à BeaverSpawn) :
    ├─ Detect player within Radius=250u
    ├─ HUD shop hint quand PlayerInside
    └─ Input "Use" (E) → TryUpgradeAxe + SFX

HUD (WoodHud, immediate-mode) :
  ├─ Crosshair centre, 3×3 px
  ├─ "WOOD" top-left + nombre, pulse à chaque gain
  ├─ "AXE TIER" top-right + "T{n}"
  ├─ Shop hint quand PlayerInside : "[E] UPGRADE AXE · T0→T1 · costs 8 wood"
  ├─ "[R] teleport to shop" en bas (hidden quand PlayerInside)
  └─ DebugToggle (B) : FPS overlay + tree counts (standing/falling/landed)

Self-test (headless, mow-the-lawn scenario) :
  SelfTest (ConVar tc_selftest=1) : Init → Approach → Swing → Verify.
  Init : reset GameState, pick le plus proche tree debout.
  Approach : TeleportTo en face du tree à 60u, set tool = Axe.
  Swing : DebugSwingVerbose en loop (max 20) jusqu'à IsStanding=false.
  Verify : assert GameState.Wood a augmenté + tree plus debout.
  Bonus diag logs : [Tree] landed Kind=X pos=Y, [Tree] GiveWood +N (now=M).
```

**Pour ajouter un système** :
- Manager singleton → ajouter une `EnsureSingleton<T>(name)` dans `SceneStarter.OnStart()`.
- Entité gameplay → spawner method statique, appelée par `SceneStarter.SpawnXxx`.
- Drop persistant (décor lourd, lumière) → main.scene via éditeur ou MCP bridge.
- Nouveau tool ou kind → étendre `ToolKind` enum (BeaverController.cs) + `IChoppable.AcceptsTool` + Tunables array, plus la sélection biome-biased si applicable.

## API s&box, Source 2, hotload, doc

→ Déplacé dans `Code/CLAUDE.md` (chargé seulement quand tu touches `Code/**/*.cs`). Y a : Component lifecycle, attributs Property/RequireComponent/Sync, `Components.Get` patterns, spawn par code, `Scene.Trace`, Input, HUD immediate-mode, Z-up / inches / WorldScale / `Model.Cube` tint gotcha, hotload pièges, MSTest setup, et où chercher la doc à jour (context7, XML locaux, base addon).

## Workflow Claude — recommandé pour ce repo

1. **Pas de commit sans demande explicite.** Lire CLAUDE.md global comportement.
2. **Compile en local** après chaque changement non-trivial : `dotnet build Code\tree_chopping.csproj`. Ça valide les types/signatures sans lancer le jeu.
3. **Pour valider le gameplay**, demander à l'utilisateur de tester dans sbox-dev — pas de moyen de scripter Play.
4. **Pour modifier la scène**, éditer `Assets\scenes\main.scene` à la main (JSON) ou demander à l'utilisateur de faire le drag-drop dans l'éditeur. Le JSON est trivial à éditer mais il faut respecter `__guid`, `__type` et les valeurs `"x,y,z,w"` séparées par virgule sans crochets.
5. **Tunables d'abord** : les constantes gameplay (`Tunables.cs`) sont la première chose à toucher pour tuner. Pas besoin de scène ouverte.
6. **Hotload** = en cas de modification d'un default d'un static field, prévenir l'utilisateur que redémarrer l'éditeur peut être nécessaire.
