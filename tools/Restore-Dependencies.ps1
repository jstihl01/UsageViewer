param([switch]$SecurityTools)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root '.build'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$pins=Get-Content (Join-Path $root 'dependencies.json') -Raw | ConvertFrom-Json

function Get-VerifiedFile([string]$Url,[string]$Path,[string]$Hash,[string]$Algorithm='SHA256') {
    if(!(Test-Path -LiteralPath $Path)) {
        Write-Output ("Descargando "+[IO.Path]::GetFileName($Path))
        Invoke-WebRequest -Uri $Url -OutFile $Path -TimeoutSec 600
    }
    if((Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash -ne $Hash) {
        throw "La descarga no coincide con la huella fijada: $([IO.Path]::GetFileName($Path))."
    }
}
function Assert-Publisher([string]$Path,[string]$Publisher) {
    $signature=Get-AuthenticodeSignature -LiteralPath $Path
    if($signature.Status -ne 'Valid' -or !$signature.SignerCertificate.Subject.Contains($Publisher)) {
        throw "La firma del proveedor no es válida: $([IO.Path]::GetFileName($Path))."
    }
}

$sdk=Join-Path $cache 'dotnet'
if(!(Test-Path (Join-Path $sdk 'sdk\10.0.404\dotnet.dll'))) {
    $archive=Join-Path $cache 'dotnet-sdk-10.0.404-win-x64.zip'
    Get-VerifiedFile $pins.sdk.url $archive $pins.sdk.sha512 'SHA512'
    New-Item -ItemType Directory -Path $sdk -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $sdk -Force
}
$server=Join-Path $cache 'codex-app-server.exe'
Get-VerifiedFile $pins.codex.url $server $pins.codex.sha256
Assert-Publisher $server $pins.codex.publisher
foreach($notice in $pins.notices) {
    Get-VerifiedFile $notice.url (Join-Path $cache $notice.file) $notice.sha256
}
$compiler=Join-Path $cache 'InnoSetup\ISCC.exe'
if(!(Test-Path -LiteralPath $compiler)) {
    $setup=Join-Path $cache 'inno.exe'
    Get-VerifiedFile $pins.compiler.url $setup $pins.compiler.installerSha256
    Assert-Publisher $setup $pins.compiler.publisher
    $location=Join-Path $cache 'InnoSetup'
    $run=Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',
        '/CURRENTUSER','/NOICONS',('/DIR="'+$location+'"')) -Wait -PassThru -WindowStyle Hidden
    if($run.ExitCode -ne 0) { throw 'No se pudo preparar Inno Setup.' }
}
if((Get-FileHash $compiler).Hash -ne $pins.compiler.sha256) { throw 'El compilador Inno Setup no coincide con el fijado.' }
Assert-Publisher $compiler $pins.compiler.publisher
if($SecurityTools) {
    $archive=Join-Path $cache 'gitleaks.zip'
    Get-VerifiedFile $pins.gitleaks.url $archive $pins.gitleaks.sha256
    $target=Join-Path $cache 'gitleaks'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
}
Write-Output 'OK: SDK, servidor, compilador y avisos verificados.'
