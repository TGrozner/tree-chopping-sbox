# Prompt de session — Tree Chopping (s&box, mow-the-lawn)

À copier-coller en début de nouvelle conversation Codex pour reprendre proprement, ou que tu lises en premier si une session démarre cold sur ce repo.

---

Je suis Thomas. Projet : **mow-the-lawn-like dans s&box** (Source 2 + C#/.NET). Loop : chop arbres → wood → shop sur le sommet de la montagne (E pour upgrade axe T0→T3) → re-chop plus gros. Continuous play, pas de runs. Pivot fait 2026-05-20. Repo GitHub : `https://github.com/TGrozner/tree-chopping-sbox`.

**Suis le protocole ci-dessous à la lettre — chaque ligne a coûté du debug.**

## Avant de toucher au code

1. **Lis `AGENTS.md`** en entier — non-négociables (whitelist, Source 2, AZERTY, F-keys, MotionEnabled, etc.).
2. **Lis `Code/AGENTS.md`** dès que tu touches `Code/**/*.cs` — patterns Component lifecycle / Property attrs / hotload / spawn par code / HUD / Source 2 gotchas.
3. **MEMORY.md** — pointeurs vers les notes durables (1 ligne chacune).
4. **Confirme l'état de départ est vert** :
   ```powershell
   dotnet build "C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj"
   & "C:\dev\tree-chopping-sbox\tools\selftest.ps1"
   ```
   Build = 0 erreur. Selftest = `RESULT: PASS`. Si rouge → **diagnose d'abord**, pas de nouveau code par-dessus.
5. **Demande-moi la feature** avant de planifier. Plan d'abord, code après.

## Boucle de travail

1. **Plan 3-5 puces max.** Quels fichiers tu touches, pourquoi. Attends `ok` ou implicit OK avant code.
2. **Petits changements.** Pas de refactor opportuniste. Un changement = une intention. Si tu vois autre chose, signale et demande.
3. **Build automatique** : un PostToolUse hook lance `dotnet build` après chaque Edit/Write sur `Code/**/*.cs`. Échec → blocking message qui te remonte les erreurs. **Tu ne peux pas accumuler 4 fichiers cassés sans le savoir.**
4. **Selftest automatique** : un Stop hook lance `tools/selftest.ps1` si tu as touché `Tree.cs` / `SceneStarter.cs` / `BeaverController.cs` / `GameState.cs` / `WoodItem.cs` / `ShopStation.cs`. Échec → bloque le stop. **Couvre le chop pipeline + le wood payout.**
5. **Play GUI obligatoire** si tu touches rendering / scale / HUD / particles / camera / input / lighting. Le headless ne rend pas. **Dis-le explicitement** quand le headless ne suffit pas, ne réclame jamais "ça marche" sans validation visuelle quand le visuel est en jeu.
6. **Pas de commit sans demande explicite (`commit ça`, `commit push`, etc.).** Je fais mes propres commits `phase5X:`.
7. **MCP `sbox` bridge** : si sbox-dev.exe est ouvert + dock bridge visible, tu peux driver l'éditeur live (start_play, take_screenshot, get_scene_hierarchy, set_runtime_property, etc.).

## Architecture en cours (rapide)

- **`SceneStarter.cs`** : bootstrap (singletons, terrain, mountain borders, beaver, shop+totem, forêt initiale, 4 gates, pet)
- **`Tree.cs`** : multi-chop → StartFell → fall-physics → BecomeLandedLog → split landed log → WoodItem drops. Branche IsGate → SceneStarter.OnGateBroken pour ring expansion. Auto-respawn par kind (Sapling 30s..Veteran 5min..Mythic +10min)
- **`BeaverController.cs`** : Idle → WindUp → Recovery state machine, hit-stop, FOV punch, applique GameState.SpeedMultiplier sur PlayerController.WalkSpeed, AerialView toggle pour top-down screenshot
- **`GameState.cs`** : Wood + AxeTier (0..6) + Speed/Luck/Power tiers (0..5) + PetTier (0..5) + Spirits + GatesBroken + TotalWoodEarned persistence (FileSystem.Data/progress_{steamId}.json — per-user clé en MP)
- **`ShopStation.cs`** : stations Tools / Sell / Upgrades / Prestige, inputs contextualisés, sell backpack → wallet
- **`WoodItem.cs`** : items bois pickables, magnet de proximité, backpack
- **`WoodHud.cs`** : wood pulse + axe tier badge avec nom (Hands→Chainsaw) + 7 pips + 6-line shop menu + replant line + teleport hint
- **`AutoPlay.cs`** : autonomous full-loop driver (chop → shop → upgrade → repeat) + LookBack one-shot
- **`SelfTest.cs`** : Init → Approach → Swing → Verify(Wood>0), wait-on-condition. GameState.Save short-circuits when active (no clobber)
- **`TerrainHeightmap.cs`** : Sandbox.Terrain procédural + materials/ground.vmat tinted green
- **`MapBorders.cs`** : ring de cubes tagged "border" warm earth tones
- **`ChipBurst.cs`** : chips/leaves/splinters custom-physics (pas de Rigidbody — perf killer)
- **`Pet.cs`** : cosmetic orb orbitant le castor, auto-sync à GameState.PetTier
- **`PerfProbe.cs`** : FpsAvg/Min + Renderers/Trees lisibles via bridge sans HUD
- **`Mat.cs`** + **`Sfx.cs`** : helpers

Architecture détaillée : `AGENTS.md` section "Architecture gameplay actuelle".

## Pièges déjà payés

- **`Model.Cube` ignore `Tint`** sauf `MaterialOverride = materials/default.vmat`. Mémoire : `sbox-model-cube-ignores-tint`. Pattern dans `Code/Mat.cs`.
- **Standing rigidbodies (Tree) → `MotionEnabled = false`** sinon le castor les renverse en marchant. `StartFell` flip à true.
- **`Scene.GetAllComponents<Component>()` renvoie 0.** Toujours query par interface (`IChoppable`) ou type concret.
- **`System.Environment.*` banni** par la whitelist. Flags → `[ConVar]`.
- **F1-F12 banni** pour bindings — éditeur les intercepte. État courant : `DebugToggle=B`, `PlayShortcut=Ctrl+Shift+P`.
- **AZERTY** : si tu scripts SendInput, VK_Z = Forward (pas VK_W). Mais `Input.config` "W" cible le caractère W.
- **`SetCursorPos` envoie `Input.AnalogLook`** dans la Play view — chaque move souris pivote la caméra. Préfère le headless selftest pour la logique.
- **`materials/default.vmat` a `g_vColorTint` alpha=0** = tint désactivé. Pour un terrain green : écris ton propre .vmat avec alpha=1 (voir `Assets/materials/ground.vmat`).
- **SelfTest = wait-on-condition, pas wait-on-time.** Tree.Wood payout timing varie 5× selon Sapling vs Normal. Fix passe par `_state.Wood > _woodBeforeSwings` + hard timeout 8s. Mémoire : `feedback-selftest-wait-conditions`.
- **Diagnostics + s'arrêter = vautrage.** Si tu ajoutes des `Log.Info` pour diagnostiquer, **re-run le harness dans le même turn** pour voir les logs. Mémoire : `feedback-no-vautrage`.
- **Z is up. Unités = inches (1 m ≈ 39.37 u). Vector3.Forward = +X.**
- **Scale pour Model.Cube** : `WorldScale = wantedSize / Tunables.CubeBase` (`CubeBase = 50f`).

## Outils

- `dotnet build "C:\dev\tree-chopping-sbox\Code\tree_chopping.csproj"` — types OK
- `& "C:\dev\tree-chopping-sbox\tools\selftest.ps1" [-Seeds N]` — mow-the-lawn scenario end-to-end, exit 0/1/3 = PASS/FAIL/TIMEOUT
- `sbox-server.exe +game ... +maxplayers 1` — Component lifecycle + physique headless (Log.Info → stdout)
- `sbox-dev.exe` (GUI) — Play F5
- MCP `sbox` bridge — si sbox-dev ouvert, drive l'éditeur live
- context7 MCP (`/llmstxt/sbox_game_llms_txt`) — doc s&box à jour (**pas WebFetch sur sbox.game**, rendu JS)
- XML s&box locaux : `C:\Program Files (x86)\Steam\steamapps\common\sbox\bin\managed\Sandbox.*.xml`

## Style

Tabs + `if ( foo )` (espaces dans parens). Default zéro commentaire — uniquement quand le *why* est non-évident. Pas de wrappers legacy, pas de stubs hypothétiques. FR OK dans logs/commits/AGENTS.md ; EN pour XML docs sbox-facing.

## Si tu hésites

Demande. Le coût d'une question est ~0, celui d'un revert ~1h.
