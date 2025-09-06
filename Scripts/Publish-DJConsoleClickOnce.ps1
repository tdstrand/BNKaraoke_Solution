# Publishes the BNKaraoke DJ Console as a ClickOnce (OneClick) application
# using the specified locations and automatically increments the build number.

[CmdletBinding()]
param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot '..\BNKaraoke.DJ\BNKaraoke.DJ.csproj'),
    [string]$PublishDir  = '\\172.16.0.25\bnkaraoke\bnkaraoke.dj\',
    [string]$InstallUrl  = 'https://www.bnkaraoke.com/DJConsole/',
    [string]$IconPath    = (Join-Path $PSScriptRoot '..\BNKaraoke.DJ\Assets\app.ico')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Validate required paths
foreach ($path in @($ProjectPath, $IconPath)) {
    if (-not (Test-Path $path)) {
        throw "Path not found: $path"
    }
}

if (-not (Test-Path $PublishDir)) {
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
}

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

$publishArgs = @(
    '-c', 'Release',
    '/p:PublishProtocol=ClickOnce',
    "/p:PublishDir=$PublishDir",
    "/p:InstallUrl=$InstallUrl",
    "/p:ApplicationVersion=$($propertyGroup.Version)",
    "/p:ApplicationRevision=$newRevision",
    '/p:UpdateEnabled=true',
    '/p:UpdateMode=Foreground',
    '/p:UpdateRequired=true',
    '/p:CheckForUpdate=true',
    '/p:RuntimeIdentifier=win-x64',
    '/p:SelfContained=true',
    "/p:ApplicationIcon=$IconPath"
)

Write-Host "Publishing version $($propertyGroup.Version) to $PublishDir"
dotnet publish $ProjectPath @publishArgs

