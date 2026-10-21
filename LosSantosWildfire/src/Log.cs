using System;
using System.IO;

namespace LosSantosWildfire
{
    /// <summary>
    /// Petit journal dans scripts\LosSantosWildfire.log : seulement les événements importants
    /// (activation, intensité, erreurs), jamais à chaque frame.
    /// </summary>
    internal static class Log
    {
        private const string Path = "scripts\\LosSantosWildfire.log";

        public static void Write(string message)
        {
            try
            {
                File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine);
            }
            catch (Exception)
            {
                // Le journal ne doit jamais faire planter le mod.
            }
        }
    }
}
