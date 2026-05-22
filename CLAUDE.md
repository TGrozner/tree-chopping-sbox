# CLAUDE.md — Tree Chopping (s&box)

Notes pour les futures sessions Claude qui bossent sur ce projet en autonomie. Ce projet est un proto **s&box** (moteur Source 2 + C#/.NET, par Facepunch). Voir `README.md` pour le pitch gameplay.

## Non-negotiables — lis ça avant de coder

Chaque ligne ici a déjà coûté du debug à une session précédente. Pas d'exception.

1. **s&box API docs → context7 first, NOT WebFetch.** sbox.game est JS-rendered et renvoie littéralement la string "s&box". Utilise le MCP context7 avec `/llmstxt/sbox_game_llms_txt` (le plus dense) ou `/websites/sbox_game_api`. Source of truth locale : `C:\Program Files (x86)\Steam\steamapps\common\sbox\bin\managed\Sandbox.*.xml` — grep dedans quand context7 manque de précision.

2. **Headless est le loop par défaut, pas l'éditeur :**
   - `dotnet build C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj` pour valider les types.
   - `sbox-server.exe +game <sbproj> +maxplayers 1` pour le lifecycle + physique (pas de rendu, pas d'input client). `Log.Info` → stdout.
   - **`tools\selftest.ps1`** lance le mow-the-lawn scenario end-to-end via la ConVar `+tc_selftest 1` → exit 0 = PASS / 1 = FAIL / 3 = TIMEOUT. Le harness dérive son contrat depuis `SelfTest.Phase` et couvre swing réel, spawn distribution, stump/respawn, split, pickup/deposit, cascade, too-hard, stats, prestige. **À relancer après TOUT changement dans Tree / GameState / SceneStarter.SpawnForest / AxeController swing path.** Build clean ≠ scénario vert.
   - `sbox.exe` ne marche PAS sur projets locaux (traite le sbproj comme un cloud package et 404). Seuls `sbox-dev.exe` (éditeur) et `sbox-server.exe` (headless) gèrent du dev non shippé.

3. **`System.Environment.*` est sur le deny-list de la whitelist du compiler s&box.** `GetEnvironmentVariable`, `GetCommandLineArgs`, etc. font échouer la compile avec `"is not allowed when whitelist is enabled"`. Pour les flags de lancement → `[ConVar("name")]`, set via `sbox-server.exe ... +name value`.

4. **Component enumeration : query par interface, pas par base type.** `Scene.GetAllComponents<Component>()` renvoie 0. `Scene.GetAllComponents<T>()` accepte un type concret ou une **interface** (la doc XML dit literally *"This can include interfaces."*). Pour scanner par capacité (IChoppable, IInteractable…) query l'interface, pas un `Component` + `OfType<>`.

5. **Pas de F-keys pour les bindings.** L'éditeur s&box intercepte F1-F12 pour ses propres shortcuts et ils passent pas fiablement en Play. Lettres ou combos modifiés seulement. État courant : `DebugToggle=B`, `PlayShortcut=Ctrl+Shift+P`.

6. **Le clavier est AZERTY.** Si tu scripts de l'input via SendInput / `keybd_event` vers la Play view de l'éditeur, envoie `VK_Z` pour Forward (pas `VK_W`), `VK_Q` pour Left. La string `"W"` dans `Input.config` cible le *caractère* W, qui sur AZERTY vient de la touche physique en position Z.

7. **`SetCursorPos` dans le viewport Play envoie de l'`Input.AnalogLook`** — chaque move souris pivote la caméra. Donc "click puis screenshot stable" ne marche pas, le click déplace d'abord le curseur. Privilégier le headless selftest pour valider la logique. GUI screenshot ([[sbox-screenshot-runtime]]) seulement quand il faut des pixels.

8. **Rigidbody arbres debout : `MotionEnabled = false` obligatoire.** Sinon le player les fait tomber juste en marchant dedans — wood serait gagné sans swing, et l'arène collapserait toute seule. `StartFell` flippe la valeur à `true` quand `ChopsRemaining` tombe à 0 (multi-chop). Les voisins debout restent donc kinematic, et la cascade Valheim passe par `Tree.OnCollisionStart` + `ApplyImpactDamage` : vitesse d'impact → damage → shake/fell/split. Garde le kinematic-standing, ne reviens pas à une propagation physique pure.

9. **Un test qui prend un raccourci passe pendant que le chemin réel est cassé.** Le premier SelfTest appelait `Tree.Chop()` direct et a caché 2 bugs prod (`GetAllComponents<Component>` retournant 0, `Tree.IChoppable.IsValid` excluant les landed logs). Chaque phase de selftest DOIT exercer au moins un code path joueur réel — `AxeController.DebugSwing → ChooseSwingTarget → Chop`, pas `Chop()` direct. **Corollaire TDD : quand tu ajoutes un nouveau pipeline runtime, écris d'abord la phase de selftest qui DOIT échouer sans ton impl, puis ajoute l'impl. Si la phase passe avant que tu codes l'impl, elle ne teste rien.**

10. **Conventions Source 2.** Z is up (pas Y). Unités = inches (`Tunables.UnitsPerMeter = 39.37`). `Vector3.Forward = +X`. Spawn de cubes via `WorldScale = wantedSize / Tunables.CubeBase` parce que `Model.Cube` est le dev cube natif 50u. Quaternions = (x, y, z, w).

11. **Style de code de ce repo.** Tabs + `if ( foo )` (espaces dans les parens). Default à zéro commentaires : un commentaire ne se justifie que quand le *why* est non-évident (contrainte cachée, incident passé, invariant subtil). Pas de wrappers "legacy", pas de stubs pour des besoins hypothétiques. Trois lignes similaires battent une abstraction prématurée. FR OK dans les commits + logs + CLAUDE.md ; XML / docs sbox-facing restent EN.

12. **Agents parallèles éditent parfois les mêmes fichiers.** Re-read avant Edit si tu as `"File has been modified"`, re-applique minimalement. Pas de commit sans `"commit ça"` explicite — l'utilisateur fait ses propres petits commits `phase2X:`.

Workflow type pour un changement runtime :
1. `dotnet build` → types OK.
2. `tools\selftest.ps1` → pipeline end-to-end + anti-collision OK.
3. Si tu touches HUD / particles / camera / rendering / son → **filmstrip** (cf. section "Visual cycle — filmstrip" plus bas). Le headless ne valide pas le visuel ; demander un Play GUI à Thomas est un dernier recours, pas le default.

**Harness automation (.claude/settings.json)** :
- **PostToolUse hook** lance `dotnet build Code/tree_chopping.csproj` après chaque Edit/Write/MultiEdit sur `Code/**/*.cs`. Échec → exit 2 + stderr remonté ici comme blocking message. Étape 1 du workflow appliquée automatiquement.
- **Stop hook** lance `tools\selftest.ps1` (1 seed, ~12s) si `Tree.cs` / `GameState.cs` / `SceneStarter.cs` / `AxeController.cs` / `WoodItem.cs` / `ShopStation.cs` apparaissent dans `git status`. Échec → bloque le stop une fois ; appel de Stop suivant override (`stop_hook_active` guard). Étape 2 partiellement appliquée — seulement pour les fichiers du chop/wood path.
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

## Visual cycle — filmstrip

**Si tu changes du visuel (HUD, particles, camera shake, tree fall feel, banner timing, log break sfx…) le selftest headless ne voit RIEN.** Avant 2026-05-21 ça forçait à demander à Thomas "lance Play et check" — boucle lente, jugement délégué. La filmstrip ferme cette boucle : le directeur in-game scripte un swing canonique et tu captures ~12 frames pour juger toi-même.

**Côté C# (`Code/FilmStrip.cs`)** — Component spawné inconditionnellement par `SceneStarter` (no-op tant que `Active=false`). Quand activé :
1. `Init` (10 ticks delay) → find player + state, optionally `ResetForTest`
2. `Setup` → pick le sapling le plus proche (fallback : nearest tree), teleport player à 80u en face
3. `Ready` (0.6s linger) → frame "before", idle pose visible
4. `Swinging` → trigger `DebugRequestSwing`, re-swing every 0.75s pour multi-chop
5. `Falling` → wait until tree leaves Standing+Falling state
6. `Landed` (1.5s linger) → log_break SFX, dust burst, SnapTrunkOnImpact, cam settle (pas de wood banner — Phase F le crédit passe par l'item pickup, hors séquence FilmStrip standard)
7. `Done` → orchestrator stoppe la capture

`Phase` + `Elapsed` + `WoodAtFinish` + `SwingsFired` exposed en `[Property, ReadOnly]` pour polling via bridge. `GameState.Save` skip quand FilmStrip est actif → la save user n'est jamais nukée par le `ResetForTest`.

**Chemin recommandé (validé 2026-05-22)** : `tools\capture-feel.ps1 -TargetKind Normal -AudioLog` pilote le bridge, capture des frames, génère `feel.mp4`, `contact-sheet.jpg`, `events.log`, puis permet de lire les PNG/contact sheet directement. Utilise ça en priorité pour les passes Valheim-feel ; le loop MCP manuel reste le fallback si le script casse.

**Côté orchestrateur manuel (Claude dans une session avec sbox-dev ouvert)** — procédure standard :
1. `mcp__sbox__get_bridge_status` → `connected:true`
2. `mcp__sbox__is_playing` ; si false → `mcp__sbox__start_play`
3. `mcp__sbox__get_scene_hierarchy` → repère le GameObject `FilmStrip`
4. `mcp__sbox__set_runtime_property` sur `FilmStrip` : `Active = true`
5. Loop : appelle `mcp__sbox__take_screenshot` ET `mcp__sbox__get_runtime_property Phase` en parallèle (un seul tool block, pas sequential — le bridge call est ~500-800ms, sequential = 3-4 captures max). Stoppe quand `Phase == Done`. **Gotcha** : le param `path` de `take_screenshot` est silencieusement ignoré — les frames atterrissent toutes en `C:\Program Files (x86)\Steam\steamapps\common\sbox\screenshots\sbox.<timestamp>.png`. Pour identifier les nouvelles, list le dossier avec `[System.IO.Directory]::GetFiles(...)` et trie par `LastWriteTime` desc.
6. `mcp__sbox__set_runtime_property` `Active = false` puis `mcp__sbox__stop_play`
7. **Tu es multimodal — lis chaque PNG via `Read`.** Map les frames aux phases (Ready→Swinging→Falling→Landed→Done) et juge le feel. Pas un seul hero shot — la séquence complète. [[feedback-deeper-video-review]] s'applique direct ici.
8. **Réalisme du throughput** : la séquence FilmStrip dure ~3-5s wall (sapling). À ~500-800ms par paire (screenshot + poll), tu auras ~4-6 frames utilisables sur l'arc complet. Pour plus de granularité sur une phase précise (ex: l'impact), modifier le linger correspondant dans `FilmStrip.cs` au lieu de spammer le bridge.

**Cold-start scripté** (éditeur pas ouvert encore) : `.\tools\filmstrip.ps1 -ColdStart` lance sbox-dev avec `+tc_filmstrip 1`. Le directeur auto-active sur le premier Play, plus besoin de flipper `Active=true`.

**Quand tu DOIS l'utiliser** : tout changement à HUD / chip burst / camera shake / FOV punch / fell torque / fall ramp / landing dust / banner timing / sfx layering. Tout ce qui se juge "à l'œil" mais pas via une assertion C#. Si tu changes `Tunables.SwingFovPunch` ou `Tunables.FellTorque` ou un seuil dans `ChopParticles` et que tu écris "ça devrait être mieux" sans capture, tu rates le feedback loop.

**Quand tu peux SKIP** : changements purement logiques (state machine refactor, GameState math, IChoppable plumbing) — le selftest headless les couvre.

`tools/filmstrip.ps1 -PrintProcedure` réimprime la cookbook à tout moment.

## Layout du projet

| Chemin | Rôle |
|---|---|
| `tree_chopping.sbproj` | Manifest s&box (JSON). `StartupScene`, `TickRate`, `GameNetworkType: Singleplayer`. |
| `tree_chopping.slnx` | Solution VS qui pointe vers `Code/`, `Editor/`, et les addons sbox dans `Program Files (x86)\Steam\steamapps\common\sbox\`. |
| `Code/` | Assembly de jeu. Namespace `TreeChopping`. `Assembly.cs` = global usings. **`Code/CLAUDE.md`** = patterns API s&box / Source 2 / hotload, chargé seulement quand tu touches `Code/**/*.cs`. |
| `Editor/` | Assembly **éditeur** séparée (`tree_chopping.editor.csproj`) — code outils, pas sandboxé. Actuellement vide à part les usings. |
| `Assets/scenes/main.scene` | Scène JSON minimale : `Sun` (DirectionalLight, overridden au runtime par SceneStarter.SetupLighting) + `Skybox` + `Fog` + `Ground` (plane initial — désactivé au boot, terrain heightmap prend le relais) + `Bootstrap` (SceneStarter) + `Camera`. Tout le reste (Citizen player + axe, terrain procédural, mountain borders, forêt biome-biased, ShopStation, WoodHud, GameState, AutoPlay) est spawné au runtime par `SceneStarter`. |
| `ProjectSettings/Input.config` | Bindings clavier/gamepad. **Tu lis ces noms** dans `Input.Pressed("Jump")` etc. "Use" (E) dépose au WOOD DEPOT ; slots 1-7 déclenchent les stations Tools/Upgrades/Prestige ; "Reload" (R) téléporte le joueur au spawn shop. |
| `ProjectSettings/Collision.config` | Matrice de collision. |
| `Libraries/` | Libs externes. Contient `claudebridge/` (MCP bridge addon — cf. section "Avec l'éditeur"). `tree_chopping.sbproj.PackageReferences = ["facepunch.woodaxe"]` — sbox-dev DL au project-open, pas de fichier local. |
| `tools/` | `selftest.ps1` (harness mow-the-lawn scenario, exit 0/1/3 = PASS/FAIL/TIMEOUT) + `filmstrip.ps1` (visual capture procedure / cold-start launcher — voir section "Visual cycle — filmstrip") + `session-prompt.md` + `hooks/` (scripts appelés par `.claude/settings.json`). |
| `.claude/settings.json` | Hooks Claude Code — voir section "Harness automation" plus haut. PostToolUse = build auto, Stop = selftest auto. |
| `.sbox/` | Cache éditeur — généré, **ne pas commiter** (déjà gitignored). |

Fichiers gitignored à noter : `*.csproj`, `*.slnx`, `*.sln`, `obj/`, `bin/`, `.sbox/`, `*.*_c` (assets compilés sauf `*.shader_c`). Donc **le sln/csproj présents sur disque ne sont pas dans git** — c'est normal.

## Architecture gameplay actuelle

**Pivot 2026-05-20 : mow-the-lawn-like façon Valheim.** Tu spawnes au sommet d'une montagne où vit ton shop. Tu descends, tu chop des arbres, ils tombent en deux temps (multi-chop debout → fell → landed log à chopper aussi → split direct en WoodItems pickables au sol). Tu ramasses (magnet de proximité), ton **BackpackWood** se remplit (cappé par BackpackTier). Tu remontes au shop, tu **flush au WOOD DEPOT → Wood (stockpile)**, puis tu upgrade aux autres stations (Tools / Upgrades / Prestige). Continuous play, pas de "fin de run". Cascade domino entre arbres = wake scripté déjà implémenté via ImpactEffect-style damage, pas de propagation physique pure (kinematic standing).

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
 │   · SelfTest? (gated par tc_selftest) · FilmStrip (always, no-op tant que Active=false)
 ├─ SetupLighting() — warm sun ×1.7 + sky fill ×2 + soft shadows
 ├─ DisableSceneGround() — la plane par défaut s'efface, le terrain heightmap la remplace
 ├─ TerrainHeightmap.Spawn(scene, seed, PlayerSpawn) — cône radial + 3-octave FBM noise,
 │   plateau au sommet pour le shop, tagged "ground"
 ├─ MapBorders.Spawn(scene, Vector3.Zero) — 40 segments en anneau (BorderRadius=2750)
 │   au-delà de la forêt, tagged "border" (pas "ground" pour pas que les trees y spawnent dessus)
 ├─ ResolvePlayerSpawnGround() — raycast au point PlayerSpawn pour poser le player au sol
 ├─ SpawnPlayerCharacter(camera) : Player GameObject + Sandbox.PlayerController (third-person,
 │   camera + input + animator owned) + child SkinnedModelRenderer (Citizen vmdl) +
 │   AxeController + axe (facepunch.woodaxe) parenté à hand_R
 ├─ SpawnShop() : HubAmphitheatre + HubProps au ResolvedPlayerSpawn, + 4× ShopStation
 │   (Tools / Depot / Upgrades / Prestige) en cercle autour, + totem vertical 1200u
 │   avec cap doré (Mythic tint) visible from distance comme nav-marker
 ├─ SpawnForest() : trees dans la bande [SpawnPadRadius .. InitialOuterRadius=1200u]
 │   au boot — le reste du arena est verrouillé derrière les gates :
 │                    ├─ uniform area sampling annulaire (forêt 360° autour du joueur)
 │                    ├─ ValueNoise2D + Hash2D gate (densité > ArenaDensityThreshold)
 │                    ├─ keepout central + SpawnPadRadius autour du shop
 │                    ├─ raycast au sol pour foot Z (terrain contour)
 │                    └─ Tree.SpawnAt(scene, pos, biomeDifficulty)
 └─ SpawnPet(player) : Pet GameObject orbite le player (auto-sync avec GameState.PetTier)

Player loop (Sandbox.PlayerController + AxeController) :
  PlayerController : WASD + souris + jump + third-person camera. On ne touche pas.
  AxeController.UpdateSwing :
    ├─ State machine : Idle → WindUp (anim b_attack) → Impact → Recovery → Idle
    ├─ Cooldown via Recovery (Tunables.SwingRecoveryDuration=0.18s)
    ├─ Aim : sphere-sweep depuis le reticle (WYSIWYG), fallback cone+range
    ├─ Tree.Chop(forward, chopPower) ← chopPower vient de GameState.ChopPower
    │   (Tunables.AxeTierChopPower[tier], T0=1, T1=2, T2=3, T3=5)
    └─ Si ChopsRemaining tombe à 0 → Tree.StartFell

  Reload (R) : AxeController.TeleportTo(SpawnShop) — pour remonter au shop fast.

Tree pipeline (Phase F/G — Valheim two-stage chop) :
  Tree.Chop(direction, chopPower) — sur standing tree :
    ├─ Tier gate (AxeTier < TreeKindMinAxeTier[Kind]) → KickWobble + "axe too weak" hint, no HP loss
    ├─ ChopsRemaining -= chopPower
    ├─ Si > 0 : KickWobble (lean réactif décay 0.6s) + DarkenTrunkOnce + SpawnChopNotch (cube sombre stuck on trunk side)
    └─ Si <= 0 : StartFell(direction)
  Tree.StartFell :
    ├─ Reset _baseRotation (wobble cleared) ; destroy _rootStump (no more promontoires)
    ├─ MotionEnabled = true (était false debout — gotcha #8 anti-bump)
    ├─ Big canopy leaves burst (3 directions, ~80 leaves total)
    └─ Groan SFX (log_break pitched 0.48..0.62 = stretched timber-creak)
  Tree.TickFall (OnFixedUpdate) :
    ├─ Mass-scaled ApplyTorque sur (Up × _fellDir), no impulse — gravité + torque only
    │   (sapling et veteran ont la même accel angulaire, fix du "petits arbres flyent")
    └─ Trigger BecomeLandedLog when upDot < TreeFallenUpDotMax OR 5s timeout (stuck against neighbor)
  FallenLog.BecomeLandedLog (n'AUTO-CREDIT PLUS de wood — Phase F change) :
    ├─ Damping landed (TreeAngularDampLanded / TreeLinearDampLanded)
    ├─ log_break SFX volume × speedFrac (mass-scaled landing pitch)
    ├─ Dust burst (3 directions, ~24 brown leaves up + side)
    ├─ SnapTrunkOnImpact : upper trunk + canopy rotate 12-22° misalign + 6u offset
    │   → lit visually comme "le tronc s'est cassé à l'impact" sans physics break
    └─ ChopsRemaining = Tunables.LogChopHP[Kind] (le tronc landed reste IChoppable)
  FallenLog.Chop sur landed log :
    └─ HP <= 0 → SplitIntoLogs : spawn N WoodItems alignés sur l'axe du tronc couché,
       N = TreeKindLandedDropCount[Kind], items burst au final chop du landed log
       (Luck stat = chance de bonus items, Mythic = +2 items, résolu ici une seule fois)
  WoodItem (small physics cube, MassOverride=0.5, brown WoodItemTint) :
    ├─ Spawn velocity = burst up+out, gravity-on initially
    ├─ MAGNET PROXIMITY : in radius < WoodItemMagnetRange → Gravity=off, damping bumped,
    │   velocity = (toPlayer).Normal × WoodItemMagnetSpeed
    ├─ Pickup quand dist < WoodItemPickupRange : GameState.AddBackpack(round(WoodMultiplier))
    │   ├─ Backpack full → ShowBackpackFullHint + item linger (not consumed)
    │   └─ Banked → ShowWoodPickupToast + "blip" SFX + destroy
    └─ WoodItemDespawnDelay timeout pour cleanup si abandonné

  **Cascade domino** : implémentée via `Tree.OnCollisionStart` + `ApplyImpactDamage`.
    Standing trees = MotionEnabled=false jusqu'à `StartFell`, donc un tronc qui tombe
    ne pousse pas réellement le voisin debout : il lui applique un damage scalé par
    vitesse d'impact façon Valheim ImpactEffect, puis le voisin shake/fell/split selon HP.

Économie deux-pool (Wood vs BackpackWood) :
  BackpackWood = ramassé sur le terrain, cappé par BackpackCapacity[BackpackTier].
  ShopStation.Deposit (WOOD DEPOT station, ring vert) → TryDeposit flush BackpackWood → Wood (stockpile).
  Wood = monnaie de dépense aux ShopStation.Upgrades (axe / speed / luck / power / pet /
  bag / range / swing-speed) + ShopStation.Prestige (replant).

Shop / progression (GameState + ShopStation) :
  GameState (singleton, persistant via FileSystem.Data/progress.json — per-Steam
  variant punted phase6r, TODO when MP wired) :
    ├─ Wood (stockpile) + BackpackWood (sac, cappé par BackpackCapacity[BackpackTier])
    ├─ TotalWoodEarned, TreesFelledTotal (lifetime, survives prestige)
    ├─ AxeTier : 0..6 (Hands/Stone/Bronze/Iron/Steel/Lumberjack/Chainsaw)
    ├─ SpeedTier / LuckTier / PowerTier / BackpackTier : 0..5 (personal stats)
    ├─ ToolRangeTier / ToolSpeedTier : 0..N (per-tool sub-stats appliquées
    │   par AxeController au swing path — range et recovery speed)
    ├─ PetTier : 0..5 (cosmetic companion, no gameplay effect)
    ├─ Spirits : prestige permanent multiplier
    ├─ ChopPower = AxeTierChopPower[axe] + PowerBonus[power]
    ├─ WoodMultiplier = AxeTierWoodMul[axe] × (1 + 0.01·Spirits) (appliqué au
    │   pickup d'un WoodItem, pas au fell)
    ├─ SpeedMultiplier (applied to PlayerController.WalkSpeed by AxeController)
    └─ LuckChance (rolled UNE FOIS dans Tree.SplitIntoLogs pour +50% items
        sur le drop entier, plus dans Tree.GiveWoodOnce qui n'existe plus)
  ShopStation × 4 (stations worldspace — supplante l'ex-ShopArea single-menu) :
    Chaque station = StationKind {Tools, Deposit, Upgrades, Prestige}, anneau de
    Radius=160u + worldspace label tinté (cyan/vert/orange/gold), PAS de pillar
    (Thomas 2026-05-21 : just the label). Inputs lus quand PlayerInside d'UNE
    station — slot inputs réutilisés (Slot1..N changent de sens selon station).
    ├─ Tools : équipe Axe T0..T6 (re-buy = swap tool actif)
    ├─ Depot : E → TryDeposit flush BackpackWood → Wood + deposit SFX
    ├─ Upgrades : Slot1=Speed Slot2=Luck Slot3=Power Slot4=Bag Slot5=Pet
    │             Slot6=ToolRange Slot7=ToolSpeed
    └─ Prestige : Slot1=Replant (TryPrestige, gated par CanPrestige)

Gates / area unlock loop (Tree.IsGate + SceneStarter.OnGateBroken) :
  ├─ 4 gates aux cardinaux à la boundary du ring courant
  ├─ Casser un gate : détection au SplitIntoLogs sur IsGate=true → 3× chip burst
  │   + ring expansion via SceneStarter.OnGateBroken (efface les 3 autres gates,
  │   spawn la prochaine bande de trees [oldOuter..newOuter], spawn 4 nouveaux gates)
  └─ Each next gate = ×1.5 chops (20 → 30 → 45 → 67 ...) gating progression

Prestige loop (Cookie-Clicker / AdVenture-Capitalist pattern) :
  ├─ Player atteint TotalWoodEarned ≥ 500 → CanPrestige=true
  ├─ Slot6 → TryPrestige : wipe Wood/Axe/Speed/Luck/Power/Pet/Gates, garde
  │   Spirits + TotalWoodEarned
  ├─ Spirits gained = floor(sqrt(TotalWoodEarned/50)), capped à pas re-decrease
  ├─ Each Spirit = +1% permanent wood multiplier
  └─ ShopStation.FirePrestigeBurst : 3-direction golden leaf burst + double sfx

HUD (WoodHud, immediate-mode) :
  ├─ Crosshair + au centre (3-arm gap pour ne pas masquer le aim highlight)
  ├─ "WOOD" top-left + nombre, pulse gold à chaque gain / orange à chaque dépense
  │   (sync au load = no false pulse au reboot)
  ├─ "AXE — <Name>" top-right + "T{n}" + 7 pips (lit jusqu'au tier courant)
  ├─ Shop menu 6 lines quand PlayerInside : [1] Axe [2] Speed [3] Luck
  │   [4] Power [5] Pet [6] Replant — affordable lines en HotColor, max en gris
  ├─ Banners (3 channels, peuvent se chevaucher) :
  │   • Prestige : "REPLANTED · +N SAPLING SPIRITS" gold 64px @ 30%h (2.5s)
  │   • Ring unlock : "RING N UNLOCKED" orange 44px @ 22%h (2.0s)
  │   • Upgrade toast : "AXE → IRON" / "SPEED → T3" / etc 28px @ 74%h (1.4s)
  │   • Welcome back : ShowUpgradeBanner channel sur first frame post-load
  ├─ "[R] teleport to shop" en bas (hidden quand PlayerInside)
  └─ DebugToggle (B) : FPS overlay + tree counts (standing/falling/landed)

Self-test (headless, mow-the-lawn + upgrade + prestige scenario) :
  SelfTest (ConVar tc_selftest=1) : Init → Approach → Swing → Verify → TestStats → TestPrestige → Done.
  Init : reset GameState, pick le plus proche tree debout, snapshot wood baseline.
  Approach : TeleportTo en face du tree à 60u, set tool = Axe.
  Swing : DebugSwingVerbose en loop jusqu'à IsStanding=false (cap 8s).
  Verify : assert target tree transitioned out of Standing. **Phase F note** : on
    n'asserte plus que `Wood` a augmenté — chopping ne crédite plus directement
    le stockpile, le pipeline complet (Tree → landed log split → WoodItem →
    pickup → AddBackpack → TryDeposit) est trop indirect pour le harness. Le
    toppled check est le smoke test minimum.
  TestStats : `_state.AddWood(totalCost)` direct, then exercise TryUpgradeSpeed +
    TryUpgradePower, assert tier++ et wood -= cost.
  TestPrestige : Push TotalWoodEarned au-dessus de 500 via AddWood, assert
    CanPrestige, TryPrestige, assert tiers reset + Spirits earned + TotalWoodEarned
    preserved.
```

**Pour ajouter un système** :
- Manager singleton → ajouter une `EnsureSingleton<T>(name)` dans `SceneStarter.OnStart()`.
- Entité gameplay → spawner method statique, appelée par `SceneStarter.SpawnXxx`.
- Drop persistant (décor lourd, lumière) → main.scene via éditeur ou MCP bridge.
- Nouveau tool ou kind → étendre `ToolKind` enum (AxeController.cs) + `IChoppable.AcceptsTool` + Tunables array, plus la sélection biome-biased si applicable.

## Alignement Valheim (mandate continu)

Thomas a dit "Valheim-tier" pour les arbres. Decompile direct via `ilspycmd -t <Class> "C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll"`. Source of truth pour tout polish-feel.

**Knowledge base Valheim** (avant de fouiller un comportement) :
- `_valheim-decompile/INDEX.md` — 545 .cs catégorisés (Trees / Damage / Items / World / Audio / UI / Entities / Production / Env / Effects / Physics). Le sommaire.
- `_valheim-decompile/TREE-PIPELINE.md` — Valheim ↔ tree-chopping-sbox mapping ligne par ligne (TreeBase, TreeLog, ImpactEffect, DamageText, Plant, Combo, DropTable, etc.). Status ✅🔧⚠️❌ pour chaque section. **Le fichier le plus utile** pour comprendre où on aligne vs dévie.
- `_valheim-decompile/<Class>.cs` — Read direct + Grep si besoin précis.

**Memory** :
- [[valheim-system-map]] — Patterns décompilés résumés (TreeBase/TreeLog/DamageText/Pickable/Skills/MineRock/EnvMan/CraftingStation/Attack/Player).
- [[tree-chopping-design-log]] — Chronologique des décisions, override events, rationale. Si tu vas changer un comportement existant, lis d'abord pourquoi il est comme ça.

**Aligné 1:1 vérifiable** :
- Two-stage prefab swap (TreeBase → TreeLog ≈ `Tree.StartFell` → `FallenLog.SpawnFromTree` → WoodItem direct)
- Stump séparée (m_stubPrefab ≈ TreeStump)
- ResetInertiaTensor avant kick au fell
- Grace period au log spawn (Valheim 0.2s, nous WoodLogChopGrace 0.2s)
- DropTable random Min/Max/Chance (TreeKindFellBonus*)
- Hit point pour kicks landed log (Destructible.RPC_Damage `hit.m_dir × hit.m_pushForce` at `hit.m_point`)
- Tier-scaled push force (`hit.m_pushForce ∝ ChopPower`)
- DamageText popup : couleurs Valheim verbatim (`Normal=white`, `TooHard=pale red 0.8/0.7/0.7`, `Bonus=orange 1/0.63/0.24`), distance cull 30m, font size split à 10m, random offset 20u, cubic alpha decay `1-pow(t,3)`, 1.5s standard / 3s bonus, cap 200.
- Bonus DamageText sur Mythic fell + Luck-triggered drops (Valheim pattern : Pickable.Interact fire Bonus sur skill-driven bonus yield)
- Grow animation au respawn (TreeBase.GrowAnimation scale 0→1 sur 0.3s)
- Per-kind impact/groan/initial-omega multipliers (Brittle = low split threshold, Veteran = slow start)
- EnhancedCcd (≈ maxDepenetrationVelocity = 1f)
- Auto-pickup magnet (Player.AutoPickup) : 80u range = 2m, grace 0.5s post-spawn (c_AutoPickupDelay)
- **Cascade domino + physics auto-split via ImpactEffect pattern** : `Tree.OnCollisionStart` calcule damage scalé `LerpStep(min, max, speed) × baseDamage`. Damage self (m_damageToSelf) + other Tree (cascade). HP=0 → StartFell ou SplitIntoLogs. Calibration : ImpactMinSpeed=250, ImpactMaxSpeed=1500, ImpactBaseDamage=6.

**Déviations RETIRÉES** (don't put them back without explicit Thomas go) :
- Cascade momentum-based (mass × velocity / mass ratio) — remplacé par damage scaling Valheim
- WoodLog intermediate chop layer — viré, items spawn direct depuis Tree.SplitIntoLogs
- Cam shake mass-scaled au landing — Valheim trees ne secouent pas l'écran à eux seuls

**Déviations VOLONTAIRES préservées** (justified) :
- Auto-pickup ON par défaut (Valheim toggle) — Cookie-Clicker arcade UX
- Respawn delay en seconds (30-300s) vs Valheim minutes — locked-arena cadence
- Skill system tier-based shop (vs Valheim continuous 0-100 RaiseSkill) — different design
- KickWobble 9° single-axis (vs Valheim 1.5° dual-axis 40Hz buzz) — style choice, lit mieux avec cubes

**Outils d'audit** :
- `ilspycmd -t <Class> <dll>` pour décompiler une classe ciblée. Cache en `$env:TEMP\valheim_*.cs`. Décompile globale : `ilspycmd -p -o /tmp/valheim_full <dll>` (603 fichiers).
- `mcp__sbox__trigger_hotload` pour pousser nos changes dans sbox-dev ouvert.
- `tools/selftest.ps1` : phases tests verrouillent les comportements clés. ~11s wall, 10+ phases.

**Déviations restantes (assumées)** :
- Pipeline TreeBase destroy + spawn TreeLog désormais aligné : `Tree` standing détruit après spawn `FallenLog`, qui porte le chop/impact landed.
- Continuous-play short respawn (30-300s par kind) vs Valheim's m_respawnTimeMinutes (minutes-scale, polled chaque 60s). Cadence différente pour notre Cookie-Clicker loop.
- Skills tier-based purchase (AxeTier 0-6, ShopStation) vs Valheim's continuous 0-100 RaiseSkill. Game design choice.
- LandingShakeAmp retiré : Valheim trees ne secouent pas l'écran à eux seuls, on a aligné.

**Outils d'audit** :
- `ilspycmd -t <Class> <dll>` pour décompiler une classe ciblée. Cache en `$env:TEMP\valheim_*.cs`.
- `mcp__sbox__trigger_hotload` pour pousser nos changes dans sbox-dev ouvert.
- `tools/selftest.ps1` : phase contract dérivé de `Code/SelfTest.cs`, ~40 PASS markers sur le pipeline chop/economy/Valheim-feel. ~13-15s wall.

**Drift restant dans CE document** : compat legacy seulement. `AGENTS.md` est la source canonique pour Codex.

## API s&box, Source 2, hotload, doc

→ Déplacé dans `Code/CLAUDE.md` (chargé seulement quand tu touches `Code/**/*.cs`). Y a : Component lifecycle, attributs Property/RequireComponent/Sync, `Components.Get` patterns, spawn par code, `Scene.Trace`, Input, HUD immediate-mode, Z-up / inches / WorldScale / `Model.Cube` tint gotcha, hotload pièges, MSTest setup, et où chercher la doc à jour (context7, XML locaux, base addon).

## Workflow Claude — recommandé pour ce repo

1. **Pas de commit sans demande explicite.** Lire CLAUDE.md global comportement.
2. **Compile en local** après chaque changement non-trivial : `dotnet build Code\tree_chopping.csproj`. Ça valide les types/signatures sans lancer le jeu.
3. **Pour valider le gameplay**, demander à l'utilisateur de tester dans sbox-dev — pas de moyen de scripter Play.
4. **Pour modifier la scène**, éditer `Assets\scenes\main.scene` à la main (JSON) ou demander à l'utilisateur de faire le drag-drop dans l'éditeur. Le JSON est trivial à éditer mais il faut respecter `__guid`, `__type` et les valeurs `"x,y,z,w"` séparées par virgule sans crochets.
5. **Tunables d'abord** : les constantes gameplay (`Tunables.cs`) sont la première chose à toucher pour tuner. Pas besoin de scène ouverte.
6. **Hotload** = en cas de modification d'un default d'un static field, prévenir l'utilisateur que redémarrer l'éditeur peut être nécessaire.
