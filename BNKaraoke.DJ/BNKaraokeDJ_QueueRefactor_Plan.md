Cool, let’s lock this in and turn it into a **Codex-friendly PR plan** that:

* Keeps **visuals unchanged**
* Touches **only BNKaraoke.DJ** (no web changes; API changes later & separate)
* Uses **small, focused PRs** that are at least buildable/testable
* Minimizes merge-conflict risk
* Gives you a **markdown “codex file”** you can reuse to bootstrap new sessions

Below is the plan as a single `.md`-style document you can save (e.g. `BNKaraokeDJ_QueueRefactor_Plan.md`) and also feed to Codex.

---

## BNKaraoke.DJ Queue / AutoPlay Refactor Plan

### 0. Scope & Constraints

* **Project:** `BNKaraoke_Solution`
* **Target project:** `BNKaraoke.DJ` only
* **Do NOT touch:**

  * `bnkaraoke.web`
  * API projects (except in completely separate future PRs)
* **Goal:**
  Fix DJ queue visibility & AutoPlay behavior **without changing visuals**, and align code with these truths:

  1. **SignalR is the truth for the queue backlog**
     (which entries exist, positions, statuses).

  2. **Player (DJ app) is authoritative for “Now Playing”** and must react instantly to Skip/Next.

  3. **AutoPlay is a playback policy**, not a filter that hides queue entries.

  4. **Visibility rules**:

     * Show all songs that:

       * Belong to the current event, and
       * Are **not sung** (`SungAt == null`), and
       * Are **not manually skipped** by the DJ (`WasSkipped == false`).
     * Hide:

       * Sung songs (separate “View Sung Songs” flow)
       * Manually skipped songs (rare, track-problem).
     * Singer status must **not** affect visibility; only playback/AutoPlay.

  5. **Marquee / UpNext rules**:

     * AutoPlay **ON** → UpNext = next AutoPlay-eligible (next “green” singer).
     * AutoPlay **OFF** → UpNext = next visible queue entry in order, regardless of singer status.

---

## Global Technical Context (for Codex)

Files in `BNKaraoke.DJ` that are relevant:

* ViewModel core:

  * `ViewModels/DJScreenViewModel.cs`
  * `ViewModels/DJScreenViewModel.Queue.cs`
  * `ViewModels/DJScreenViewModel.Player.cs`
  * `ViewModels/DJScreenViewModel.Overlays.cs`
  * `ViewModels/DJScreenViewModel.Singers.cs`
* Queue entry VM:

  * `ViewModels/QueueEntryViewModel.cs`
* Overlays:

  * `ViewModels/Overlays/OverlayViewModel.cs`
  * `Services/Playback/NowNextResolver.cs`
* XAML:

  * `Views/DJScreen.xaml` (ListView `QueueItemsListView` bound to `QueueEntriesInternal`)

Key members already present in **master**:

* `ObservableCollection<QueueEntryViewModel> QueueEntriesInternal`
* `Dictionary<int, QueueEntryViewModel> _queueEntryLookup`
* `HashSet<int> _hiddenQueueEntryIds`
* `QueueEntryViewModel` (inherits from `QueueEntry` and adds:

  * `IsReady`, `ShowAsOnHold`, `IsPlayed`, `StatusBrush`, etc.)
* `ApplyQueueRules()` in `DJScreenViewModel.cs`:

  * Currently clears `QueueEntriesInternal` and re-adds entries where `!WasSkipped && SungAt == null`.
* `UpdateEntryVisibility(QueueEntryViewModel entry)` in `DJScreenViewModel.Queue.cs`
* AutoPlay selection in `DJScreenViewModel.Player.cs`:

  * Local `AutoplayEligible(QueueEntryViewModel q)` function
* Overlay wiring in `DJScreenViewModel.Overlays.cs`:

  * `UpdateOverlayState()` → `OverlayViewModel.UpdatePlaybackState(...)`
* `NowNextResolver` in `Services/Playback/NowNextResolver.cs`:

  * `ResolveNow()` / `ResolveUpNext(...)` based on `IsActive`, `IsOnHold`, `Position`, `ReorderMode`.

---

## PR 1 – Document the Rules & Centralize Queue Visibility Predicate

