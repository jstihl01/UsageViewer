param([ValidateSet('Prepare','Publish','Verify')][string]$Mode='Verify')
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$root=Split-Path $PSScriptRoot -Parent
$version=([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$tag="v$version"
$repo='jstihl01/UsageViewer'
$api="https://api.github.com/repos/$repo"
$files=@('UsageViewer-Setup-x64.exe','UsageViewer-Setup-x64.exe.sha256','UsageViewer-SBOM.cdx.json',
    'UsageViewer-package-manifest.json','UsageViewer-provenance.json','UsageViewer-provenance.sigstore.json',
    'UsageViewer-SBOM.sigstore.json','UsageViewer-MIGRACION.md','UsageViewer-COMPROBACIONES.md','UsageViewer-AUDITORIA.md')
$token=$env:GITHUB_TOKEN
if(!$token) {
    $env:GIT_TERMINAL_PROMPT='0'
    $lines=@('protocol=https','host=github.com','') | git -c credential.interactive=never credential fill
    if($LASTEXITCODE) {throw 'No hay sesión de GitHub disponible.'}
    foreach($line in $lines) {if($line.StartsWith('password=')) {$token=$line.Substring(9)}}
    $lines=$null
}
if(!$token) {throw 'No hay sesión de GitHub disponible.'}
$headers=@{Authorization="Bearer $token";Accept='application/vnd.github+json';
    'X-GitHub-Api-Version'='2026-03-10';'User-Agent'='UsageViewer-Release'}
$token=$null
function Api([string]$Method,[string]$Uri,$Body=$null,[string]$File='') {
    $uriObject=[Uri]$Uri
    if($uriObject.Scheme -ne 'https' -or $uriObject.Host -notin @('api.github.com','uploads.github.com')) {
        throw 'Destino de publicación no válido.'
    }
    $params=@{Method=$Method;Uri=$Uri;Headers=$headers;TimeoutSec=900}
    if($File) {$params.InFile=$File;$params.ContentType='application/octet-stream'}
    elseif($null -ne $Body) {$params.Body=$Body|ConvertTo-Json -Depth 12;$params.ContentType='application/json; charset=utf-8'}
    try {
        $response=Invoke-RestMethod @params
        foreach($entry in $response) {$entry}
    }
    catch {throw "GitHub rechazó $Method $($uriObject.AbsolutePath): HTTP $($_.Exception.Response.StatusCode)."}
}
function Assert-Assets($Release) {
    if($Release.assets.Count -ne $files.Count) {throw 'El número de activos no coincide con la entrega prevista.'}
    foreach($name in $files) {
        $path=Join-Path $root "dist/$name"
        $local=Get-Item -LiteralPath $path
        $asset=$Release.assets|Where-Object name -eq $name
        $hash=(Get-FileHash -LiteralPath $path).Hash.ToLowerInvariant()
        if(!$asset -or $asset.state -ne 'uploaded' -or $asset.size -ne $local.Length -or $asset.digest -ne "sha256:$hash") {
            throw "No coincide el activo verificado: $name"
        }
    }
}
foreach($name in $files) {
    if(!(Test-Path -LiteralPath (Join-Path $root "dist/$name"))) {throw "Falta el archivo de entrega: $name"}
}
$record=Get-Content (Join-Path $root 'dist/UsageViewer-provenance.json') -Raw|ConvertFrom-Json
$manifest=Get-Content (Join-Path $root 'dist/UsageViewer-package-manifest.json') -Raw|ConvertFrom-Json
$commit=(& git -C $root rev-parse HEAD).Trim()
if($record.sourceDirty -or $manifest.sourceDirty -or $record.version -ne $version -or $manifest.version -ne $version -or
    $record.commit -ne $manifest.commit -or ($Mode -ne 'Verify' -and $record.commit -ne $commit)) {
    throw 'La entrega no corresponde a un commit limpio y a esta versión.'
}
$setup=Join-Path $root 'dist/UsageViewer-Setup-x64.exe'
$sha=(Get-FileHash $setup).Hash.ToLowerInvariant()
$shaFile=(Get-Content "$setup.sha256" -Raw).Split(' ')[0].Trim()
if($record.installer.sha256 -ne $sha -or $shaFile -ne $sha) {throw 'El SHA-256 del instalador no coincide.'}
if($Mode -ne 'Verify') {
    if($env:GITHUB_ACTIONS -ne 'true' -or $record.workflowRun -ne $env:GITHUB_RUN_ID -or $record.workflowCommit -ne $commit) {
        throw 'La publicación requiere la compilación y atestación del flujo oficial de GitHub.'
    }
}
$releases=@(Api GET "$api/releases?per_page=100")
$release=$releases|Where-Object tag_name -eq $tag|Select-Object -First 1
if($Mode -eq 'Verify') {
    if(!$release) {throw 'No existe la versión solicitada.'}
    $reference=Api GET "$api/git/ref/tags/$tag"
    for($i=0;$reference.object.type -eq 'tag' -and $i -lt 5;$i++) {
        $reference=Api GET "$api/git/tags/$($reference.object.sha)"
    }
    if($reference.object.type -ne 'commit' -or $reference.object.sha -ne $record.commit) {
        throw 'La etiqueta publicada no coincide con la procedencia del artefacto.'
    }
    Assert-Assets $release
    Write-Output "OK: todos los activos de $tag coinciden en tamaño y SHA-256."
    exit
}
if($release -and !$release.draft) {throw 'La versión ya está publicada; no se sobrescribe.'}
if(!$release) {
    $notes=Get-Content (Join-Path $root 'NOTAS_VERSION.md') -Raw
    $release=Api POST "$api/releases" @{tag_name=$tag;target_commitish=$commit;name="Usage Viewer $version";
        body=$notes;draft=$true;prerelease=$false}
}
foreach($name in $files) {
    $path=Join-Path $root "dist/$name"
    $asset=$release.assets|Where-Object name -eq $name
    if($asset) {
        $hash=(Get-FileHash $path).Hash.ToLowerInvariant()
        if($asset.digest -eq "sha256:$hash" -and $asset.size -eq (Get-Item $path).Length) {continue}
        Api DELETE "$api/releases/assets/$($asset.id)"|Out-Null
    }
    $upload=$release.upload_url.Split('{')[0]+'?name='+[Uri]::EscapeDataString($name)
    Api POST $upload -File $path|Out-Null
}
$release=Api GET "$api/releases/$($release.id)"
Assert-Assets $release
Write-Output "OK: borrador $tag, todos los activos verificados."
if($Mode -eq 'Publish') {
    $release=Api PATCH "$api/releases/$($release.id)" @{draft=$false;make_latest='true'}
    Assert-Assets $release
    $latest=Api GET "$api/releases/latest"
    if($latest.id -ne $release.id) {throw 'La versión no aparece como la más reciente.'}
    Write-Output "Publicado: $($release.html_url)"
}
