using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatSaberMarkupLanguage.Components;
using BeatSaverUpdater.Migration;
using HMUI;
using IPA.Utilities;
using IPA.Utilities.Async;
using Legato;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using VRUIControls;
using Zenject;

namespace BeatSaverUpdater.UI
{
    internal class UpdateButton : IInitializable, IDisposable
    {
        private ClickableImage? image;
        private CancellationTokenSource? selectionSource;
        private CancellationTokenSource? downloadSource;
        private IDisposable? songsLoadedSubscription;
        private bool disposed;
        private string? oldLevelHash;
        private string? downloadedLevelHash;

        private readonly DiContainer container;
        private readonly HoverHintController hoverHintController;
        private readonly SelectLevelCategoryViewController selectLevelCategoryViewController;
        private readonly IconSegmentedControl levelCategorySegmentedControl;
        private readonly LevelCollectionNavigationController levelCollectionNavigationController;
        private readonly StandardLevelDetailViewController standardLevelDetailViewController;
        private readonly PopupModal popupModal;
        private readonly List<IMigrator> migrators;
        private readonly CustomLevelLoader customLevelLoader;
        private SongDetailsWrapper? songDetailsWrapper;

        public UpdateButton(DiContainer container, HoverHintController hoverHintController, SelectLevelCategoryViewController selectLevelCategoryViewController,
            LevelCollectionNavigationController levelCollectionNavigationController, StandardLevelDetailViewController standardLevelDetailViewController,
            PopupModal popupModal, List<IMigrator> migrators, CustomLevelLoader customLevelLoader, [InjectOptional] SongDetailsWrapper? songDetailsWrapper)
        {
            this.container = container;
            this.hoverHintController = hoverHintController;
            this.selectLevelCategoryViewController = selectLevelCategoryViewController;
            levelCategorySegmentedControl = selectLevelCategoryViewController.GetField<IconSegmentedControl, SelectLevelCategoryViewController>("_levelFilterCategoryIconSegmentedControl");
            this.levelCollectionNavigationController = levelCollectionNavigationController;
            this.standardLevelDetailViewController = standardLevelDetailViewController;
            this.popupModal = popupModal;
            this.migrators = migrators;
            this.customLevelLoader = customLevelLoader;
            this.songDetailsWrapper = songDetailsWrapper;
        }

        public void Initialize()
        {
            _ = InitializeAsync();

            standardLevelDetailViewController.didChangeContentEvent += ContentChanged;
        }

        public void Dispose()
        {
            disposed = true;
            standardLevelDetailViewController.didChangeContentEvent -= ContentChanged;
            selectionSource?.Cancel();
            downloadSource?.Cancel();
            songsLoadedSubscription?.Dispose();
            if (image != null)
            {
                image.OnClickEvent -= Clicked;
                if (image.sprite != null)
                {
                    UnityEngine.Object.Destroy(image.sprite.texture);
                    UnityEngine.Object.Destroy(image.sprite);
                }
                UnityEngine.Object.Destroy(image.gameObject);
                image = null;
            }
        }

        private async Task InitializeAsync()
        {
            var createdImage = image = CreateImage();
            using var mrs = Plugin.Metadata.Assembly.GetManifestResourceStream("BeatSaverUpdater.Images.Logo.png");
            using var ms = new MemoryStream();
            if (mrs != null)
            {
                await mrs.CopyToAsync(ms);
            }

            var sprite = await BeatSaberMarkupLanguage.Utilities.LoadSpriteAsync(ms.ToArray());
            if (disposed)
            {
                UnityEngine.Object.Destroy(sprite.texture);
                UnityEngine.Object.Destroy(sprite);
                return;
            }
            createdImage.OnClickEvent += Clicked;
            createdImage.sprite = sprite;
            sprite.texture.wrapMode = TextureWrapMode.Clamp;
            createdImage.gameObject.SetActive(false);
        }

