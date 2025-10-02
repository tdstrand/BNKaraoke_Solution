# DJ Client Manual QA Guide

The following checklist covers the DJ screen workflows that depend on the new queue reorder SignalR broadcast and modal-driven apply flow. Complete these steps after deploying the updated API and DJ client so that regressions in drag-and-drop, previews, and plan application are caught early.

## Pre-requisites

1. Launch the BNKaraoke API with the new queue reorder endpoints enabled.
2. Start the DJ client and authenticate as a user with the **Karaoke DJ** role.
3. Ensure at least three live queue entries exist for an active event (include a mature song to validate policy handling).
4. Open a second browser session (or device) logged in as the same event to confirm SignalR fan-out.

## Preview workflow

1. In the DJ queue, drag a song from position 3 to position 2.
2. Confirm the preview modal opens automatically with the reordered list.
3. Validate the modal highlights:
   - Locked items at the top are marked "Locked" and immovable.
   - Mature requests show the "Deferred due to mature content policy" badge when the defer policy is active.
   - Movement indicators (e.g., +2 / -1) match the drag action.
4. Switch to the **Warnings** tab and verify any horizon or policy warnings render.
5. Close and reopen the modal via the **Preview Changes** button to ensure state resets correctly.

## Apply workflow and versioning

1. With the preview modal open, note the fairness metrics and move count.
2. Click **Apply Reorder** and wait for the success toast.
3. Verify the queue updates immediately in both browser sessions.
4. Inspect the audit trail (Developer Tools → Network → `queue/reorder/apply`) to confirm a 200 response and the version hash in the payload.
5. Attempt a second apply without previewing; ensure the UI blocks the action and surfaces "No pending changes".
6. Simulate a version mismatch by reloading the DJ screen in the secondary session, performing a different reorder, and applying it. Back in the first session, try to apply the stale modal—confirm a conflict toast instructs to refresh.

## SignalR broadcast validation

1. Trigger another preview/apply cycle.
2. In the secondary session, observe the queue reordering without a manual refresh.
3. Check browser dev tools → Console for `queue/reorder_applied` logs (the client should log the payload for debugging).
4. If available, tail the API logs to ensure a `queue/reorder_applied` message was emitted for the event group.

## Drag-and-drop regression checks

1. Drag a request within the locked head segment—verify it snaps back and shows a tooltip explaining the lock.
2. Drag a mature request forward while the defer policy is active—ensure it returns to its original slot.
3. Toggle the defer policy to "Allow" and confirm the same mature request can now advance.
4. Perform rapid consecutive drags; check that only the latest preview plan remains cached (modal should show updated plan ID).

## Modal UX smoke tests

1. Resize the browser to tablet width; ensure the modal remains scrollable and action buttons are accessible.
2. Use the keyboard to navigate (Tab/Shift+Tab) through modal controls and activate **Apply** with the Enter key.
3. Hit the **Cancel** button and verify no plan remains active (Preview button should be enabled but no plan badge shown).
4. Attempt to close the modal via the Escape key and confirm unsaved changes warning appears if applicable.

## Post-apply validation

1. Check the audit log table (if exposed in the UI) for matching PREVIEW and APPLY records with consistent plan IDs.
2. Confirm that queue metrics (fairness score, move count) in the modal summary match the audit details.
3. Verify the queue reorder banner or badge clears once the apply completes.
4. Repeat the entire flow with an empty queue to ensure the API surfaces "No songs available" and the UI handles the 422 gracefully.

Document the results of each step, including screenshots of discrepancies, so the team can act quickly on regressions.
