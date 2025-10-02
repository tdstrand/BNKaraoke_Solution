using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public record ReorderHorizonOption(string Label, int? Value);

    public partial class ReorderQueueModalViewModel : ObservableObject
    {
        private const string AllMatureDeferredCode = "ALL_MATURE_DEFERRED";

        private readonly IApiService _apiService;
        private readonly SettingsService _settingsService;
        private readonly IReadOnlyList<ReorderQueuePreviewItem> _snapshot;
        private readonly int _eventId;
        private readonly int _confirmationThreshold;
        private bool _synchronizingMode;
        private bool _requiresConfirmation;
        private CancellationTokenSource? _previewCts;

        public ObservableCollection<ReorderQueuePreviewItem> CurrentItems { get; }
        public ObservableCollection<ReorderQueuePreviewItem> ProposedItems { get; } = new();
        public ObservableCollection<ReorderWarning> Warnings { get; } = new();
        public ObservableCollection<ReorderPreviewDiff> Diffs { get; } = new();
        public ObservableCollection<ReorderHorizonOption> HorizonOptions { get; }

        [ObservableProperty]
        private ReorderHorizonOption _selectedHorizon;

        [ObservableProperty]
        private bool _isDeferMature;

        [ObservableProperty]
        private bool _isAllowMature;

        [ObservableProperty]
        private int _maxMove = 4;

        [ObservableProperty]
        private bool _isMaxMoveEnabled;

        [ObservableProperty]
        private bool _isPreviewGenerated;

        [ObservableProperty]
        private bool _isApproveEnabled;

        [ObservableProperty]
        private bool _isApprovalBlocked;

        [ObservableProperty]
        private int _movedCount;

        [ObservableProperty]
        private double _fairnessBefore;

        [ObservableProperty]
        private double _fairnessAfter;

        [ObservableProperty]
        private bool _noAdjacentRepeat = true;

        [ObservableProperty]
        private string? _planId;

        [ObservableProperty]
        private string? _basedOnVersion;

        [ObservableProperty]
        private string? _proposedVersion;

        [ObservableProperty]
        private DateTime? _planExpiresAt;

        [ObservableProperty]
        private string? _idempotencyKey = Guid.NewGuid().ToString("N");

        [ObservableProperty]
        private string _previewStatus = "Preview has not been generated.";

        [ObservableProperty]
        private bool _isBusy;

        public bool IsApproved { get; private set; }

        public event EventHandler<bool>? RequestClose;

        public ReorderMode Mode => IsAllowMature ? ReorderMode.AllowMature : ReorderMode.DeferMature;

        public int? Horizon => SelectedHorizon?.Value;

        public int? MaxMoveConstraint => IsMaxMoveEnabled ? MaxMove : null;

        public IEnumerable<string> WarningMessages => Warnings.Select(w => w.Message);

        public bool HasWarnings => Warnings.Any();

        public string AdjacentRepeatStatus => NoAdjacentRepeat ? "✓" : "✗";

        public ReorderQueueModalViewModel(
            IApiService apiService,
            SettingsService settingsService,
            int eventId,
            IEnumerable<ReorderQueuePreviewItem> snapshot,
            string? basedOnVersion = null)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
            _eventId = eventId;
            _snapshot = snapshot?.ToList() ?? new List<ReorderQueuePreviewItem>();
            _confirmationThreshold = Math.Max(1, _settingsService.Settings.QueueReorderConfirmationThreshold);
            _basedOnVersion = basedOnVersion;

            CurrentItems = new ObservableCollection<ReorderQueuePreviewItem>(_snapshot);

            HorizonOptions = new ObservableCollection<ReorderHorizonOption>(new[]
            {
                new ReorderHorizonOption("Entire queue", null),
                new ReorderHorizonOption("Next 10", 10),
                new ReorderHorizonOption("Next 20", 20)
            });
            _selectedHorizon = HorizonOptions.First();

            ConfigureDefaultMatureMode(_settingsService.Settings.DefaultReorderMaturePolicy);

            Warnings.CollectionChanged += Warnings_CollectionChanged;
            UpdateApproveState();
        }

        private void ConfigureDefaultMatureMode(string? policy)
        {
            var defaultMode = ReorderMode.DeferMature;
            if (!string.IsNullOrWhiteSpace(policy) && Enum.TryParse(policy, true, out ReorderMode parsed))
            {
                defaultMode = parsed;
            }

            _synchronizingMode = true;
            IsDeferMature = defaultMode != ReorderMode.AllowMature;
            IsAllowMature = defaultMode == ReorderMode.AllowMature;
            _synchronizingMode = false;
        }

        private void Warnings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(WarningMessages));
            OnPropertyChanged(nameof(HasWarnings));
            UpdateApproveState();
        }

        partial void OnIsDeferMatureChanged(bool value)
        {
            if (_synchronizingMode)
            {
                return;
            }

            _synchronizingMode = true;
            IsAllowMature = !value;
            _synchronizingMode = false;
            OnPropertyChanged(nameof(Mode));
            InvalidatePreview();
        }

        partial void OnIsAllowMatureChanged(bool value)
        {
            if (_synchronizingMode)
            {
                return;
            }

            _synchronizingMode = true;
            IsDeferMature = !value;
            _synchronizingMode = false;
            OnPropertyChanged(nameof(Mode));
            InvalidatePreview();
        }

        partial void OnSelectedHorizonChanged(ReorderHorizonOption value)
        {
            OnPropertyChanged(nameof(Horizon));
            InvalidatePreview();
        }

        partial void OnIsMaxMoveEnabledChanged(bool value)
        {
            OnPropertyChanged(nameof(MaxMoveConstraint));
            InvalidatePreview();
        }

        partial void OnMaxMoveChanged(int value)
        {
            if (value < 1)
            {
                MaxMove = 1;
                return;
            }

            OnPropertyChanged(nameof(MaxMoveConstraint));
            InvalidatePreview();
        }

        private void InvalidatePreview()
        {
            _previewCts?.Cancel();

            if (ProposedItems.Count > 0)
            {
                Log.Information("[REORDER MODAL] Preview invalidated due to configuration change.");
            }

            ProposedItems.Clear();
            Diffs.Clear();
            if (Warnings.Count > 0)
            {
                Warnings.Clear();
            }

            _requiresConfirmation = false;
            IsPreviewGenerated = false;
            PreviewStatus = "Preview has not been generated.";
            PlanId = null;
            ProposedVersion = null;
            PlanExpiresAt = null;
            MovedCount = 0;
            FairnessBefore = 0;
            FairnessAfter = 0;
            NoAdjacentRepeat = true;
            UpdateApproveState();
        }

        [RelayCommand]
        private async Task GeneratePreviewAsync()
        {
            if (IsBusy)
            {
                Log.Information("[REORDER MODAL] Cancelling previous preview request.");
                _previewCts?.Cancel();
            }

            var cts = new CancellationTokenSource();
            _previewCts = cts;
            var token = cts.Token;

            try
            {
                IsBusy = true;
                PreviewStatus = "Generating preview…";
                ProposedItems.Clear();
                Diffs.Clear();
                if (Warnings.Count > 0)
                {
                    Warnings.Clear();
                }
                UpdateApproveState();

                var request = new ReorderPreviewRequest
                {
                    EventId = _eventId,
                    BasedOnVersion = BasedOnVersion,
                    MaturePolicy = Mode.ToMaturePolicy(),
                    Horizon = Horizon,
                    MovementCap = MaxMoveConstraint
                };

                Log.Information(
                    "[REORDER MODAL] Requesting preview for EventId={EventId}, Mode={Mode}, Horizon={Horizon}, MovementCap={MovementCap}",
                    request.EventId,
                    request.MaturePolicy,
                    request.Horizon,
                    request.MovementCap);

                var response = await _apiService.PreviewQueueReorderAsync(request, token);
                ApplyPreviewResponse(response);
            }
            catch (ApiRequestException apiEx) when (!token.IsCancellationRequested)
            {
                Log.Warning(apiEx, "[REORDER MODAL] Preview request returned API error: {Message}", apiEx.Message);

                if (apiEx.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    HandleUnprocessablePreview(apiEx.Message);
                }
                else
                {
                    PreviewStatus = apiEx.Message;
                }

                PopulateWarnings(apiEx.Warnings);
            }
            catch (HttpRequestException ex) when (!token.IsCancellationRequested)
            {
                Log.Error(ex, "[REORDER MODAL] HTTP failure during preview: {Message}", ex.Message);
                PreviewStatus = $"Failed to generate preview: {ex.Message}";
            }
            catch (TaskCanceledException) when (token.IsCancellationRequested)
            {
                Log.Information("[REORDER MODAL] Preview request cancelled.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[REORDER MODAL] Unexpected error during preview: {Message}", ex.Message);
                PreviewStatus = $"Failed to generate preview: {ex.Message}";
            }
            finally
            {
                if (_previewCts == cts)
                {
                    _previewCts = null;
                }

                IsBusy = false;
                UpdateApproveState();
            }
        }

        private void ApplyPreviewResponse(ReorderPreviewResponse response)
        {
            if (response == null)
            {
                PreviewStatus = "Preview response was empty.";
                return;
            }

            PlanId = response.PlanId;
            BasedOnVersion = response.BasedOnVersion;
            ProposedVersion = response.ProposedVersion;
            PlanExpiresAt = response.ExpiresAt;
            IdempotencyKey = Guid.NewGuid().ToString("N");

            PopulateProposedItems(response.Items);
            PopulateWarnings(response.Warnings);
            PopulateDiffs(response.Diffs);

            var summary = response.Summary;
            MovedCount = summary.MoveCount;
            FairnessBefore = summary.FairnessBefore;
            FairnessAfter = summary.FairnessAfter;
            NoAdjacentRepeat = summary.NoAdjacentRepeat;
            _requiresConfirmation = summary.RequiresConfirmation || MovedCount >= _confirmationThreshold;

            IsPreviewGenerated = true;
            PreviewStatus = PlanExpiresAt.HasValue
                ? $"Preview ready. Plan expires at {PlanExpiresAt:HH:mm:ss}."
                : "Preview generated.";
            UpdateApproveState();
        }

        private void PopulateProposedItems(IEnumerable<ReorderQueuePreviewItem> items)
        {
            ProposedItems.Clear();
            foreach (var item in items.OrderBy(i => i.DisplayIndex))
            {
                ProposedItems.Add(item);
            }
        }

        private void PopulateWarnings(IEnumerable<ReorderWarning> warnings)
        {
            Warnings.Clear();
            foreach (var warning in warnings ?? Enumerable.Empty<ReorderWarning>())
            {
                var blocks = warning.BlocksApproval || string.Equals(warning.Code, AllMatureDeferredCode, StringComparison.OrdinalIgnoreCase);
                Warnings.Add(blocks ? warning with { BlocksApproval = true } : warning);
            }
        }

        private void PopulateDiffs(IEnumerable<ReorderPreviewDiff>? diffs)
        {
            Diffs.Clear();
            if (diffs == null)
            {
                return;
            }

            foreach (var diff in diffs)
            {
                Diffs.Add(diff);
            }
        }

        private void HandleUnprocessablePreview(string message)
        {
            var statusMessage = string.IsNullOrWhiteSpace(message)
                ? "The optimizer could not generate a new order. Showing the current queue."
                : $"{message} Showing the current queue.";

            PreviewStatus = statusMessage;
            PopulateProposedItems(_snapshot);

            PlanId = null;
            ProposedVersion = null;
            PlanExpiresAt = null;
            IdempotencyKey = null;

            MovedCount = 0;
            FairnessBefore = 0;
            FairnessAfter = 0;
            NoAdjacentRepeat = true;
            _requiresConfirmation = false;

            IsPreviewGenerated = true;
        }

        partial void OnMovedCountChanged(int value)
        {
            UpdateApproveState();
        }

        [RelayCommand]
        private void Approve()
        {
            if (!IsPreviewGenerated)
            {
                PreviewStatus = "Generate a preview before approving.";
                return;
            }

            if (!IsApproveEnabled)
            {
                return;
            }

            if (_requiresConfirmation)
            {
                var result = MessageBox.Show(
                    "Are you sure you want to apply this reorder plan?",
                    "Confirm reorder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            IsApproved = true;
            RequestClose?.Invoke(this, true);
        }

        [RelayCommand]
        private void Cancel()
        {
            IsApproved = false;
            RequestClose?.Invoke(this, false);
        }

        private void UpdateApproveState()
        {
            IsApprovalBlocked = Warnings.Any(warning => warning.BlocksApproval || string.Equals(warning.Code, AllMatureDeferredCode, StringComparison.OrdinalIgnoreCase));

            var hasMeaningfulChanges = IsPreviewGenerated && MovedCount > 0;
            if (IsPreviewGenerated && !hasMeaningfulChanges)
            {
                PreviewStatus = "Preview matches the current queue. Reorder items before approving.";
            }

            IsApproveEnabled = hasMeaningfulChanges && !IsApprovalBlocked && !IsBusy;
            OnPropertyChanged(nameof(AdjacentRepeatStatus));
        }
    }
}
