# BNKaraoke.DJ – SignalR Contracts (Discovery)

## Hub
- Class: `BNKaraoke.Api.Hubs.KaraokeDJHub`
- Route: `/hubs/karaoke-dj`

## Client Push Events (names, payloads)
- `InitialQueue`: `System.Collections.Generic.List<BNKaraoke.Api.Dtos.EventQueueDto>` (full queue snapshot per event)
- `InitialSingers`: `System.Collections.Generic.List<BNKaraoke.Api.Dtos.DJSingerDto>` (current singer roster)
- `Connected`: `string` (the caller's SignalR connectionId)
- `SingerStatusUpdated`: anonymous object `{ userName: string, eventId: int, displayName?: string, isLoggedIn: bool, isJoined: bool, isOnBreak: bool }`
- `QueueUpdated`: anonymous object `{ data: <varies>, action: string }` where `data` is either a `BNKaraoke.Api.Dtos.EventQueueDto`, a reordered collection (`System.Collections.Generic.List<BNKaraoke.Api.Dtos.EventQueueDto>`), a queue identifier (`int`), or contextual metadata depending on the broadcast source
- `QueuePlaying`: anonymous object `{ QueueId: int, EventId: int, YouTubeUrl?: string }`
- `queue/reorder_applied`: `BNKaraoke.Api.Controllers.DJController.QueueReorderAppliedSignal` (record fields: `int EventId`, `string Version`, `string Mode`, `BNKaraoke.Api.Contracts.QueueReorder.QueueReorderSummaryDto Metrics`, `System.Collections.Generic.IReadOnlyList<BNKaraoke.Api.Controllers.DJController.QueueReorderOrderItem> Order`, `System.Collections.Generic.IReadOnlyList<int> MovedQueueIds`)

## Initial-State Mechanism
- Startup pushes: `InitialQueue`, `InitialSingers`, `Connected`
- RequestInitialState: absent (hub exposes only `OnConnectedAsync`, `OnDisconnectedAsync`, `UpdateSingerStatus`, `UpdateQueue`, `QueuePlaying`, and `JoinEventGroup`)
  - Signature: n/a
  - Sends: n/a

## Update Events (live changes)
- `SingerStatusUpdated`: anonymous object `{ userName: string, eventId: int, displayName?: string, isLoggedIn: bool, isJoined: bool, isOnBreak: bool }`
- `QueueUpdated`: anonymous object `{ data: <varies>, action: string }` (actions observed: `Added`, `Playing`, `Skipped`, `Reset`, `OnHold`, `Eligible`, `Held`, `SingersUpdated`, `Reordered`, `EventStarted`, `EventEnded`, status-specific notifications, etc.)
- `QueuePlaying`: anonymous object `{ QueueId: int, EventId: int, YouTubeUrl?: string }`
- `queue/reorder_applied`: `BNKaraoke.Api.Controllers.DJController.QueueReorderAppliedSignal`

## Client Join Semantics
- Query string: `?eventId=<value>` is required; the hub inspects this on connect to add the caller to `Event_{eventId}` and send the initial state.
- Explicit join call required on connect/reconnect: optional but supported — `JoinEventGroup(int eventId)` adds the caller to `Event_{eventId}` with retry logic. The DJ client invokes it after establishing the connection.

## DJ Client Touchpoints
- Hub URL construction: `BNKaraoke.DJ.Services.SignalRService.StartAsync` builds `{ApiUrl}/hubs/karaoke-dj?eventId={eventId}`.
- Current handler registrations found: `QueueUpdated`, `SingerStatusUpdated`, `queue/reorder_applied`, `InitialQueue`, `InitialSingers`, plus reconnect callbacks that try to `RequestInitialState`.
- Any duplicate-subscription risks noted: No. The service builds a new `HubConnection` per `StartAsync` invocation, so handler registrations are re-created on a fresh connection instead of stacking on an existing instance.
