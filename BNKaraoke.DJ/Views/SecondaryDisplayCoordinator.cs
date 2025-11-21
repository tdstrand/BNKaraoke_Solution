using System;

namespace BNKaraoke.DJ.Views
{
    internal interface ISecondaryDisplayCoordinator
    {
        void ActivateVideoSurface();
        void ActivateBrandSurface();
        void HandleVideoWindowClosed();
    }

    internal sealed class SecondaryDisplayCoordinator : ISecondaryDisplayCoordinator, IDisposable
    {
        private readonly VideoPlayerWindow _videoWindow;
        private readonly BrandScreenWindow _brandWindow;
        private bool _initialized;
        private bool _disposed;

        public SecondaryDisplayCoordinator(VideoPlayerWindow videoWindow, BrandScreenWindow brandWindow)
        {
            _videoWindow = videoWindow;
            _brandWindow = brandWindow;
        }

        public void Initialize()
        {
            if (_initialized || _disposed)
            {
                return;
            }

            _videoWindow.DeactivateSurface();
            _brandWindow.DeactivateSurface();
            _videoWindow.ShowWindow();
            _brandWindow.ShowWindow();

            _brandWindow.ActivateSurface();

            _initialized = true;
        }

        public void ActivateVideoSurface()
        {
            if (!_initialized || _disposed)
            {
                return;
            }

            _brandWindow.DeactivateSurface();
            _videoWindow.ActivateSurface();
        }

        public void ActivateBrandSurface()
        {
            if (!_initialized || _disposed)
            {
                return;
            }

            _videoWindow.DeactivateSurface();
            _brandWindow.ActivateSurface();
        }

        public void HandleVideoWindowClosed()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _brandWindow.DeactivateSurface();
            _brandWindow.SafeClose();
        }
    }
}
