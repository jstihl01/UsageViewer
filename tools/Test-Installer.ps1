param([string]$Installer=(Join-Path (Split-Path $PSScriptRoot -Parent) 'dist/UsageViewer-Setup-x64.exe'))
$ErrorActionPreference='Stop'
if($env:GITHUB_ACTIONS -ne 'true' -or !$env:RUNNER_TEMP){throw 'Esta prueba solo puede ejecutarse en un runner efímero de GitHub; no en el escritorio del usuario.'}
$root=Split-Path $PSScriptRoot -Parent
$target=Join-Path $env:RUNNER_TEMP ('UsageViewer-Smoke-'+[Guid]::NewGuid().ToString('N'))
$absolute=[IO.Path]::GetFullPath($target)
if(!$absolute.StartsWith([IO.Path]::GetFullPath($env:RUNNER_TEMP)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Ruta de prueba fuera del runner.'}
$profile=Join-Path $env:LOCALAPPDATA 'UsageViewer'
if(Test-Path -LiteralPath $profile){throw 'El runner ya contiene un perfil de Usage Viewer; no se modifica.'}
$logs=Join-Path $root '.build/installer-smoke'
New-Item -ItemType Directory -Force -Path $logs,$target|Out-Null
$checks=[Collections.Generic.List[object]]::new()
function Run-Hidden([string]$File,[string[]]$Arguments,[int]$Expected=0) {
    $run=Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -WindowStyle Hidden
    if(!$run.WaitForExit(90000)){try{$run.Kill()}catch{};throw 'Plazo de instalación agotado.'}
    if($run.ExitCode -ne $Expected){throw "Código de instalación inesperado: $($run.ExitCode), esperado: $Expected"}
}
function Check([string]$Name,[bool]$Passed){$checks.Add(@{name=$Name;passed=$Passed});if(!$Passed){throw "No se cumple: $Name"}}
function Verify-Installed {
    $manifest=Get-Content (Join-Path $target 'package-manifest.json') -Raw|ConvertFrom-Json
    foreach($file in $manifest.files){Check ('installed:'+ $file.path) ((Get-FileHash -LiteralPath (Join-Path $target $file.path)).Hash -eq $file.sha256)}
}
try {
    # An authentic obsolete runtime file tests the matching-hash branch; a changed legacy name
    # tests preservation. The old DLL is test data only and is never loaded or executed.
    $legacy=Get-Content (Join-Path $root 'installer/legacy-1.0.15.json') -Raw|ConvertFrom-Json
    $current=Get-Content (Join-Path $root 'dist/UsageViewer-package-manifest.json') -Raw|ConvertFrom-Json
    $obsolete=$legacy.files|Where-Object {$_.path -notin $current.files.path -and $_.path -notmatch '[/\\]' }
    $owned=$obsolete|Where-Object path -match '^mscordaccore_.*_8\.0\.'|Select-Object -First 1
    if(!$owned){throw 'Falta el archivo de prueba de migración heredada.'}
    $package=Join-Path $logs 'legacy-runtime.nupkg'
    Invoke-WebRequest 'https://api.nuget.org/v3-flatcontainer/microsoft.netcore.app.runtime.win-x64/8.0.34/microsoft.netcore.app.runtime.win-x64.8.0.34.nupkg' -OutFile $package -TimeoutSec 60
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip=[IO.Compression.ZipFile]::OpenRead($package)
    try {
        $entry=$zip.GetEntry('runtimes/win-x64/native/'+$owned.path)
        if(!$entry -or $entry.Length -ne $owned.bytes){throw 'El componente histórico no coincide con su inventario.'}
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $target $owned.path))
    } finally {$zip.Dispose();Remove-Item -LiteralPath $package -Force}
    if((Get-FileHash -LiteralPath (Join-Path $target $owned.path)).Hash -ne $owned.sha256){throw 'La huella del componente histórico no coincide.'}
    $foreign=$obsolete|Where-Object path -ne $owned.path|Select-Object -First 1
    $known=Join-Path $target $foreign.path;[IO.File]::WriteAllText($known,'not an owned legacy binary')
    $foreignHash=(Get-FileHash $known).Hash
    $args=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$target+'"'),('/LOG="'+(Join-Path $logs 'install.log')+'"'))
    Run-Hidden $Installer $args
    Verify-Installed
    Check 'matching-legacy-hash-removed' (!(Test-Path -LiteralPath (Join-Path $target $owned.path)))
    Check 'foreign-file-preserved' ((Get-FileHash $known).Hash -eq $foreignHash)
    $exe=Join-Path $target 'UsageViewer.exe'
    Run-Hidden $exe @('--shutdown')
    $app=Start-Process -FilePath $exe -ArgumentList '--background' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3;Check 'background-start' (!$app.HasExited)
    # Update while the app holds its mutex. The installer must signal it and restart in background.
    Run-Hidden $Installer @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$target+'"'),('/LOG="'+(Join-Path $logs 'upgrade.log')+'"'))
    Check 'old-instance-closed' $app.HasExited
    $upgrade=Get-Content (Join-Path $logs 'upgrade.log') -Raw
    Check 'sid-resolved-and-upgrade-detected' ($upgrade -match 'UsageViewer detected running: yes')
    Verify-Installed
    Run-Hidden $exe @('--shutdown')
    Run-Hidden (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $logs 'uninstall.log')+'"'))
    Check 'profile-removed' (!(Test-Path -LiteralPath $profile))
    Check 'owned-executable-removed' (!(Test-Path -LiteralPath $exe))
    Check 'foreign-file-survives-uninstall' ((Get-FileHash $known).Hash -eq $foreignHash)
    Remove-Item -LiteralPath $known -Force
    if(Test-Path -LiteralPath $target){Remove-Item -LiteralPath $target -Force}
    # A second clean install must disappear completely when uninstalled.
    Run-Hidden $Installer $args
    $exe=Join-Path $target 'UsageViewer.exe';$missing=$exe+'.missing'
    Move-Item -LiteralPath $exe -Destination $missing
    Run-Hidden (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $logs 'missing-exe.log')+'"')) 1
    Check 'silent-missing-exe-fails-with-instructions' ((Get-Content (Join-Path $logs 'missing-exe.log') -Raw) -match 'Reinstala Usage Viewer')
    Move-Item -LiteralPath $missing -Destination $exe
    Run-Hidden (Join-Path $target 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+(Join-Path $logs 'uninstall-clean.log')+'"'))
    Check 'clean-install-directory-removed' (!(Test-Path -LiteralPath $target))
    Check 'clean-profile-removed' (!(Test-Path -LiteralPath $profile))
}
finally {$checks|ConvertTo-Json -Depth 5|Set-Content (Join-Path $logs 'results.json') -Encoding utf8}
Write-Output 'OK: instalación, actualización con app abierta, cierre, desinstalación y ejecutable ausente.'
