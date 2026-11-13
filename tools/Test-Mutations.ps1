$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME=Join-Path $root '.build\dotnet-home'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $root '.build\nuget'
$env:DOTNET_ROOT=Join-Path $root '.build\dotnet'
$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
$dotnet=Join-Path $root '.build\dotnet\dotnet.exe'
$mutations=@(
    @{name='interval-100-minutes';file='Core\RefreshSchedule.cs';old='TimeSpan.FromSeconds(60)';new='TimeSpan.FromMinutes(100)';test='PollIsDueAtSixtySecondsNotBefore'},
    @{name='missing-weekly-reset';file='Core\RefreshSchedule.cs';old=' || Crossed(limits.Weekly)';new='';test='WeeklyResetTriggersIndependently'},
    @{name='missing-bucket-filter';file='Core\Usage.cs';old='if (id.GetString() != "codex") return new(ParseKind.NoMatchingWindows, Limits.Empty);';new='';test='FallbackBucketIsFiltered'}
)
foreach($mutation in $mutations) {
    $target=Join-Path $root ('.build\mutation-'+$mutation.name+'-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $target | Out-Null
    foreach($name in @('global.json','Directory.Build.props','NuGet.config')) {Copy-Item (Join-Path $root $name) $target}
    foreach($folder in @('Core','tests\Unit')) {
        $destination=Join-Path $target $folder
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath (Join-Path $root $folder) -File |
            Where-Object Extension -in '.cs','.csproj','.json' | Copy-Item -Destination $destination
    }
    $path=Join-Path $target $mutation.file
    $original=Get-Content -LiteralPath $path -Raw
    if(!$original.Contains($mutation.old)) {throw "No se encontró la mutación $($mutation.name)"}
    [IO.File]::WriteAllText($path,$original.Replace($mutation.old,$mutation.new))
    $project=Join-Path $target 'tests\Unit\UsageViewer.Core.Tests.csproj'
    $restore=& $dotnet restore $project -r win-x64 --locked-mode 2>&1
    if($LASTEXITCODE) {throw "Falló la restauración de la mutación $($mutation.name)"}
    $output=& $dotnet test $project -c Release -r win-x64 --no-restore --filter $mutation.test --logger 'trx;LogFileName=mutation.trx' --results-directory $target 2>&1
    $code=$LASTEXITCODE
    $output | Set-Content (Join-Path $target 'run.log')
    $trx=Join-Path $target 'mutation.trx'
    if($code -eq 0 -or !(Test-Path $trx)) {throw "La prueba no detectó la mutación $($mutation.name)"}
    [xml]$result=Get-Content $trx -Raw
    if([int]$result.TestRun.ResultSummary.Counters.failed -lt 1) {throw 'El fallo de la mutación no es una aserción de prueba.'}
    Write-Output "OK: mutación detectada: $($mutation.name)"
}
# dotnet test intentionally returns 1 for a detected mutation. Report the verified script outcome to CI.
exit 0
