using System.Threading.Tasks;
using SongDetailsCache;

namespace BeatSaverUpdater
{
    internal class SongDetailsWrapper
    {
        private Task<SongDetails>? initialization;
        public async Task<bool> SongExists(string hash)
        {
            try
            {
                var details = await (initialization ??= SongDetails.Init());
                return details.songs.FindByHash(hash, out var song);
            }
            catch
            {
                initialization = null;
                throw;
            }
        }
    }
}
