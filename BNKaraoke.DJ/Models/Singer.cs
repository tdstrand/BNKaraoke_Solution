using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Windows.Media;
using BNKaraoke.DJ.Services.Presentation;
using Serilog;

namespace BNKaraoke.DJ.Models
{
    public class Singer : INotifyPropertyChanged
    {
        private string _userId = string.Empty;
        public string UserId
        {
            get => _userId;
            set
            {
                if (_userId != value)
                {
                    Log.Information("[SINGER] Setting UserId from {_userId} to {value}", _userId, value);
                    _userId = value;
                    OnPropertyChanged(nameof(UserId));
                }
            }
        }

        private string _displayName = string.Empty;
        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    Log.Information("[SINGER] Setting DisplayName from {DisplayName} to {value} for UserId={UserId}", _displayName, value, UserId);
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        private bool _isLoggedIn;
        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set
            {
                if (_isLoggedIn != value)
                {
                    Log.Information("[SINGER] Setting IsLoggedIn from {IsLoggedIn} to {value} for UserId={UserId}", _isLoggedIn, value, UserId);
                    _isLoggedIn = value;
                    OnPropertyChanged(nameof(IsLoggedIn));
                    UpdateStatusFlagsFromBooleans(_isLoggedIn, _isJoined, _isOnBreak);
                }
            }
        }

        private bool _isJoined;
        public bool IsJoined
        {
            get => _isJoined;
            set
            {
                if (_isJoined != value)
                {
                    Log.Information("[SINGER] Setting IsJoined from {IsJoined} to {value} for UserId={UserId}", _isJoined, value, UserId);
                    _isJoined = value;
                    OnPropertyChanged(nameof(IsJoined));
                    UpdateStatusFlagsFromBooleans(_isLoggedIn, _isJoined, _isOnBreak);
                }
            }
        }

        private bool _isOnBreak;
        public bool IsOnBreak
        {
            get => _isOnBreak;
            set
            {
                if (_isOnBreak != value)
                {
                    Log.Information("[SINGER] Setting IsOnBreak from {IsOnBreak} to {value} for UserId={UserId}", _isOnBreak, value, UserId);
                    _isOnBreak = value;
                    OnPropertyChanged(nameof(IsOnBreak));
                    UpdateStatusFlagsFromBooleans(_isLoggedIn, _isJoined, _isOnBreak);
                }
            }
        }

        private DateTime _updatedAt;
        public DateTime UpdatedAt
        {
            get => _updatedAt;
            set
            {
                if (_updatedAt != value)
                {
                    Log.Information("[SINGER] Setting UpdatedAt from {UpdatedAt} to {value} for UserId={UserId}", _updatedAt, value, UserId);
                    _updatedAt = value;
                    OnPropertyChanged(nameof(UpdatedAt));
                }
            }
        }

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrEmpty(UserId) && !string.IsNullOrEmpty(DisplayName);

        public SingerStatusFlags Status { get; private set; } = SingerStatusFlags.None;

        private SolidColorBrush _statusBrush = SingerStyleMapper.DefaultForeground();
        public SolidColorBrush StatusBrush
        {
            get => _statusBrush;
            private set
            {
                if (!ReferenceEquals(_statusBrush, value))
                {
                    _statusBrush = value;
                    OnPropertyChanged(nameof(StatusBrush));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void UpdateStatusFlagsFromBooleans(bool isLoggedIn, bool isJoined, bool isOnBreak, bool isMuted = false)
        {
            var flags = SingerStatusFlags.None;
            if (isLoggedIn) flags |= SingerStatusFlags.LoggedIn;
            if (isJoined) flags |= SingerStatusFlags.Joined;
            if (isOnBreak) flags |= SingerStatusFlags.OnBreak;
            if (isMuted) flags |= SingerStatusFlags.Muted;

            if (Status != flags)
            {
                Status = flags;
                OnPropertyChanged(nameof(Status));
            }

            StatusBrush = SingerStyleMapper.MapForeground(Status);
        }
    }
}
