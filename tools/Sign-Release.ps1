param([Parameter(Mandatory)][string[]]$Files,
    [string]$Thumbprint=$env:USAGEVIEWER_SIGN_THUMBPRINT,
    [string]$SignTool=$env:USAGEVIEWER_SIGNTOOL)
$ErrorActionPreference='Stop'
if(!$Thumbprint -or $Thumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Hace falta la huella pública de un certificado de firma válido.' }
if(!$SignTool) { $SignTool=(Get-Command signtool.exe -ErrorAction Stop).Source }
if((Get-AuthenticodeSignature $SignTool).Status -ne 'Valid') { throw 'SignTool no tiene una firma válida.' }
foreach($file in $Files) {
    & $SignTool sign /sha1 $Thumbprint /fd SHA256 /tr 'http://timestamp.digicert.com' /td SHA256 /d 'Usage Viewer' $file
    if($LASTEXITCODE) { throw 'Falló la firma del artefacto.' }
    if((Get-AuthenticodeSignature -LiteralPath $file).Status -ne 'Valid') { throw 'No se pudo verificar la firma generada.' }
}
