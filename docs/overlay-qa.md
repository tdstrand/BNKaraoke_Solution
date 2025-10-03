# Overlay QA Checklist

## Environment
- Launch BNKaraoke DJ on a system with access to 1080p and 4K displays.
- Ensure overlay output window is visible on the target display.

## Automated Test Coverage
- `dotnet test BNKaraoke.DJ.Tests/BNKaraoke.DJ.Tests.csproj`
  - Confirms overlay template rendering, queue resolution, marquee behavior, layout sizing, and settings persistence.

## Manual Verification Steps
1. **Display Validation**
   - Verify overlay output renders correctly at **1080p** and **4K** resolutions.
   - Toggle the overlay on/off via the DJ console controls and confirm the video feed reacts immediately.
2. **Template Editing**
   - Open overlay settings and edit both top and bottom templates.
   - Confirm token placeholders (e.g., `{Brand}`, `{Requestor}`, `{UpNextSong}`) resolve with live queue data on the overlay.
3. **Deferred Mature Handling**
   - Switch the "Defer Mature" option off/on and ensure the "Up Next" messaging reflects the policy change in real time.
4. **Marquee Behavior**
   - Queue a track with an intentionally long string (artist/title/requestor) and confirm the marquee scrolls seamlessly without stutter or tearing.
   - Observe frame rate/performance counters or OS metrics for at least 60 seconds to ensure steady performance.
5. **Settings Persistence**
   - Modify marquee speed, spacer width, and crossfade values; restart the application and confirm the settings persist.

## Execution Log
- _Document date, tester, and any issues found each time the checklist is executed prior to release._
