# CLAUDE.md — Tree Chopping (s&box)

Notes pour les futures sessions Claude qui bossent sur ce projet en autonomie. Ce projet est un proto **s&box** (moteur Source 2 + C#/.NET, par Facepunch). Voir `README.md` pour le pitch gameplay.

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

## Layout du projet

| Chemin | Rôle |
|---|---|
| `tree_chopping.sbproj` | Manifest s&box (JSON). `StartupScene`, `TickRate`, `GameNetworkType: Singleplayer`. |
| `tree_chopping.slnx` | Solution VS qui pointe vers `Code/`, `Editor/`, et les addons sbox dans `Program Files (x86)\Steam\steamapps\common\sbox\`. |
| `Code/` | Assembly de jeu. Namespace `TreeChopping`. `Assembly.cs` = global usings. |
| `Editor/` | Assembly **éditeur** séparée (`tree_chopping.editor.csproj`) — code outils, pas sandboxé. Actuellement vide à part les usings. |
| `Assets/scenes/main.scene` | Scène JSON. `Sun` (DirectionalLight) + `Skybox` + `Ground` (plane + BoxCollider) + `Bootstrap` (TreeChopping.SceneStarter). Tout le reste est spawn au runtime depuis `SceneStarter`. |
| `ProjectSettings/Input.config` | Bindings clavier/gamepad. **Tu lis ces noms** dans `Input.Pressed("Jump")` etc. |
| `ProjectSettings/Collision.config` | Matrice de collision. |
| `Libraries/` | Libs externes (vides ici). Une lib = `Assets/`, `Code/`, `Editor/` sous-dossiers. |
| `.sbox/` | Cache éditeur — généré, **ne pas commiter** (déjà gitignored). |

Fichiers gitignored à noter : `*.csproj`, `*.slnx`, `*.sln`, `obj/`, `bin/`, `.sbox/`, `*.*_c` (assets compilés sauf `*.shader_c`). Donc **le sln/csproj présents sur disque ne sont pas dans git** — c'est normal.

## Architecture gameplay actuelle

```
SceneStarter (OnAwake)
 ├─ Ensure WoodInventory
 ├─ Spawn Beaver (cube + Rigidbody + BeaverController)
 ├─ Ensure Camera (créée si absente de la scène)
 ├─ Ensure WoodHud
 └─ SpawnForest → N×Tree (cube + Rigidbody + Tree component)

BeaverController     : input WASD/souris/saut, swing cone-based → IChoppable.Chop()
Tree (IChoppable)    : standing → chops−− → falling (torque + slowTip) → landed → log hits → BreakIntoPieces → spawn LogPiece×N
LogPiece (IChoppable): hits → BreakIntoChunks → spawn WoodChunk×4
WoodChunk            : aimanté au castor (lerp) → Collect → WoodInventory.Add
WoodHud              : HudPainter immediate-mode (pas de Razor)
Tunables             : toutes les constantes game-feel, **en unités s&box** (Source 2)
```

Tout le contenu de scène hors décor (sun/skybox/ground) est **spawné par code** depuis `SceneStarter`. Si tu rajoutes un système, deux options :
- Spawn au runtime depuis `SceneStarter` (pattern actuel — rapide à itérer).
- Drop dans `main.scene` via l'éditeur (persistant, inspectable).

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
