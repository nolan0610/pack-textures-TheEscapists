using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace LosSantosWildfire
{
    /// <summary>
    /// Pompiers et réactions des piétons.
    /// - Pompiers : un "incident" de dispatch est créé près d'un feu proche du joueur (max 2 à la fois).
    /// - Piétons : ceux qui sont proches d'un feu reçoivent une tâche de fuite (max 8 par seconde).
    /// </summary>
    internal sealed class EmergencyManager
    {
        // Valeur de l'énumération eDispatchType pour les pompiers (DT_FireDepartment = 3),
        // telle que documentée par la communauté (natives DB). À vérifier si les pompiers n'arrivent pas.
        private const int DispatchFireDepartment = 3;
        private const int FireUnits = 2;
        private const int MaxIncidents = 2;
        private const int IncidentIntervalMs = 15000;
        private const int IncidentLifetimeMs = 90000;
        private const float IncidentSpacing = 30f;

        private const int PedUpdateIntervalMs = 1000;
        private const float PedScanRadius = 50f;
        private const float PedFleeTriggerDistance = 30f;
        private const float PedFleeDistance = 120f;
        private const int PedFleeDurationMs = 20000;
        private const int MaxFleeTasksPerUpdate = 8;
        private const int MaxTrackedPeds = 256;

        private struct Incident
        {
            public int Id;
            public int CreatedAt;
            public Vector3 Position;
        }

        private readonly Config _config;
        private readonly List<Incident> _incidents = new List<Incident>(MaxIncidents);
        private readonly HashSet<int> _fledPeds = new HashSet<int>();
        private readonly int _firemanModelHash;
        private int _nextIncident;
        private int _nextPedUpdate;

        public EmergencyManager(Config config)
        {
            _config = config;
            _firemanModelHash = new Model("s_m_y_fireman_01").Hash; // les pompiers ne fuient pas
        }

        public void Update(int now, Vector3 player, List<Vector3> fires)
        {
            if (_config.FirefightersEnabled && now >= _nextIncident)
            {
                _nextIncident = now + IncidentIntervalMs;
                UpdateIncidents(now, player, fires);
            }

            if (_config.PedsFlee && now >= _nextPedUpdate)
            {
                _nextPedUpdate = now + PedUpdateIntervalMs;
                UpdatePeds(player, fires);
            }
        }

        private void UpdateIncidents(int now, Vector3 player, List<Vector3> fires)
        {
            // Supprime les incidents trop anciens (les pompiers repartent ensuite d'eux-mêmes).
            for (int i = _incidents.Count - 1; i >= 0; i--)
            {
                if (now - _incidents[i].CreatedAt > IncidentLifetimeMs)
                {
                    DeleteIncident(_incidents[i].Id);
                    _incidents.RemoveAt(i);
                }
            }

            if (_incidents.Count >= MaxIncidents || fires.Count == 0) return;

            // Feu le plus proche du joueur, sans incident déjà actif à moins de 30 m.
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < fires.Count; i++)
            {
                if (IsCovered(fires[i])) continue;
                float d = (fires[i] - player).LengthSquared();
                if (d < bestDist) { bestDist = d; best = i; }
            }
            if (best < 0) return;

            Vector3 p = fires[best];
            var outId = new OutputArgument();
            // Les deux derniers paramètres (p7, p8) existent sur les versions récentes du jeu ; 0 par défaut.
            bool ok = Function.Call<bool>(Hash.CREATE_INCIDENT, DispatchFireDepartment,
                p.X, p.Y, p.Z, FireUnits, 3f, outId, 0, 0);
            if (ok)
            {
                _incidents.Add(new Incident { Id = outId.GetResult<int>(), CreatedAt = now, Position = p });
            }
        }

        private bool IsCovered(Vector3 fire)
        {
            for (int i = 0; i < _incidents.Count; i++)
                if ((_incidents[i].Position - fire).LengthSquared() < IncidentSpacing * IncidentSpacing) return true;
            return false;
        }

        private void UpdatePeds(Vector3 player, List<Vector3> fires)
        {
            if (fires.Count == 0) return;

            // On ne scanne les piétons que si un feu est assez près du joueur (sinon personne ne le voit).
            float scanSq = (PedScanRadius + PedFleeTriggerDistance) * (PedScanRadius + PedFleeTriggerDistance);
            bool fireNearby = false;
            for (int i = 0; i < fires.Count && !fireNearby; i++)
                fireNearby = (fires[i] - player).LengthSquared() < scanSq;
            if (!fireNearby) return;

            if (_fledPeds.Count > MaxTrackedPeds) _fledPeds.Clear();

            Ped playerPed = Game.Player.Character;
            Ped[] peds = World.GetNearbyPeds(playerPed, PedScanRadius);
            int tasks = 0;
            float triggerSq = PedFleeTriggerDistance * PedFleeTriggerDistance;

            for (int i = 0; i < peds.Length && tasks < MaxFleeTasksPerUpdate; i++)
            {
                Ped ped = peds[i];
                if (ped == null || !ped.Exists() || ped.IsDead || ped.IsPlayer) continue;
                if (ped.IsPersistent || ped.IsInVehicle()) continue; // personnages de mission, conducteurs
                if (ped.Model.Hash == _firemanModelHash) continue;
                if (_fledPeds.Contains(ped.Handle)) continue;

                Vector3 pp = ped.Position;
                for (int f = 0; f < fires.Count; f++)
                {
                    if ((fires[f] - pp).LengthSquared() > triggerSq) continue;
                    Function.Call(Hash.TASK_SMART_FLEE_COORD, ped.Handle,
                        fires[f].X, fires[f].Y, fires[f].Z, PedFleeDistance, PedFleeDurationMs, false, false);
                    _fledPeds.Add(ped.Handle);
                    tasks++;
                    break;
                }
            }
        }

        private static void DeleteIncident(int id)
        {
            if (id != 0) Function.Call(Hash.DELETE_INCIDENT, id);
        }

        public void Shutdown()
        {
            for (int i = 0; i < _incidents.Count; i++) DeleteIncident(_incidents[i].Id);
            _incidents.Clear();
            _fledPeds.Clear();
            _nextIncident = _nextPedUpdate = 0;
        }
    }
}
