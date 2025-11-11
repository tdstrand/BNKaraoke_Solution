# DJ Queue Visibility Rules

- `_queueEntryLookup` holds the canonical in-memory queue state for the current event.
- `QueueEntriesInternal` is the observable collection bound to the DJ queue UI.
- Queue entries are currently visible when both conditions are true:
  - `SungAt == null`
  - `WasSkipped == false`
- Singer status does not control visibility; only the queue entry's song state determines whether it appears in the DJ queue.
