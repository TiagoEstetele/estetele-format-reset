param([string]$Dotnet = 'dotnet', [switch]$SkipRestore)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    if (-not $SkipRestore) {
        & $Dotnet restore 'src/Efr.App/Efr.App.csproj' --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar o aplicativo.' }
        & $Dotnet restore 'tests/Efr.Tests/Efr.Tests.csproj' --configfile NuGet.Config
        if ($LASTEXITCODE -ne 0) { throw 'Falha ao restaurar os testes.' }
    }
    & $Dotnet run --project 'tests/Efr.Tests/Efr.Tests.csproj' -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Os testes falharam.' }
    & $Dotnet publish 'src/Efr.App/Efr.App.csproj' -c Release -r win-x64 --self-contained true --no-restore -o 'dist/win-x64'
    if ($LASTEXITCODE -ne 0) { throw 'Falha na publicação.' }
    Copy-Item -LiteralPath 'dist/win-x64/estetele-format-reset.exe' -Destination 'estetele-format-reset.exe' -Force
    & (Join-Path $PSScriptRoot 'test-ui.ps1')
    & (Join-Path $PSScriptRoot 'package.ps1')
    Write-Output 'Executável: estetele-format-reset.exe (raiz) e dist/win-x64/estetele-format-reset.exe'
} finally { Pop-Location }
