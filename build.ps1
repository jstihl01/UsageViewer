param([string]$Compiler="$PSScriptRoot/.build/InnoSetup/ISCC.exe",
    [switch]$Development,[switch]$Sign)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
Set-Location $PSScriptRoot
. "$PSScriptRoot/tools/Build-Validation.ps1"
Assert-GitCheckout $PSScriptRoot
$env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.build\dotnet-home'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:NUGET_PACKAGES=Join-Path $PSScriptRoot '.build\nuget'
$env:DOTNET_ROOT=Join-Path $PSScriptRoot '.build\dotnet'
$env:DOTNET_ROOT_X64=$env:DOTNET_ROOT
$env:VSTEST_CONNECTION_TIMEOUT='120'
& "$PSScriptRoot/tools/Restore-Dependencies.ps1"
$dotnet=Join-Path $PSScriptRoot '.build\dotnet\dotnet.exe'
$pins=Get-Content dependencies.json -Raw | ConvertFrom-Json
if((& $dotnet --version).Trim() -ne $pins.sdk.version) { throw 'El SDK activo no coincide con la versión fijada.' }
$version=([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version
Assert-VersionDocuments $PSScriptRoot $version
& "$PSScriptRoot/tools/Test-BuildValidation.ps1"
$runtimeProperty=([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.RuntimeFrameworkVersion
if($runtimeProperty -ne $pins.runtime){throw 'Actualiza RuntimeFrameworkVersion en Directory.Build.props para que coincida con dependencies.json.'}
if((Get-Content global.json -Raw|ConvertFrom-Json).sdk.version -ne $pins.sdk.version){throw 'El SDK de global.json no coincide con dependencies.json.'}
$source=(& git rev-parse HEAD).Trim()
$sourceChanges=@(& git status --porcelain --untracked-files=normal)
$sourceDirty=$sourceChanges.Count -gt 0
if($env:GITHUB_ACTIONS -eq 'true' -and $sourceDirty) {
    $sourceChanges|Write-Output
    throw 'La compilación oficial exige un checkout limpio; revisar los archivos indicados antes de empaquetar.'
}
$beforeAssets=@{}
foreach($file in @('src/Assets/UsageViewer.png','src/Assets/UsageViewer.ico')) {
    $beforeAssets[$file]=(Get-FileHash $file).Hash
}
function Run-Dotnet([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments) {
    & $dotnet @Arguments
    if($LASTEXITCODE) { throw ('Falló dotnet '+($Arguments -join ' ')) }
}
$projects=@('src/UsageViewer.csproj','tests/Unit/UsageViewer.Core.Tests.csproj',
    'tests/Windows/UsageViewer.Windows.Tests.csproj','tests/Integration/UsageViewer.Integration.Tests.csproj',
    'tests/ProcessFixture/ProcessFixture.csproj')
foreach($project in $projects) { Run-Dotnet @('restore',$project,'--locked-mode','-r','win-x64','--configfile','NuGet.config') }
Run-Dotnet @('build','tests/ProcessFixture/ProcessFixture.csproj','-c','Release','--no-restore','-r','win-x64')
$env:USAGEVIEWER_CODEX_BINARY=Join-Path $PSScriptRoot '.build\codex-app-server.exe'
$env:USAGEVIEWER_PROCESS_FIXTURE=Join-Path $PSScriptRoot 'tests\ProcessFixture\bin\Release\net10.0-windows\win-x64\ProcessFixture.exe'
$results=Join-Path $PSScriptRoot ('.build/test-results/'+$version)
$minimums=@(109,34,10)
for($index=1;$index -le 3;$index++) {
    $project=$projects[$index]
    $resultName=[IO.Path]::GetFileNameWithoutExtension($project)+'.trx'
    Run-Dotnet @('test',$project,'-c','Release','--no-restore','-r','win-x64',
        '--blame-hang-timeout','2m','--logger',"trx;LogFileName=$resultName",'--results-directory',$results)
    Assert-TestResults (Join-Path $results $resultName) $minimums[$index-1]
}
$audits=@()
foreach($project in $projects) {
    $auditRaw=& $dotnet list $project package --vulnerable --include-transitive --format json --output-version 1 --no-restore
    if($LASTEXITCODE) {throw 'No se pudo auditar NuGet.'}
    $audit=$auditRaw -join [Environment]::NewLine | ConvertFrom-Json
    $audits+=@{project=$project;result=$audit}
    foreach($framework in $audit.projects.frameworks) {
        foreach($package in @($framework.topLevelPackages)+@($framework.transitivePackages)) {
            foreach($vulnerability in $package.vulnerabilities) {
                $exceptions=(Get-Content dependency-audit-exceptions.json -Raw|ConvertFrom-Json).exceptions
                $exception=$exceptions|Where-Object {$_.package -eq $package.id -and $_.advisory -eq $vulnerability.advisoryurl -and
                    $_.project -eq $project -and $_.reason -and [DateTimeOffset]$_.expires -gt [DateTimeOffset]::UtcNow}
                if($project -eq $projects[0] -or !$exception){throw "Dependencia vulnerable sin excepción vigente: $($package.id)"}
                Write-Warning "Excepción temporal de dependencia de pruebas: $($package.id)."
            }
        }
    }
}
$audits|ConvertTo-Json -Depth 20|Set-Content '.build/nuget-audit.json' -Encoding utf8
foreach($package in $pins.runtimePackages) {
    $nupkg=Join-Path $env:NUGET_PACKAGES ($package.id.ToLowerInvariant()+'/'+$pins.runtime+'/'+$package.id.ToLowerInvariant()+'.'+$pins.runtime+'.nupkg')
    if((Get-FileHash -LiteralPath $nupkg -Algorithm SHA512).Hash -ne $package.sha512){throw "Runtime alterado: $($package.id)"}
    Run-Dotnet @('nuget','verify',$nupkg,'--all','--configfile','NuGet.config')
}
foreach($oldStage in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '.build') -Directory -Filter 'stage-*') {
    Remove-OwnedBuildDirectory $PSScriptRoot $oldStage.FullName
}
$stage=Join-Path $PSScriptRoot ('.build\stage-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
Run-Dotnet @('publish','src/UsageViewer.csproj','-c','Release','-r','win-x64',
    '--self-contained','true','--no-restore','-o',$stage)
New-Item -ItemType Directory -Path (Join-Path $stage 'codex'),(Join-Path $stage 'licenses') | Out-Null
Copy-Item '.build/codex-app-server.exe' (Join-Path $stage 'codex/codex-app-server.exe')
Copy-Item '.build/Codex-LICENSE.txt' (Join-Path $stage 'codex/LICENSE.txt')
Copy-Item '.build/Codex-NOTICE.download' (Join-Path $stage 'codex/NOTICE.txt')
Copy-Item '.build/Ratatui-MIT.txt' (Join-Path $stage 'licenses/Ratatui-MIT.txt')
Copy-Item '.build/WPF-THIRD-PARTY-NOTICES.txt','.build/WinForms-THIRD-PARTY-NOTICES.txt' (Join-Path $stage 'licenses')
Copy-Item LICENSE (Join-Path $stage 'LICENSE.txt')
Copy-Item COMPROBACIONES.md,PRIVACIDAD.md (Join-Path $stage 'licenses')
Copy-Item MIGRACION.md,TERCEROS.md (Join-Path $stage 'licenses')
foreach($name in @('Microsoft.NETCore.App.Runtime.win-x64','Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $package=Join-Path $env:NUGET_PACKAGES ($name.ToLowerInvariant()+'\'+$pins.runtime)
    if(!(Test-Path $package)) { throw "No se encontró el runtime fijado: $name" }
    $notices=Get-ChildItem -LiteralPath $package -File | Where-Object Name -Match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$'
    if(!$notices) { throw "Faltan los avisos oficiales: $name" }
    foreach($notice in $notices) { Copy-Item $notice.FullName (Join-Path $stage "licenses/$name-$($notice.Name).txt") }
}
$runtime=Get-Content (Join-Path $stage 'UsageViewer.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach($framework in $runtime.runtimeOptions.includedFrameworks) {
    if($framework.version -ne $pins.runtime) { throw 'El paquete contiene un runtime diferente del fijado.' }
}
Assert-RuntimePayload $stage $env:NUGET_PACKAGES $pins
if((Get-FileHash (Join-Path $stage 'codex/codex-app-server.exe')).Hash -ne $pins.codex.sha256) { throw 'La copia de Codex no coincide con su huella.' }
if((Get-AuthenticodeSignature (Join-Path $stage 'codex/codex-app-server.exe')).Status -ne 'Valid') { throw 'Firma de Codex no válida en el paquete.' }
if($Sign) {
    & "$PSScriptRoot/tools/Sign-Release.ps1" -Files @('UsageViewer.exe','UsageViewer.dll','UsageViewer.Core.dll','UsageViewer.Windows.dll' | ForEach-Object {Join-Path $stage $_})
}
$inventory=@(Get-ChildItem $stage -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{path=[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/');
        bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}
})
[ordered]@{version=$version;commit=$source;sourceDirty=$sourceDirty;runtime=$pins.runtime;codex=$pins.codex.version;unsigned=(!$Sign);files=$inventory} |
    ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $stage 'package-manifest.json')
$components=@(
    @{type='application';name='Usage Viewer';version=$version;licenses=@(@{license=@{id='MIT'}})}
    @{type='application';name='OpenAI Codex app-server';version=$pins.codex.version;purl=('pkg:github/openai/codex@rust-v'+$pins.codex.version);hashes=@(@{alg='SHA-256';content=$pins.codex.sha256});licenses=@(@{license=@{id='Apache-2.0'}})}
)
foreach($package in $pins.runtimePackages) {
    $components+=@{type='framework';name=$package.id;version=$pins.runtime;purl=('pkg:nuget/'+$package.id+'@'+$pins.runtime);
        hashes=@(@{alg='SHA-512';content=$package.sha512});licenses=@(@{license=@{id='MIT'}})}
}
$assets=Get-Content src/obj/project.assets.json -Raw | ConvertFrom-Json
foreach($entry in $assets.libraries.PSObject.Properties) {
    if($entry.Value.type -eq 'package') {
        $split=$entry.Name.LastIndexOf('/')
        $components+=@{type='library';name=$entry.Name.Substring(0,$split);version=$entry.Name.Substring($split+1);
            purl=('pkg:nuget/'+$entry.Name)}
    }
}
New-Item -ItemType Directory -Force dist | Out-Null
[ordered]@{bomFormat='CycloneDX';specVersion='1.6';serialNumber=('urn:uuid:'+[Guid]::NewGuid());version=1;
    metadata=@{timestamp=[DateTime]::UtcNow.ToString('o');component=$components[0]};components=$components[1..($components.Count-1)]} |
    ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 'dist/UsageViewer-SBOM.cdx.json'
Copy-Item (Join-Path $stage 'package-manifest.json') 'dist/UsageViewer-package-manifest.json'
$currentNames=@($inventory.path)
$cleanup=Join-Path $PSScriptRoot ('.build\legacy-cleanup-'+[Guid]::NewGuid().ToString('N')+'.iss')
& "$PSScriptRoot/tools/New-LegacyCleanup.ps1" -CurrentNames $currentNames -OutFile $cleanup
$args=@("/DAppVersion=$version","/DPublishDir=$stage","/DCleanupInclude=$cleanup")
if($Development) { $args+='/DDevelopment=1' }
if($Sign) {
    $args+='/DSigned=1'
    $pwsh=(Get-Command pwsh.exe -ErrorAction Stop).Source
    $command='$q'+$pwsh+'$q -NoProfile -File $q'+$PSScriptRoot+'\tools\Sign-Release.ps1$q -Files $f'
    $args+=('/SUsageViewerSign='+$command)
}
Assert-Compiler $Compiler $pins
# Recheck the full staged inventory immediately before invoking the packager.
foreach($entry in $inventory) {
    if((Get-FileHash (Join-Path $stage $entry.path)).Hash -ne $entry.sha256) {throw "Cambió un archivo del paquete: $($entry.path)"}
}
$expected=@($inventory.path)+@('package-manifest.json')
$actual=@(Get-ChildItem $stage -File -Recurse | ForEach-Object {[IO.Path]::GetRelativePath($stage,$_.FullName).Replace('\','/')})
if(Compare-Object ($expected|Sort-Object) ($actual|Sort-Object)) {throw 'El inventario no cubre el directorio completo.'}
$output=& $Compiler @args installer/UsageViewer.iss
$exit=$LASTEXITCODE
$output | Set-Content -Encoding utf8 '.build/inno-build.log'
if($exit) { $output | Write-Output;throw 'Falló el instalador.' }
$packaged=@($output | ForEach-Object {
    $line=$_.Trim()
    if($line.StartsWith('Compressing: ')){[IO.Path]::GetFullPath($line.Substring('Compressing: '.Length))}
})
if(Compare-Object ($expected|ForEach-Object {[IO.Path]::GetFullPath((Join-Path $stage $_))}|Sort-Object) ($packaged|Sort-Object)) {
    throw 'El contenido confirmado por Inno Setup no coincide con el inventario completo.'
}
$setup=Get-Item 'dist/UsageViewer-Setup-x64.exe'
$pe=[IO.BinaryReader]::new([IO.File]::OpenRead($setup.FullName))
try {
    $pe.BaseStream.Position=0x3c
    $pe.BaseStream.Position=$pe.ReadInt32()+4
    if($pe.ReadUInt16() -ne 0x14c) {throw 'El stub de Inno Setup cambió de arquitectura; revisar interoperabilidad del instalador.'}
}
finally {$pe.Dispose()}
$hash=(Get-FileHash $setup.FullName).Hash.ToLowerInvariant()
"$hash *$($setup.Name)" | Set-Content -Encoding ASCII "$($setup.FullName).sha256"
[ordered]@{schema='UsageViewer-build-record-1';version=$version;commit=$source;sourceDirty=$sourceDirty;
    repository=([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.RepositoryUrl;sdk=$pins.sdk.version;runtime=$pins.runtime;
    codex=$pins.codex;compiler=$pins.compiler;unsigned=(!$Sign);
    installer=@{name=$setup.Name;sha256=$hash;bytes=$setup.Length};
    workflowRun=$env:GITHUB_RUN_ID;workflowCommit=$env:GITHUB_SHA} |
    ConvertTo-Json -Depth 8 | Set-Content -Encoding utf8 'dist/UsageViewer-provenance.json'
Copy-Item MIGRACION.md 'dist/UsageViewer-MIGRACION.md'
Copy-Item COMPROBACIONES.md 'dist/UsageViewer-COMPROBACIONES.md'
Copy-Item AUDITORIA.md 'dist/UsageViewer-AUDITORIA.md'
Copy-Item REVISION_2026-11-17.md 'dist/UsageViewer-REVISION.md'
Compress-Archive -Path (Join-Path $results '*.trx'),'.build/nuget-audit.json','.build/inno-build.log' -DestinationPath 'dist/UsageViewer-pruebas.zip' -Force
foreach($file in $beforeAssets.Keys) {if((Get-FileHash $file).Hash -ne $beforeAssets[$file]) {throw 'La compilación modificó el logo versionado.'}}
Remove-Item -LiteralPath $cleanup -Force
Write-Output "Instalador: $($setup.FullName)"
Write-Output "SHA-256: $hash"
