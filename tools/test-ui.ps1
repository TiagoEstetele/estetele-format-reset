param(
    [string]$Executable = 'dist/win-x64/estetele-format-reset.exe',
    [string]$OutputDirectory = 'artifacts/ui-check'
)
$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
$previousExtraction = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
try {
    $exe = (Resolve-Path -LiteralPath $Executable).Path
    $output = [IO.Path]::GetFullPath($OutputDirectory)
    # Each run has its own history. Reusing a prior fixture changes its defaults.
    $runDirectory = Join-Path $output ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = Join-Path $PWD 'artifacts/bundle-cache'
    $process = Start-Process -FilePath $exe -ArgumentList ('--self-test-ui "' + $runDirectory + '"') -PassThru -WindowStyle Hidden
    if (-not $process.WaitForExit(120000)) {
        # This process was created by this test and is forced into simulation.
        Stop-Process -Id $process.Id -ErrorAction SilentlyContinue
        throw 'O teste WPF simulado excedeu dois minutos.'
    }
    $process.Refresh()
    if ($process.ExitCode -ne 0) { throw "Teste WPF falhou: código $($process.ExitCode). Consulte $runDirectory" }
    if (-not (Test-Path -LiteralPath (Join-Path $runDirectory 'ui-results.txt'))) { throw 'O teste não gerou relatório.' }
    if (@(Get-ChildItem -LiteralPath $runDirectory -Filter '*.png').Count -ne 6) { throw 'O teste não gerou as seis telas esperadas.' }
    Get-Content -LiteralPath (Join-Path $runDirectory 'ui-results.txt')
    Write-Output "Telas: $runDirectory"
} finally {
    $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $previousExtraction
    Pop-Location
}
