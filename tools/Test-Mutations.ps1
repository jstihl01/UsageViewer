$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Build-Validation.ps1')
$env:DOTNET_CLI_HOME=Join-Path $root '.build/dotnet-home'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:NUGET_PACKAGES=Join-Path $root '.build/nuget'
$env:DOTNET_ROOT=Join-Path $root '.build/dotnet'
$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
$env:USAGEVIEWER_PROCESS_FIXTURE=Join-Path $root 'tests/ProcessFixture/bin/Release/net10.0-windows/win-x64/ProcessFixture.exe'
$dotnet=Join-Path $root '.build/dotnet/dotnet.exe'
$unit='tests/Unit/UsageViewer.Core.Tests.csproj'
$windows='tests/Windows/UsageViewer.Windows.Tests.csproj'
$integration='tests/Integration/UsageViewer.Integration.Tests.csproj'
$mutations=@(
    @{name='interval-100-minutes';file='Core/RefreshSchedule.cs';old='TimeSpan.FromSeconds(60)';new='TimeSpan.FromMinutes(100)';test='PollIsDueAtSixtySecondsNotBefore';project=$unit},
    @{name='missing-weekly-reset';file='Core/RefreshSchedule.cs';old=' || Crossed(limits.Weekly)';new='';test='WeeklyResetTriggersIndependently';project=$unit},
    @{name='missing-bucket-filter';file='Core/Usage.cs';old='if (id.GetString() != "codex") return new(ParseKind.NoMatchingWindows, Limits.Empty);';new='';test='FallbackBucketIsFiltered';project=$unit},
    @{name='missing-connection-exclusion';file='Core/SessionController.cs';old='SemaphoreSlim connectionGate = new(1)';new='SemaphoreSlim connectionGate = new(20)';test='ConnectionGateSharesAnActuallyPendingServerStart';project=$unit},
    @{name='missing-notification-throttle';file='Core/SessionController.cs';old='TimeSpan.FromSeconds(5)) return Task.CompletedTask';new='TimeSpan.Zero) return Task.CompletedTask';test='ChangedQuotaNotificationsAreLimitedToOnePerFiveSeconds';project=$unit},
    @{name='missing-three-failure-recheck';file='Core/SessionController.cs';old='quotaFailures == 3';new='quotaFailures == 100';test='ThirdNetworkFailureRechecksAccountOnceWithoutRefreshingToken';project=$unit},
    @{name='missing-signature-check';file='Platform/CodexProcessClient.cs';old='!verifier.Verify(executable)';new='false';test='CorrectHashStillRejectsAnInvalidAuthenticodeSignature';project=$integration},
    @{name='missing-job-assignment';file='Platform/CodexProcessClient.cs';old='job.Assign(process);';new='';test='ActualProcessClientAssignsTheServerAndItsDescendantsToItsJob';project=$integration},
    @{name='missing-update-size-limits';file='Core/UpdateChecker.cs';old='>131072';new='>int.MaxValue';test='LargeValidUpdateReplyIsRejectedWithOrWithoutContentLength';project=$unit},
    @{name='overly-broad-instance-dacl';file='Platform/WaitHandleSecurity.cs';old='D:P(A;;GA;;;SY)';new='D:P(A;;GA;;;WD)(A;;GA;;;SY)';test='NamedHandlesAreRestrictedAndRemainUsableByTheOwner';project=$windows},
    @{name='rpc-error-poisons-transport';file='Core/ICodexClient.cs';old=': Exception("Codex rechazó la solicitud.")';new=': IOException("Codex rechazó la solicitud.")';test='RpcErrorsRetainCodeAndMessage';project=$unit}
)
$evidence=Join-Path $root '.build/mutation-evidence'
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$results=[Collections.Generic.List[object]]::new()
foreach($mutation in $mutations) {
    $target=Join-Path $root ('.build/mutation-'+$mutation.name+'-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $target|Out-Null
    try {
        foreach($name in @('global.json','Directory.Build.props','NuGet.config','dependencies.json')){Copy-Item (Join-Path $root $name) $target}
        foreach($folder in @('Core','Platform','src','tests')) {
            foreach($file in Get-ChildItem -LiteralPath (Join-Path $root $folder) -File -Recurse|Where-Object {$_.FullName -notmatch '[\\/](bin|obj)[\\/]'}) {
                $relative=[IO.Path]::GetRelativePath($root,$file.FullName)
                $destination=Join-Path $target $relative
                New-Item -ItemType Directory -Force -Path (Split-Path $destination)|Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $destination
            }
        }
        $path=Join-Path $target $mutation.file;$original=Get-Content -LiteralPath $path -Raw
        if(!$original.Contains($mutation.old)){throw "No se encontró la mutación $($mutation.name)"}
        [IO.File]::WriteAllText($path,$original.Replace($mutation.old,$mutation.new))
        $project=Join-Path $target $mutation.project
        $restore=& $dotnet restore $project -r win-x64 --locked-mode --configfile (Join-Path $root 'NuGet.config') 2>&1
        if($LASTEXITCODE){$restore|Write-Output;throw "Falló la restauración de $($mutation.name)"}
        $output=& $dotnet test $project -c Release -r win-x64 --no-restore --filter $mutation.test --blame-hang-timeout 1m --logger 'trx;LogFileName=mutation.trx' --results-directory $target 2>&1
        $code=$LASTEXITCODE;$trx=Join-Path $target 'mutation.trx'
        $output|Set-Content (Join-Path $evidence ($mutation.name+'.log'))
        if($code -eq 0 -or !(Test-Path $trx)){throw "La prueba no detectó $($mutation.name)"}
        [xml]$result=Get-Content $trx -Raw
        if([int]$result.TestRun.ResultSummary.Counters.failed -lt 1){throw 'El fallo no es una aserción de prueba.'}
        Copy-Item -LiteralPath $trx -Destination (Join-Path $evidence ($mutation.name+'.trx'))
        $results.Add(@{mutation=$mutation.name;test=$mutation.test;failed=[int]$result.TestRun.ResultSummary.Counters.failed;detected=$true})
        Write-Output "OK: mutación detectada: $($mutation.name)"
    }
    finally {
        $results|ConvertTo-Json -Depth 5|Set-Content (Join-Path $root '.build/mutation-results.json') -Encoding utf8
        Remove-OwnedBuildDirectory $root $target
    }
}
exit 0
