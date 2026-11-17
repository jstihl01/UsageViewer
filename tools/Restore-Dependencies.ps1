param([switch]$SecurityTools)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $root '.build'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$pins=Get-Content (Join-Path $root 'dependencies.json') -Raw | ConvertFrom-Json

function Get-VerifiedFile([string]$Url,[string]$Path,[string]$Hash,[string]$Algorithm='SHA256') {
    if((Test-Path -LiteralPath $Path) -and (Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash -ne $Hash) {
        Remove-Item -LiteralPath $Path -Force
    }
    if(!(Test-Path -LiteralPath $Path)) {
        Write-Output ("Descargando "+[IO.Path]::GetFileName($Path))
        $partial=$Path+'.partial'
        try {
            Invoke-WebRequest -Uri $Url -OutFile $partial -TimeoutSec 600
            if((Get-FileHash -LiteralPath $partial -Algorithm $Algorithm).Hash -ne $Hash) {throw 'La descarga no coincide con su huella.'}
            Move-Item -LiteralPath $partial -Destination $Path -Force
        }
        finally {if(Test-Path -LiteralPath $partial){Remove-Item -LiteralPath $partial -Force}}
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
$archive=Join-Path $cache ('dotnet-sdk-'+$pins.sdk.version+'-win-x64.zip')
Get-VerifiedFile $pins.sdk.url $archive $pins.sdk.sha512 'SHA512'
$marker=Join-Path $sdk '.verified-extraction.json'
if(!(Test-Path (Join-Path $sdk ('sdk\'+$pins.sdk.version+'\dotnet.dll')))) {
    New-Item -ItemType Directory -Path $sdk -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $sdk -Force
}
# Verify the executed SDK host and assemblies against the authenticated archive, not just existence.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach($name in @('dotnet.exe',('sdk/'+$pins.sdk.version+'/dotnet.dll'),('sdk/'+$pins.sdk.version+'/NuGet.Build.Tasks.dll'),
        ('packs/Microsoft.NETCore.App.Host.win-x64/'+$pins.runtime+'/runtimes/win-x64/native/apphost.exe'))) {
        $entry=$zip.GetEntry($name)
        if(!$entry){throw "Falta un componente del SDK: $name"}
        $stream=$entry.Open()
        try {$expected=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))} finally {$stream.Dispose()}
        if((Get-FileHash -LiteralPath (Join-Path $sdk $name)).Hash -ne $expected){throw "La caché del SDK cambió: $name. Retira .build/dotnet y recompila."}
    }
} finally {$zip.Dispose()}
@{sha512=$pins.sdk.sha512;version=$pins.sdk.version}|ConvertTo-Json|Set-Content -LiteralPath $marker -Encoding utf8
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
foreach($stub in $pins.compiler.stubs) {
    if((Get-FileHash -LiteralPath (Join-Path (Split-Path $compiler) $stub.file)).Hash -ne $stub.sha256) {throw "Cambió un stub de Inno Setup: $($stub.file)"}
}
if($SecurityTools) {
    $archive=Join-Path $cache 'gitleaks.zip'
    Get-VerifiedFile $pins.gitleaks.url $archive $pins.gitleaks.sha256
    $target=Join-Path $cache 'gitleaks'
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Expand-Archive -LiteralPath $archive -DestinationPath $target -Force
}
Write-Output 'OK: SDK, servidor, compilador y avisos verificados.'
