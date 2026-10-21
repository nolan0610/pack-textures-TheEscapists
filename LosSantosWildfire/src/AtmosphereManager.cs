using GTA;
using GTA.Math;
using GTA.Native;

namespace LosSantosWildfire
{
    /// <summary>
    /// Ambiance globale, quasiment gratuite en performances :
    /// - modificateur timecycle (ciel orangé, lumière rougeâtre) ;
    /// - météo persistante SMOG / FOGGY ;
    /// - un seul effet de cendres, attaché au joueur (il le suit sans calcul côté script).
    /// </summary>
    internal sealed class AtmosphereManager
    {
        private const int UpdateIntervalMs = 1000;
        private const float WeatherTransitionSeconds = 15f;
        private static readonly Vector3 AshOffset = new Vector3(0f, 0f, 6f);

        private readonly Config _config;
        private readonly PtfxManager _ptfx;
        private IntensityProfile _profile;
        private bool _applied;
        private int _ashFx;          // 0 = à créer, -1 = échec définitif (nom d'effet invalide)
        private int _ashOwner;       // handle du ped qui porte l'effet (change si on change de perso)
        private int _nextUpdate;

        public AtmosphereManager(Config config, PtfxManager ptfx)
        {
            _config = config;
            _ptfx = ptfx;
        }

        /// <summary>Applique (ou ré-applique) l'ambiance pour une intensité donnée.</summary>
        public void Apply(IntensityProfile profile)
        {
            _profile = profile;
            if (!_config.AtmosphereEnabled) return;

            if (!string.IsNullOrEmpty(_config.TimecycleModifier))
            {
                Function.Call(Hash.SET_TIMECYCLE_MODIFIER, _config.TimecycleModifier);
                Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, profile.TimecycleStrength);
            }

            // Transition douce vers la météo voulue, maintenue jusqu'à la désactivation.
            Function.Call(Hash.SET_WEATHER_TYPE_OVERTIME_PERSIST, profile.Weather, WeatherTransitionSeconds);

            if (_ashFx > 0) Function.Call(Hash.SET_PARTICLE_FX_LOOPED_SCALE, _ashFx, profile.AshScale);
            _applied = true;
        }

        public void Update(int now)
        {
            if (!_applied || now < _nextUpdate) return;
            _nextUpdate = now + UpdateIntervalMs;

            // Certaines missions / scripts effacent le timecycle : on le remet si besoin.
            if (!string.IsNullOrEmpty(_config.TimecycleModifier)
                && Function.Call<int>(Hash.GET_TIMECYCLE_MODIFIER_INDEX) == -1)
            {
                Function.Call(Hash.SET_TIMECYCLE_MODIFIER, _config.TimecycleModifier);
                Function.Call(Hash.SET_TIMECYCLE_MODIFIER_STRENGTH, _profile.TimecycleStrength);
            }

            if (_config.AshEnabled) UpdateAsh();
        }

        private void UpdateAsh()
        {
            if (_ashFx == -1) return; // effet introuvable : on n'insiste pas

            Ped player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            // Changement de personnage (Michael/Franklin/Trevor) : l'ancien ped ne porte plus l'effet.
            if (_ashFx > 0 && (player.Handle != _ashOwner
                || !Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, _ashFx)))
            {
                PtfxManager.Stop(ref _ashFx);
            }

            if (_ashFx == 0 && _ptfx.EnsureLoaded(_config.AshAsset))
            {
                int h = _ptfx.StartLoopedOnEntity(_config.AshAsset, _config.AshEffect, player, AshOffset, _profile.AshScale);
                _ashFx = h != 0 ? h : -1;
                _ashOwner = player.Handle;
            }
        }

        /// <summary>Retire l'ambiance et rend la main au cycle météo normal du jeu.</summary>
        public void Restore()
        {
            if (_ashFx > 0) PtfxManager.Stop(ref _ashFx);
            _ashFx = 0;
            _ashOwner = 0;

            if (_applied && _config.AtmosphereEnabled)
            {
                Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                Function.Call(Hash.CLEAR_WEATHER_TYPE_PERSIST);
                Function.Call(Hash.CLEAR_OVERRIDE_WEATHER);
            }
            _applied = false;
            _nextUpdate = 0;
        }
    }
}