**Goal:**
No or minimal functional change, just:

* Make the design explicit.
* Introduce a single “visibility predicate” used everywhere queue membership is decided.

**Files to touch:**

* `BNKaraoke.DJ/ViewModels/DJScreenViewModel.Queue.cs`
* `BNKaraoke.DJ/ViewModels/DJScreenViewModel.cs`
* New documentation file: `BNKaraoke.DJ/Docs/QueueRules.md` (or `BNKaraoke.DJ/QueueRules.md`)

**Codex Instructions (PR 1):**

1. **Add a small markdown doc for queue rules**

   * Create `BNKaraoke.DJ/Docs/QueueRules.md` with:

     * A brief summary of:

       * SignalR as queue truth (backlog).
       * Player as Now Playing authority.
       * Visibility rules (SungAt / WasSkipped).
       * AutoPlay vs visibility.
       * AutoPlay vs UpNext.
     * This is design documentation only; no code samples required.

2. **Add a comment block near queue-related fields**

   * In `DJScreenViewModel.Queue.cs` (inside the `partial class DJScreenViewModel`), above the fields that track the queue (the dictionary, collection, hidden set), add a concise comment explaining:

     * `_queueEntryLookup` = canonical queue state for current event.
     * `QueueEntriesInternal` = UI-visible queue.
     * `_hiddenQueueEntryIds` = entries not currently in UI (e.g., sung/skipped).
     * Visibility is determined by a single helper method (to be created next).

3. **Introduce a single visibility helper**

   * In `DJScreenViewModel.Queue.cs`, define a private method:

     ```csharp
     // PR 1: Keep behavior equivalent to current ApplyQueueRules logic.
     // For now, visibility is: !WasSkipped && SungAt == null.
     // (We’ll refine semantics later PRs if needed, but this centralizes the rule.)
     private bool IsVisiblyQueued(QueueEntryViewModel entry)
     {
         // null-check etc. and return equivalently:
         // !entry.WasSkipped && entry.SungAt == null;
     }
     ```

     > **Important:** In PR 1, keep `IsVisiblyQueued` equivalent to current `ApplyQueueRules` filter:
     > `!WasSkipped && SungAt == null`
     > so there’s no behavior change, only centralization.

4. **Use `IsVisiblyQueued` inside `ApplyQueueRules`**

   * In `DJScreenViewModel.cs`, update `ApplyQueueRules()` to use `IsVisiblyQueued(entry)` instead of hardcoding `!q.WasSkipped && q.SungAt == null`.
   * Ensure the logic is otherwise unchanged (still clear and then re-add to `QueueEntriesInternal`).

5. **Use `IsVisiblyQueued` in `UpdateEntryVisibility` (non-breaking)**

   * In `DJScreenViewModel.Queue.cs`, adjust `UpdateEntryVisibility(QueueEntryViewModel entry)` to:

     * Early return on null.
     * If `!IsVisiblyQueued(entry)`:

       * Add to `_hiddenQueueEntryIds`.
       * Remove from `QueueEntriesInternal` if present.
       * Return.
     * Else:

       * Remove from `_hiddenQueueEntryIds`.
       * Ensure `entry` is present in `QueueEntriesInternal` in correct position using `InsertQueueEntryOrdered`.

   * This may slightly change *when* skipped entries disappear (immediately vs only after `ApplyQueueRules`), but should be aligned with intent and gives us a single rule going forward.

6. **Keep visuals and public APIs untouched**

   * Do not change XAML.
   * Do not change property names or public methods.
   * Ensure the project builds and the queue still appears (broken behavior may remain but should not be worse).

---

## PR 2 – Make SignalR the Primary Queue Backlog Source (Quarantine `LoadQueueData`)

**Goal:**
Stop “fighting” between SignalR and API pulls. SignalR owns the queue backlog; API `GetQueueAsync` is no longer part of normal operation.

**Files to touch:**

* `BNKaraoke.DJ/ViewModels/DJScreenViewModel.Queue.cs`
* Possibly `DJScreenViewModel.cs` where `ManualRefreshDataAsync` is defined.

**Codex Instructions (PR 2):**

