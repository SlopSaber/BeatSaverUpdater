using System.Threading;
using System.Threading.Tasks;

namespace BeatSaverUpdater.Migration
{
    internal interface IMigrator
    {
        /// <summary>
        /// Migrates the references of an old map to a new map
        /// </summary>
        /// <param name="oldMap"></param>
        /// <param name="newMap"></param>
        /// <returns>Returns true if old map should not be deleted</returns>
        public Task<bool> MigrateMapAsync(BeatmapLevel oldMap, BeatmapLevel newMap, CancellationToken token);
    }
}
