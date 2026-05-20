# CLAUDE.md — Tree Chopping (s&box)

Notes pour les futures sessions Claude qui bossent sur ce projet en autonomie. Ce projet est un proto **s&box** (moteur Source 2 + C#/.NET, par Facepunch). Voir `README.md` pour le pitch gameplay.

## Non-negotiables — lis ça avant de coder

Chaque ligne ici a déjà coûté du debug à une session précédente. Pas d'exception.

1. **s&box API docs → context7 first, NOT WebFetch.** sbox.game est JS-rendered et renvoie littéralement la string "s&box". Utilise le MCP context7 avec `/llmstxt/sbox_game_llms_txt` (le plus dense) ou `/websites/sbox_game_api`. Source of truth locale : `C:\Program Files (x86)\Steam\steamapps\common\sbox\bin\managed\Sandbox.*.xml` — grep dedans quand context7 manque de précision.

2. **Headless est le loop par défaut, pas l'éditeur :**
   - `dotnet build C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj` pour valider les types.
   - `sbox-server.exe +game <sbproj> +maxplayers 1` pour le lifecycle + physique (pas de rendu, pas d'input client). `Log.Info` → stdout.
   - **`tools\selftest.ps1`** lance le full bowling scenario end-to-end via la ConVar `+tc_selftest 1` → exit 0 = PASS / 1 = FAIL / 3 = TIMEOUT en ~5 s. **À relancer après TOUT changement dans Tree / RunManager / SceneStarter.SpawnForest / BeaverController swing path.** Build clean ≠ scénario vert.
   - `sbox.exe` ne marche PAS sur projets locaux (traite le sbproj comme un cloud package et 404). Seuls `sbox-dev.exe` (éditeur) et `sbox-server.exe` (headless) gèrent du dev non shippé.

3. **`System.Environment.*` est sur le deny-list de la whitelist du compiler s&box.** `GetEnvironmentVariable`, `GetCommandLineArgs`, etc. font échouer la compile avec `"is not allowed when whitelist is enabled"`. Pour les flags de lancement → `[ConVar("name")]`, set via `sbox-server.exe ... +name value`.

4. **Component enumeration : query par interface, pas par base type.** `Scene.GetAllComponents<Component>()` renvoie 0. `Scene.GetAllComponents<T>()` accepte un type concret ou une **interface** (la doc XML dit literally *"This can include interfaces."*). Pour scanner par capacité (IChoppable, IInteractable…) query l'interface, pas un `Component` + `OfType<>`.

5. **Pas de F-keys pour les bindings.** L'éditeur s&box intercepte F1-F12 pour ses propres shortcuts et ils passent pas fiablement en Play. Lettres ou combos modifiés seulement. État courant : `DebugToggle=B`, `PlayShortcut=Ctrl+Shift+P`.

6. **Le clavier est AZERTY.** Si tu scripts de l'input via SendInput / `keybd_event` vers la Play view de l'éditeur, envoie `VK_Z` pour Forward (pas `VK_W`), `VK_Q` pour Left. La string `"W"` dans `Input.config` cible le *caractère* W, qui sur AZERTY vient de la touche physique en position Z.

7. **`SetCursorPos` dans le viewport Play envoie de l'`Input.AnalogLook`** — chaque move souris pivote la caméra. Donc "click puis screenshot stable" ne marche pas, le click déplace d'abord le curseur. Privilégier le headless selftest pour valider la logique. GUI screenshot ([[sbox-screenshot-runtime]]) seulement quand il faut des pixels.

8. **Rigidbody arbres debout : `MotionEnabled = false` obligatoire.** Sinon le castor les fait tomber juste en marchant dedans — ruine le bowling-with-trees (Score se déclencherait sans swing). `StartFell` flippe la valeur à `true` quand le compteur de chops tombe à 0. Régression couverte par la phase `BumpTree` du selftest — ne la désactive pas.

9. **Un test qui prend un raccourci passe pendant que le chemin réel est cassé.** Le premier SelfTest appelait `Tree.Chop()` direct et a caché 2 bugs prod (`GetAllComponents<Component>` retournant 0, `Tree.IChoppable.IsValid` excluant les landed logs). Chaque phase de selftest DOIT exercer au moins un code path joueur réel — `BeaverController.DebugSwing → ChooseSwingTarget → Chop`, pas `Chop()` direct.

10. **Conventions Source 2.** Z is up (pas Y). Unités = inches (`Tunables.UnitsPerMeter = 39.37`). `Vector3.Forward = +X`. Spawn de cubes via `WorldScale = wantedSize / Tunables.CubeBase` parce que `Model.Cube` est le dev cube natif 50u. Quaternions = (x, y, z, w).

11. **Style de code de ce repo.** Tabs + `if ( foo )` (espaces dans les parens). Default à zéro commentaires : un commentaire ne se justifie que quand le *why* est non-évident (contrainte cachée, incident passé, invariant subtil). Pas de wrappers "legacy", pas de stubs pour des besoins hypothétiques. Trois lignes similaires battent une abstraction prématurée. FR OK dans les commits + logs + CLAUDE.md ; XML / docs sbox-facing restent EN.

12. **Agents parallèles éditent parfois les mêmes fichiers.** Re-read avant Edit si tu as `"File has been modified"`, re-applique minimalement. Pas de commit sans `"commit ça"` explicite — l'utilisateur fait ses propres petits commits `phase2X:`.

Workflow type pour un changement runtime :
1. `dotnet build` → types OK.
2. `tools\selftest.ps1` → pipeline end-to-end + anti-collision OK.
3. Si tu touches HUD / particles / camera / rendering / son → dis explicitement que le headless ne valide pas le visuel, demande un Play GUI. Ne réclame jamais "ça marche" sans preuve.

User = Thomas : FR, 10 ans Godot, deux mois s&box. Préfère terse, pas de hand-holding, pas de résumé trailing. Confirmer avant destructive ops (`git reset`, force-push, mass-delete). Quand tu as plusieurs streams search/edit qui ne touchent pas les mêmes fichiers, fan-out en Agent calls parallèles — c'est le default qu'il a demandé.

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
| `Code/` | Assembly de jeu. Namespace `TreeChopping`. `Assembly.cs` = global usings. |
| `Editor/` | Assembly **éditeur** séparée (`tree_chopping.editor.csproj`) — code outils, pas sandboxé. Actuellement vide à part les usings. |
| `Assets/scenes/main.scene` | Scène JSON. Arène inclinée persistée : `Sun` (DirectionalLight, golden-hour tints — LightColor 1/0.86/0.62, SkyColor 0.42/0.50/0.60, FogMode Enabled FogStrength 0.45) + `Skybox` (SkyBox2D) + `Ground` (plane, scale 60×60×1, tint 0.35/0.49/0.27, tags `ground`, static collider, **Rotation `0.10395,0,0,0.99458` = +12° autour de X — slope visuel qui matche `Tunables.ArenaSlope=0.21`**) + `Bootstrap` (SceneStarter, TreeCount=1000, MinSpacing=35, BeaverSpawn=(0,0,60)) + `Camera` (CameraComponent **FieldOfView=90**, pos (0,0,140), BackgroundColor 0.45/0.55/0.68, **Tonemapping HableFilmic + ExposureCompensation 0.3**, **Bloom** Color 1/0.95/0.85 Strength 0.45 Threshold 0.55, **ColorGrading** Sat 1.18 Contrast 1.10). Tout le reste (beaver, forêt noise-clusterisée 1000-tree, HUD, managers, ambient leaves, aim indicator) est spawné au runtime depuis `SceneStarter`. |
| `ProjectSettings/Input.config` | Bindings clavier/gamepad. **Tu lis ces noms** dans `Input.Pressed("Jump")` etc. La touche "Reload" (R) déclenche `RunManager.Regenerate()` une fois en état `Scored`. |
| `ProjectSettings/Collision.config` | Matrice de collision. |
| `Libraries/` | Libs externes. Contient `claudebridge/` (MCP bridge addon — cf. section "Avec l'éditeur"). Le package distant `titanovsky.low_poly_tree` est déclaré dans `tree_chopping.sbproj.PackageReferences` — sbox-dev le DL au project-open, pas de fichier dans `Libraries/`. |
| `tools/` | `selftest.ps1` (harness headless du SelfTest, exit 0/1/3 = PASS/FAIL/TIMEOUT) + `session-prompt.md`. |
| `.sbox/` | Cache éditeur — généré, **ne pas commiter** (déjà gitignored). |

Fichiers gitignored à noter : `*.csproj`, `*.slnx`, `*.sln`, `obj/`, `bin/`, `.sbox/`, `*.*_c` (assets compilés sauf `*.shader_c`). Donc **le sln/csproj présents sur disque ne sont pas dans git** — c'est normal.

## Architecture gameplay actuelle

**Pivot 2026-05-19 : bowling-with-trees.** Un seul swing par run. Tu choisis ton angle, tu frappes l'arbre d'entrée, tu regardes la cascade. Score = arbres tombés ce run. Restart sur R → nouvelle seed, nouvelle forêt.

```
SceneStarter.OnStart()
 ├─ Singletons (Ensure*, créés si absents) :
 │   RunManager · WoodInventory · ComboTracker · Weather · BiomeManager
 │   DayNightCycle · WoodHud · HudCompass · PauseMenu · AmbientLeaves
 │   · AimIndicator
 │   · SelfTest? · AutoSpin?
 │   (SelfTest et AutoSpin gated par ConVars tc_selftest / tc_autospin)
 ├─ SpawnBeaver(camera) : cube body + Rigidbody (Locking Pitch+Roll=true) + BeaverController
 │   └─ BeaverController.OnStart() swap le cube pour Models.Beaver (Kenney
 │       animal-beaver.vmdl si compilé, sinon BuildBeaverProps() — composite
 │       procédural sphere body + sphere head + cube snout + 2 sphere ears +
 │       flat cube tail + 4 cube legs ; quand le proc est actif, le ModelRenderer
 │       cube body parent est désactivé). Pas de collider/rigidbody sur les props.
 ├─ SpawnForest()  : 1000 Tree dans un disque (Tunables.ArenaRadius=2500), MinSpacing=35
 │                   ├─ uniform disc sampling avec √u correction
 │                   ├─ ValueNoise2D + Hash2D gate (densité > ArenaDensityThreshold=0.05)
 │                   ├─ keepout central (ArenaCenterKeepout=120) autour du spawn beaver
 │                   ├─ MinSpacing adaptatif 0.7×–1.4× selon la noise (clusters denses + clairières)
 │                   └─ pos.z = GroundZ - y * ArenaSlope (les arbres collent au plan incliné)
 ├─ SpawnGrassTufts() : décor cubes (cosmetic, no collider)
 └─ AmbientLeaves (singleton) : 60 petits cubes feuilles (5×5×3u, variantes vert-jaune)
     dérivant entre Z=320 et Z=540. Vent global lent qui pivote à 0.04 Hz (~25s par
     rotation complète). Drift horizontal 30 u/s, chute 15 u/s. Recyclés quand Z<10.

RegenerateForest() (public, appelé par RunManager.Regenerate)
 ├─ Détruit tout Tree / LogPiece / WoodChunk / Stump existant
 ├─ Rotate la Seed (Tunables.ArenaNoiseScale=500)
 └─ Re-run SpawnForest()

Player loop (BeaverController) :
  WASD/souris/jump + walk-bob caméra + FOV juice (sprint widen, chop punch)
  attack1   → UpdateSwing : gate sur RunManager.CanSwing
              → ChooseSwingTarget (cone+range, filtre Tree IChoppable)
              → Tree.Chop() — réduit chops à 0 d'un coup pour démarrer le fell
              → RunManager.OnSwingFired() : flip state WaitingForSwing → Cascading
  AimIndicator (WaitingForSwing only) : Gizmo ground-skim line forward depuis
              le beaver + hot-tint highlight sur le tree que ChooseSwingTarget
              prendrait (preview du shot).
  Cinematic cam (RunState != WaitingForSwing) : input gelé, orbit -30°/s yaw,
              radius lerp 350→850u sur 3s, height 220→420u sur 4s, look at
              centroid des Trees en mouvement +60u up, FOV pinned 75°.

Cascade pipeline :
  Tree.StartFell() :
    ├─ MotionEnabled = true (était false standing — gotcha #8 anti-bump)
    ├─ Apply torque (FellTorque=130000) + push (FellPush=3200) + slowTip ramp
    │   (SlowTipDuration=0.32, SlowTipRampFrac=0.42)
    ├─ Premier arbre du run (RunManager.Score == 0) : +0.15 trauma bonus
    ├─ Canopy leaf burst (22 chips green-yellow, speed 240, à TreeHeight×0.85)
    ├─ Spawn ScorePop "+N" world-space text au-dessus du tree (rise+fade 1.2s,
    │   tinté par milestone tier courant)
    └─ RunManager.OnTreeFell() — Score++, refresh idle-timer cascade, check
       milestones (3/8/20/50/100/200) → si seuil franchi, set LastMilestoneIndex
       + LastMilestoneName + MilestoneShownTime, trigger trauma + banner.
       Tier 1-2 (Chain Reaction / Lumberjack) → ComboTracker.TriggerSlowmo()
       pour time-slow cinématique.
  Tree.ICollisionListener.OnCollisionStart :
    ├─ Si voisin standing & vitesse > MinContactSpeed=25 → CascadeStrike
    │   propage l'impulsion (ImpulseTransfer=0.95) + torque angulaire
    │   (axe Up×dir, magnitude impulse.Length × 0.4) → voisin StartFell.
    │   Le torque fait spinner/rouler les arbres cascadés (feel Valheim).
    └─ Sur landed log : Shatter si IncomingSpeed > 300 u/s
  Tree.BecomeLandedLog() (thunk d'atterrissage) :
    ├─ Trauma punch +0.25 + chip burst brun côté impact
    ├─ Landing canopy leaf burst (18 chips, speed 200, à l'extrémité gisante)
    ├─ AudioBank.PlayLogBreak
    └─ Sphere 80u → impulse knock-back (mass × 80) sur les rigidbodies voisins
       (peut déclencher des cascades secondaires par concussion)
  Après 1.5s sans nouveau StartFell : RunManager → state Scored
    └─ HUD bannière : "NEW BEST — N trees felled. Press R for another run"

Milestones (RunManager + WoodHud) :
  Tiers Tunables.ScoreMilestones = { 3, 8, 20, 50, 100, 200 }
  Noms : Spark / Chain Reaction / Lumberjack / Domino King / Forest Killer / TIMBER SHOCK
  Couleurs par tier dans ScoreMilestoneColors. Quand un seuil tombe :
    ├─ DrawMilestoneFlash : overlay couleur plein écran sur 0.4s
    ├─ DrawMilestonePopup : texte centré fade-in/scale-pop/fade-out
    │                       (MilestonePopupDuration=1.8s)
    └─ Score line PULSE 1.45×→1× sur 0.4s à chaque OnTreeFell

Restart :
  Input "Reload" (R) en état Scored → RunManager.Regenerate()
    ├─ State → WaitingForSwing
    ├─ Rotate Seed → SceneStarter.RegenerateForest()
    └─ Beaver téléporté à (0,0,60), vélocité reset

Visuel debris (toujours là, plus surfacé au HUD) :
  Tree.BreakIntoPieces() → LogPiece×N + Stump
  LogPiece.BreakIntoChunks() → WoodChunk×4 → lerp vers beaver → WoodInventory.Add
  (WoodInventory garde le compte interne pour ComboTracker beat, pas affiché)

Juice & ambient :
  ComboTracker : chain++ par "beat" (chop/pickup), slowmo @5, flash @8, trauma decay
                 Cascade pulse trauma sur chaque OnTreeFell pendant Cascading state
  Tree         : wind sway (multi-axis sin + gust envelope) sur les standing
                 + per-chop visual lean cumulatif avant le fell
  Weather      : CLEAR / CLOUDY / RAIN → retune DirectionalLight + Skybox tints
  BiomeManager : Forest / Autumn / Frost — palette tween + biais d'espèce
                 (Forest=Beech/Spruce, Autumn=Ironwood, Frost=Crystal)
  DayNightCycle: 90s loop, sun yaw + énergie (SunMaxEnergyMul=1.85), démarre à
                 DayPhase=0.78 (golden hour profond)
  AmbientLeaves: 60 cubes feuilles drift+fall, vent rotatif global (cf. layout)

HUD & UI (WoodHud, RunState-aware) :
  Top-left panel  : "Score: N/initial" · "Best: M" · "Sky: <state>"
  Center banner   : state-driven prompt
                    WaitingForSwing → "Pick your shot — click to swing"
                    Cascading       → "Falling… N so far"
                    Scored          → "NEW BEST — N trees felled. Press R for another run"
                                      (hot tint si Score > BestScore précédent)
  HudCompass      : repères directionnels
  PauseMenu       : gate l'input gameplay quand IsPaused

Self-test (headless, bowling scenario) :
  SelfTest (ConVar tc_selftest=1) : Init → BumpTree (anti-régression : tree NE
  TOMBE PAS au contact, MotionEnabled=false obligatoire) → DetectTree →
  WaitCascade → Verify (Score >= 1). DetectTree cible le **centre du cluster le
  plus dense** (trees ordonnés par compte de voisins dans 120u, desc) et **pin
  le tree cible à ChopsRemaining=1** pour que la variété d'espèces ne casse pas
  la validation pipeline. Le swing de DebugSwingVerbose à l'intérieur de
  DetectTree est le swing final. Bonus log "[TC_TEST] cascade chained" si
  Score >= 2. Actuellement passe avec score=1/1000 — le selftest valide le
  pipeline ; les cascades in-game chainent plus loin.

Tunables (polish wave triple-A) :
  ArenaRadius=2500, GroundZ=0, ArenaCenterKeepout=120
  ArenaSlope=0.21 (≈12° downhill +Y — trees roulent, cascades plus longues ;
    matche le Rotation +12° du Ground dans main.scene)
  ArenaNoiseScale=400, ArenaDensityThreshold=0.05 (~5% rejet — très très dense)
  TreeCount=1000, MinSpacing=35 (defaults SceneStarter + main.scene Bootstrap)
  CascadeImpulseTransfer=0.95, CascadeMinContactSpeed=25 (chaines extra-longues)
  FellTorque=130000, FellPush=3200, SlowTipDuration=0.32, SlowTipRampFrac=0.42
  ScoreMilestones = { 3, 8, 20, 50, 100, 200 } + ScoreMilestoneColors par tier
  MilestonePopupDuration=1.8
  SpeciesScaleMul = { 0.85, 1.0, 1.25, 1.4 } — Crystal trees 1.4× scaled
  CameraDistance=280, CameraHeightAboveBeaver=130, CameraMinPitch=-55,
    CameraMaxPitch=40, FovSprintWiden=4 (framing wide bowling-cam, FOV=90 base)
  DayPhaseStart=0.78, SunMaxEnergyMul=1.85 (golden hour profond)
  Tree.TickWindSway : WindAmplitudeDeg=4.5, WindGustHz=0.18 (canopée + visible)
  Tree per-instance jitter @ SpawnAt : scale 0.75×–1.30× hashed sur foot XY,
    tint ±10% per channel (hashed indépendant), chops ±1 tied to scale (gros
    arbres plus durs, floor 1)
  + species, swing, beaver, juice constants — tous en u (Source 2 inches).
Models : `Models.cs` map low_poly_tree.vmdl (titanovsky.low_poly_tree) pour les
         Tree ; `Models.Beaver = Get("models/animal-beaver.vmdl")` (Kenney Cube
         Pets, CC0, .glb dans Assets/models/ — éditeur doit compiler une fois
         via asset browser, sinon SafeLoad fallback Model.Cube → procédural
         BuildBeaverProps). Axe + Stump + Log + Grass + Bird = Model.Cube
         fallback. Assets CC0 also : animal-bee/bunny/deer/fox.glb.
```

**Pour ajouter un système** :
- Manager singleton (Weather, etc.) → ajouter une `Ensure*()` dans `SceneStarter.OnStart()`.
- Entité gameplay → spawner method statique, appelée par `SceneStarter.SpawnXxx` ou par `RegenerateForest`.
- Drop persistant (décor lourd, lumière) → main.scene via éditeur ou MCP bridge.

## API s&box utiles (réflexes)

### Cycle de vie d'un Component

Ordre : `OnAwake` → `OnStart` → `OnEnabled` → (loop `OnUpdate`/`OnFixedUpdate`/`OnPreRender`) → `OnDisabled` → `OnDestroy`.

- `OnAwake` : appelé une fois à la création **après désérialisation**, si parent activé. **Pas garanti que les autres Components du même GameObject soient prêts** — pour ça, utilise `OnStart`.
- `OnStart` : avant le 1er `OnFixedUpdate`, après que tout est prêt.
- `OnEnabled` / `OnDisabled` : à chaque activation/désactivation (incluant après hotload).
- `OnUpdate` : par frame, après les Updates internes.
- `OnFixedUpdate` : tick fixe (50Hz ici, cf. `Metadata.TickRate`). Pour physique.
- `OnPreRender` : juste avant le rendu — bon pour position de caméra finale.
- `OnDestroy` : nettoyage.
- Pour exécuter en mode édition : implémenter `Component.ExecuteInEditor`.

### Attributs sur les propriétés

- `[Property]` → champ visible/éditable dans l'Inspector, sérialisé dans `.scene`/prefab.
- `[Property, ReadOnly]` → visible, non éditable (ex: `WoodInventory.Wood`).
- `[RequireComponent]` → cherche le component sur le même GameObject, **le crée s'il manque**.
- `[Hide]` → fonctionne et sérialise mais invisible dans l'éditeur.
- `[Sync]` → propriété répliquée réseau (singleplayer ici, donc ignorer).
- `[SkipHotload]` → exclut un champ du hotload (rare).

### Trouver des Components

```csharp
go.Components.Get<T>()                          // 1er match
go.Components.GetAll<T>()                       // tous
go.Components.Get<T>( FindMode.EverythingInSelfAndDescendants ) // ce qu'utilise BeaverController
Scene.GetAllComponents<T>().FirstOrDefault()    // global scène (ce qu'on fait pour Inventory/Hud)
```

### Spawn d'un GameObject par code (pattern dans `Tree.SpawnLogPiece`)

```csharp
var go = Scene.CreateObject();
go.Name = "X";
go.WorldPosition = ...;
go.WorldScale = ...;
var mr  = go.AddComponent<ModelRenderer>(); mr.Model = Model.Cube; mr.Tint = ...;
var col = go.AddComponent<BoxCollider>();   col.Scale = Vector3.One;
var rb  = go.AddComponent<Rigidbody>();     rb.MassOverride = ...;
```

### Trace / raycast

```csharp
var hit = Scene.Trace
    .Ray( origin, end )                 // ou .Sphere( radius, a, b ) / .Box(...)
    .Size( new BBox(-5, 5) )            // optionnel : box around ray
    .IgnoreGameObjectHierarchy( GameObject )
    .WithoutTags( "player" )
    .Run();
if ( hit.Hit ) { ... hit.EndPosition / hit.Normal / hit.Distance ... }
```

### Input

`Input.Pressed("Jump")`, `Input.Down("Run")`, `Input.Released(...)` — les noms sont ceux de `ProjectSettings/Input.config` (**case-insensitive**, mais on a déjà du `"attack1"` minuscule qui marche). Pour les axes : `Input.AnalogMove` (Vector3) et `Input.AnalogLook` (Angles).

### HUD immediate-mode

```csharp
var hud = Scene.Camera.Hud;
hud.DrawRect( new Rect(x, y, w, h), color );
hud.DrawText( new TextRendering.Scope( "Hello", Color.White, 32f ), pos );
```
**Pas de Razor** ici (choix volontaire — cf. README, repris de `moon_fps`).

## Source 2 — gotchas

- **Z is up**. Pas Y. `Vector3.Up = (0,0,1)`. Très facile d'oublier en venant de Godot/Unity.
- **Unités = inches**. 1 m ≈ 39.37 u (cf. `Tunables.UnitsPerMeter`). Toutes les distances en jeu sont en u. Ex: vitesse de marche ~240 u/s.
- **Échelle via `WorldScale`** sur GameObject — `Model.Cube` est 1×1×1, donc une boîte de tronc d'arbre = `WorldScale = (32, 32, 280)`.
- **Forward** = `+X` historiquement Source, mais la doc bouge vers `Vector3.Forward` cleané — utilise les helpers (`Rotation.Forward`), pas les axes en dur.

## Hotload — bon à savoir

- Le hotload recompile et reload les assemblies à chaque save de `.cs`/`.razor`. Pas besoin de redémarrer l'éditeur pour 99% des changements.
- **Pièges** :
  - Changer la valeur par défaut d'un `static` ou d'un `{ get; } = ...` **ne prend pas effet** au hotload (le runtime garde l'ancienne). Utiliser un `=> "World"` (expression-bodied) ou `const` pour bypass. Cf. doc hotload.
  - Pour reset un state hotload-sensible, redémarrer l'éditeur.
  - `[SkipHotload]` existe pour exclure un champ.

## Tests unitaires (si besoin)

- Créer un dossier `UnitTests/` dans le projet — sbox-dev génère automatiquement un projet de test.
- Utilise `Microsoft.VisualStudio.TestTools.UnitTesting` (MSTest).
- Pour des tests qui touchent au moteur :
  ```csharp
  [TestClass] public class TestInit {
      [AssemblyInitialize] public static void Init(TestContext _) {
          var s = new Sandbox.AppSystem(); s.Init();
      }
  }
  // dans un test :
  var scene = new Scene();
  using var scope = scene.Push();
  var go = scene.CreateObject();
  var c  = go.Components.Create<MyComponent>();
  ```

## Où chercher de la doc à jour

- **context7 MCP** : préférer les library IDs `/llmstxt/sbox_game_llms_txt` (le plus dense pour LLM) ou `/websites/sbox_game_api`. WebFetch sur `sbox.game/dev/doc/...` **ne marche pas** (rendu JS — la page renvoie juste `s&box`).
- **XML locaux** : `C:\Program Files (x86)\Steam\steamapps\common\sbox\bin\managed\Sandbox.*.xml` — doc générée des XML comments, source of truth.
- **Source du base addon** : `C:\Program Files (x86)\Steam\steamapps\common\sbox\addons\base\code\` — exemples canoniques (Citizen, Networking).
- **Templates** : `...\sbox\templates\game.minimal`, `game.playercontroller`, etc. Bon point de référence pour la structure.
- **Sample** : `...\sbox\samples\sweeper`.

## Workflow Claude — recommandé pour ce repo

1. **Pas de commit sans demande explicite.** Lire CLAUDE.md global comportement.
2. **Compile en local** après chaque changement non-trivial : `dotnet build Code\tree_chopping.csproj`. Ça valide les types/signatures sans lancer le jeu.
3. **Pour valider le gameplay**, demander à l'utilisateur de tester dans sbox-dev — pas de moyen de scripter Play.
4. **Pour modifier la scène**, éditer `Assets\scenes\main.scene` à la main (JSON) ou demander à l'utilisateur de faire le drag-drop dans l'éditeur. Le JSON est trivial à éditer mais il faut respecter `__guid`, `__type` et les valeurs `"x,y,z,w"` séparées par virgule sans crochets.
5. **Tunables d'abord** : les constantes gameplay (`Tunables.cs`) sont la première chose à toucher pour tuner. Pas besoin de scène ouverte.
6. **Hotload** = en cas de modification d'un default d'un static field, prévenir l'utilisateur que redémarrer l'éditeur peut être nécessaire.
