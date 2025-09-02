# Usage: .\test-loudness.ps1 <media-file> [target-lufs]
# Runs ffmpeg loudnorm filter and prints the detected input loudness.
param(
    [Parameter(Mandatory=$true)]
    [string]$MediaFile,

    [double]$TargetLufs = -14
)

if (-not (Test-Path $MediaFile)) {
    Write-Error "File not found: $MediaFile"
    exit 1
}

$ffmpegArgs = @(
    '-i', $MediaFile,
    '-af', "loudnorm=I=$TargetLufs:TP=-1.5:LRA=11",
    '-f', 'null', '-'
)

$ffmpegOutput = & ffmpeg @ffmpegArgs 2>&1
$ffmpegOutput | Select-String 'Input Integrated'
