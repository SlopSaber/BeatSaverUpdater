using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatSaverSharp;
using BeatSaverSharp.Models;
using IPA.Utilities;

namespace BeatSaverUpdater
{
    internal static class Utilities
    {
        private static BeatSaver? beatSaverInstance;
        private static readonly SemaphoreSlim extractionGate = new(1, 1);

        public static string GetBeatmapHash(this BeatmapLevel beatmapLevel) =>
            SongCore.Collections.GetCustomLevelHash(beatmapLevel.levelID);

        public static async Task<Beatmap?> GetBeatSaverBeatmap(this BeatmapLevel beatmapLevel, CancellationToken token)
        {
            if (beatSaverInstance == null)
            {
                var beatSaverOptions = new BeatSaverOptions(Plugin.Metadata.Name, Plugin.Metadata.HVersion.ToString());
                beatSaverInstance = new BeatSaver(beatSaverOptions);
            }

            var hash = beatmapLevel.GetBeatmapHash();
            var map = await beatSaverInstance.BeatmapByHash(hash, token);

            if (map != null && !string.Equals(map.LatestVersion.Hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return map;
            }

            return null;
        }

        public static async Task<bool> NeedsUpdate(this BeatmapLevel beatmapLevel, CancellationToken token)
        {
            var map = await beatmapLevel.GetBeatSaverBeatmap(token);
            return map != null;
        }

        public static async Task<string?> UpdateBeatmap(this BeatmapLevel beatmapLevel, CancellationToken token, IProgress<double> progress)
        {
            await UnityGame.SwitchToMainThreadAsync();
            var songDownloaded = false;
            while (!songDownloaded)
            {
                try
                {
                    var map = await beatmapLevel.GetBeatSaverBeatmap(token);
                    if (map == null)
                    {
                        return null;
                    }

                    var customSongsPath = CustomLevelPathHelper.customLevelsDirectoryPath;
                    var songName = FolderNameForBeatSaverMap(map);
                    var latestVersion = map.LatestVersion;
                    var latestHash = latestVersion.Hash;
                    await Task.Run(() =>
                    {
                        token.ThrowIfCancellationRequested();
                        if (!Directory.Exists(customSongsPath))
                            Directory.CreateDirectory(customSongsPath);
                    }, token);

                    var zip = await latestVersion.DownloadZIP(token, progress);
                    if (zip != null && !token.IsCancellationRequested)
                    {
                        await ExtractZipAsync(new ZipExtraction(zip, customSongsPath, songName, token));
                        return latestHash;
                    }

                    songDownloaded = true;
                }
                catch (Exception e)
                {
                    if (!(e is OperationCanceledException))
                    {
                        Plugin.Log.Error($"Failed to download Song {beatmapLevel}. Exception: {e}");
                    }
                    songDownloaded = true;
                }
            }
            return null;
        }


        private static string FolderNameForBeatSaverMap(Beatmap song)
        {
            var maxLength = 100;
            var longFolderName = song.ID + " (" + song.Metadata.SongName + " - " + song.Metadata.LevelAuthorName;
            if (longFolderName.Length > maxLength)
            {
                longFolderName = longFolderName.Substring(0, maxLength - 3) + "...";
            }
            return longFolderName + ")";
        }

        private static async Task ExtractZipAsync(ZipExtraction request)
        {
            await extractionGate.WaitAsync(request.Token).ConfigureAwait(false);
            try
            {
                await Task.Run(request.Extract, request.Token).ConfigureAwait(false);
            }
            finally
            {
                extractionGate.Release();
            }
        }

        private sealed class ZipExtraction
        {
            private readonly byte[] zip;
            private readonly string customSongsPath;
            private readonly string songName;
            public CancellationToken Token { get; }

            public ZipExtraction(byte[] zip, string customSongsPath, string songName, CancellationToken token)
            {
                this.zip = zip;
                this.customSongsPath = customSongsPath;
                this.songName = songName;
                Token = token;
            }

            public void Extract()
            {
                Token.ThrowIfCancellationRequested();
                using Stream zipStream = new MemoryStream(zip);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
                var basePath = string.Join("", songName.Split(Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).ToArray()));
                var path = Path.Combine(customSongsPath, basePath);

                if (Directory.Exists(path))
                {
                    var pathNum = 1;
                    while (Directory.Exists(path + $" ({pathNum})"))
                    {
                        Token.ThrowIfCancellationRequested();
                        ++pathNum;
                    }
                    path += $" ({pathNum})";
                }

                Token.ThrowIfCancellationRequested();
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                foreach (var entry in archive.Entries)
                {
                    Token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(entry.Name) && entry.Name == entry.FullName)
                    {
                        var entryPath = Path.Combine(path, entry.Name); // Only root entries belong to a song archive.
                        if (!File.Exists(entryPath))
                            entry.ExtractToFile(entryPath, false);
                    }
                }
            }
        }
    }
}
