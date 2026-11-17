function Assert-GitCheckout([string]$Root) {
    if(!(Get-Command git -ErrorAction SilentlyContinue)){throw 'Se requiere Git. Compila desde un clon del repositorio para registrar la procedencia.'}
    $inside=& git -C $Root rev-parse --is-inside-work-tree 2>$null
    if($LASTEXITCODE -or $inside -ne 'true'){throw 'Compila desde un clon de Git; un ZIP no contiene el commit necesario para la procedencia.'}
    $top=& git -C $Root rev-parse --show-toplevel 2>$null
    if([IO.Path]::GetFullPath($top.Trim()) -ne [IO.Path]::GetFullPath($Root)){throw 'Compila desde la raíz de un clon de Usage Viewer; no desde un ZIP dentro de otro repositorio.'}
}
function Assert-VersionDocuments([string]$Root,[string]$Version) {
    foreach($name in @('NOTAS_VERSION.md','MIGRACION.md','COMPROBACIONES.md','AUDITORIA.md','REVISION_2026-11-17.md')) {
        $first=Get-Content -LiteralPath (Join-Path $Root $name) -TotalCount 1
        if($first -notmatch ('\b'+[regex]::Escape($Version)+'\b')){throw "La cabecera de $name no corresponde a $Version."}
    }
}
function Assert-TestResults([string]$Path,[int]$Minimum) {
    [xml]$trx=Get-Content -LiteralPath $Path -Raw
    $c=$trx.TestRun.ResultSummary.Counters
    if([int]$c.executed -lt $Minimum -or [int]$c.notExecuted -ne 0 -or [int]$c.failed -ne 0 -or [int]$c.passed -ne [int]$c.total) {
        throw "Pruebas incompletas: $Path. Se exigen al menos $Minimum ejecutadas, sin fallos ni omitidas."
    }
}
function Assert-Compiler([string]$Compiler,$Pins) {
    if((Get-FileHash -LiteralPath $Compiler).Hash -ne $Pins.compiler.sha256){throw 'El compilador no coincide con el fijado.'}
    foreach($stub in $Pins.compiler.stubs) {
        if((Get-FileHash -LiteralPath (Join-Path (Split-Path $Compiler) $stub.file)).Hash -ne $stub.sha256){throw "El stub $($stub.file) cambió."}
    }
}
function Assert-RuntimePayload([string]$Stage,[string]$Packages,$Pins) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $candidates=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($package in $Pins.runtimePackages) {
        $path=Join-Path $Packages ($package.id.ToLowerInvariant()+'/'+$Pins.runtime+'/'+$package.id.ToLowerInvariant()+'.'+$Pins.runtime+'.nupkg')
        $zip=[IO.Compression.ZipFile]::OpenRead($path)
        try {
            foreach($entry in $zip.Entries) {
                if($entry.FullName -notmatch '^runtimes/win-x64/(?:native|lib/[^/]+)/(.+)$'){continue}
                $relative=$Matches[1]
                $target=Join-Path $Stage $relative
                if(!(Test-Path -LiteralPath $target -PathType Leaf)){continue}
                $stream=$entry.Open()
                try {$hash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))} finally {$stream.Dispose()}
                if(!$candidates.ContainsKey($relative)){$candidates[$relative]=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)}
                $candidates[$relative].Add($hash)|Out-Null
            }
        } finally {$zip.Dispose()}
    }
    # WindowsDesktop intentionally replaces NETCore facades such as WindowsBase.dll.
    # Accept only bytes present in one of the authenticated, pinned runtime packs.
    foreach($relative in $candidates.Keys) {
        if(!$candidates[$relative].Contains((Get-FileHash -LiteralPath (Join-Path $Stage $relative)).Hash)) {
            throw "El runtime publicado no coincide con los paquetes autenticados: $relative"
        }
    }
    foreach($name in @('System.Private.CoreLib.dll','PresentationFramework.dll','coreclr.dll')) {
        if(!(Test-Path -LiteralPath (Join-Path $Stage $name))){throw "Falta un componente de runtime: $name"}
    }
}
function Remove-OwnedBuildDirectory([string]$Root,[string]$Path) {
    $cache=[IO.Path]::GetFullPath((Join-Path $Root '.build'))+[IO.Path]::DirectorySeparatorChar
    $full=[IO.Path]::GetFullPath($Path)
    if(!$full.StartsWith($cache,[StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $full).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'Ruta de limpieza fuera de la caché del proyecto.'
    }
    Remove-Item -LiteralPath $full -Recurse -Force
}
