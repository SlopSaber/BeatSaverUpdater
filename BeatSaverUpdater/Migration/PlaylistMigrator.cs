using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IPA.Utilities;

namespace BeatSaverUpdater.Migration
{
    internal class PlaylistMigrator : IMigrator
    {
        public async Task<bool> MigrateMapAsync(BeatmapLevel oldMap, BeatmapLevel newMap, CancellationToken token)
        {
            await UnityGame.SwitchToMainThreadAsync();
            token.ThrowIfCancellationRequested();
            var playlists = BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.GetAllPlaylists(true).ToArray();
            var preventDelete = false;
            var mapHash = oldMap.GetBeatmapHash();
            var newLevelId = newMap.levelID;

            foreach (var playlist in playlists)
            {
                token.ThrowIfCancellationRequested();
                if (playlist.Any(s => s.Hash == mapHash))
                {
                    if (playlist.TryGetCustomData("syncURL", out var sync))
                    {
                        preventDelete = true;
                    }
                    else
                    {
                        foreach (var song in playlist.Where(s => s.Hash == mapHash))
                        {
                            song.LevelId = newLevelId;
                        }
                        var manager = BeatSaberPlaylistsLib.PlaylistManager.DefaultManager.GetManagerForPlaylist(playlist);
                        if (manager != null)
                            await manager.StorePlaylistAsync(playlist);
                    }
                }
            }

            token.ThrowIfCancellationRequested();
            return preventDelete;
        }
    }
}
