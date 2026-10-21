using System.Collections.Generic;
using GTA;
using GTA.Math;
using GTA.Native;

namespace LosSantosWildfire
{
    /// <summary>
    /// Centralise le chargement des dictionnaires ptfx et la création des effets en boucle.
    /// - Chaque dictionnaire n'est demandé (REQUEST) qu'une seule fois.
    /// - On ne bloque jamais le jeu : si l'asset n'est pas encore chargé, on renvoie 0 et
    ///   l'appelant réessaiera à la mise à jour suivante.
    /// - Tous les dictionnaires sont libérés à la désactivation.
    /// </summary>
    internal sealed class PtfxManager
    {
        private readonly HashSet<string> _requested = new HashSet<string>();

        /// <summary>Demande l'asset une fois, puis vérifie seulement HAS_NAMED_PTFX_ASSET_LOADED.</summary>
        public bool EnsureLoaded(string asset)
        {
            if (string.IsNullOrEmpty(asset)) return false;
            if (!_requested.Contains(asset))
            {
                Function.Call(Hash.REQUEST_NAMED_PTFX_ASSET, asset);
                _requested.Add(asset);
            }
            return Function.Call<bool>(Hash.HAS_NAMED_PTFX_ASSET_LOADED, asset);
        }

        /// <summary>Démarre un effet en boucle à une position. Renvoie 0 si impossible.</summary>
        public int StartLoopedAtCoord(string asset, string effect, Vector3 pos, float scale)
        {
            if (string.IsNullOrEmpty(effect) || !EnsureLoaded(asset)) return 0;
            // Indique le dictionnaire à utiliser pour le PROCHAIN appel ptfx uniquement.
            Function.Call(Hash.USE_PARTICLE_FX_ASSET, asset);
            return Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_AT_COORD, effect,
                pos.X, pos.Y, pos.Z, 0f, 0f, 0f, scale, false, false, false, false);
        }

        /// <summary>Démarre un effet en boucle attaché à une entité (suit l'entité sans coût script).</summary>
        public int StartLoopedOnEntity(string asset, string effect, Entity entity, Vector3 offset, float scale)
        {
            if (entity == null || string.IsNullOrEmpty(effect) || !EnsureLoaded(asset)) return 0;
            Function.Call(Hash.USE_PARTICLE_FX_ASSET, asset);
            return Function.Call<int>(Hash.START_PARTICLE_FX_LOOPED_ON_ENTITY, effect, entity.Handle,
                offset.X, offset.Y, offset.Z, 0f, 0f, 0f, scale, false, false, false);
        }

        /// <summary>Arrête proprement un effet en boucle (sans erreur si déjà détruit).</summary>
        public static void Stop(ref int handle)
        {
            if (handle != 0 && Function.Call<bool>(Hash.DOES_PARTICLE_FX_LOOPED_EXIST, handle))
            {
                // Uniquement STOP : enchaîner STOP puis REMOVE sur le même handle peut viser un
                // effet déjà libéré par le moteur (source possible de crash).
                Function.Call(Hash.STOP_PARTICLE_FX_LOOPED, handle, false);
            }
            handle = 0;
        }

        /// <summary>Libère tous les dictionnaires demandés par le mod.</summary>
        public void ReleaseAll()
        {
            foreach (string asset in _requested)
            {
                Function.Call(Hash.REMOVE_NAMED_PTFX_ASSET, asset);
            }
            _requested.Clear();
        }
    }
}
