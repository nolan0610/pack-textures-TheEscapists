using System;
using System.Collections.Generic;
using GTA.Math;

namespace LosSantosWildfire
{
    /// <summary>
    /// Grosses colonnes de fumée lointaines (max 8), uniquement en ptfx : aucun vrai feu.
    /// Elles sont placées sur les centres des zones prédéfinies situées HORS du rayon des feux
    /// de proximité, pour donner l'impression que d'autres quartiers brûlent au loin.
    /// Mise à jour toutes les 2 s.
    /// </summary>
    internal sealed class SmokeManager
    {
        private const int UpdateIntervalMs = 2000;
        private const float ColumnScale = 2.5f;     // ajustez si la colonne paraît trop petite / grosse
        private const float ColumnHeightOffset = 5f;

        /// <summary>Emplacement du pool de colonnes (pré-alloué).</summary>
        private sealed class Column
        {
            public FireZone Zone; // null = libre
            public int Fx;        // 0 = à (re)créer, -1 = échec définitif
        }

        private readonly Config _config;
        private readonly PtfxManager _ptfx;
        private readonly FireZone[] _zones;
        private readonly Column[] _columns;
        private readonly List<int> _order;
        private readonly float[] _dist;
        private readonly bool[] _desired;
        private readonly Comparison<int> _byDistance;

        private IntensityProfile _profile;
        private int _nextUpdate;

        public SmokeManager(Config config, PtfxManager ptfx, FireZone[] zones)
        {
            _config = config;
            _ptfx = ptfx;
            _zones = zones;
            _columns = new Column[Config.HardSmokeCap];
            for (int i = 0; i < _columns.Length; i++) _columns[i] = new Column();
            _order = new List<int>(zones.Length);
            _dist = new float[zones.Length];
            _desired = new bool[zones.Length];
            _byDistance = (a, b) => _dist[a].CompareTo(_dist[b]);
        }

        public void SetIntensity(IntensityProfile profile)
        {
            _profile = profile;
            _nextUpdate = 0; // ré-évaluer immédiatement le nombre de colonnes
        }

        private int MaxColumns
        {
            get
            {
                int n = (int)Math.Round(_config.SmokeColumns * _profile.SmokeMultiplier);
                return Math.Max(0, Math.Min(Config.HardSmokeCap, n));
            }
        }

        public void Update(int now, Vector3 player)
        {
            if (now < _nextUpdate) return;
            _nextUpdate = now + UpdateIntervalMs;

            // Distance minimale : au-delà du rayon des vrais feux (là, ce sont eux qui fument).
            float minDist = _config.Radius + 50f;
            float maxDist = _config.SmokeMaxDistance;

            _order.Clear();
            for (int i = 0; i < _zones.Length; i++)
            {
                _desired[i] = false;
                float dx = player.X - _zones[i].Center.X, dy = player.Y - _zones[i].Center.Y;
                float d = (float)Math.Sqrt(dx * dx + dy * dy);
                _dist[i] = d;

                // Petite hystérésis pour les colonnes déjà affichées.
                bool active = FindColumn(_zones[i]) != null;
                float lo = active ? minDist * 0.85f : minDist;
                float hi = active ? maxDist * 1.1f : maxDist;
                if (d >= lo && d <= hi) _order.Add(i);
            }

            _order.Sort(_byDistance);
            int count = Math.Min(MaxColumns, _order.Count);
            for (int i = 0; i < count; i++) _desired[_order[i]] = true;

            // Libère les colonnes qui ne sont plus voulues.
            for (int c = 0; c < _columns.Length; c++)
            {
                Column col = _columns[c];
                if (col.Zone != null && !_desired[Array.IndexOf(_zones, col.Zone)]) ReleaseColumn(col);
            }

            // Crée les colonnes manquantes (ou réessaie si l'asset n'était pas encore chargé).
            for (int i = 0; i < count; i++)
            {
                FireZone zone = _zones[_order[i]];
                Column col = FindColumn(zone);
                if (col == null)
                {
                    col = FindFreeColumn();
                    if (col == null) break;
                    col.Zone = zone;
                    col.Fx = 0;
                }
                if (col.Fx == 0 && _ptfx.EnsureLoaded(_config.ColumnAsset))
                {
                    int h = _ptfx.StartLoopedAtCoord(_config.ColumnAsset, _config.ColumnEffect,
                        zone.Center + new Vector3(0f, 0f, ColumnHeightOffset), ColumnScale);
                    col.Fx = h != 0 ? h : -1;
                }
            }
        }

        private Column FindColumn(FireZone zone)
        {
            for (int c = 0; c < _columns.Length; c++)
                if (_columns[c].Zone == zone) return _columns[c];
            return null;
        }

        private Column FindFreeColumn()
        {
            for (int c = 0; c < _columns.Length; c++)
                if (_columns[c].Zone == null) return _columns[c];
            return null;
        }

        private static void ReleaseColumn(Column col)
        {
            PtfxManager.Stop(ref col.Fx);
            col.Zone = null;
        }

        public void Shutdown()
        {
            for (int c = 0; c < _columns.Length; c++)
                if (_columns[c].Zone != null) ReleaseColumn(_columns[c]);
            _nextUpdate = 0;
        }
    }
}
