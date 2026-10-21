using System;
using System.Windows.Forms;
using GTA;

namespace LosSantosWildfire
{
    /// <summary>Niveaux d'intensité sélectionnables avec F11.</summary>
    public enum Intensity
    {
        Faible = 0,
        Moyenne = 1,
        Extreme = 2
    }

    /// <summary>
    /// Paramètres dérivés d'une intensité : multiplicateurs appliqués aux valeurs du .ini.
    /// </summary>
    public sealed class IntensityProfile
    {
        public string Label;
        public float FireMultiplier;       // multiplie MaxFires
        public float SmokeMultiplier;      // multiplie SmokeColumns
        public float TimecycleStrength;    // force du filtre orange (0..1)
        public string Weather;             // type météo GTA (SMOG, FOGGY...)
        public float FlameScale;           // échelle des flammes ptfx
        public float AshScale;             // échelle des cendres autour du joueur
        public float SpreadChance;         // probabilité de propagation à chaque cycle (0..1)

        public static IntensityProfile For(Intensity intensity)
        {
            switch (intensity)
            {
                case Intensity.Faible:
                    return new IntensityProfile
                    {
                        Label = "Faible", FireMultiplier = 0.5f, SmokeMultiplier = 0.5f,
                        TimecycleStrength = 0.35f, Weather = "SMOG", FlameScale = 0.8f,
                        AshScale = 0.6f, SpreadChance = 0.25f
                    };
                case Intensity.Extreme:
                    return new IntensityProfile
                    {
                        Label = "Extrême", FireMultiplier = 1.4f, SmokeMultiplier = 1.0f,
                        TimecycleStrength = 0.85f, Weather = "FOGGY", FlameScale = 1.4f,
                        AshScale = 1.5f, SpreadChance = 0.8f
                    };
                default:
                    return new IntensityProfile
                    {
                        Label = "Moyenne", FireMultiplier = 1.0f, SmokeMultiplier = 0.75f,
                        TimecycleStrength = 0.6f, Weather = "SMOG", FlameScale = 1.1f,
                        AshScale = 1.0f, SpreadChance = 0.5f
                    };
            }
        }
    }

    /// <summary>
    /// Lecture du fichier scripts\LosSantosWildfire.ini. Toutes les valeurs sont bornées
    /// pour éviter qu'un réglage extrême ne dépasse les limites du moteur.
    /// </summary>
    public sealed class Config
    {
        public const string IniPath = "scripts\\LosSantosWildfire.ini";

        /// <summary>Plafond absolu de feux de script (le moteur en gère environ 128 au total, joueur et PNJ compris).</summary>
        public const int HardFireCap = 60;

        /// <summary>Plafond absolu de colonnes de fumée lointaines.</summary>
        public const int HardSmokeCap = 8;

        // [General]
        public bool EnabledOnStart = false;
        public Intensity DefaultIntensity = Intensity.Moyenne;
        public Keys ToggleKey = Keys.F10;
        public Keys IntensityKey = Keys.F11;

        // [Fires]
        public float Radius = 150f;
        public int MaxFires = 40;
        public int MaxChildFires = 1;          // enfants autorisés par feu de script (propagation native)
        public int UpdateIntervalMs = 500;
        public int MaxActivationsPerUpdate = 4; // étale le coût des créations sur plusieurs mises à jour
        public bool RandomFires = true;

        // [Spread]
        public bool SpreadEnabled = true;
        public int SpreadIntervalSeconds = 20;

        // [Smoke]
        public int SmokeColumns = 6;
        public float SmokeMaxDistance = 3000f;

        // [Atmosphere]
        public bool AtmosphereEnabled = true;
        public string TimecycleModifier = "glasses_orange";
        public bool AshEnabled = true;

        // [Emergency]
        public bool FirefightersEnabled = true;
        public bool PedsFlee = true;

        // [Effects] — noms d'assets ptfx modifiables sans recompiler
        public string FlameAsset = "core";
        public string FlameEffect = "fire_wrecked_plane_cockpit";
        public string FireSmokeAsset = "core";
        public string FireSmokeEffect = "ent_amb_smoke_foundry";
        public string ColumnAsset = "scr_trevor3";
        public string ColumnEffect = "scr_trev3_trailer_plume";
        public string AshAsset = "core";
        public string AshEffect = "ent_amb_falling_ash";

