# Define paths
$reposPath = "C:\Users\tstra\source\repos\BNKaraoke"
$oneDrivePath = "C:\Users\tstra\OneDrive\BNKaraoke"
$logPath = "C:\Users\tstra\Logs\MonitorAndSyncRepos_$(Get-Date -Format 'yyyyMMdd_HHmmss').log"

# Define exclusions (similar to .gitignore)
$excludedFolders = @(
    "bin",
    "obj",
    ".vs",
    "packages",
    "node_modules",
    "dist",
    "build",
    "out",
    ".next"
)

$excludedExtensions = @(
    ".dll",
    ".exe",
    ".pdb",
    ".user",
    ".suo"
)

# Create log directory if it doesn't exist
$logDir = Split-Path $logPath -Parent
if (-not (Test-Path $logDir)) {
    New-Item -Path $logDir -ItemType Directory
}

# Function to log messages
function Write-Log {
    param ($Message)
    $logMessage = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss'): $Message"
    Write-Host $logMessage
    Add-Content -Path $logPath -Value $logMessage
}

# Function to check if a path should be excluded
function Test-ExcludedPath {
    param ($Path)

    # Check for excluded folders
    foreach ($folder in $excludedFolders) {
        if ($Path -match [regex]::Escape($folder)) {
            return $true
        }
    }

    # Check for excluded extensions
    $extension = [System.IO.Path]::GetExtension($Path).ToLower()
    if ($excludedExtensions -contains $extension) {
        return $true
    }

    return $false
}

try {
    Write-Log "Starting sync process for $reposPath to $oneDrivePath"

    # Verify source and destination paths
    if (-not (Test-Path $reposPath)) {
        throw "Source path $reposPath does not exist."
    }
    Write-Log "Source path $reposPath exists."

    if (-not (Test-Path $oneDrivePath)) {
        New-Item -Path $oneDrivePath -ItemType Directory
        Write-Log "Created destination folder $oneDrivePath"
    }
    else {
        Write-Log "Destination folder $oneDrivePath already exists."
    }

    # Initial Sync: Copy all existing files (excluding build artifacts)
    Write-Log "Performing initial sync of existing files..."
    $filesToSync = Get-ChildItem -Path $reposPath -Recurse -ErrorAction Stop | Where-Object {
        -not (Test-ExcludedPath -Path $_.FullName)
    }

    if (-not $filesToSync) {
        Write-Log "No files to sync after applying exclusions."
    }
    else {
        Write-Log "Found $($filesToSync.Count) files to sync."
        foreach ($file in $filesToSync) {
            try {
                $relativePath = $file.FullName.Substring($reposPath.Length + 1)
                $destinationPath = Join-Path $oneDrivePath $relativePath

                # Create the destination directory if it doesn't exist
                $destinationDir = Split-Path $destinationPath -Parent
                if (-not (Test-Path $destinationDir)) {
                    New-Item -Path $destinationDir -ItemType Directory -Force
                    Write-Log "Created directory $destinationDir"
                }

                # Copy the file to OneDrive
                Copy-Item -Path $file.FullName -Destination $destinationPath -Force -ErrorAction Stop
                Write-Log "Initial sync: Copied $($file.FullName) to $destinationPath"
            }
            catch {
                Write-Log "Error copying $($file.FullName) during initial sync: $_"
            }
        }
        Write-Log "Initial sync completed."
    }

    # Log the project directories being monitored
    $projectDirs = Get-ChildItem -Path $reposPath -Directory | Where-Object { $_.Name -in @("BNKaraoke.Api", "BNKaraoke.DJ", "bnkaraoke.web") }
    foreach ($dir in $projectDirs) {
        Write-Log "Monitoring project directory: $($dir.FullName)"
    }

    # Create a FileSystemWatcher object
    $watcher = New-Object IO.FileSystemWatcher $reposPath, "*.*" -Property @{
        IncludeSubdirectories = $true
        NotifyFilter = [IO.NotifyFilters]'FileName, LastWrite, DirectoryName'
    }

    # Define the action to take when a change is detected
    $action = {
        $path = $Event.SourceEventArgs.FullPath
        $changeType = $Event.SourceEventArgs.ChangeType

        # Check if the path should be excluded
        if (Test-ExcludedPath -Path $path) {
            Write-Log "Skipping $changeType event for $path (excluded path)"
            return
        }

        $relativePath = $path.Substring($reposPath.Length + 1)
        $destinationPath = Join-Path $oneDrivePath $relativePath

        Write-Log "Detected $changeType event for $path"

        try {
            if ($changeType -eq "Deleted") {
                # Remove the file or directory from OneDrive
                if (Test-Path $destinationPath) {
                    Remove-Item -Path $destinationPath -Recurse -Force
                    Write-Log "Deleted $destinationPath"
                }
            }
            else {
                # Create the destination directory if it doesn't exist
                $destinationDir = Split-Path $destinationPath -Parent
                if (-not (Test-Path $destinationDir)) {
                    New-Item -Path $destinationDir -ItemType Directory -Force
                    Write-Log "Created directory $destinationDir"
                }

                # Copy the changed file or directory to OneDrive
                Copy-Item -Path $path -Destination $destinationPath -Force -ErrorAction Stop
                Write-Log "Copied $path to $destinationPath"
            }
        }
        catch {
            Write-Log "Error handling $changeType event for $path : $_"
        }
    }

    # Register event handlers for Created, Changed, and Deleted events
    Register-ObjectEvent $watcher "Created" -Action $action
    Register-ObjectEvent $watcher "Changed" -Action $action
    Register-ObjectEvent $watcher "Deleted" -Action $action

    # Enable event raising
    $watcher.EnableRaisingEvents = $true
    Write-Log "Monitoring started. Press Ctrl+C to stop."

    # Keep the script running
    while ($true) {
        Start-Sleep -Seconds 1
    }
}
catch {
    Write-Log "Error occurred: $_"
    throw $_
}
finally {
    # Clean up event subscriptions and dispose of the watcher
    if ($watcher) {
        $watcher.EnableRaisingEvents = $false
        Get-EventSubscriber | Where-Object { $_.SourceObject -eq $watcher } | Unregister-Event
        $watcher.Dispose()
        Write-Log "Monitoring stopped and resources cleaned up."
    }
}