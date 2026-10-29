$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    $exe = 'dist/win-x64/estetele-format-reset.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'Compile primeiro com tools/build.ps1.' }
    $stage = Join-Path $PWD ('artifacts/package-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -LiteralPath $exe -Destination $stage
    Copy-Item -LiteralPath 'LEIA-ME.txt','README.md','LICENSE','THIRD-PARTY-NOTICES.md' -Destination $stage
    Copy-Item -LiteralPath 'docs' -Destination $stage -Recurse
    New-Item -ItemType Directory -Path (Join-Path $stage 'assets') | Out-Null
    Copy-Item -LiteralPath 'assets/efr-logo.svg' -Destination (Join-Path $stage 'assets')
    $zip = 'dist/estetele-format-reset-win-x64.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
    @($exe,$zip) | ForEach-Object {
        $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(),[IO.Path]::GetFileName($_)
    } | Set-Content -LiteralPath 'dist/SHA256SUMS.txt' -Encoding ascii
    Write-Output "Pacote: $zip; integridade: dist/SHA256SUMS.txt"
} finally { Pop-Location }
