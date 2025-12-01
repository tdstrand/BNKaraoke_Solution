# BNKaraoke Bug Fix Prompts -- Per‑Bug, Fully Self‑Contained (Final Version)

This file contains **exactly what you requested**:\
✔ Each bug has **its own complete prompt**\
✔ Each prompt includes ALL required instructions\
✔ Correct model (Mini or Max) embedded **inside each bug**\
✔ No triage plan included\
✔ Fully ready to copy/paste into VS Code Codex\
✔ One prompt = one fix

------------------------------------------------------------------------

# 🛠 How to Use This File in VS Code

For each bug:\
1. Open the file(s) listed in the bug block.\
2. Copy its "Full Codex Prompt."\
3. Switch VS Code Codex to the recommended model.\
4. Paste and run.\
5. Apply patches.

------------------------------------------------------------------------

# ✅ BUG‑01 -- Duplicate queue entries per event

**Model:** 5.1 **Max**

### 🔧 Full Codex Prompt (Max)

    You are fixing BUG‑01: Duplicate queue entries per event.

    MODEL: Use 5.1 MAX.

    FILES TO MODIFY:
    - API server queue-add endpoint
    - EventQueues DB migration / constraint
    - Services/ApiService.cs (AddSongAsync)

    INSTRUCTIONS:
    - Only modify the files listed.
    - No project-wide refactors.
    - Do not alter unrelated DTOs or endpoints.
    - Show updated code for each file separately.

    TASK:
    1. Add a database uniqueness constraint or partial unique index on (EventId, SongId) for ACTIVE queue rows.
    2. Update the API add-song logic: check for an active row before insert.
    3. If a duplicate exists, return HTTP 409 with a clear message.
    4. Update AddSongAsync to surface the conflict to DJ + Web clients.

    OUTPUT:
    - Updated code blocks for each modified file.
    - Database migration or ALTER script if needed.

------------------------------------------------------------------------

# ✅ BUG‑02 -- Request limit counts finished songs

**Model:** 5.1 **Max**

### 🔧 Full Codex Prompt (Max)

    You are fixing BUG‑02: Request limit counts finished songs.

    MODEL: Use 5.1 MAX.

    FILES:
    - API queue-add flow
    - Models/EventDto.cs
    - Web request form + server validation

    INSTRUCTIONS:
    - Only modify listed files.
    - Keep code changes tightly scoped.
    - Show updated code per file.

    TASK:
    1. Modify limit calculation to count ONLY active rows:
       pending, now-playing, on-hold.
    2. Exclude skipped, sung, completed, or archived.
    3. Update any cached/derived counts to use active rows.
    4. Update Web/DJ forms to reflect the active-count-based rule.
    5. Refresh view after song completion.

    OUTPUT:
    - Updated code for each changed file.

------------------------------------------------------------------------

# ✅ BUG‑03 -- No master volume default between songs

**Model:** 5.1 **Mini**

### 🔧 Full Codex Prompt (Mini)

    You are fixing BUG‑03: No persisted master volume between songs.

    MODEL: Use 5.1 MINI.

    FILES:
    - DJScreen.xaml
    - DJScreenViewModel.Player.cs
    - DjSettings.cs
    - SettingsService.cs

    INSTRUCTIONS:
    - Modify only the listed files.
    - Keep UI changes local to DJScreen.xaml.
    - Keep logic changes local to Player.cs and SettingsService.
    - Show updated code for each file.

    TASK:
    1. Add a "Master Volume" slider to DJScreen.xaml.
    2. Bind it to a new MasterVolume property.
    3. Persist/load MasterVolume through SettingsService + DjSettings.
    4. Initialize playback volumes (_baseVolume, _preFadeVolume) from MasterVolume.
    5. Ensure mute/unmute + fades restore to MasterVolume.

    OUTPUT:
    - Updated code for all modified files.

------------------------------------------------------------------------

# ✅ BUG‑04 -- Hard to select/edit existing events

**Model:** 5.1 **Max**

### 🔧 Full Codex Prompt (Max)

    You are fixing BUG‑04: Event selection and editing is too limited.

    MODEL: Use 5.1 MAX.

    FILES:
    - EventSelectorWindow.xaml
    - DJScreenViewModel.Header.cs
    - ApiService.cs

    INSTRUCTIONS:
    - Only modify these files unless dependencies require otherwise.
    - Keep scope to event browsing/editing improvements.
    - Output updated code per file.

    TASK:
    1. Replace the simple event dropdown with a management dialog:
       list, search, sort, open/edit.
    2. Add necessary API calls in ApiService to fetch full metadata.
    3. Add a header button to launch the new management dialog.
    4. Refresh event list after edits.

    OUTPUT:
    - All updated XAML + ViewModel + API service code.

