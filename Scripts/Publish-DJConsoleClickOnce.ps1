# Publishes the BNKaraoke DJ Console as a ClickOnce (OneClick) application
# using the specified locations and automatically increments the build number.

[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\BNKaraoke.DJ\BNKaraoke.DJ.csproj'),
    [string]$PublishDir  = '\\172.16.0.25\bnkaraoke\bnkaraoke.dj\',
    [string]$InstallUrl  = 'https://www.bnkaraoke.com/DJConsole/',
    [string]$IconPath    = (Join-Path $PSScriptRoot '..\BNKaraoke.DJ\Assets\app.ico'),
    [string]$StagingDir  = (Join-Path $env:TEMP 'BNKaraoke.DJ.Publish')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Show which .NET SDK is being used for troubleshooting
$dotnetVersion = dotnet --version
Write-Host "Using .NET SDK version $dotnetVersion"
Write-Host "ProjectPath: $ProjectPath"
Write-Host "PublishDir: $PublishDir"
Write-Host "InstallUrl: $InstallUrl"
Write-Host "IconPath: $IconPath"

# Validate required paths
foreach ($path in @($ProjectPath, $IconPath)) {
    if (-not (Test-Path $path)) {
        throw "Path not found: $path"
    }
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
dotnet clean $ProjectPath -c Release
Remove-Item -Path (Join-Path $projectDir 'bin') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -Path (Join-Path $projectDir 'obj') -Recurse -Force -ErrorAction SilentlyContinue

# Load the project to increment the ClickOnce version
[xml]$csproj = Get-Content $ProjectPath
$propertyGroup = $csproj.Project.PropertyGroup | Select-Object -First 1
if (-not $propertyGroup.Version) {
    $propertyGroup.AppendChild($csproj.CreateElement('Version')) | Out-Null
    $propertyGroup.Version = '1.0.0.0'
}
$oldVersion  = [Version]$propertyGroup.Version
$newRevision = $oldVersion.Revision + 1
$propertyGroup.Version = "{0}.{1}.{2}.{3}" -f $oldVersion.Major, $oldVersion.Minor, $oldVersion.Build, $newRevision
$csproj.Save($ProjectPath)
Write-Host "Incremented version from $oldVersion to $($propertyGroup.Version)"

# Restore the project for the desired runtime so the assets file contains
# the correct target before publishing.
Write-Host "Restoring project $ProjectPath for win-x64"
dotnet restore $ProjectPath -r win-x64

$publishArgs = @(
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '/p:PublishProtocol=ClickOnce',
    "/p:PublishDir=$StagingDir",
    "/p:InstallUrl=$InstallUrl",
    "/p:ApplicationVersion=$($propertyGroup.Version)",
    "/p:ApplicationRevision=$newRevision",
    '/p:UpdateEnabled=true',
    '/p:UpdateMode=Foreground',
    '/p:UpdateRequired=true',
    '/p:CheckForUpdate=true',
    "/p:ApplicationIcon=$IconPath",
    '/p:PublishSingleFile=true'
)

Write-Host "Publishing version $($propertyGroup.Version) to staging directory $StagingDir"
Write-Host "dotnet publish $ProjectPath $($publishArgs -join ' ')"
dotnet publish $ProjectPath @publishArgs

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

