using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class DJScreenViewModel
    {
        public IRelayCommand UpdateSingerStatusCommand => new RelayCommand<string>(async parameter =>
        {
            try
            {
                if (string.IsNullOrEmpty(parameter) || string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Warning("[DJSCREEN] UpdateSingerStatusCommand: Invalid parameter or no event joined, Parameter={Parameter}, EventId={EventId}", parameter, _currentEventId);
                    WarningMessage = "Please select a singer and join an event.";
                    return;
                }

                var parts = parameter.Split('|');
                if (parts.Length != 2)
                {
                    Log.Warning("[DJSCREEN] UpdateSingerStatusCommand: Invalid parameter format: {Parameter}", parameter);
                    WarningMessage = "Invalid singer status format.";
                    return;
                }

                var status = parts[0];
                var userId = parts[1];

                bool isLoggedIn, isJoined, isOnBreak;
                switch (status)
                {
                    case "Active":
                        isLoggedIn = true;
                        isJoined = true;
                        isOnBreak = false;
                        break;
                    case "OnBreak":
                        isLoggedIn = true;
                        isJoined = true;
                        isOnBreak = true;
                        break;
                    case "NotJoined":
                        isLoggedIn = true;
                        isJoined = false;
                        isOnBreak = false;
                        break;
                    case "LoggedOut":
                        isLoggedIn = false;
                        isJoined = false;
                        isOnBreak = false;
                        break;
                    default:
                        Log.Warning("[DJSCREEN] UpdateSingerStatusCommand: Unknown status: {Status}", status);
                        WarningMessage = "Unknown singer status.";
                        return;
                }

                Log.Information("[DJSCREEN] Sending singer status update request: EventId={EventId}, UserId={UserId}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}",
                    _currentEventId, userId, isLoggedIn, isJoined, isOnBreak);
                await _apiService.UpdateSingerStatusAsync(_currentEventId, userId, isLoggedIn, isJoined, isOnBreak);
                Log.Information("[DJSCREEN] Singer status update request sent for UserId={UserId}, Status={Status}", userId, status);
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to update singer status: {Message}", ex.Message);
                WarningMessage = $"Failed to update singer status: {ex.Message}";
            }
        });

        private async Task LoadSingersAsync()
        {
            try
            {
                Log.Information("[DJSCREEN] Loading singer data for event: {EventId}", _currentEventId);
                if (string.IsNullOrEmpty(_currentEventId))
                {
                    Log.Information("[DJSCREEN] No event joined, skipping singer data load");
                    Singers.Clear();
                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();
                    NonDummySingersCount = 0;
                    OnPropertyChanged(nameof(NonDummySingersCount));
                    return;
                }

                var singers = await _apiService.GetSingersAsync(_currentEventId);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Singers.Clear();
                    foreach (var singer in singers)
                    {
                        Singers.Add(singer);
                    }
                    SortSingers();
                    NonDummySingersCount = Singers.Count;
                    Log.Information("[DJSCREEN] Loaded {Count} singers for event {EventId}, Names={Names}",
                        NonDummySingersCount, _currentEventId, string.Join(", ", Singers.Select(s => s.DisplayName)));
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to load singers for event: {EventId}: {Message}", _currentEventId, ex.Message);
                WarningMessage = $"Failed to load singers: {ex.Message}";
            }
        }

        private void SortSingers()
        {
            try
            {
                Log.Information("[DJSCREEN] Sorting singers");
                var sortedSingers = Singers.OrderBy(singer =>
                {
                    if (singer.IsLoggedIn && singer.IsJoined && !singer.IsOnBreak) return 1; // Green
                    if (singer.IsLoggedIn && singer.IsJoined && singer.IsOnBreak) return 2; // Yellow
                    if (singer.IsLoggedIn && !singer.IsJoined) return 3; // Orange
                    return 4; // Red
                }).ThenBy(singer => singer.DisplayName).ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    Singers.Clear();
                    foreach (var singer in sortedSingers)
                    {
                        Singers.Add(singer);
                    }

                    GreenSingers.Clear();
                    YellowSingers.Clear();
                    OrangeSingers.Clear();
                    RedSingers.Clear();

                    foreach (var singer in Singers)
                    {
                        if (singer.IsLoggedIn && singer.IsJoined && !singer.IsOnBreak)
                            GreenSingers.Add(singer);
                        else if (singer.IsLoggedIn && singer.IsJoined && singer.IsOnBreak)
                            YellowSingers.Add(singer);
                        else if (singer.IsLoggedIn && !singer.IsJoined)
                            OrangeSingers.Add(singer);
                        else
                            RedSingers.Add(singer);
                    }

                    NonDummySingersCount = Singers.Count;
                    OnPropertyChanged(nameof(NonDummySingersCount));
                    OnPropertyChanged(nameof(GreenSingers));
                    OnPropertyChanged(nameof(YellowSingers));
                    OnPropertyChanged(nameof(OrangeSingers));
                    OnPropertyChanged(nameof(RedSingers));
                    Log.Information("[DJSCREEN] Sorted singers: {Count} singers, Names={Names}",
                        NonDummySingersCount, string.Join(", ", Singers.Select(s => s.DisplayName)));
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DJSCREEN] Failed to sort singers: {Message}", ex.Message);
                WarningMessage = $"Failed to sort singers: {ex.Message}";
            }
        }
    }
}