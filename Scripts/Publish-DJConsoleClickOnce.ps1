# Publishes the BNKaraoke DJ Console as a ClickOnce (OneClick) application
# using the specified locations and automatically increments the build.

$projectPath = "C:\Users\tstra\source\repos\BNKaraoke\BNKaraoke.DJ\BNKaraoke.DJ.csproj"
$publishDir  = "\\172.16.0.25\bnkaraoke\bnkaraoke.dj\"
$installUrl  = "https://www.bnkaraoke.com/DJConsole/"
$iconPath    = "C:\Users\tstra\source\repos\BNKaraoke\BNKaraoke.DJ\Assets\app.ico"

# Load the project to increment the ClickOnce version
[xml]$csproj = Get-Content $projectPath
$propertyGroup = $csproj.Project.PropertyGroup | Select-Object -First 1
if (-not $propertyGroup.Version) {
    $propertyGroup.AppendChild($csproj.CreateElement('Version')) | Out-Null
    $propertyGroup.Version = '1.0.0.0'
}
$oldVersion  = [Version]$propertyGroup.Version
$newRevision = $oldVersion.Revision + 1
$propertyGroup.Version = "{0}.{1}.{2}.{3}" -f $oldVersion.Major, $oldVersion.Minor, $oldVersion.Build, $newRevision
$csproj.Save($projectPath)

$publishArgs = @(
    '-c', 'Release',
    '/p:PublishProtocol=ClickOnce',
    "/p:PublishDir=$publishDir", 
    "/p:InstallUrl=$installUrl",
    "/p:ApplicationVersion=$($propertyGroup.Version)",
    "/p:ApplicationRevision=$newRevision",
    '/p:UpdateEnabled=true',
    '/p:UpdateMode=Foreground',    # checks for updates before start
    '/p:UpdateRequired=true',
    '/p:CheckForUpdate=true',
    '/p:RuntimeIdentifier=win-x64',
    '/p:SelfContained=true',
    "/p:ApplicationIcon=$iconPath"
)

dotnet publish $projectPath @publishArgs
