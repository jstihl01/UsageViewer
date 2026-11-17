param([Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$repo=([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.RepositoryUrl
$api='https://api.github.com/repos/'+([Uri]$repo).AbsolutePath.Trim('/')
$release=Invoke-RestMethod "$api/releases/tags/v$Version"
$asset=$release.assets|Where-Object name -eq 'UsageViewer-package-manifest.json'
if(!$asset -or $asset.digest -notmatch '^sha256:([a-f0-9]{64})$'){throw 'No existe un inventario publicado con huella verificable.'}
$sha=$Matches[1]
$folder=Join-Path $root 'installer/legacy';New-Item -ItemType Directory -Path $folder -Force|Out-Null
$temporary=Join-Path $folder ($Version+'.json.partial')
try {
    Invoke-WebRequest $asset.browser_download_url -OutFile $temporary
    if((Get-FileHash -LiteralPath $temporary).Hash -ne $sha){throw 'El inventario descargado no coincide con GitHub.'}
    $manifest=Get-Content -LiteralPath $temporary -Raw|ConvertFrom-Json
    if($manifest.version -ne $Version -or $manifest.sourceDirty){throw 'El inventario no procede de una versión limpia.'}
    Move-Item -LiteralPath $temporary -Destination (Join-Path $folder ($Version+'.json')) -Force
}
finally {if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
Write-Output "Importado inventario público v$Version, SHA-256 $sha."