        public static Config Load()
        {
            var c = new Config();
            ScriptSettings ini;
            try
            {
                ini = ScriptSettings.Load(IniPath);
            }
            catch (Exception)
            {
                return c; // fichier absent ou illisible : valeurs par défaut
            }

            c.EnabledOnStart = ini.GetValue("General", "EnabledOnStart", c.EnabledOnStart);
            c.DefaultIntensity = ParseIntensity(ini.GetValue("General", "DefaultIntensity", "Moyenne"));
            c.ToggleKey = ParseKey(ini.GetValue("General", "ToggleKey", "F10"), Keys.F10);
            c.IntensityKey = ParseKey(ini.GetValue("General", "IntensityKey", "F11"), Keys.F11);

            c.Radius = Clamp(ini.GetValue("Fires", "Radius", c.Radius), 30f, 400f);
            c.MaxFires = Clamp(ini.GetValue("Fires", "MaxFires", c.MaxFires), 1, HardFireCap);
            c.MaxChildFires = Clamp(ini.GetValue("Fires", "MaxChildFires", c.MaxChildFires), 0, 5);
            c.UpdateIntervalMs = Clamp(ini.GetValue("Fires", "UpdateIntervalMs", c.UpdateIntervalMs), 500, 5000);
            c.MaxActivationsPerUpdate = Clamp(ini.GetValue("Fires", "MaxActivationsPerUpdate", c.MaxActivationsPerUpdate), 1, 20);
            c.RandomFires = ini.GetValue("Fires", "RandomFires", c.RandomFires);

            c.SpreadEnabled = ini.GetValue("Spread", "Enabled", c.SpreadEnabled);
            c.SpreadIntervalSeconds = Clamp(ini.GetValue("Spread", "IntervalSeconds", c.SpreadIntervalSeconds), 5, 600);

            c.SmokeColumns = Clamp(ini.GetValue("Smoke", "Columns", c.SmokeColumns), 0, HardSmokeCap);
            c.SmokeMaxDistance = Clamp(ini.GetValue("Smoke", "MaxDistance", c.SmokeMaxDistance), 300f, 8000f);

            c.AtmosphereEnabled = ini.GetValue("Atmosphere", "Enabled", c.AtmosphereEnabled);
            c.TimecycleModifier = ini.GetValue("Atmosphere", "TimecycleModifier", c.TimecycleModifier);
            c.AshEnabled = ini.GetValue("Atmosphere", "Ash", c.AshEnabled);

            c.FirefightersEnabled = ini.GetValue("Emergency", "Firefighters", c.FirefightersEnabled);
            c.PedsFlee = ini.GetValue("Emergency", "PedsFlee", c.PedsFlee);

            c.FlameAsset = ini.GetValue("Effects", "FlameAsset", c.FlameAsset);
            c.FlameEffect = ini.GetValue("Effects", "FlameEffect", c.FlameEffect);
            c.FireSmokeAsset = ini.GetValue("Effects", "FireSmokeAsset", c.FireSmokeAsset);
            c.FireSmokeEffect = ini.GetValue("Effects", "FireSmokeEffect", c.FireSmokeEffect);
            c.ColumnAsset = ini.GetValue("Effects", "ColumnAsset", c.ColumnAsset);
            c.ColumnEffect = ini.GetValue("Effects", "ColumnEffect", c.ColumnEffect);
            c.AshAsset = ini.GetValue("Effects", "AshAsset", c.AshAsset);
            c.AshEffect = ini.GetValue("Effects", "AshEffect", c.AshEffect);

            return c;
        }

        private static Intensity ParseIntensity(string value)
        {
            if (string.IsNullOrEmpty(value)) return Intensity.Moyenne;
            switch (value.Trim().ToLowerInvariant())
            {
                case "faible":
                case "low":
                case "0":
                    return Intensity.Faible;
                case "extreme":
                case "extrême":
                case "high":
                case "2":
                    return Intensity.Extreme;
                default:
                    return Intensity.Moyenne;
            }
        }

        private static Keys ParseKey(string value, Keys fallback)
        {
            Keys key;
            return Enum.TryParse(value, true, out key) ? key : fallback;
        }

        private static int Clamp(int v, int min, int max) { return v < min ? min : (v > max ? max : v); }
        private static float Clamp(float v, float min, float max) { return v < min ? min : (v > max ? max : v); }
    }
}
