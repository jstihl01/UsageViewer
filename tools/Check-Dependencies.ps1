$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$pins=Get-Content (Join-Path $root 'dependencies.json') -Raw|ConvertFrom-Json
$findings=[Collections.Generic.List[string]]::new()
$metadata=Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 30
if($metadata.'latest-runtime' -ne $pins.runtime){$findings.Add(".NET runtime: fijado $($pins.runtime), publicado $($metadata.'latest-runtime').")}
if($metadata.'latest-sdk' -ne $pins.sdk.version){$findings.Add(".NET SDK: fijado $($pins.sdk.version), publicado $($metadata.'latest-sdk').")}
$releases=Invoke-RestMethod 'https://api.github.com/repos/openai/codex/releases?per_page=30' -TimeoutSec 30
$codex=$releases|Where-Object {!$_.prerelease -and !$_.draft -and $_.tag_name -match '^rust-v\d+\.\d+\.\d+$'}|Select-Object -First 1
if(!$codex){throw 'No se pudo identificar la última versión estable de Codex.'}
if($codex.tag_name -ne 'rust-v'+$pins.codex.version){$findings.Add("Codex: fijado $($pins.codex.version), publicado $($codex.tag_name). Revisar contrato y seguridad antes de actualizar.")}
$inno=Invoke-RestMethod 'https://api.github.com/repos/jrsoftware/issrc/releases/latest' -TimeoutSec 30
if($inno.tag_name.Replace('is-','').Replace('_','.') -ne $pins.compiler.version){$findings.Add("Inno Setup: fijado $($pins.compiler.version), publicado $($inno.tag_name).")}
& (Join-Path $PSScriptRoot 'Test-Lockfiles.ps1')
$dotnet=Join-Path $root '.build/dotnet/dotnet.exe'
foreach($project in @('src/UsageViewer.csproj','tests/Unit/UsageViewer.Core.Tests.csproj','tests/Windows/UsageViewer.Windows.Tests.csproj','tests/Integration/UsageViewer.Integration.Tests.csproj')) {
    $raw=& $dotnet list (Join-Path $root $project) package --vulnerable --include-transitive --format json --no-restore
    if($LASTEXITCODE){throw 'La auditoría programada de NuGet falló.'}
    $audit=$raw -join [Environment]::NewLine|ConvertFrom-Json
    foreach($framework in $audit.projects.frameworks) {
        foreach($package in @($framework.topLevelPackages)+@($framework.transitivePackages)) {
            if($package.vulnerabilities.Count -gt 0){$findings.Add("Aviso de seguridad NuGet: $($package.id) en $project.")}
        }
    }
}
@{needsReview=($findings.Count -gt 0);checked=[DateTimeOffset]::UtcNow;findings=@($findings)}|ConvertTo-Json -Depth 6|Set-Content (Join-Path $root '.build/dependency-report.json') -Encoding utf8
("Revisión semanal de componentes fijados. No se actualizan automáticamente.`n`n"+($findings -join "`n`n"))|Set-Content (Join-Path $root '.build/dependency-report.md') -Encoding utf8
