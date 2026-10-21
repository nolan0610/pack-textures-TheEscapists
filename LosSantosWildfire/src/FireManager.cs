using System;
using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace LosSantosWildfire
{
    /// <summary>
    /// Foyer "virtuel" : une simple position en mémoire. Il ne coûte rien tant qu'il n'est pas
    /// matérialisé dans un FireSlot (vrai feu + ptfx). On peut en garder quelques centaines.
    /// </summary>
    internal sealed class FireSite
    {
        public Vector3 Position;
        public bool GroundResolved;   // Z recalé au sol ?
        public int GroundAttempts;    // échecs de recalage (terrain pas encore chargé)
        public FireZone Zone;         // zone d'origine (null = foyer aléatoire ou propagé)
        public int ReigniteAt;        // 0 = brûle ; sinon GameTime à partir duquel il peut se rallumer
        public bool Dead;             // foyer invalide (dans l'eau, sol introuvable) : à supprimer
        public bool Desired;          // fait partie des N plus proches à cette mise à jour
        public float DistSq;          // distance 2D² au joueur (calculée à chaque mise à jour)
        public FireSlot Slot;         // emplacement actif, ou null
    }

    /// <summary>
    /// Emplacement du pool : un vrai feu de script + ses effets. Les objets FireSlot sont
    /// alloués une seule fois au démarrage puis réutilisés indéfiniment.
    /// </summary>
    internal sealed class FireSlot
    {
        public FireSite Site;
        public int FireHandle;
        public int FlameFx;   // 0 = à (re)créer, -1 = échec définitif, >0 = handle valide
        public int SmokeFx;   // idem (une fumée locale seulement sur un feu sur trois)
        public bool WantsSmoke;
        public bool WantsFlame;
        public int StartedAt;
    }

    /// <summary>
    /// Gère les feux de proximité : génération des foyers, tri par distance, allumage /
    /// extinction des N plus proches, propagation et nettoyage.
    /// Mise à jour au maximum toutes les UpdateIntervalMs (≥ 500 ms), jamais à chaque frame.
    /// </summary>
    internal sealed class FireManager
    {
        private const int MaxSites = 400;               // foyers virtuels gardés en mémoire
        private const float KeepHysteresis = 1.15f;     // un feu actif reste allumé jusqu'à 115 % du rayon
        private const float RandomPruneFactor = 2.5f;   // foyers aléatoires oubliés au-delà de 2,5 × rayon
        private const float ZoneResetFactor = 3f;       // zones "réinitialisées" au-delà de 3 × rayon
        private const int ReigniteDelayMs = 180000;     // un foyer éteint peut repartir après 3 min
        private const int ExtinguishGraceMs = 4000;     // délai avant de tester si un feu est éteint
        private const int RandomSpawnIntervalMs = 1000;
        private const float ExtinguishCheckRadius = 3f;
        private const float CleanupRadius = 6f;         // rayon d'extinction (inclut les feux enfants)
        private const int MaxGroundAttempts = 6;

        private readonly Config _config;
        private readonly PtfxManager _ptfx;
        private readonly FireZone[] _zones;
        private readonly Random _rng;
        private readonly FireSlot[] _slots;
        private readonly List<FireSite> _sites = new List<FireSite>(MaxSites);
        private readonly List<FireSite> _candidates = new List<FireSite>(MaxSites);
        private static readonly Comparison<FireSite> ByDistance = (a, b) => a.DistSq.CompareTo(b.DistSq);

        private IntensityProfile _profile;
        private int _maxActive;
        private int _activeCount;
        private int _nextUpdate;
        private int _nextRandomSpawn;
        private int _nextSpread;
        private int _smokeCounter;

        /// <summary>Positions des feux actuellement allumés (lue par EmergencyManager).</summary>
        public readonly List<Vector3> ActivePositions = new List<Vector3>(Config.HardFireCap);

        public int ActiveCount { get { return _activeCount; } }
        public int MaxActive { get { return _maxActive; } }

        public FireManager(Config config, PtfxManager ptfx, FireZone[] zones, Random rng)
        {
            _config = config;
            _ptfx = ptfx;
            _zones = zones;
            _rng = rng;

            // Pool pré-alloué : aucun "new" pendant le jeu pour les emplacements.
            _slots = new FireSlot[Config.HardFireCap];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new FireSlot();
        }

        public void SetIntensity(IntensityProfile profile)
        {
            _profile = profile;
            int max = (int)Math.Round(_config.MaxFires * profile.FireMultiplier);
            _maxActive = Math.Max(1, Math.Min(Config.HardFireCap, max));

            // Ajuste l'échelle des flammes déjà en place (les feux en trop seront éteints
            // automatiquement à la prochaine mise à jour, car hors du top N).
            for (int i = 0; i < _slots.Length; i++)
            {
                FireSlot s = _slots[i];
                if (s.Site != null && s.FlameFx > 0)
                    Function.Call(Hash.SET_PARTICLE_FX_LOOPED_SCALE, s.FlameFx, profile.FlameScale);
            }
        }

        public void Update(int now, Vector3 player)
        {
            if (now < _nextUpdate) return;
            _nextUpdate = now + _config.UpdateIntervalMs;

            float r = _config.Radius;
            float radiusSq = r * r;
            float keepSq = radiusSq * KeepHysteresis * KeepHysteresis;

            SeedZones(player, r);

            if (_config.RandomFires && now >= _nextRandomSpawn)
            {
                _nextRandomSpawn = now + RandomSpawnIntervalMs;
                SpawnRandomSites(player, r, radiusSq);
            }

            CheckExtinguished(now);
            SelectAndApply(now, player, radiusSq, keepSq);
            RetryMissingEffects();

            if (_config.SpreadEnabled && now >= _nextSpread)
            {
                _nextSpread = now + _config.SpreadIntervalSeconds * 1000;
                TrySpread();
            }

            PruneSites(player, r);
            RebuildActivePositions();
        }

        // ------------------------------------------------------------------ génération

        /// <summary>Génère les foyers d'une zone prédéfinie quand le joueur s'en approche.</summary>
        private void SeedZones(Vector3 player, float r)
        {
            for (int i = 0; i < _zones.Length; i++)
            {
                FireZone zone = _zones[i];
                if (zone.Seeded) continue;

                float reach = r + zone.Radius;
                if (Dist2DSq(player, zone.Center) > reach * reach) continue;

                for (int n = 0; n < zone.SiteCount && _sites.Count < MaxSites; n++)
                {
                    // Point uniforme dans un disque (racine pour éviter de tout concentrer au centre).
                    double angle = _rng.NextDouble() * Math.PI * 2.0;
                    double dist = Math.Sqrt(_rng.NextDouble()) * zone.Radius;
                    var pos = new Vector3(
                        zone.Center.X + (float)(Math.Cos(angle) * dist),
                        zone.Center.Y + (float)(Math.Sin(angle) * dist),
                        zone.Center.Z);
                    AddSite(pos, zone);
                }
                zone.Seeded = true;
            }
        }

        /// <summary>Ajoute quelques foyers aléatoires autour du joueur si la densité est trop faible.</summary>
        private void SpawnRandomSites(Vector3 player, float r, float radiusSq)
        {
            int burningNearby = 0;
            for (int i = 0; i < _sites.Count; i++)
            {
                FireSite s = _sites[i];
                if (!s.Dead && s.ReigniteAt == 0 && Dist2DSq(player, s.Position) <= radiusSq) burningNearby++;
            }

            // On vise ~1,5 × le nombre de feux actifs pour avoir un peu de réserve.
            int target = _maxActive + _maxActive / 2;
            int toSpawn = Math.Min(6, target - burningNearby);
            for (int n = 0; n < toSpawn && _sites.Count < MaxSites; n++)
            {
                double angle = _rng.NextDouble() * Math.PI * 2.0;
                double dist = 12.0 + _rng.NextDouble() * Math.Max(1.0, r - 12.0);
                var pos = new Vector3(
                    player.X + (float)(Math.Cos(angle) * dist),
                    player.Y + (float)(Math.Sin(angle) * dist),
                    player.Z);
                AddSite(pos, null);
            }
        }

        /// <summary>Propagation simple : un feu actif "lance" un nouveau foyer à 6–15 m.</summary>
        private void TrySpread()
        {
            if (_activeCount == 0 || _sites.Count >= MaxSites) return;
            if (_rng.NextDouble() > _profile.SpreadChance) return;
            // Pas de propagation infinie : on limite le nombre de foyers candidats autour du joueur.
            if (_candidates.Count >= _maxActive * 2) return;

            int pick = _rng.Next(_activeCount);
            for (int i = 0; i < _slots.Length; i++)
            {
                FireSlot s = _slots[i];
                if (s.Site == null) continue;
                if (pick-- > 0) continue;

                double angle = _rng.NextDouble() * Math.PI * 2.0;
                double dist = 6.0 + _rng.NextDouble() * 9.0;
                Vector3 p = s.Site.Position;
                AddSite(new Vector3(p.X + (float)(Math.Cos(angle) * dist), p.Y + (float)(Math.Sin(angle) * dist), p.Z), null);
                return;
            }
        }

        private void AddSite(Vector3 pos, FireZone zone)
        {
            _sites.Add(new FireSite { Position = pos, Zone = zone });
        }

        // ------------------------------------------------------------------ sélection

        /// <summary>
        /// Tri par distance : seuls les N foyers les plus proches (dans le rayon) sont allumés.
        /// Les feux déjà actifs bénéficient d'une petite marge (hystérésis) pour éviter le clignotement.
        /// </summary>
        private void SelectAndApply(int now, Vector3 player, float radiusSq, float keepSq)
        {
            _candidates.Clear();
            for (int i = 0; i < _sites.Count; i++)
            {
                FireSite s = _sites[i];
                s.Desired = false;
                if (s.Dead) continue;
                if (s.ReigniteAt != 0)
                {
                    if (now < s.ReigniteAt) continue;
                    s.ReigniteAt = 0; // le foyer peut repartir
                }

                s.DistSq = Dist2DSq(player, s.Position);
                float limit = s.Slot != null ? keepSq : radiusSq;
                if (s.DistSq <= limit) _candidates.Add(s);
            }

            _candidates.Sort(ByDistance);
            int desired = Math.Min(_maxActive, _candidates.Count);
            for (int i = 0; i < desired; i++) _candidates[i].Desired = true;

            // 1) Extinction des feux qui ne font plus partie du top N (trop loin ou en surplus).
            for (int i = 0; i < _slots.Length; i++)
            {
                FireSlot slot = _slots[i];
                if (slot.Site != null && !slot.Site.Desired) Release(slot);
            }

            // 2) Allumage des nouveaux, en nombre limité par mise à jour pour lisser le coût.
            int attempts = 0;
            for (int i = 0; i < desired && attempts < _config.MaxActivationsPerUpdate; i++)
            {
                FireSite s = _candidates[i];
                if (s.Slot != null) continue;
                attempts++;
                TryIgnite(s, now);
            }
        }

        private bool TryIgnite(FireSite site, int now)
        {
            FireSlot slot = FindFreeSlot();
            if (slot == null) return false;

            if (!site.GroundResolved && !ResolveGround(site, now)) return false;

            Vector3 pos = site.Position;
            // Feu de script réel. maxChildren limite la propagation native du moteur.
            slot.FireHandle = Function.Call<int>(Hash.START_SCRIPT_FIRE, pos.X, pos.Y, pos.Z, _config.MaxChildFires, false);
            // Le feu de script affiche déjà ses propres flammes : le ptfx "renfort" n'est mis
            // que sur un feu sur deux, et la fumée locale sur un sur quatre (budget particules).
            int n = _smokeCounter++;
            slot.WantsFlame = (n % 2) == 0;
            slot.WantsSmoke = (n % 4) == 0;
            slot.FlameFx = slot.WantsFlame
                ? _ptfx.StartLoopedAtCoord(_config.FlameAsset, _config.FlameEffect, pos, _profile.FlameScale)
                : 0;
            slot.SmokeFx = slot.WantsSmoke
                ? _ptfx.StartLoopedAtCoord(_config.FireSmokeAsset, _config.FireSmokeEffect, pos + new Vector3(0f, 0f, 1f), 1f)
                : 0;
            slot.StartedAt = now;
            slot.Site = site;
            site.Slot = slot;
            _activeCount++;
            return true;
        }

        /// <summary>
        /// Recale le foyer au sol. Le terrain n'est interrogé qu'une fois par foyer (résultat mis
        /// en cache). Les foyers tombant dans l'eau sont marqués pour suppression.
        /// </summary>
        private bool ResolveGround(FireSite site, int now)
        {
            // Zones de collines : on sonde depuis très haut. Ville, foyers aléatoires ou propagés :
            // on sonde depuis juste au-dessus de la hauteur de référence (joueur, feu parent ou
            // centre de zone), pour placer le feu au niveau de la rue et pas sur les toits.
            bool fromSky = site.Zone != null && !site.Zone.Urban;
            float probeZ = fromSky ? 1000f : site.Position.Z + 15f;
            var outZ = new OutputArgument();
            bool found = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                site.Position.X, site.Position.Y, probeZ, outZ, false, false);
            if (!found && !fromSky)
            {
                found = Function.Call<bool>(Hash.GET_GROUND_Z_FOR_3D_COORD,
                    site.Position.X, site.Position.Y, 1000f, outZ, false, false);
            }
            if (!found)
            {
                // Collision pas encore chargée : on réessaiera plus tard, dans la limite de MaxGroundAttempts.
                if (++site.GroundAttempts >= MaxGroundAttempts) site.Dead = true;
                return false;
            }

            float groundZ = outZ.GetResult<float>();
            var outWater = new OutputArgument();
            bool water = Function.Call<bool>(Hash.GET_WATER_HEIGHT_NO_WAVES,
                site.Position.X, site.Position.Y, groundZ + 2f, outWater);
            if (water && outWater.GetResult<float>() > groundZ)
            {
                site.Dead = true; // pas de feu sous l'eau
                return false;
            }

            site.Position = new Vector3(site.Position.X, site.Position.Y, groundZ);
            site.GroundResolved = true;
            return true;
        }

        /// <summary>Si un dictionnaire ptfx n'était pas encore chargé à l'allumage, on recrée l'effet plus tard.</summary>
        private void RetryMissingEffects()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                FireSlot s = _slots[i];
                if (s.Site == null) continue;

                if (s.WantsFlame && s.FlameFx == 0 && _ptfx.EnsureLoaded(_config.FlameAsset))
                {
                    int h = _ptfx.StartLoopedAtCoord(_config.FlameAsset, _config.FlameEffect, s.Site.Position, _profile.FlameScale);
                    s.FlameFx = h != 0 ? h : -1; // -1 : nom d'effet invalide, on n'insiste pas
                }
                if (s.WantsSmoke && s.SmokeFx == 0 && _ptfx.EnsureLoaded(_config.FireSmokeAsset))
                {
                    int h = _ptfx.StartLoopedAtCoord(_config.FireSmokeAsset, _config.FireSmokeEffect,
                        s.Site.Position + new Vector3(0f, 0f, 1f), 1f);
                    s.SmokeFx = h != 0 ? h : -1;
                }
            }
        }

        /// <summary>
        /// Détecte les feux éteints (pompiers, extincteur, pluie...) : plus aucun feu dans un
        /// rayon de 3 m. Le foyer passe alors en "repos" et pourra se rallumer plus tard.
        /// </summary>
        private void CheckExtinguished(int now)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                FireSlot s = _slots[i];
                if (s.Site == null || now - s.StartedAt < ExtinguishGraceMs) continue;

                Vector3 p = s.Site.Position;
                int fires = Function.Call<int>(Hash.GET_NUMBER_OF_FIRES_IN_RANGE, p.X, p.Y, p.Z, ExtinguishCheckRadius);
                if (fires == 0)
                {
                    s.Site.ReigniteAt = now + ReigniteDelayMs;
                    Release(s);
                }
            }
        }

        // ------------------------------------------------------------------ nettoyage

        /// <summary>Éteint un feu et rend son emplacement au pool.</summary>
        private void Release(FireSlot slot)
        {
            if (slot.Site == null) return;
            Vector3 p = slot.Site.Position;

            if (slot.FireHandle != 0) Function.Call(Hash.REMOVE_SCRIPT_FIRE, slot.FireHandle);
            // Les feux "enfants" créés par le moteur n'ont pas de handle : on les éteint par zone.
            Function.Call(Hash.STOP_FIRE_IN_RANGE, p.X, p.Y, p.Z, CleanupRadius);
            PtfxManager.Stop(ref slot.FlameFx);
            PtfxManager.Stop(ref slot.SmokeFx);

            slot.FireHandle = 0;
            slot.WantsSmoke = false;
            slot.WantsFlame = false;
            slot.Site.Slot = null;
            slot.Site = null;
            _activeCount--;
        }

        /// <summary>Oublie les foyers lointains pour garder une liste courte.</summary>
        private void PruneSites(Vector3 player, float r)
        {
            // Zones lointaines : on supprime leurs foyers, elles seront régénérées au retour du joueur.
            float zoneReset = r * ZoneResetFactor;
            for (int z = 0; z < _zones.Length; z++)
            {
                FireZone zone = _zones[z];
                if (!zone.Seeded) continue;
                float limit = zoneReset + zone.Radius;
                if (Dist2DSq(player, zone.Center) > limit * limit)
                {
                    RemoveSites(s => s.Zone == zone);
                    zone.Seeded = false;
                }
            }

            float randomLimit = r * RandomPruneFactor;
            float randomLimitSq = randomLimit * randomLimit;
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                FireSite s = _sites[i];
                if (s.Slot != null) continue;
                if (s.Dead || (s.Zone == null && Dist2DSq(player, s.Position) > randomLimitSq))
                    SwapRemove(i);
            }
        }

        private void RemoveSites(Predicate<FireSite> match)
        {
            for (int i = _sites.Count - 1; i >= 0; i--)
            {
                FireSite s = _sites[i];
                if (s.Slot == null && match(s)) SwapRemove(i);
            }
        }

        /// <summary>Suppression en O(1) : l'ordre de la liste n'a pas d'importance.</summary>
        private void SwapRemove(int index)
        {
            int last = _sites.Count - 1;
            _sites[index] = _sites[last];
            _sites.RemoveAt(last);
        }

        private void RebuildActivePositions()
        {
            ActivePositions.Clear();
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Site != null) ActivePositions.Add(_slots[i].Site.Position);
            }
        }

        /// <summary>Libère tous les feux et effets, et oublie tous les foyers.</summary>
        public void Shutdown()
        {
            for (int i = 0; i < _slots.Length; i++) Release(_slots[i]);
            _sites.Clear();
            _candidates.Clear();
            ActivePositions.Clear();
            for (int z = 0; z < _zones.Length; z++) _zones[z].Seeded = false;
            _activeCount = 0;
            _nextUpdate = _nextRandomSpawn = _nextSpread = 0;
        }

        // ------------------------------------------------------------------ utilitaires

        private FireSlot FindFreeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Site == null) return _slots[i];
            }
            return null;
        }

        private static float Dist2DSq(Vector3 a, Vector3 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }
    }
}