------------------------------------------------------------------------

# ✅ BUG‑05 -- Event time incorrectly required

**Model:** 5.1 **Mini**

### 🔧 Full Codex Prompt (Mini)

    Fix BUG‑05: Event time incorrectly required.

    MODEL: Use 5.1 MINI.

    FILES:
    - API DTO + validator
    - EventDto.cs
    - Web event admin form

    INSTRUCTIONS:
    - Only modify listed files.
    - Keep the changes isolated to optional-time behavior.
    - Show updated code for each file.

    TASK:
    1. Make start/end time nullable in API validator + DTO.
    2. Remove "required" flags for time in the Web admin form.
    3. Update DTO parsing to handle null.
    4. Update UI display logic to handle when times are missing.

    OUTPUT:
    - Code updates for DTO, validator, and Web form.

------------------------------------------------------------------------

# ✅ BUG‑06 -- Cannot remove queue item without logging a skip

**Model:** 5.1 **Mini**

### 🔧 Full Codex Prompt (Mini)

    Fix BUG‑06: Remove queue entry WITHOUT marking skip/sung.

    MODEL: Use 5.1 MINI.

    FILES:
    - DJScreenViewModel.Queue.cs
    - DJScreen.xaml
    - ApiService.cs

    INSTRUCTIONS:
    - Modify only these files.
    - Stay focused on adding delete behavior.
    - Output updated code for each file.

    TASK:
    1. Add a dedicated "delete queue entry" API endpoint.
    2. Update RemoveSelected to call delete endpoint, not skip logic.
    3. Re-sync ordering after deletion.
    4. Add a right-click context menu: “Remove Song from Queue”.
    5. Add confirmation dialog in UI.

    OUTPUT:
    - Updated ViewModel, XAML, and ApiService code.

------------------------------------------------------------------------

# ✅ BUG‑07 -- Audio not pinned to selected device

**Model:** 5.1 **Max**

### 🔧 Full Codex Prompt (Max)

    Fix BUG‑07: Audio output not pinned to selected device.

    MODEL: Use 5.1 MAX.

    FILES:
    - VideoPlayerWindow.xaml.cs
    - SettingsWindowViewModel.cs
    - SettingsService.cs

    INSTRUCTIONS:
    - Only modify listed files.
    - Keep changes limited to audio device selection logic.
    - Show updated code per file.

    TASK:
    1. Persist the actual VLC device ID into AudioOutputDeviceId.
    2. Rebind to saved device ID on startup or device change.
    3. Present clear UI state: "Locked to device" vs "Windows default".
    4. Warn user if device missing rather than silently switching.

    OUTPUT:
    - Updated code for all affected files.

------------------------------------------------------------------------

# ✅ BUG‑08 -- Fade‑out button unreliable

**Model:** 5.1 **Mini**

### 🔧 Full Codex Prompt (Mini)

    Fix BUG‑08: Fade-out timing problems.

    MODEL: Use 5.1 MINI.

    FILES:
    - DJScreenViewModel.Player.cs
    - DJScreen.xaml

    INSTRUCTIONS:
    - Modify only these files.
    - Keep logic changes localized to fade flow.
    - Show updated code per file.

    TASK:
    1. Allow manual fade whenever a song is active.
    2. Clamp fade start time if player state lags.
    3. Prevent _fadeStartTimeSeconds from resetting during active fade.
    4. Add a UI indicator for “fade armed”.

    OUTPUT:
    - Updated ViewModel + XAML code.

------------------------------------------------------------------------

# ✅ BUG‑09 -- Cannot reset a song's video back to pending

**Model:** 5.1 **Max**

### 🔧 Full Codex Prompt (Max)

    Fix BUG‑09: Cannot reset a song’s video to pending.

    MODEL: Use 5.1 MAX.

    FILES:
    - API song controller
    - ApiService.cs
    - CacheSyncService.cs
    - CacheManagerViewModel.cs

    INSTRUCTIONS:
    - Only modify listed files.
    - Keep reset workflow clean and predictable.
    - Output updated code per file.

    TASK:
    1. Add API endpoint to reset video → pending.
    2. Clear analysis fields + delete server mp4 cache.
    3. ApiService: add call for reset + cache delete.
    4. Web Admin: surface “Reset to Pending” action.
    5. DJ cache: purge local version + prompt re-download.

    OUTPUT:
    - All updated server, service, and UI code.

------------------------------------------------------------------------

# End of File
