# Prompt de session — port Godot → s&box

À copier-coller dans une nouvelle conversation Claude Code pour reprendre proprement.

---

Je suis Thomas, je porte un proto Godot (`C:\dev\tree-chopping\` — référence read-only) vers s&box (`C:\dev\tree-chopping-sbox\` — la copie de travail). Tu vas m'aider à porter une feature spécifique que je te dirai. **Suis le protocole ci-dessous à la lettre — il existe parce qu'on s'est cassé la gueule plusieurs fois sans.**

## Avant de toucher au code

1. **Lis `C:\dev\tree-chopping-sbox\CLAUDE.md`** en entier — règles non-négociables (whitelist, Source 2, AZERTY, F-keys, etc.).
2. **Lis `C:\Users\thoma\.claude\projects\C--dev-tree-chopping-sbox\memory\MEMORY.md`** — pointeurs vers les notes durables.
3. **Confirme l'état de départ est vert :**
   ```powershell
   dotnet build "C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj"
   & "C:\dev\tree-chopping-sbox\tools\selftest.ps1" -TimeoutSeconds 30
   ```
   Build = 0 erreur. Selftest = `RESULT: PASS`. Si l'un des deux est rouge, **n'écris pas de code** — diagnose d'abord.
4. **Demande-moi la feature à porter.** Si je t'ai déjà donné un nom, identifie le fichier Godot source (`grep -r feature_name C:\dev\tree-chopping\`) et lis-le AVANT de proposer un plan s&box.

## Pour chaque feature portée

Boucle stricte :

1. **Plan d'abord, 3-5 puces max.** Dis quels fichiers tu touches (max 2-3) et pourquoi. Attends `ok` avant de commencer.
2. **Petits changements.** Pas de refactor opportuniste, pas de "tant qu'on y est". Un changement = une intention. Si tu vois un autre truc à fixer, dis-le et **demande** avant.
3. **Build après chaque édition non triviale.** Pas "j'éditerai 4 fichiers et je build à la fin".
4. **Selftest si tu touches au pipeline chop** (Tree / LogPiece / WoodChunk / WoodInventory / BeaverController swing). Build vert ≠ pipeline vert.
5. **Play GUI obligatoire si tu touches rendering / scale / HUD / particles / camera / input.** Le headless valide la logique, pas les pixels. **Dis-le explicitement** quand le headless ne suffit pas — ne réclame jamais "ça marche" si tu n'as pas validé visuellement et que le visuel est en jeu.
6. **Pas de commit sans `commit ça` explicite de ma part.** Je fais mes propres petits commits `phaseNx:`. Laisse les changements stagés/unstagés.

## Pièges déjà payés (lis avant de t'embourber dedans)

- **`Model.Load(path)` ment.** Si le `.vmdl_c` n'est pas mounté, l'engine logue `engine/R Error loading resource file "X.vmdl_c" (File not found)` MAIS `Model.Load` retourne quand même un objet non-null, non-`Model.Error`, avec **bounds `(10,100,25)`** (placeholder partagé). Donc `m == null || m == Model.Error` ne détecte rien. **Vraie vérification : `Model.Bounds.Size.Length > 1f && Bounds.Size != (10,100,25)`.** Et surtout : lance `sbox-server.exe +game ... +maxplayers 1` 10s et grep `engine/R Error` dans le stdout — c'est la source de vérité.
- **`Scene.GetAllComponents<Component>()` renvoie 0.** Toujours query par interface ou type concret.
- **`System.Environment.*` banni** par la whitelist compiler. Flags de lancement → `[ConVar]`.
- **F1-F12 banni** pour les bindings — éditeur les intercepte. État courant : `DebugToggle = B`, `PlayShortcut = Ctrl+Shift+P`.
- **AZERTY** : si tu scripts SendInput, `VK_Z` pour Forward, `VK_Q` pour Left. Dans `Input.config` les noms `W/A/S/D` ciblent les caractères, pas les positions physiques.
- **Standing rigidbodies (Tree, Rock debout) → `MotionEnabled = false`** sinon le castor les renverse en marchant. `StartFell` flip à true.
- **Z is up, unités = inches, `Vector3.Forward = +X`.** `UnitsPerMeter = 39.37`.
- **Scale pour Model.Cube** : `WorldScale = wantedSize / Tunables.CubeBase` (`CubeBase = 50f`), et `BoxCollider.Scale = new Vector3( Tunables.CubeBase )` pour récupérer la taille world. **Toute scale qui ne suit pas ce pattern est suspecte** — phase3 a fait des `WorldScale = 30f` partout qui ont produit des cubes de 1500u une fois retombé sur `Model.Cube`.
- **Class member name collision** : ne nomme pas un membre `Log` dans une classe (il shadowait `Sandbox.Internal.GlobalGameNamespace.Log` dans le scope). Renomme proactivement si tu vois `class X { ... public static Y Log => ... }`.
- **Headless ≠ Play GUI.** Le selftest ne valide pas le rendu, ni l'input, ni les particles, ni le son, ni la camera, ni la position relative des choses à l'écran. Si tu touches à ça, dis-le.
- **SDF dormant** : `Code/BeaverController.TrySdfDig` + `Assets/sdf/rock_volume.sdfvol` + `EnsureSdfWorld()` sont en place mais déconnectés (`main.scene` n'a plus `RockVolume`). Si tu réactives, **refais le design** : `EnsureSdfWorld` rempli toute la map avec un `BoxSdf3D` solide qui mange le creek et les banks. Faut soit localiser à une zone (montagne dans un coin), soit faire des Adds par Rock individuel.
- **Models actuellement = `Model.Cube` partout** (phase3 reverted, paths asset.party `pr/gta5_*` / `rock_kit/*` / `models/dead*` confirmés non mountés sur cette install). API `Models.TreeFor / RockVariant / etc.` préservée, callers inchangés.

## Pour le port de modèles 3D — protocole strict

Si je te demande de réimporter des modèles 3D (style **Valheim-like / low-poly / stylized**) :

1. **Cherche les packs asset.party d'abord, AVANT d'écrire la moindre ligne.** Utilise WebSearch sur `asset.party low poly nature` / `asset.party stylized trees` / `asset.party valheim` — pas WebFetch sur sbox.game (rendu JS). Cible : tags `nature`, `low-poly`, `stylized`, `painterly`. Préfère les packs avec **plusieurs species** (sapin / chêne / souche) et **rochers low-poly** dans le même style. Reporte-moi les candidats avec leur ident asset.party (genre `username.pack_name`) — **je décide avant qu'on importe.**
2. **Référence-les via `PackageReferences` dans `tree_chopping.sbproj`.** C'est la SEULE façon fiable de garantir qu'un asset est mounté sur ma machine. **Ne te base PAS sur `pr/...` ou `rock_kit/...` qui ont "marché ailleurs"** — phase3 s'est plantée précisément là-dessus.
3. **Vérifie le mounting AVANT d'écrire des call sites.** Boot `sbox-server.exe +game ... +maxplayers 1` 10s, grep `engine/R Error.*\.vmdl_c` — chaque path doit être absent de cette liste. Si un path apparaît → il n'est pas mounté, le pack référencé est faux.
4. **Double-check via bounds.** Pour chaque path, vérifie que `Model.Load(path).Bounds.Size` ≠ `(10,100,25)` (placeholder) et ≠ proche-zéro. Ajoute un helper d'audit (cf. `Models.AuditAllPaths` pattern qu'on avait écrit phase 3 + reverted) — gate-le sur un `[ConVar]` pour ne pas spammer le log normal.
5. **N'introduis JAMAIS un model dans un call site sans avoir validé son mounting empiriquement.** Code qui compile ≠ asset qui charge.
6. **Garde le fallback `Model.Cube + tint` dans `Models.cs`** pour chaque entry. Si le pack disparaît ou un path bouge, le jeu redevient lisible immédiatement au lieu de spawner des placeholders fantômes.
7. **Tune le scale par la mesure, pas l'estimation.** Les low-poly Valheim-like sont typiquement authored à ~1m. Un tree-canopy de 6m (~240u s&box) demande un WorldScale d'environ `240u / model_intrinsic_height`. Lance Play GUI, mesure visuellement contre le castor (72u tall) et le creek (200u wide), itère sur le scale. Ne devine pas.
8. **Animations / vertex colors / matériaux** : si le pack a des matériaux baked, garde le `Tint` à `Color.White` pour ne pas le multiplier en sombre. Si tu veux le tinter quand même (species coding), teste visuellement — beaucoup de packs stylized partent en bouillie quand tu tint un albedo déjà saturé.

## Outils à ta disposition

- `dotnet build "C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj"` — types OK
- `& "C:\dev\tree-chopping-sbox\tools\selftest.ps1" -TimeoutSeconds 30` — pipeline chop end-to-end via le vrai swing path, exit 0=PASS / 1=FAIL / 3=TIMEOUT
- `sbox-server.exe +game tree_chopping.sbproj +maxplayers 1` (avec Register-ObjectEvent harness, cf. `tools/selftest.ps1`) — Component lifecycle + physique + stdout, **PAS le rendu ni l'input**
- `sbox-dev.exe` (GUI) — Play F5 = Ctrl+Shift+P configuré sur l'éditeur, pour valider visuellement
- context7 MCP (`/llmstxt/sbox_game_llms_txt` ou `/websites/sbox_game_api`) — doc s&box à jour. **NE FAIS PAS WebFetch sur sbox.game** (rendu JS, renvoie littéralement la string "s&box").
- XML s&box locaux : `C:\Program Files (x86)\Steam\steamapps\common\sbox\bin\managed\Sandbox.*.xml` — source of truth pour les signatures API.

## Style de code

Tabs + `if ( foo )` (espaces dans les parens). Default = **zéro commentaire** — un commentaire ne se justifie que quand le *why* est non-évident (contrainte cachée, incident passé, invariant subtil). Pas de wrappers "legacy", pas de stubs pour des besoins hypothétiques. FR OK dans logs/commits/CLAUDE.md, EN pour XML docs sbox-facing.

## Si tu hésites

Demande. Le coût d'une question est ~0, le coût d'un revert d'un mauvais refactor est ~1h.
