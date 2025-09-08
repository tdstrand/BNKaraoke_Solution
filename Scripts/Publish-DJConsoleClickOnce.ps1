# Publishes the BNKaraoke DJ Console as a ClickOnce (OneClick) application
# using the specified locations and automatically increments the build number.

[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\BNKaraoke.DJ\BNKaraoke.DJ.csproj'),
    [string]$PublishDir  = '\\172.16.0.25\bnkaraoke\bnkaraoke.dj\',
    [string]$InstallUrl  = 'https://www.bnkaraoke.com/DJConsole/',
    [string]$StagingDir  = (Join-Path $env:TEMP 'BNKaraoke.DJ.Publish'),
    [string]$VersionFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Show which .NET SDK is being used for troubleshooting
$dotnetVersion = dotnet --version
Write-Host "Using .NET SDK version $dotnetVersion"
Write-Host "ProjectPath: $ProjectPath"
Write-Host "PublishDir: $PublishDir"
Write-Host "InstallUrl: $InstallUrl"

# Validate required paths
if (-not (Test-Path $ProjectPath)) {
    throw "Path not found: $ProjectPath"
}

# Prepare local staging directory to avoid locking issues when publishing
# directly to the network share.
if (Test-Path $StagingDir) {
    Remove-Item -Path $StagingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $StagingDir -Force | Out-Null

# Remove any previous build output to prevent MSBuild copy errors such as
# "DestinationFiles refers to 2 item(s), and SourceFiles refers to 1 item(s)".
$projectDir = Split-Path $ProjectPath -Parent
Write-Host "Cleaning project $ProjectPath"
dotnet clean $ProjectPath -c Release -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet clean failed with exit code $LASTEXITCODE"
}
Remove-Item -Path (Join-Path $projectDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $projectDir 'obj') -Recurse -Force -ErrorAction SilentlyContinue

# Determine where to persist the version between runs. Default to a file in the
# user's local application data folder so the repository remains untouched.
if (-not $VersionFile) {
    $VersionFile = Join-Path $env:LOCALAPPDATA 'BNKaraoke.DJ\publish-version.txt'
}

# Ensure the version file directory exists
$versionDir = Split-Path -Parent $VersionFile
if (-not (Test-Path $versionDir)) {
    New-Item -ItemType Directory -Path $versionDir -Force | Out-Null
}

# Read the last published version
if (Test-Path $VersionFile) {
    # Use ASCII to guarantee a plain-text file without BOM or multibyte characters
    $encoding = [System.Text.Encoding]::ASCII
    $oldVersion = [Version]([System.IO.File]::ReadAllText($VersionFile, $encoding))
} else {
    [xml]$csproj = Get-Content $ProjectPath
    $propertyGroup = $csproj.Project.PropertyGroup | Select-Object -First 1
    $oldVersion = if ($propertyGroup.Version) { [Version]$propertyGroup.Version } else { [Version]'1.5.0.0' }
}
if ($oldVersion -lt [Version]'1.5.0.0') {
    $oldVersion = [Version]'1.5.0.0'
}

$newRevision = $oldVersion.Revision + 1
$newVersion = "{0}.{1}.{2}.{3}" -f $oldVersion.Major, $oldVersion.Minor, $oldVersion.Build, $newRevision
[System.IO.File]::WriteAllText($VersionFile, $newVersion, [System.Text.Encoding]::ASCII)
Write-Host "Incremented version from $oldVersion to $newVersion"

# Restore the project for the desired runtime so the assets file contains
# the correct target before publishing.
Write-Host "Restoring project $ProjectPath for win-x64"
dotnet restore $ProjectPath -r win-x64
if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

$publishArgs = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '--no-restore',
    '--self-contained', 'true',
    "/p:PublishDir=$StagingDir",
    "/p:InstallUrl=$InstallUrl",
    "/p:ApplicationVersion=$newVersion",
    "/p:ApplicationRevision=$newRevision"
)

Write-Host "Publishing version $newVersion to staging directory $StagingDir"
Write-Host "dotnet publish $ProjectPath $($publishArgs -join ' ') -v diag"

# Capture detailed MSBuild diagnostics to a log file so build failures such as
# MSB3094 can be investigated. The final lines are echoed on failure for quick
# feedback.
$publishLog = Join-Path $StagingDir 'publish.log'
dotnet publish $ProjectPath @publishArgs -v diag 2>&1 |
    Tee-Object -FilePath $publishLog
if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    if (Test-Path $publishLog) {
        Write-Host "Last 20 lines of publish log:"
        Get-Content $publishLog -Tail 20
    }
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# After publishing locally, sync to the final network location.
Write-Host "Preparing final publish directory at $PublishDir"
if (Test-Path $PublishDir) {
    Write-Host "Clearing existing contents in $PublishDir"
    try {
        Get-ChildItem -Path $PublishDir -Force |
            Remove-Item -Recurse -Force -ErrorAction Stop
    } catch {
        Write-Error "Failed to clear publish directory. Close any running instances and retry."
        exit 1
    }
    if (Test-Path (Join-Path $PublishDir 'BNKaraoke.DJ.exe')) {
        Write-Error 'Publish directory is still in use'
        exit 1
    }
} else {
    Write-Host "Creating publish directory at $PublishDir"
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
}

Write-Host "Copying published files to $PublishDir"
Copy-Item -Path (Join-Path $StagingDir '*') -Destination $PublishDir -Recurse -Force

