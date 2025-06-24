# rebuildfe.ps1
Set-Location -Path "C:\Users\tstra\source\repos\BNKaraoke\bnkaraoke.web"
Clear-Host
Write-Host "Ensuring .env File"
Set-Content -Path ".env" -Value "NODE_ENV=development`nREACT_APP_API_URL=https://localhost:7290`nPORT=8080"
Write-Host "Starting Development Server"
Remove-Item -Path Env:\NODE_ENV -ErrorAction SilentlyContinue
$env:NODE_ENV = "development"
Write-Host "NODE_ENV set to: $env:NODE_ENV"
try {
    npm start
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Start failed. Please check npm logs or run 'npm install' if dependencies are missing." -ForegroundColor Red
        Pause
        exit 1
    }
}
catch {
    Write-Host "Start error: $_" -ForegroundColor Red
    Pause
    exit 1
}