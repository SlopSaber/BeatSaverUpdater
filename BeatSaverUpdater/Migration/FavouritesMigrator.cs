using System.Threading;
using System.Threading.Tasks;

namespace BeatSaverUpdater.Migration
{
    internal class FavouritesMigrator : IMigrator
    {
        private readonly PlayerDataModel playerDataModel;

        public FavouritesMigrator(PlayerDataModel playerDataModel)
        {
            this.playerDataModel = playerDataModel;
        }

        public Task<bool> MigrateMapAsync(BeatmapLevel oldMap, BeatmapLevel newMap, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (playerDataModel.playerData.IsLevelUserFavorite(oldMap))
            {
                playerDataModel.playerData.RemoveLevelFromFavorites(oldMap);
                playerDataModel.playerData.AddLevelToFavorites(newMap);
                playerDataModel.Save();
            }
            return Task.FromResult(false);
        }
    }
}
