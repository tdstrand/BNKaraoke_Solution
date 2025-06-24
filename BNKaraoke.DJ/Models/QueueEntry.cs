using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;

namespace BNKaraoke.DJ.Models
{
    public class QueueEntry : INotifyPropertyChanged
    {
        private int _queueId;
        private int _eventId;
        private int _songId;
        private string? _songTitle;
        private string? _songArtist;
        private string? _requestorDisplayName;
        private string? _videoLength;
        private int _position;
        private string? _status;
        private string? _requestorUserName;
        private List<string>? _singers;
        private bool _isActive;
        private bool _wasSkipped;
        private bool _isCurrentlyPlaying;
        private DateTime? _sungAt;
        private string? _genre;
        private string? _decade;
        private string? _youTubeUrl;
        private bool _isVideoCached;
        private bool _isOnBreak;
        private bool _isOnHold;
        private bool _isUpNext;
        private string? _holdReason;
        private bool _isSingerLoggedIn;
        private bool _isSingerJoined;
        private bool _isSingerOnBreak;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(IsUpNext) || propertyName == nameof(IsOnHold) ||
                propertyName == nameof(RequestorDisplayName) || propertyName == nameof(IsSingerLoggedIn) ||
                propertyName == nameof(IsSingerJoined) || propertyName == nameof(IsSingerOnBreak))
            {
                Log.Information("[QUEUE ENTRY] {PropertyName} changed for SongId={SongId}: {Value}",
                    propertyName, SongId, GetPropertyValue(propertyName));
            }
        }

        private object? GetPropertyValue(string propertyName)
        {
            return propertyName switch
            {
                nameof(IsUpNext) => IsUpNext,
                nameof(IsOnHold) => IsOnHold,
                nameof(RequestorDisplayName) => RequestorDisplayName ?? "null",
                nameof(IsSingerLoggedIn) => IsSingerLoggedIn,
                nameof(IsSingerJoined) => IsSingerJoined,
                nameof(IsSingerOnBreak) => IsSingerOnBreak,
                _ => null
            };
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public int QueueId
        {
            get => _queueId;
            set => SetProperty(ref _queueId, value);
        }

        public int EventId
        {
            get => _eventId;
            set => SetProperty(ref _eventId, value);
        }

        public int SongId
        {
            get => _songId;
            set => SetProperty(ref _songId, value);
        }

        public string? SongTitle
        {
            get => _songTitle;
            set => SetProperty(ref _songTitle, value);
        }

        public string? SongArtist
        {
            get => _songArtist;
            set => SetProperty(ref _songArtist, value);
        }

        public string? RequestorDisplayName
        {
            get => _requestorDisplayName;
            set => SetProperty(ref _requestorDisplayName, value);
        }

        public string? VideoLength
        {
            get => _videoLength;
            set => SetProperty(ref _videoLength, value);
        }

        public int Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        public string? Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        public string? RequestorUserName
        {
            get => _requestorUserName;
            set => SetProperty(ref _requestorUserName, value);
        }

        public List<string>? Singers
        {
            get => _singers;
            set => SetProperty(ref _singers, value);
        }

        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        public bool WasSkipped
        {
            get => _wasSkipped;
            set => SetProperty(ref _wasSkipped, value);
        }

        public bool IsCurrentlyPlaying
        {
            get => _isCurrentlyPlaying;
            set => SetProperty(ref _isCurrentlyPlaying, value);
        }

        public DateTime? SungAt
        {
            get => _sungAt;
            set => SetProperty(ref _sungAt, value);
        }

        public string? Genre
        {
            get => _genre;
            set => SetProperty(ref _genre, value);
        }

        public string? Decade
        {
            get => _decade;
            set => SetProperty(ref _decade, value);
        }

        public string? YouTubeUrl
        {
            get => _youTubeUrl;
            set => SetProperty(ref _youTubeUrl, value);
        }

        public bool IsVideoCached
        {
            get => _isVideoCached;
            set => SetProperty(ref _isVideoCached, value);
        }

        public bool IsOnBreak
        {
            get => _isOnBreak;
            set => SetProperty(ref _isOnBreak, value);
        }

        public bool IsOnHold
        {
            get => _isOnHold;
            set => SetProperty(ref _isOnHold, value);
        }

        public bool IsUpNext
        {
            get => _isUpNext;
            set => SetProperty(ref _isUpNext, value);
        }

        public string? HoldReason
        {
            get => _holdReason;
            set => SetProperty(ref _holdReason, value);
        }

        public bool IsSingerLoggedIn
        {
            get => _isSingerLoggedIn;
            set => SetProperty(ref _isSingerLoggedIn, value);
        }

        public bool IsSingerJoined
        {
            get => _isSingerJoined;
            set => SetProperty(ref _isSingerJoined, value);
        }

        public bool IsSingerOnBreak
        {
            get => _isSingerOnBreak;
            set => SetProperty(ref _isSingerOnBreak, value);
        }
    }
}