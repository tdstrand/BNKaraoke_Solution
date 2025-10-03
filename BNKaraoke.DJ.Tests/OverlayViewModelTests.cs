using System;
using System.Reflection;
using BNKaraoke.DJ.Models;
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

            viewModel.UpdateFromQueue(nowPlaying, upNext, null);

            viewModel.IsBlueState = false;

            Assert.Equal("Playback Alice", viewModel.TopBandText);
            Assert.Equal("Playback Wonderwall", viewModel.BottomBandText);

            viewModel.IsBlueState = true;

            Assert.Equal("Blue Bob", viewModel.TopBandText);
            Assert.Equal("Blue BNK", viewModel.BottomBandText);
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
