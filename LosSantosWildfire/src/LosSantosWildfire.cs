using System;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.UI;

namespace LosSantosWildfire
{
    /// <summary>
    /// Point d'entrée du mod (chargé automatiquement par ScriptHookVDotNet v3).
    /// Le Tick est volontairement léger : chaque gestionnaire a sa propre cadence
    /// (feux 500 ms, ambiance 1 s, fumée 2 s, pompiers 15 s), donc presque rien n'est fait par frame.
    /// </summary>
    public sealed class LosSantosWildfireScript : Script
    {
        private readonly Config _config;
        private readonly PtfxManager _ptfx;
        private readonly FireManager _fires;
        private readonly AtmosphereManager _atmosphere;
        private readonly SmokeManager _smoke;
        private readonly EmergencyManager _emergency;

        private bool _enabled;
        private Intensity _intensity;

        public LosSantosWildfireScript()
        {
            _config = Config.Load();
            _intensity = _config.DefaultIntensity;

            var rng = new Random();
            FireZone[] zones = FireZones.CreateDefault();
            _ptfx = new PtfxManager();
            _fires = new FireManager(_config, _ptfx, zones, rng);
            _atmosphere = new AtmosphereManager(_config, _ptfx);
            _smoke = new SmokeManager(_config, _ptfx, zones);
            _emergency = new EmergencyManager(_config);

            // 100 ms suffit : la logique lourde est cadencée plus lentement à l'intérieur.
            Interval = 100;
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += OnAborted; // rechargement des scripts / fermeture du jeu

            if (_config.EnabledOnStart) Enable(false);
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_enabled) return;

            Ped player = Game.Player.Character;
            if (player == null || !player.Exists()) return;

            int now = Game.GameTime;
            Vector3 pos = player.Position;

            _fires.Update(now, pos);
            _atmosphere.Update(now);
            _smoke.Update(now, pos);
            _emergency.Update(now, pos, _fires.ActivePositions);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == _config.ToggleKey)
            {
                if (_enabled) Disable(true);
                else Enable(true);
            }
            else if (e.KeyCode == _config.IntensityKey)
            {
                _intensity = (Intensity)(((int)_intensity + 1) % 3);
                ApplyIntensity();
                Notification.Show("~o~LosSantosWildfire~s~ : intensité ~y~" + IntensityProfile.For(_intensity).Label
                    + (_enabled ? "" : "~s~ (mod désactivé)"));
            }
        }

        private void Enable(bool notify)
        {
            _enabled = true;
            ApplyIntensity();
            if (notify)
            {
                Notification.Show("~o~LosSantosWildfire~s~ : ~r~incendie activé~s~ (intensité "
                    + IntensityProfile.For(_intensity).Label + ")");
            }
        }

        /// <summary>Libère proprement TOUT : feux, ptfx, incidents, météo, dictionnaires d'effets.</summary>
        private void Disable(bool notify)
        {
            _enabled = false;
            _fires.Shutdown();
            _smoke.Shutdown();
            _emergency.Shutdown();
            _atmosphere.Restore();
            _ptfx.ReleaseAll();
            if (notify) Notification.Show("~o~LosSantosWildfire~s~ : ~g~incendie désactivé");
        }

        private void ApplyIntensity()
        {
            IntensityProfile profile = IntensityProfile.For(_intensity);
            _fires.SetIntensity(profile);
            _smoke.SetIntensity(profile);
            if (_enabled) _atmosphere.Apply(profile);
        }

        private void OnAborted(object sender, EventArgs e)
        {
            if (_enabled) Disable(false);
        }
    }
}
