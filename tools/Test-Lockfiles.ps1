param([switch]$Update)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME=Join-Path $root '.build/dotnet-home'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $root '.build/nuget'
$dotnet=Join-Path $root '.build/dotnet/dotnet.exe'
function Get-LockHash([string]$File) {
    $text=[IO.File]::ReadAllText($File).Replace("`r`n","`n")
    [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}
$projects=@('src/UsageViewer.csproj','tests/Unit/UsageViewer.Core.Tests.csproj','tests/Windows/UsageViewer.Windows.Tests.csproj',
    'tests/Integration/UsageViewer.Integration.Tests.csproj','tests/ProcessFixture/ProcessFixture.csproj')
$before=@{}
foreach($path in @('Core','Platform','src','tests/Unit','tests/Windows','tests/Integration','tests/ProcessFixture')) {
    $file=Join-Path $root "$path/packages.lock.json"
    $before[$file]=Get-LockHash $file
}
foreach($project in $projects) {
    & $dotnet restore (Join-Path $root $project) --force-evaluate -r win-x64 --configfile (Join-Path $root 'NuGet.config')
    if($LASTEXITCODE){throw 'Falló la regeneración de bloqueos.'}
}
if(!$Update) {
    foreach($file in $before.Keys) {
        if((Get-LockHash $file) -ne $before[$file]) {
            throw 'Los bloqueos no están sincronizados. Ejecuta tools/Test-Lockfiles.ps1 -Update y revisa los cambios.'
        }
    }
}
foreach($project in $projects) {
    & $dotnet restore (Join-Path $root $project) --locked-mode -r win-x64 --configfile (Join-Path $root 'NuGet.config')
    if($LASTEXITCODE){throw 'No se pudo restaurar en modo bloqueado.'}
}
# NuGet writes Windows CRLF; the repository explicitly stores LF. Keep the verified
# contents byte-identical to checkout so git status cannot report a false dirty build.
foreach($file in $before.Keys) {
    $text=[IO.File]::ReadAllText($file).Replace("`r`n","`n")
    [IO.File]::WriteAllText($file,$text,[Text.UTF8Encoding]::new($false))
}
Write-Output 'OK: bloqueos y restauración verificados.'
