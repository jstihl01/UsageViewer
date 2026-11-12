param([string]$Compiler = "$PSScriptRoot/.build/InnoSetup/ISCC.exe")

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

& "$PSScriptRoot/tools/Generate-AppLogo.ps1"

$codex = '.build/codex-app-server.exe'
$codexHash = 'D781E2C96CAEAE19F5C8280814D778B92D4958BCCA86064D6BEC09D789AE7E22'
if ((Get-FileHash $codex -Algorithm SHA256).Hash -ne $codexHash) {
    throw 'El servidor de Codex no coincide con la versión fijada (0.167.0).'
}

dotnet publish src/UsageViewer.csproj -c Release -r win-x64 --self-contained true --ignore-failed-sources -o .build/publish
if ($LASTEXITCODE) { throw 'Falló la publicación de Usage Viewer.' }

$package = '.build/publish'
New-Item -ItemType Directory -Force "$package/codex" | Out-Null
Copy-Item $codex "$package/codex/codex-app-server.exe"
Copy-Item '.build/Codex-LICENSE.txt' "$package/codex/LICENSE.txt"
Copy-Item '.build/Codex-NOTICE.download' "$package/codex/NOTICE.txt"

$assets = Get-Content src/obj/project.assets.json -Raw | ConvertFrom-Json
$roots = $assets.packageFolders.PSObject.Properties.Name
$downloads = $assets.project.frameworks.PSObject.Properties.Value.downloadDependencies
New-Item -ItemType Directory -Force "$package/licenses" | Out-Null
foreach ($name in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $dependency = $downloads | Where-Object name -eq $name
    $version = ($dependency.version -split ',')[0].Trim('[', ']', ' ')
    $relative = Join-Path $name.ToLowerInvariant() $version
    $runtime = $roots | ForEach-Object { Join-Path $_ $relative } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
    if (!$runtime) { throw "No se encontraron los avisos de licencia de $name." }
    $notices = Get-ChildItem $runtime -File | Where-Object Name -Match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$'
    if (!$notices) { throw "No se encontraron los avisos de licencia de $name." }
    foreach ($notice in $notices) {
        $destination = "$package/licenses/$name-$($notice.BaseName).txt"
        Copy-Item $notice.FullName $destination
    }
}

dotnet build tests/Checks.csproj -c Release --ignore-failed-sources
if ($LASTEXITCODE) { throw 'Falló la compilación de las pruebas.' }
$testOutput = 'tests/bin/Release/net8.0'
$testCodex = "$testOutput/codex/codex-app-server.exe"
New-Item -ItemType Directory -Force "$testOutput/codex" | Out-Null
if (!(Test-Path $testCodex) -or (Get-FileHash $testCodex).Hash -ne $codexHash) {
    Copy-Item $codex $testCodex -Force
}
dotnet "$testOutput/Checks.dll" ".build/smoke-$([Guid]::NewGuid().ToString('N'))"
if ($LASTEXITCODE) { throw 'Fallaron las comprobaciones automatizadas.' }

dotnet run --project tests/WindowsChecks.csproj -c Release -p:RestoreIgnoreFailedSources=true -- .build/tray-ring-preview.png
if ($LASTEXITCODE) { throw 'Fallaron las comprobaciones del icono de Windows.' }

$log = & $Compiler installer/UsageViewer.iss
$compileExit = $LASTEXITCODE
$log | Set-Content -Encoding UTF8 .build/inno-build.log
if ($compileExit) { $log | Write-Output; throw 'Falló la compilación del instalador.' }
$expected = @(
    'UsageViewer.exe', 'UsageViewer.dll', 'UsageViewer.runtimeconfig.json',
    'coreclr.dll', 'PresentationFramework.dll', 'codex\codex-app-server.exe',
    'codex\LICENSE.txt', 'codex\NOTICE.txt',
    'licenses\Microsoft.NETCore.App.Runtime.win-x64-LICENSE.txt',
    'licenses\Microsoft.NETCore.App.Runtime.win-x64-THIRD-PARTY-NOTICES.txt',
    'licenses\Microsoft.WindowsDesktop.App.Runtime.win-x64-LICENSE.txt'
)
foreach ($file in $expected) {
    $suffix = '\publish\' + $file
    if (!(Test-Path (Join-Path $package $file)) -or
        !($log | Where-Object { $_.TrimEnd().EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase) })) {
        throw "El compilador no confirmó la inclusión de $file."
    }
}
$setup = Get-Item dist/UsageViewer-Setup-x64.exe
$hash = (Get-FileHash $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash *$($setup.Name)" | Set-Content -Encoding ASCII "$($setup.FullName).sha256"
Write-Output "Instalador: $($setup.FullName)"
Write-Output "SHA-256: $hash"
