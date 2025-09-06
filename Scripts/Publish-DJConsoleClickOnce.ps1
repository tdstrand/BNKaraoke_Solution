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
    '/t:Publish',
    '/p:Configuration=Release',
    '/p:Platform=x64',
    '/p:PublishProtocol=ClickOnce',
    "/p:PublishDir=$publishDir",
    "/p:InstallUrl=$installUrl",
    "/p:ApplicationVersion=$($propertyGroup.Version)",
    "/p:ApplicationRevision=$newRevision",
    '/p:UpdateEnabled=true',
    '/p:UpdateMode=Foreground',    # checks for updates before start
    '/p:UpdateRequired=true',
    '/p:CheckForUpdate=true',
    "/p:ApplicationIcon=$iconPath"
)

# Clean out old deployment artifacts that may clutter the root directory
Get-ChildItem -Path $publishDir -Exclude 'Application Files' | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

dotnet msbuild $projectPath @publishArgs
