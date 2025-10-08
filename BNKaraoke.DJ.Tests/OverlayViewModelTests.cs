using System;
using System.Collections.Generic;
using System.Reflection;
using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services.Overlay;
using BNKaraoke.DJ.ViewModels.Overlays;
using Xunit;

namespace BNKaraoke.DJ.Tests
{
    public class OverlayViewModelTests
    {
        [Fact]
        public void ActiveTemplatesSwitchWhenBlueStateChanges()
        {
            var viewModel = CreateViewModel();

            viewModel.TopTemplatePlayback = "Playback {Requestor}";
            viewModel.BottomTemplatePlayback = "Playback {Song}";
            viewModel.TopTemplateBlue = "Blue {UpNextRequestor}";
            viewModel.BottomTemplateBlue = "Blue {Brand}";
            viewModel.BrandText = "BNK";

            var nowPlaying = new QueueEntry
            {
                RequestorDisplayName = "Alice",
                SongTitle = "Wonderwall",
                SongArtist = "Oasis"
            };
            var upNext = new QueueEntry
            {
                RequestorDisplayName = "Bob"
            };

            var queue = new List<QueueEntry> { nowPlaying, upNext };
            viewModel.UpdatePlaybackState(queue, nowPlaying, null, ReorderMode.AllowMature);

            viewModel.IsBlueState = false;

            Assert.Equal("Playback Alice", viewModel.TopBandText);
            Assert.Equal("Playback Wonderwall", viewModel.BottomBandText);

            viewModel.IsBlueState = true;

            Assert.Equal("Blue Bob", viewModel.TopBandText);
            Assert.Equal("Blue BNK", viewModel.BottomBandText);
        }

        [Fact]
        public void BottomBandTextFallsBackWhenTemplateProducesNoContent()
        {
            var viewModel = CreateViewModel();

            viewModel.BottomTemplatePlayback = "{Song}";
            viewModel.UpdatePlaybackState(new List<QueueEntry>(), null, null, ReorderMode.AllowMature);

            Assert.Equal("BNKaraoke.com • NOW PLAYING: —", viewModel.BottomBandText);

            viewModel.BottomTemplateBlue = "{Requestor}";
            viewModel.IsBlueState = true;

            Assert.Equal("BNKaraoke.com • REQUEST A SONG AT BNKaraoke.com", viewModel.BottomBandText);
        }

        [Fact]
        public void BrandTextFallsBackToDefaultWhenCleared()
        {
            var viewModel = CreateViewModel();

            viewModel.BrandText = "Custom Brand";
            Assert.Equal("Custom Brand", viewModel.BrandText);

            viewModel.BrandText = "   ";

            Assert.Equal(OverlaySettings.DefaultBrand, viewModel.BrandText);
        }

        [Fact]
        public void BottomBandBlueTemplateUsesBrandFallbackWhenContextBrandMissing()
        {
            var viewModel = CreateViewModel();

            viewModel.BottomTemplateBlue = "{Brand} • REQUEST A SONG AT {Brand} - Be sure to get your song request in Early !!! The song queue fills up quickly.";
            viewModel.IsBlueState = true;

            var contextField = typeof(OverlayViewModel).GetField("_templateContext", BindingFlags.Instance | BindingFlags.NonPublic);
            var context = (OverlayTemplateContext?)contextField?.GetValue(viewModel);
            Assert.NotNull(context);
            context!.Brand = string.Empty;

            viewModel.UpdatePlaybackState(new List<QueueEntry>(), null, null, ReorderMode.AllowMature);

            Assert.Equal(
                "BNKaraoke.com • REQUEST A SONG AT BNKaraoke.com - Be sure to get your song request in Early !!! The song queue fills up quickly.",
                viewModel.BottomBandText);
        }

        private static OverlayViewModel CreateViewModel()
        {
            var type = typeof(OverlayViewModel);
            var instance = (OverlayViewModel)Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic, null, Array.Empty<object?>(), null)!;
            var suppressField = type.GetField("_suppressSave", BindingFlags.Instance | BindingFlags.NonPublic);
            suppressField?.SetValue(instance, true);
            return instance;
        }
    }
}
