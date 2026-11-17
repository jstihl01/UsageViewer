param([Parameter(Mandatory)][string[]]$CurrentNames,[Parameter(Mandatory)][string]$OutFile)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$inventories=@(Get-Item -LiteralPath (Join-Path $root 'installer/legacy-1.0.15.json'))
if(Test-Path (Join-Path $root 'installer/legacy')){$inventories+=Get-ChildItem -LiteralPath (Join-Path $root 'installer/legacy') -File -Filter '*.json'}
$lines=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($file in $inventories) {
    $old=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json
    foreach($entry in $old.files) {
        $relative=$entry.path.Replace('\','/')
        if($relative -match '(^|/)\.\.(/|$)|^[\\/]|:' -or $entry.sha256 -notmatch '^[a-fA-F0-9]{64}$'){throw 'Inventario heredado no válido.'}
        if($relative -notin $CurrentNames){$lines.Add("  DeleteLegacyIfHashMatches('"+$entry.path.Replace("'","''")+"', '"+$entry.sha256+"');")|Out-Null}
    }
}
$lines|Sort-Object|Set-Content -LiteralPath $OutFile -Encoding utf8
