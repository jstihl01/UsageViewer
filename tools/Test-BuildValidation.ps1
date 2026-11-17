$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. (Join-Path $PSScriptRoot 'Build-Validation.ps1')
$target=Join-Path $root ('.build/validation-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $target|Out-Null
function Must-Fail([scriptblock]$Action){$failed=$false;try{& $Action}catch{$failed=$true};if(!$failed){throw 'La guarda no rechazó el caso inválido.'}}
try {
    Must-Fail {Assert-GitCheckout $target}
    foreach($name in @('NOTAS_VERSION.md','MIGRACION.md','COMPROBACIONES.md','AUDITORIA.md','REVISION_2026-11-17.md')) {
        Set-Content -LiteralPath (Join-Path $target $name) -Value '# Documento 1.2.3'
    }
    Assert-VersionDocuments $target '1.2.3'
    Set-Content -LiteralPath (Join-Path $target 'NOTAS_VERSION.md') -Value '# Usage Viewer 1.2.2'
    Must-Fail {Assert-VersionDocuments $target '1.2.3'}
    $trx=Join-Path $target 'fake.trx'
    Set-Content $trx '<TestRun><ResultSummary><Counters total="10" executed="10" passed="10" failed="0" notExecuted="0" /></ResultSummary></TestRun>'
    Assert-TestResults $trx 10
    Must-Fail {Assert-TestResults $trx 11}
    Set-Content $trx '<TestRun><ResultSummary><Counters total="10" executed="9" passed="9" failed="0" notExecuted="1" /></ResultSummary></TestRun>'
    Must-Fail {Assert-TestResults $trx 9}
    Write-Output 'OK: guardas de clon Git, versión documental, mínimo de pruebas y omitidas.'
}
finally {Remove-OwnedBuildDirectory $root $target}
