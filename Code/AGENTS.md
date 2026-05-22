# Code/ — s&box C# reflexes

Ce file ne se charge que quand tu touches des fichiers sous `Code/`. Le AGENTS.md root reste source of truth pour les non-negotiables, l'architecture gameplay et le workflow général. Ici = juste les patterns API s&box / Source 2 / hotload qui reviennent à chaque session.

## Cycle de vie d'un Component

Ordre : `OnAwake` → `OnStart` → `OnEnabled` → (loop `OnUpdate`/`OnFixedUpdate`/`OnPreRender`) → `OnDisabled` → `OnDestroy`.

- `OnAwake` : appelé une fois à la création **après désérialisation**, si parent activé. **Pas garanti que les autres Components du même GameObject soient prêts** — pour ça, utilise `OnStart`.
- `OnStart` : avant le 1er `OnFixedUpdate`, après que tout est prêt.
- `OnEnabled` / `OnDisabled` : à chaque activation/désactivation (incluant après hotload).
- `OnUpdate` : par frame, après les Updates internes.
- `OnFixedUpdate` : tick fixe (50Hz ici, cf. `Metadata.TickRate`). Pour physique.
- `OnPreRender` : juste avant le rendu — bon pour position de caméra finale.
- `OnDestroy` : nettoyage.
- Pour exécuter en mode édition : implémenter `Component.ExecuteInEditor`.

## Attributs sur les propriétés

- `[Property]` → champ visible/éditable dans l'Inspector, sérialisé dans `.scene`/prefab.
- `[Property, ReadOnly]` → visible, non éditable (ex: `FilmStrip.Phase`).
- `[RequireComponent]` → cherche le component sur le même GameObject, **le crée s'il manque**.
- `[Hide]` → fonctionne et sérialise mais invisible dans l'éditeur.
- `[Sync]` → propriété répliquée réseau (singleplayer ici, donc ignorer).
- `[SkipHotload]` → exclut un champ du hotload (rare).

## Trouver des Components

```csharp
go.Components.Get<T>()                          // 1er match
go.Components.GetAll<T>()                       // tous
go.Components.Get<T>( FindMode.EverythingInSelfAndDescendants ) // ce qu'utilise AxeController
Scene.GetAllComponents<T>().FirstOrDefault()    // global scène (ce qu'on fait pour Inventory/Hud)
```

Rappel non-negotiable #4 : `Scene.GetAllComponents<Component>()` renvoie 0. Query par **interface concrète** (IChoppable, IInteractable…) ou type concret. Pas par base type `Component`.

## Spawn d'un GameObject par code (pattern dans `Tree.SpawnAt`)

```csharp
var go = Scene.CreateObject();
go.Name = "X";
go.WorldPosition = ...;
go.WorldScale = ...;
var mr  = go.AddComponent<ModelRenderer>(); mr.Model = Model.Cube; mr.Tint = ...;
var col = go.AddComponent<BoxCollider>();   col.Scale = Vector3.One;
var rb  = go.AddComponent<Rigidbody>();     rb.MassOverride = ...;
```

Pour standing trees : **`rb.MotionEnabled = false`** (non-negotiable #8). `StartFell` flippe à `true`.

## Trace / raycast

```csharp
var hit = Scene.Trace
    .Ray( origin, end )                 // ou .Sphere( radius, a, b ) / .Box(...)
    .Size( new BBox(-5, 5) )            // optionnel : box around ray
    .IgnoreGameObjectHierarchy( GameObject )
    .WithoutTags( "player" )
    .Run();
if ( hit.Hit ) { ... hit.EndPosition / hit.Normal / hit.Distance ... }
```

## Input

`Input.Pressed("Jump")`, `Input.Down("Run")`, `Input.Released(...)` — les noms sont ceux de `ProjectSettings/Input.config` (**case-insensitive**, mais on a déjà du `"attack1"` minuscule qui marche). Pour les axes : `Input.AnalogMove` (Vector3) et `Input.AnalogLook` (Angles).

## HUD immediate-mode

```csharp
var hud = Scene.Camera.Hud;
hud.DrawRect( new Rect(x, y, w, h), color );
hud.DrawText( new TextRendering.Scope( "Hello", Color.White, 32f ), pos );
```

**Pas de Razor** ici (choix volontaire — cf. README, repris de `moon_fps`).

## Source 2 — gotchas

- **Z is up**. Pas Y. `Vector3.Up = (0,0,1)`. Très facile d'oublier en venant de Godot/Unity.
- **Unités = inches**. 1 m ≈ 39.37 u. Toutes les distances en jeu sont en u. Ex: vitesse de marche ~240 u/s.
- **Échelle via `WorldScale`** sur GameObject — `Model.Cube` est 1×1×1, donc une boîte de tronc d'arbre = `WorldScale = (32, 32, 280)`.
- **Forward** = `+X` historiquement Source, mais la doc bouge vers `Vector3.Forward` cleané — utilise les helpers (`Rotation.Forward`), pas les axes en dur.
- **`Model.Cube` / `Model.Sphere` ignorent `ModelRenderer.Tint`** ([[sbox-model-cube-ignores-tint]], [[sbox-model-sphere-tint-unreliable]]). Pour geometry tintée il faut un .vmdl ou un Material override.

## Hotload — bon à savoir

- Le hotload recompile et reload les assemblies à chaque save de `.cs`/`.razor`. Pas besoin de redémarrer l'éditeur pour 99% des changements.
- **Pièges** :
  - Changer la valeur par défaut d'un `static` ou d'un `{ get; } = ...` **ne prend pas effet** au hotload (le runtime garde l'ancienne). Utiliser un `=> "World"` (expression-bodied) ou `const` pour bypass. Cf. doc hotload.
  - Pour reset un state hotload-sensible, redémarrer l'éditeur ([[sbox-editor-restart-when-static-tunables-change]] pour la procédure non-interactive).
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

Mais pour valider du gameplay end-to-end, on a déjà `tools\selftest.ps1` : swing réel, spawn distribution, stump/respawn, split, pickup/sell, cascade, too-hard, stats, prestige. Pas de MSTest project actuel.

## Où chercher de la doc à jour

- **context7 MCP** : préférer les library IDs `/llmstxt/sbox_game_llms_txt` (le plus dense pour LLM) ou `/websites/sbox_game_api`. WebFetch sur `sbox.game/dev/doc/...` **ne marche pas** (rendu JS — la page renvoie juste `s&box`).
- **XML locaux** : `C:\Program Files (x86)\Steam\steamapps\common\sbox\bin\managed\Sandbox.*.xml` — doc générée des XML comments, source of truth.
- **Source du base addon** : `C:\Program Files (x86)\Steam\steamapps\common\sbox\addons\base\code\` — exemples canoniques (Citizen, Networking).
- **Templates** : `...\sbox\templates\game.minimal`, `game.playercontroller`, etc. Bon point de référence pour la structure.
- **Sample** : `...\sbox\samples\sweeper`.
- **Reflection live (MCP bridge)** : si sbox-dev est ouvert, `mcp__sbox__describe_type` / `mcp__sbox__get_method_signature` battent grep XML — la réflexion est toujours à jour.
