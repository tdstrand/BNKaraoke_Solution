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

        public int? MaxMoveConstraint => IsMaxMoveEnabled ? MaxMove : null;

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
                MaxMove = 1;
                return;
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

                var orderedSnapshot = CurrentItems
                    .OrderBy(item => item.DisplayIndex)
                    .Select((item, index) => item with { DisplayIndex = index })
                    .ToList();

                if (orderedSnapshot.Count == 0)
                {
                    PreviewStatus = "Queue is empty. Nothing to preview.";
                    UpdateApproveState();
                    return;
                }

                var horizonCount = Horizon.HasValue
                    ? Math.Min(Horizon.Value, orderedSnapshot.Count)
                    : orderedSnapshot.Count;

                var defaultReasons = BuildDefaultReasons(horizonCount);

                var reorderableWindow = orderedSnapshot.Take(horizonCount).ToList();
                var reorderableItems = reorderableWindow.Where(item => !item.IsLocked).ToList();

                if (reorderableItems.Count == 0)
                {
                    foreach (var item in orderedSnapshot)
                    {
                        var reasons = BuildReasonsForUnchangedItem(item, defaultReasons, horizonCount);
                        ProposedItems.Add(CreateProposedItem(item, item.DisplayIndex, reasons, isDeferred: false));
                    }

                    FinalizePreviewMetadata(orderedSnapshot, ProposedItems.ToList(), horizonCount);
                    return;
                }

                var remainingCandidates = PrioritizeItems(reorderableItems);

                var proposedOrder = new List<ReorderQueuePreviewItem>(orderedSnapshot.Count);

                for (var displayIndex = 0; displayIndex < orderedSnapshot.Count; displayIndex++)
                {
                    var snapshotItem = orderedSnapshot[displayIndex];

                    if (displayIndex >= horizonCount)
                    {
                        var reasons = BuildReasonsOutsideHorizon(snapshotItem, defaultReasons);
                        proposedOrder.Add(CreateProposedItem(snapshotItem, displayIndex, reasons, isDeferred: false));
                        continue;
                    }

                    if (snapshotItem.IsLocked)
                    {
                        var reasons = BuildReasonsForLockedItem(snapshotItem, defaultReasons);
                        proposedOrder.Add(CreateProposedItem(snapshotItem, displayIndex, reasons, isDeferred: false));
                        continue;
                    }

                    if (remainingCandidates.Count == 0)
                    {
                        var reasons = BuildReasonsForUnchangedItem(snapshotItem, defaultReasons, horizonCount);
                        proposedOrder.Add(CreateProposedItem(snapshotItem, displayIndex, reasons, isDeferred: false));
                        continue;
                    }

                    var previousItem = proposedOrder.LastOrDefault();
                    var candidateIndex = FindNextCandidateIndex(remainingCandidates, previousItem, displayIndex, MaxMoveConstraint);
                    var candidate = remainingCandidates[candidateIndex];
                    remainingCandidates.RemoveAt(candidateIndex);

                    var reasonsForItem = BuildReasonsForAssignment(candidate, previousItem, defaultReasons, displayIndex);
                    var isDeferred = Mode == ReorderMode.DeferMature && candidate.IsMature && !candidate.IsLocked;
                    var proposed = CreateProposedItem(candidate, displayIndex, reasonsForItem, isDeferred);
                    proposedOrder.Add(proposed);
                }

                ProposedItems.Clear();
                foreach (var item in proposedOrder)
                {
                    ProposedItems.Add(item);
                }

                FinalizePreviewMetadata(orderedSnapshot, proposedOrder, horizonCount);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[REORDER MODAL] Failed to generate preview: {Message}", ex.Message);
                PreviewStatus = $"Failed to generate preview: {ex.Message}";
                ProposedItems.Clear();
                UpdateApproveState();
            }
        }

        private List<string> BuildDefaultReasons(int horizonCount)
        {
            var reasons = new List<string>
            {
                "Preview generated using local optimization heuristics."
            };

            if (Horizon.HasValue)
            {
                reasons.Add($"Preview limited to next {Math.Max(1, horizonCount)} items.");
            }

            if (MaxMoveConstraint.HasValue)
            {
                reasons.Add($"Movement capped at {MaxMoveConstraint.Value} slots per item.");
            }

            return reasons;
        }

        private static List<string> BuildReasonsForUnchangedItem(
            ReorderQueuePreviewItem item,
            IReadOnlyCollection<string> defaultReasons,
            int horizonCount)
        {
            var reasons = new List<string>(defaultReasons)
            {
                "No viable alternative slot was identified within the configured horizon."
            };

            if (item.IsLocked)
            {
                reasons.Add("Entry is locked and cannot be moved.");
            }
            else if (item.DisplayIndex >= horizonCount)
            {
                reasons.Add("Entry falls outside the optimization horizon.");
            }

            return reasons;
        }

        private static List<string> BuildReasonsOutsideHorizon(
            ReorderQueuePreviewItem item,
            IReadOnlyCollection<string> defaultReasons)
        {
            var reasons = new List<string>(defaultReasons)
            {
                "Position retained because the item is outside the optimization horizon."
            };

            if (item.IsMature)
            {
                reasons.Add("Mature status respected outside the optimization window.");
            }

            return reasons;
        }

        private static List<string> BuildReasonsForLockedItem(
            ReorderQueuePreviewItem item,
            IReadOnlyCollection<string> defaultReasons)
        {
            var reasons = new List<string>(defaultReasons)
            {
                "Position locked (currently playing or up next)."
            };

            return reasons;
        }

        private List<string> BuildReasonsForAssignment(
            ReorderQueuePreviewItem candidate,
            ReorderQueuePreviewItem? previousItem,
            IReadOnlyCollection<string> defaultReasons,
            int targetIndex)
        {
            var reasons = new List<string>(defaultReasons);

            if (previousItem == null)
            {
                reasons.Add("Starting point selected based on queue order and singer availability.");
            }
            else if (!string.IsNullOrWhiteSpace(previousItem.Requestor) &&
                     !string.IsNullOrWhiteSpace(candidate.Requestor) &&
                     string.Equals(previousItem.Requestor, candidate.Requestor, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add("Kept adjacent to the same singer because no alternative satisfied the constraints.");
            }
            else
            {
                reasons.Add("Position adjusted to avoid back-to-back songs from the same singer.");
            }

            if (targetIndex == candidate.OriginalIndex)
            {
                reasons.Add("Remains in original relative position.");
            }
            else
            {
                reasons.Add($"Moved from position {candidate.OriginalIndex + 1} to {targetIndex + 1} to improve rotation.");
            }

            if (Mode == ReorderMode.DeferMature && candidate.IsMature && !candidate.IsLocked)
            {
                reasons.Add("Scheduled later due to Defer Mature mode.");
            }

            return reasons;
        }

        private static int FindNextCandidateIndex(
            IReadOnlyList<ReorderQueuePreviewItem> candidates,
            ReorderQueuePreviewItem? previousItem,
            int targetIndex,
            int? maxMoveConstraint)
        {
            if (candidates.Count == 0)
            {
                return 0;
            }

            var fallbackIndex = -1;
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (maxMoveConstraint.HasValue && Math.Abs(targetIndex - candidate.OriginalIndex) > maxMoveConstraint.Value)
                {
                    if (fallbackIndex == -1)
                    {
                        fallbackIndex = i;
                    }
                    continue;
                }

                if (previousItem != null &&
                    !string.IsNullOrWhiteSpace(previousItem.Requestor) &&
                    !string.IsNullOrWhiteSpace(candidate.Requestor) &&
                    string.Equals(previousItem.Requestor, candidate.Requestor, StringComparison.OrdinalIgnoreCase))
                {
                    if (fallbackIndex == -1)
                    {
                        fallbackIndex = i;
                    }
                    continue;
                }

                return i;
            }

            if (fallbackIndex != -1)
            {
                return fallbackIndex;
            }

            return 0;
        }

        private List<ReorderQueuePreviewItem> PrioritizeItems(List<ReorderQueuePreviewItem> reorderableItems)
        {
            var sorted = reorderableItems
                .OrderBy(item => item.IsMature && Mode == ReorderMode.DeferMature)
                .ThenBy(item => item.DisplayIndex)
                .ToList();

            return sorted;
        }

        private static ReorderQueuePreviewItem CreateProposedItem(
            ReorderQueuePreviewItem item,
            int targetIndex,
            IReadOnlyList<string> reasons,
            bool isDeferred)
        {
            return item with
            {
                DisplayIndex = targetIndex,
                Movement = targetIndex - item.OriginalIndex,
                IsDeferred = isDeferred,
                Reasons = reasons.ToArray()
            };
        }

        private void FinalizePreviewMetadata(
            IReadOnlyList<ReorderQueuePreviewItem> original,
            IReadOnlyList<ReorderQueuePreviewItem> proposed,
            int horizonCount)
        {
            PlanId = Guid.NewGuid().ToString("N");
            IdempotencyKey = Guid.NewGuid().ToString("N");
            IsPreviewGenerated = true;
            PreviewStatus = "Preview generated. Review the proposed order before approving.";

            FairnessBefore = CalculateFairness(original, horizonCount);
            FairnessAfter = CalculateFairness(proposed, horizonCount);
            NoAdjacentRepeat = CheckAdjacentRepeat(proposed);
            MovedCount = proposed.Count(item => item.Movement != 0);

            EvaluateWarnings();
            UpdateApproveState();
        }

        private static double CalculateFairness(
            IReadOnlyList<ReorderQueuePreviewItem> items,
            int horizonCount)
        {
            if (items.Count == 0 || horizonCount <= 0)
            {
                return 0;
            }

            horizonCount = Math.Min(horizonCount, items.Count);
            var window = items.Take(horizonCount)
                .Select(item => item.Requestor?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.ToUpperInvariant())
                .ToList();

            if (window.Count == 0)
            {
                return 0;
            }

            var unique = window.Distinct().Count();
            return Math.Round(unique / (double)window.Count, 2);
        }

        private static bool CheckAdjacentRepeat(IEnumerable<ReorderQueuePreviewItem> items)
        {
            string? previous = null;
            foreach (var item in items)
            {
                var requestor = item.Requestor?.Trim();
                if (!string.IsNullOrWhiteSpace(previous) &&
                    !string.IsNullOrWhiteSpace(requestor) &&
                    string.Equals(previous, requestor, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(requestor))
                {
                    previous = requestor;
                }
                else
                {
                    previous = null;
                }
            }

            return true;
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

        partial void OnMovedCountChanged(int value)
        {
            UpdateApproveState();
        }

        private void UpdateApproveState()
        {
            IsApprovalBlocked = Warnings.Any(warning => warning.Code == AllMatureDeferredCode);

            var hasMeaningfulChanges = MovedCount > 0;
            if (IsPreviewGenerated && !hasMeaningfulChanges)
            {
                PreviewStatus = "Preview matches the current queue. Reorder items before approving.";
            }

            IsApproveEnabled = IsPreviewGenerated && !IsApprovalBlocked && hasMeaningfulChanges;
            OnPropertyChanged(nameof(AdjacentRepeatStatus));
        }
    }
}