        private ClickableImage CreateImage()
        {
            var gameObject = new GameObject("BeatSaverUpdater");
            var image = gameObject.AddComponent<ClickableImage>();
            image.material = BeatSaberMarkupLanguage.Utilities.ImageResources.NoGlowMat;

            image.rectTransform.SetParent(standardLevelDetailViewController.transform);
            image.rectTransform.localPosition = new Vector3(32f, 32f, 0f);
            image.rectTransform.localScale = new Vector3(.3f, .3f, .3f);
            image.rectTransform.sizeDelta = new Vector2(20f, 20f);
            gameObject.AddComponent<LayoutElement>();

            var canvas = gameObject.AddComponent<Canvas>();
            var additionalShaderChannels = canvas.additionalShaderChannels;
            additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord2;
            additionalShaderChannels |= AdditionalCanvasShaderChannels.Tangent;
            additionalShaderChannels |= AdditionalCanvasShaderChannels.Normal;
            canvas.additionalShaderChannels = additionalShaderChannels;
            container.InstantiateComponent<VRGraphicRaycaster>(gameObject);

            var hoverHint = image.gameObject.AddComponent<HoverHint>();
            hoverHint.SetField("_hoverHintController", hoverHintController);
            hoverHint.text = "Update Map!";

            return image;
        }

        private void ContentChanged(StandardLevelDetailViewController standardLevelDetailViewController, StandardLevelDetailViewController.ContentType contentType)
        {
            if (contentType == StandardLevelDetailViewController.ContentType.OwnedAndReady)
            {
                _ = BeatmapSelected(standardLevelDetailViewController.beatmapLevel);
            }
        }

