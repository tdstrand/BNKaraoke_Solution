using BNKaraoke.DJ.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public enum ReorderMode
    {
        DeferMature,
        AllowMature
    }

    public record ReorderHorizonOption(string Label, int? Value);

    public record ReorderWarning(string Code, string Message);

    public partial class ReorderQueueModalViewModel : ObservableObject
    {
        private const string AllMatureDeferredCode = "ALL_MATURE_DEFERRED";
        private const int AreYouSureMoveThreshold = 8;
        private const int LargeMoveDistanceThreshold = 5;
        private bool _synchronizingMode;
        private readonly IReadOnlyList<ReorderQueuePreviewItem> _snapshot;

        public ObservableCollection<ReorderQueuePreviewItem> CurrentItems { get; }
        public ObservableCollection<ReorderQueuePreviewItem> ProposedItems { get; } = new();
        public ObservableCollection<ReorderWarning> Warnings { get; } = new();
        public ObservableCollection<ReorderHorizonOption> HorizonOptions { get; }

        [ObservableProperty]
        private ReorderHorizonOption _selectedHorizon;

        [ObservableProperty]
        private bool _isDeferMature = true;

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
        private string _idempotencyKey = Guid.NewGuid().ToString("N");

        [ObservableProperty]
        private string _previewStatus = "Preview has not been generated.";

        public bool IsApproved { get; private set; }

        public event EventHandler<bool>? RequestClose;

        public ReorderMode Mode => IsAllowMature ? ReorderMode.AllowMature : ReorderMode.DeferMature;

        public int? Horizon => SelectedHorizon?.Value;

        public int? MaxMoveConstraint => IsMaxMoveEnabled ? _maxMove : null;

        public IEnumerable<string> WarningMessages => Warnings.Select(w => w.Message);

        public bool HasWarnings => Warnings.Any();

        public string AdjacentRepeatStatus => NoAdjacentRepeat ? "✓" : "✗";

        public ReorderQueueModalViewModel(IEnumerable<ReorderQueuePreviewItem> snapshot, string? basedOnVersion = null)
        {
            _snapshot = snapshot?.ToList() ?? new List<ReorderQueuePreviewItem>();
            CurrentItems = new ObservableCollection<ReorderQueuePreviewItem>(_snapshot);
            _basedOnVersion = basedOnVersion;

            HorizonOptions = new ObservableCollection<ReorderHorizonOption>(new[]
            {
                new ReorderHorizonOption("Entire queue", null),
                new ReorderHorizonOption("Next 10", 10),
                new ReorderHorizonOption("Next 20", 20)
            });
            _selectedHorizon = HorizonOptions.First();

            Warnings.CollectionChanged += Warnings_CollectionChanged;
            UpdateApproveState();
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
                _maxMove = 1;
            }

            OnPropertyChanged(nameof(MaxMoveConstraint));
            InvalidatePreview();
        }

        private void InvalidatePreview()
        {
            if (ProposedItems.Count > 0)
            {
                Log.Information("[REORDER MODAL] Preview invalidated due to configuration change.");
            }

            ProposedItems.Clear();
            if (Warnings.Count > 0)
            {
                Warnings.Clear();
            }

            IsPreviewGenerated = false;
            PreviewStatus = "Preview has not been generated.";
            MovedCount = 0;
            FairnessBefore = 0;
            FairnessAfter = 0;
            NoAdjacentRepeat = true;
            UpdateApproveState();
        }

        [RelayCommand]
        private void GeneratePreview()
        {
            try
            {
                ProposedItems.Clear();
                if (Warnings.Count > 0)
                {
                    Warnings.Clear();
                }

                var defaultReasons = new List<string>
                {
                    "Preview reflects the current queue order. Optimization service is not yet connected."
                };

                if (Horizon.HasValue)
                {
                    defaultReasons.Add($"Preview limited to next {Horizon.Value} items.");
                }

                if (MaxMoveConstraint.HasValue)
                {
                    defaultReasons.Add($"Movement capped at {MaxMoveConstraint.Value} slots per item.");
                }

                foreach (var item in CurrentItems)
                {
                    var reasons = new List<string>(defaultReasons);
                    var isDeferred = Mode == ReorderMode.DeferMature && item.IsMature && !item.IsLocked;
                    if (isDeferred)
                    {
                        reasons.Add("Deferred because mature tracks cannot be scheduled in this mode.");
                    }

                    var proposed = item with
                    {
                        DisplayIndex = item.DisplayIndex,
                        Movement = item.DisplayIndex - item.OriginalIndex,
                        IsDeferred = isDeferred,
                        Reasons = reasons
                    };

                    ProposedItems.Add(proposed);
                }

                PlanId = Guid.NewGuid().ToString("N");
                IdempotencyKey = Guid.NewGuid().ToString("N");
                IsPreviewGenerated = true;
                PreviewStatus = "Preview generated. Review the proposed order before approving.";
                FairnessBefore = 0.0;
                FairnessAfter = 0.0;
                NoAdjacentRepeat = true;
                MovedCount = ProposedItems.Count(item => item.Movement != 0);

                EvaluateWarnings();
                UpdateApproveState();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[REORDER MODAL] Failed to generate preview: {Message}", ex.Message);
                PreviewStatus = $"Failed to generate preview: {ex.Message}";
                ProposedItems.Clear();
                UpdateApproveState();
            }
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

            var requiresConfirmation = MovedCount >= AreYouSureMoveThreshold
                || ProposedItems.Any(item => Math.Abs(item.Movement) > LargeMoveDistanceThreshold)
                || Mode == ReorderMode.AllowMature;

            if (requiresConfirmation)
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

        private void EvaluateWarnings()
        {
            if (Mode == ReorderMode.DeferMature)
            {
                var reorderable = ProposedItems.Where(item => !item.IsLocked).ToList();
                if (reorderable.Count > 0 && reorderable.All(item => item.IsDeferred || item.IsMature))
                {
                    AddWarning(new ReorderWarning(
                        AllMatureDeferredCode,
                        "All mature tracks are deferred. No playable songs remain within the selected horizon."));
                }
            }
        }

        private void AddWarning(ReorderWarning warning)
        {
            if (Warnings.All(existing => existing.Code != warning.Code))
            {
                Warnings.Add(warning);
            }
        }

        private void UpdateApproveState()
        {
            IsApprovalBlocked = Warnings.Any(warning => warning.Code == AllMatureDeferredCode);
            IsApproveEnabled = IsPreviewGenerated && !IsApprovalBlocked;
            OnPropertyChanged(nameof(AdjacentRepeatStatus));
        }
    }
}
