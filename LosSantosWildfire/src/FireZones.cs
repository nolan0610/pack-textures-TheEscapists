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
        public readonly bool Urban; // zone de ville : feux placés au niveau de la rue, pas sur les toits
        public bool Seeded; // vrai une fois que les foyers de la zone ont été générés

        public FireZone(string name, float x, float y, float z, float radius, int siteCount, bool urban = false)
        {
            Urban = urban;
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
                // Zones urbaines : surtout utiles pour les colonnes de fumée vues de la ville.
                new FireZone("Downtown (Pillbox Hill)",          100f, -800f,  31f,  80f,  8, true),
                new FireZone("Rockford Hills",                  -820f, -150f,  38f,  90f,  8, true),
                new FireZone("Vespucci",                       -1150f, -1250f,  5f,  80f,  8, true),
                new FireZone("Davis",                            100f, -1700f, 29f,  90f,  8, true),
                new FireZone("La Mesa",                          800f, -1100f, 27f,  80f,  8, true),
                new FireZone("Del Perro",                      -1450f, -500f,  33f,  80f,  8, true),
                new FireZone("Port de Los Santos",               900f, -2900f,  6f, 100f,  8, true),
            };
        }
    }
}