1. **Identify all call sites of `LoadQueueData()`**

   * In `DJScreenViewModel.Queue.cs` and `DJScreenViewModel.cs`, locate every place that calls `LoadQueueData()`:

     * Reorder suggestions apply.
     * Drag-and-drop reorder completion.
     * Manual refresh.
     * Any others.

2. **Quarantine `LoadQueueData`**

   * Update comments on `LoadQueueData()` to clearly state it is:

     > “Emergency / debug resync using API snapshot. Normal queue flow should rely on SignalR events (`HandleInitialQueue`, `HandleQueueUpdated`, `HandleQueueReorderApplied`).”

   * Change calls so that:

     * **Normal operations** (like reorder suggestions apply, drag-drop reorder, skip, etc.) **do not** trigger `LoadQueueData`.
     * Only a dedicated “Manual refresh” action (if present) can call `LoadQueueData`. Optionally gate this behind a debug flag or advanced menu text so DJs don’t hit it accidentally.

3. **Ensure normal queue mutations are SignalR-driven**

   * Confirm that for:

     * Initial connection → queue built via `HandleInitialQueue` → `OnInitialQueue`.
     * Reorder suggestions apply → API call → queue changed via `HandleQueueReorderApplied`.
     * Generic queue updates → `HandleQueueUpdated`.
   * After PR 2, **no normal control path** should clear and rebuild the queue from `GetQueueAsync` except optional manual refresh.

4. **Keep behavior testable**

   * After PR 2, the queue may still be functionally broken for your regression, but:

     * The project must build.
     * Typical flows (join event, see some queue entries, reorder, etc.) should still run.
   * Visuals remain unchanged.

---

## PR 3 – Codify DJ Player Authority & Optimistic Local Updates for Skip/Sung

**Goal:**
Make DJ Player the owner of “Now Playing” and allow instant local Skip/Next/Song-End actions, with SignalR used as async confirmation.

**Files to touch:**

* `BNKaraoke.DJ/ViewModels/DJScreenViewModel.Player.cs`
* `BNKaraoke.DJ/ViewModels/DJScreenViewModel.Queue.cs` (if needed for helper reuse)
* Potentially `DJScreenViewModel.cs` if there’s a central place that tracks `PlayingQueueEntry`.

**Codex Instructions (PR 3):**

1. **Document the “Now Playing authority” invariant**

   * In `DJScreenViewModel.Player.cs`, near `PlayingQueueEntry` and the main play/skip logic, add a clear comment stating:

     * `PlayingQueueEntry` is owned by the DJ app (Player).
     * Only the Player can change which song is currently “Now Playing”.
     * SignalR events that disagree with the local `PlayingQueueEntry` should be treated as confirmations or warnings, not commands to forcibly swap the current song.

2. **Ensure Skip and natural Song-End apply local state first**

   For both of these paths (already present in code):

   * **Skip:**

     * When the DJ clicks “Skip”:

       * Immediately set the current `PlayingQueueEntry`’s fields to match a manual skip:

         * `WasSkipped = true`
         * Update any `Status` or related fields as they are currently used to represent “skipped”.
       * Re-evaluate visibility via `IsVisiblyQueued(entry)`:

         * The entry should become invisible (removed from `QueueEntriesInternal`).
       * Immediately choose the next entry:

         * If AutoPlay ON: the next AutoPlay-eligible (“green”) entry.
         * If AutoPlay OFF: the next visible entry by queue order.
       * Immediately set `PlayingQueueEntry` to this new entry and start playback.
       * Call overlay update so marquee reflects new Now/Next instantly.
       * Fire an API call to “Skip queueId X” (async, non-blocking).

   * **Song end (natural finish):**

     * When playback completes:

       * Immediately set `SungAt = DateTime.UtcNow` (or appropriate timestamp) and update `Status` to “sung”.
       * Re-evaluate visibility via `IsVisiblyQueued` (should now hide).
       * Immediately select next entry (same logic as Skip).
       * Start playback and update overlay.
       * Fire an API call to “mark queueId X as sung” (if such call exists; if not, this part can be stubbed/left for API PR).