        private async Task BeatmapSelected(BeatmapLevel beatmapLevel)
        {
            selectionSource?.Cancel();
            var source = selectionSource = new CancellationTokenSource();
            try
            {
                if (image != null)
                {
                    image.gameObject.SetActive(false);
                    if (beatmapLevel is { hasPrecalculatedData: false } && !beatmapLevel.levelID.EndsWith(" WIP"))
                    {
                        if (!PluginConfig.Instance.UseCache || songDetailsWrapper == null || !await songDetailsWrapper.SongExists(beatmapLevel.GetBeatmapHash()))
                        {
                            source.Token.ThrowIfCancellationRequested();
                            var needsUpdate = await beatmapLevel.NeedsUpdate(source.Token);
                            if (!disposed && !source.IsCancellationRequested && standardLevelDetailViewController.beatmapLevel == beatmapLevel)
                                image.gameObject.SetActive(needsUpdate);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (source.IsCancellationRequested) { }
            catch (Exception ex) { Plugin.Log.Warn($"Checking map update failed: {ex}"); }
            finally
            {
                if (selectionSource == source)
                    selectionSource = null;
                source.Dispose();
            }
        }

        private async void Clicked(PointerEventData _)
        {
            if (standardLevelDetailViewController.beatmapLevel is { hasPrecalculatedData: false } beatmapLevel)
            {
                var newHash = (await beatmapLevel.GetBeatSaverBeatmap(CancellationToken.None))?.LatestVersion.Hash;
                if (disposed || standardLevelDetailViewController.beatmapLevel != beatmapLevel)
                    return;

                if (newHash != null)
                {
                    var newLevel = SongCore.Loader.GetLevelByHash(newHash ?? "");
                    if (newLevel != null)
                    {
                        popupModal.ShowYesNoModal("Updated map already exists!", () =>
                        {
                            if (popupModal.CheckboxValue)
                            {
                                downloadedLevelHash = newHash;
                                UpdateReferences(beatmapLevel, newLevel);
                            }
                            else
                            {
                                popupModal.HideModal();
                                OpenMap(newLevel);
                            }
                        }, "Open Map", "Dismiss", showCheckbox: true, referencesActive: true);
                        return;
                    }
                }

                popupModal.ShowYesNoModal("This map has an update on BeatSaver. Do you want to download it?", () => UpdateRequested(beatmapLevel));
            }
        }

        private async void UpdateRequested(BeatmapLevel beatmapLevel)
        {
            downloadSource?.Cancel();
            var source = downloadSource = new CancellationTokenSource();
            popupModal.ShowDownloadingModal("Updating map", () =>
            {
                if (downloadSource == source)
                    source.Cancel();
            });
            try
            {
                oldLevelHash = beatmapLevel.GetBeatmapHash();
                var newHash = await beatmapLevel.UpdateBeatmap(source.Token, popupModal);
                if (downloadSource != source || disposed)
                    return;
                downloadedLevelHash = newHash;
                if (newHash != null && !source.IsCancellationRequested)
                {
                    var loader = SongCore.Loader.Instance;
                    if (loader == null)
                    {
                        Plugin.Log.Warn("SongCore loader is unavailable after map download.");
                        popupModal.HideModal();
                        return;
                    }
                    songsLoadedSubscription?.Dispose();
                    songsLoadedSubscription = SongCoreLoaderEvents.SubscribeToSongsLoaded(OnSongsLoaded);
                    loader.RefreshSongs(false);
                }
                else
                    popupModal.HideModal();
            }
            finally
            {
                if (downloadSource == source)
                    downloadSource = null;
                source.Dispose();
            }
        }

        private void OpenMap(BeatmapLevel beatmapLevel)
        {
            levelCategorySegmentedControl.SelectCellWithNumber(3);
            selectLevelCategoryViewController.InvokeMethod<object, SelectLevelCategoryViewController>("LevelFilterCategoryIconSegmentedControlDidSelectCell", levelCategorySegmentedControl, 3);
            levelCollectionNavigationController.SelectLevel(beatmapLevel);
        }

        private void OnSongsLoaded()
        {
            songsLoadedSubscription?.Dispose();
            songsLoadedSubscription = null;
            var oldLevel = SongCore.Loader.GetLevelByHash(oldLevelHash ?? "");
            var downloadedLevel = SongCore.Loader.GetLevelByHash(downloadedLevelHash ?? "");
            if (downloadedLevel != null)
            {
                OpenMap(downloadedLevel);
                popupModal.ShowYesNoModal("Map Updated!\nWould you also like to update all of its references?", () => UpdateReferences(oldLevel, downloadedLevel), "Update", "Dismiss", true);
            }
            else
            {
                popupModal.HideModal();
            }
        }

        private async void UpdateReferences(BeatmapLevel? oldLevel, BeatmapLevel downloadedLevel)
        {
            if (oldLevel != null)
            {
                popupModal.ShowLoadingModal("Migrating References");
                await Task.Run(() => UpdateReferencesAsync(oldLevel, downloadedLevel));
                var downloadedLevelAfterUpdate = SongCore.Loader.GetLevelByHash(downloadedLevelHash ?? "");
                if (downloadedLevelAfterUpdate != null)
                {
                    OpenMap(downloadedLevelAfterUpdate);
                }
            }
            popupModal.HideModal();
        }

        private void UpdateReferencesAsync(BeatmapLevel oldLevel, BeatmapLevel downloadedLevel)
        {
            var preventDelete = false;

            foreach (var migrator in migrators)
            {
                preventDelete = migrator.MigrateMap(oldLevel, downloadedLevel) || preventDelete;
            }

            if (!preventDelete && customLevelLoader._loadedBeatmapSaveData.TryGetValue(oldLevel.levelID, out var saveData))
            {
                if (!string.IsNullOrEmpty(saveData.customLevelFolderInfo.folderPath))
                {
                    if (SongCore.Loader.Instance is { } loader)
                        loader.DeleteSong(saveData.customLevelFolderInfo.folderPath);
                    else
                        Plugin.Log.Warn("SongCore loader is unavailable for old map deletion.");
                }
            }
        }
    }
}
