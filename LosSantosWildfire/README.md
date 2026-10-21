# LosSantosWildfire

Mod GTA V (ScriptHookVDotNet v3, C#) qui simule un grand incendie façon Los Angeles sur Los Santos :
ciel orangé, smog, cendres, feux réels autour du joueur, colonnes de fumée au loin, propagation,
pompiers et piétons qui fuient — avec un coût en FPS volontairement minimal.

## Contrôles

| Touche | Action |
|---|---|
| **F10** | Activer / désactiver le mod (tout est nettoyé à la désactivation) |
| **F11** | Intensité : Faible → Moyenne → Extrême |

Une notification s'affiche à chaque changement. Les touches se modifient dans le `.ini`.

## Installation

1. Installer **ScriptHookV** (Alexander Blade) : copier `ScriptHookV.dll` et `dinput8.dll` à la racine de GTA V.
2. Installer **ScriptHookVDotNet v3** (crosire, version 3.6 ou plus récente) : copier
   `ScriptHookVDotNet.asi`, `ScriptHookVDotNet2.dll`, `ScriptHookVDotNet3.dll` à la racine du jeu.
   Prérequis : .NET Framework 4.8 et Visual C++ Redistributable x64.
3. Créer le dossier `scripts` à la racine du jeu s'il n'existe pas.
4. Copier dans `GTA V\scripts\` :
   - `LosSantosWildfire.dll` (compilé, voir ci-dessous)
   - `LosSantosWildfire.ini`
5. Lancer le jeu en **mode histoire** (jamais en GTA Online), puis appuyer sur **F10**.

> Utilisez bien la DLL : SHVDN compile chaque `.cs` séparément, donc copier les fichiers de `src/`
> un par un dans `scripts\` ne fonctionnerait pas (les classes dépendent les unes des autres).

### Compiler

```bash
dotnet build -c Release
# → bin/Release/net48/LosSantosWildfire.dll
```

Le projet référence le paquet NuGet `ScriptHookVDotNet3` (3.6.0) et compile sous Windows, Linux ou
macOS avec le SDK .NET 8. Avec Visual Studio : ouvrir `LosSantosWildfire.csproj` et compiler en Release.

## Architecture

| Fichier | Rôle |
|---|---|
| `src/LosSantosWildfire.cs` | Script principal : touches, notifications, cadence, activation / nettoyage |
| `src/Config.cs` | Lecture du `.ini` (valeurs bornées) + profils d'intensité |
| `src/FireManager.cs` | Feux de proximité : foyers virtuels, tri par distance, pool de feux, propagation |
| `src/AtmosphereManager.cs` | Timecycle, météo persistante, cendres attachées au joueur |
| `src/SmokeManager.cs` | Colonnes de fumée lointaines (ptfx seul, pas de vrais feux) |
| `src/EmergencyManager.cs` | Pompiers (incidents de dispatch) et fuite des piétons |
| `src/PtfxManager.cs` | Chargement unique des dictionnaires ptfx, création / arrêt des effets |
| `src/FireZones.cs` | Zones prédéfinies (Vinewood Hills, Mirror Park, Chumash, Paleto…) |

### Comment les performances sont protégées

- **Foyers virtuels** : une zone ou un feu aléatoire n'est qu'une position en mémoire (jusqu'à 400).
  Seuls les **N plus proches** dans le rayon deviennent de vrais feux + ptfx.
- **Pool pré-alloué** de 60 emplacements de feux et 8 colonnes de fumée : aucune création sans limite.
- **Cadences** : feux toutes les 500 ms (minimum), ambiance 1 s, fumée 2 s, piétons 1 s, pompiers 15 s.
  Le `Tick` ne fait presque rien à chaque appel.
- **Hystérésis** : un feu actif ne s'éteint qu'au-delà de 115 % du rayon → pas de clignotement.
- **Allumages étalés** : au plus 4 nouveaux feux par mise à jour.
- **Assets** : chaque dictionnaire ptfx est demandé **une seule fois**, puis on teste
  `HAS_NAMED_PTFX_ASSET_LOADED` sans bloquer le jeu. Un effet introuvable n'est pas redemandé en boucle.
- **Sol** : le Z de chaque foyer n'est calculé qu'une fois ; les foyers dans l'eau sont abandonnés.
- **Nettoyage complet** (F10, rechargement des scripts, fermeture) : feux, feux enfants, ptfx,
  incidents, timecycle, météo et dictionnaires sont libérés.

## Natives utilisées

| Native | Rôle |
|---|---|
| `START_SCRIPT_FIRE` | Crée un vrai feu (dégâts, propagation limitée par `maxChildren`) |
| `REMOVE_SCRIPT_FIRE` | Supprime un feu de script par son handle |
| `STOP_FIRE_IN_RANGE` | Éteint aussi les feux enfants autour d'un foyer libéré |
| `GET_NUMBER_OF_FIRES_IN_RANGE` | Détecte un feu éteint (pompiers, extincteur) |
| `GET_GROUND_Z_FOR_3D_COORD` | Place les foyers au sol |
| `GET_WATER_HEIGHT_NO_WAVES` | Évite de placer un feu dans l'eau |
| `REQUEST_NAMED_PTFX_ASSET` | Charge un dictionnaire d'effets (une fois) |
| `HAS_NAMED_PTFX_ASSET_LOADED` | Vérifie le chargement sans bloquer |
| `REMOVE_NAMED_PTFX_ASSET` | Libère le dictionnaire à la désactivation |
| `USE_PARTICLE_FX_ASSET` | Choisit le dictionnaire du prochain effet |
| `START_PARTICLE_FX_LOOPED_AT_COORD` | Flammes, fumées locales, colonnes lointaines |
| `START_PARTICLE_FX_LOOPED_ON_ENTITY` | Cendres attachées au joueur |
| `SET_PARTICLE_FX_LOOPED_SCALE` | Ajuste la taille des effets au changement d'intensité |
| `DOES_PARTICLE_FX_LOOPED_EXIST` | Vérifie un effet avant de l'arrêter |
| `STOP_PARTICLE_FX_LOOPED` / `REMOVE_PARTICLE_FX` | Arrête et détruit un effet |
| `SET_TIMECYCLE_MODIFIER` / `SET_TIMECYCLE_MODIFIER_STRENGTH` | Filtre orangé et sa force |
| `GET_TIMECYCLE_MODIFIER_INDEX` | Ré-applique le filtre si une mission l'a effacé |
| `CLEAR_TIMECYCLE_MODIFIER` | Retire le filtre |
| `SET_WEATHER_TYPE_OVERTIME_PERSIST` | Transition vers SMOG / FOGGY, maintenue |
| `CLEAR_WEATHER_TYPE_PERSIST` / `CLEAR_OVERRIDE_WEATHER` | Rend la main au cycle météo normal |
| `CREATE_INCIDENT` / `DELETE_INCIDENT` | Envoie puis libère une intervention des pompiers |
| `TASK_SMART_FLEE_COORD` | Fait fuir un piéton loin d'un feu |

Plus les fonctions SHVDN `World.GetNearbyPeds`, `Game.Player.Character`, `Notification.Show`, `ScriptSettings`.

## Noms d'assets : niveau de certitude

| Élément | Nom | Certitude |
|---|---|---|
| Flammes | `core` / `fire_wrecked_plane_cockpit` | Connu et très utilisé par les mods |
| Fumée locale | `core` / `ent_amb_smoke_foundry` | Connu |
| Colonne lointaine | `scr_trevor3` / `scr_trev3_trailer_plume` | Connu (mission Trevor 3) |
| Cendres | `core` / `ent_amb_falling_ash` | **Incertain** — à vérifier en jeu, modifiable dans le `.ini` |
| Timecycle | `glasses_orange` | Présent dans les timecycle mods du jeu, rendu à valider |
| Météo | `SMOG`, `FOGGY` | Types météo officiels |
| Dispatch pompiers | `3` (DT_FireDepartment) | Valeur documentée par la communauté |
| Coordonnées des zones | `FireZones.cs` | Approximatives (le Z des feux est recalé au sol) |

Si un nom d'effet est faux, le mod ne plante pas : l'effet est simplement absent (le feu réel reste).
Autres timecycles à essayer : `nervousRON_fog`, `glasses_brown`, `NG_filmic04`.

## Régler selon la puissance du PC

| Réglage | PC modeste | PC moyen (défaut) | PC puissant |
|---|---|---|---|
| `Radius` | 100 | 150 | 200–250 |
| `MaxFires` | 15–20 | 40 | 50–60 |
| `MaxChildFires` | 0 | 1 | 2 |
| `Columns` | 2–3 | 6 | 8 |
| `UpdateIntervalMs` | 1000 | 500 | 500 |
| `MaxActivationsPerUpdate` | 2 | 4 | 6 |
| `Spread / Enabled` | false | true | true |
| `Ash` | false | true | true |

Conseils :
- Le plus coûteux, ce sont les **flammes ptfx et les feux enfants** : baissez d'abord `MaxFires` et `MaxChildFires`.
- `Atmosphere` (timecycle + météo) ne coûte quasiment rien : gardez-le même sur un petit PC.
- En voiture rapide, un rayon trop grand provoque plus d'allumages : `MaxActivationsPerUpdate` lisse ces pics.
- Si les pompiers saturent la zone, mettez `Firefighters = false` ou réduisez `MaxFires`.
- Les colonnes très lointaines peuvent être masquées par la distance d'affichage des particules du moteur : c'est normal.