3. **Reconcile with SignalR messages**

   * In the SignalR handlers (`HandleQueueUpdated`, etc.), when updates arrive for queue entries that are currently Now Playing:

     * If the incoming status matches what the Player already did (e.g., entry is marked as skipped or sung):

       * Treat it as confirmation; do not re-run any selection logic.
     * If the incoming update tries to set a different Now Playing track than `PlayingQueueEntry`:

       * Prefer the local `PlayingQueueEntry`.
       * Log a warning but do **not** override the local playback.

   * This step can be done primarily via logging and simple guards, not a full-blown conflict resolution system.

4. **Keep behavior verifiable**

   * After PR 3, you should be able to:

     * Join an event.
     * Start a song.
     * Hit Skip and observe that:

       * The player moves immediately to the next song.
       * The skipped song disappears from the visible queue.
     * Let a song finish naturally with similar behavior.

---

## PR 4 – Align Marquee/Overlay “Up Next” with AutoPlay & Local Now

**Goal:**
Make the marquee/overlay show UpNext based on the same logic the Player uses:

* AutoPlay ON → next green singer.
* AutoPlay OFF → next visible queue entry, ignoring singer status.

**Files to touch:**

* `BNKaraoke.DJ/ViewModels/DJScreenViewModel.Overlays.cs`
* `BNKaraoke.DJ/Services/Playback/NowNextResolver.cs`
* Possibly `OverlayViewModel` if needed to accept more explicit inputs.

**Codex Instructions (PR 4):**

1. **Stop recomputing Now/Next independently in the overlay layer**

   * Currently, `UpdateOverlayState()` in `DJScreenViewModel.Overlays.cs` builds:

     * `queueSnapshot = QueueEntriesInternal?.Cast<QueueEntry>().ToList()`
     * Passes `queueSnapshot`, `PlayingQueueEntry`, `CurrentEvent` to `OverlayViewModel.UpdatePlaybackState(...)`, which internally uses `NowNextResolver`.

   * The goal is to ensure UpNext follows **Player logic**, not a separate set of rules.

2. **Introduce a “GetUpNextForOverlay” helper in DJScreenViewModel**

   * In `DJScreenViewModel` (probably `Overlays.cs` or a shared place), define a helper method that:

     * Uses:

       * `PlayingQueueEntry`
       * `QueueEntriesInternal`
       * `IsAutoPlayEnabled`
       * Singer state (`IsReady`, `ShowAsOnHold`, etc.)
     * When AutoPlay ON:

       * Returns the same candidate that AutoPlay would choose as “next” (next green singer after Now in position order, with wrap-around if needed).
     * When AutoPlay OFF:

       * Returns the next visible entry after `PlayingQueueEntry` by `Position`, ignoring singer status, but still respecting queue visibility rules (e.g., no sung/skipped).

   * This helper should reuse whatever logic exists in `DJScreenViewModel.Player.cs` for candidate selection to avoid divergence.

3. **Wire overlay to this helper**

   * Update `UpdateOverlayState()` to:

     * Use `PlayingQueueEntry` as Now.
     * Use `GetUpNextForOverlay()` as UpNext.

   * You can maintain `NowNextResolver` for secondary cases or refactor it to accept explicit Now/Next candidates rather than re-deriving them.

4. **Respect maturity / OnHold rules where appropriate**

   * When AutoPlay is ON, ensure the UpNext helper respects the same maturity and “on hold” logic as AutoPlay.
   * When AutoPlay is OFF, you may still honor maturity settings (*your call*, but aim to keep behavior close to current UX unless specified otherwise).

5. **Keep overlay visuals unchanged**

   * Do not modify XAML or the visual design of the overlay.
   * Only adjust the data inputs (Now / UpNext) to the overlay viewmodel.

---

## How to Use This Plan

* Save this file as something like:
  `BNKaraokeDJ_QueueRefactor_Plan.md`
* For each PR:

  * Copy the relevant **“Codex Instructions (PR X)”** section.
  * Paste it into your Codex session, along with:

    * Repo link: `https://github.com/tdstrand/BNKaraoke_Solution`
    * Confirmation that `master` is up to date.
  * Let Codex generate the code changes.
  * Run the app and do a quick smoke test:

    * Join event, see queue, try basic playback actions.

If you want, next step we can zoom in on **PR 1** and I can rewrite its Codex block into a very strict, bullet-by-bullet “do this, don’t do that” version tailored exactly to your coding style and logging conventions.
