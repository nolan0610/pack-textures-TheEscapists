using GTA.Math;

namespace LosSantosWildfire
{
    /// <summary>Zone d'incendie prédéfinie : un centre et un rayon dans lequel on répartit des foyers.</summary>
    internal sealed class FireZone
    {
        public readonly string Name;
        public readonly Vector3 Center;
        public readonly float Radius;
        public readonly int SiteCount;
        public bool Seeded; // vrai une fois que les foyers de la zone ont été générés

        public FireZone(string name, float x, float y, float z, float radius, int siteCount)
        {
            Name = name;
            Center = new Vector3(x, y, z);
            Radius = radius;
            SiteCount = siteCount;
        }
    }

    /// <summary>
    /// Liste des zones prédéfinies. Les coordonnées X/Y sont approximatives (relevées en jeu,
    /// arrondies) ; la hauteur Z n'est qu'indicative : pour les vrais feux, le Z est recalculé
    /// au sol (World.GetGroundHeight) au moment de l'allumage. Le Z sert surtout à placer les
    /// colonnes de fumée lointaines, où le terrain n'est pas encore chargé.
    /// </summary>
    internal static class FireZones
    {
        public static FireZone[] CreateDefault()
        {
            return new[]
            {
                new FireZone("Collines de Vinewood (panneau)",   720f, 1200f, 345f, 120f, 14),
                new FireZone("Vinewood Hills (observatoire)",   -420f, 1120f, 325f, 100f, 10),
                new FireZone("Vinewood Hills (résidences)",     -120f,  880f, 235f, 110f, 12),
                new FireZone("Mirror Park",                     1180f, -560f,  65f,  90f, 10),
                new FireZone("Collines du barrage Land Act",    1700f,   60f, 170f, 120f, 10),
                new FireZone("Tataviam Mountains",              2300f, -650f, 120f, 150f, 10),
                new FireZone("Chumash",                        -3100f, 1250f,  30f, 100f, 10),
                new FireZone("Tongva Hills",                   -1850f, 2050f, 140f, 140f, 12),
                new FireZone("Great Chaparral",                 -400f, 2600f,  70f, 150f, 10),
                new FireZone("Raton Canyon",                   -1000f, 4400f,  60f, 140f, 10),
                new FireZone("Mont Chiliad (versant sud)",       450f, 5300f, 480f, 150f, 12),
                new FireZone("Forêt de Paleto",                 -550f, 5550f,  70f, 150f, 14),
                new FireZone("Paleto Bay",                      -250f, 6300f,  31f, 100f, 10),
                new FireZone("Sandy Shores",                    1850f, 3700f,  33f, 110f,  8),
                new FireZone("Mont Gordo",                      2850f, 5500f, 300f, 140f, 10),
            };
        }
    }
}
