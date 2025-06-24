<#
    Syncreposod.ps1

    This script performs an initial sync of the repository (source) to your OneDrive folder (destination)
    using robocopy with exclusion switches for unimportant directories and file types.
    
    Then, it sets up a FileSystemWatcher to monitor for changes. Only changes in the important directories
    (listed in $includeDirectories) will trigger another sync.
    
    The script runs continuously until you press "q" to quit.
#>

param(
    # Source path: defaults to the folder that contains this script (repository root)
    [string]$SourcePath = "C:\Users\tstra\source\repos\BNKaraoke\",
    # Destination path for OneDrive sync – update <YourUserName> as needed.
    [string]$DestinationPath = "C:\Users\tstra\OneDrive\BNKaraoke"
)

# --------------------
# Exclusions for robocopy
# --------------------
# These directories are not important to sync.
$excludeDirectories = @(
    "bin",
    "obj",
    ".git",
    "node_modules",
    "packages",
    "TestResults",
    ".vs"  # Visual Studio folder, if present.
)


# File patterns not important to sync.
$excludeFilePatterns = @(
    "*.user",
    "*.suo",
    "*.tmp",
    "*.bin",  # Excludes binary files that aren't useful.
    "*.pdb",
    "*.exe"
)


# --------------------
# Inclusions for monitoring:
# --------------------
# Only changes that occur in these directories are considered important.
$includeDirectories = @(
    "BNKaraoke.Api",
    "BNKaraoke.DJ",
    "bnkaraoke.web"
)

# --------------------
# Function: Sync-Files
# --------------------
function Sync-Files {
    Write-Host "Syncing from '$SourcePath' to '$DestinationPath'..." -ForegroundColor Yellow

    # Build up an array of arguments for exclusion.
    $robocopyArgs = @()
    $robocopyArgs += "/MIR"  # Mirror the source in the destination.
    $robocopyArgs += "/XD"
    $robocopyArgs += $excludeDirectories
    $robocopyArgs += "/XF"
    $robocopyArgs += $excludeFilePatterns

    # Call robocopy with the argument array. Each element is passed as a separate parameter.
    robocopy $SourcePath $DestinationPath $robocopyArgs

    Write-Host "Sync complete at $(Get-Date)" -ForegroundColor Green
}

# --------------------
# Initial Sync
# --------------------
Write-Host "Performing initial sync..." -ForegroundColor Cyan
Sync-Files
Write-Host "Initial sync complete. Monitoring changes. Press 'q' to quit." -ForegroundColor Cyan

# --------------------
# Set up FileSystemWatcher for the entire source.
# --------------------
$watcher = New-Object System.IO.FileSystemWatcher
$watcher.Path = $SourcePath
$watcher.IncludeSubdirectories = $true
$watcher.EnableRaisingEvents = $true

# Define the action to take when an event occurs.
$action = {
    $filepath = $Event.SourceEventArgs.FullPath
    $shouldSync = $false

    # Check if the changed file is located in one of the important directories.
    foreach ($inc in $includeDirectories) {
        if ($filepath -like "*\$inc\*") {
            $shouldSync = $true
            break
        }
    }
    if ($shouldSync) {
        # Delay briefly to let rapid events settle.
        Start-Sleep -Seconds 2
        Write-Host "Relevant change detected in: $filepath at $(Get-Date). Running sync..." -ForegroundColor Magenta
        Sync-Files
    }
    else {
        Write-Host "Ignored change in non-important file: $filepath" -ForegroundColor DarkGray
    }
}

# --------------------
# Register events for Created, Changed, Deleted, and Renamed.
# --------------------
$createdEvent = Register-ObjectEvent -InputObject $watcher -EventName Created -Action $action
$changedEvent = Register-ObjectEvent -InputObject $watcher -EventName Changed -Action $action
$deletedEvent = Register-ObjectEvent -InputObject $watcher -EventName Deleted -Action $action
$renamedEvent = Register-ObjectEvent -InputObject $watcher -EventName Renamed -Action $action

# --------------------
# Continuous monitoring loop. Press "q" to exit.
# --------------------
while ($true) {
    $key = $host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    if ($key.Character -eq 'q') {
        Write-Host "Quit command received. Exiting monitoring loop..." -ForegroundColor Cyan
        break
    }
}

# Cleanup the registered event handlers.
Unregister-Event -SourceIdentifier $createdEvent.Name
Unregister-Event -SourceIdentifier $changedEvent.Name
Unregister-Event -SourceIdentifier $deletedEvent.Name
Unregister-Event -SourceIdentifier $renamedEvent.Name

Write-Host "Sync monitoring stopped."