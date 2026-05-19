# Tree Chopping — s&box prototype

Migration vertical-slice du proto Godot `tree-chopping` vers s&box. **Ce dossier ne touche pas au projet Godot d'origine** (`../tree-chopping`).

## Statut

Ce qui marche (compile + scène jouable côté éditeur, gameplay à valider à la main) :
- Castor TPS — WASD, sprint (Shift), saut (Espace), souris pour regarder
- Hache → clic gauche → cycle de chop (3 hits → arbre tombe avec slow tip)
- Tombe + log → 2 hits → casse en log pieces → 4 wood chunks par piece
- Wood chunks aimantés au castor (radius 48u), comptés dans `WoodInventory`
- HUD compteur `Wood : N` en haut-gauche

Hors scope cette session :
- SWE / APIC water sim
- Voxel terrain + dig
- Dam / pool
- Biomes / axe ladder / weather
- Kenney assets (cube DEV uniquement)

## Ouvrir le projet

L'éditeur a déjà été enregistré (`config/addons.json` mis à jour). Au lancement de **sbox-dev.exe**, `Tree Chopping` apparaîtra dans la liste des projets.

Sinon, à l'éditeur :
1. File → Open Project → naviguer vers `C:\dev\tree-chopping-sbox\tree_chopping.sbproj`
2. Une fois ouvert, ouvre `Assets/scenes/main.scene`
3. Play (F5)

## Build manuel (vérification compile sans GUI)

Le code C# est validé offline avec :

```powershell
cd C:\dev\tree-chopping-sbox\Code
dotnet build tree_chopping.csproj
```

Sortie : `Code/bin/tree_chopping.dll`. Le csproj est gitignored et régénéré au besoin — celui fourni référence directement `bin/managed/Sandbox.*.dll` depuis l'install s&box.

## Architecture

| Fichier | Rôle |
|---|---|
| `Code/Tunables.cs` | Constantes game-feel (vitesse, masse, distances). Source 2 units (≈ inches, 1 m ≈ 40 u). |
| `Code/SceneStarter.cs` | Bootstrap component sur la scène — spawn castor + forêt + HUD + inventaire au `OnAwake`. |
| `Code/BeaverController.cs` | Move/look/jump + chooseSwingTarget (cône + range, dot-based). |
| `Code/Tree.cs` | États : standing → falling (slow tip torque + push) → landed log. Implémente `IChoppable`. |
| `Code/LogPiece.cs` | Multi-hit → spawn de wood chunks. Implémente `IChoppable`. |
| `Code/WoodChunk.cs` | Aimantation distance-based vers le castor + `WoodInventory.Add`. |
| `Code/WoodInventory.cs` | Singleton scope-scène, expose `Wood` count. |
| `Code/WoodHud.cs` | `Scene.Camera.Hud` immediate-mode pour afficher le compteur. |
| `Assets/scenes/main.scene` | Sun + Skybox + Ground plane + Bootstrap GameObject. Tout le reste est spawn-au-runtime. |

Pattern repris de `C:/dev/nico/moon_fps` (pas de Razor, HudPainter en immediate mode).

## Différences vs proto Godot

- **Units** : Source 2 utilise des inches (1u ≈ 2.54 cm). Tous les Tunables sont en unités s&box, pas en mètres.
- **Coords** : Z is up (vs Y in Godot).
- **Pas de voxel** : ground = plane statique. Pioche + dig non portés.
- **Pas d'eau** : aucun équivalent SWE/APIC. Trees tombent sur le plan, pas dans une rivière.
- **Pas de Kenney** : `Model.Cube` partout, tinte via `ModelRenderer.Tint`. Bon enough pour un prototype, à remplacer par des `.vmdl` plus tard.

## Étapes suivantes plausibles

1. Test à la main, vérifier que les arbres tombent + se cassent + chunks ramassés
2. Tuner Tunables (probablement masse / torque off d'un facteur dans Source units)
3. Remplacer Model.Cube par 1-2 `.vmdl` (tree.vmdl + chunk.vmdl) si on veut un look
4. Ajouter axe-anim (swing visible sur le castor)
5. Decider : porter l'eau (SWE → HLSL compute), ou rester sur ce slice
