# Instala a previa 3D (3MF/STL/OBJ) no painel de visualizacao do Explorer.
# Pede admin uma vez (UAC): a DLL vai para C:\Program Files\Bambu3DPreview e a classe e registrada em HKLM.
$ErrorActionPreference = "Stop"
$clsid = "{7A3E5B1D-4C2F-4E8A-9B61-3D0F5C2A8E47}"
$previewKey = "{8895b1c6-b41f-4c1c-a562-0d564250836f}"

$script = Join-Path $PSScriptRoot "register-machine.ps1"
$src = Join-Path $PSScriptRoot "bin"
$p = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $script + '"'), "-Src", ('"' + $src + '"')
if ($p.ExitCode -ne 0) { throw "Falha no registro (codigo $($p.ExitCode))" }

# associa as extensoes ao handler (por usuario; tem prioridade sobre o leitor 3MF da Microsoft)
foreach ($ext in ".3mf", ".stl", ".obj") {
    foreach ($base in "HKCU:\Software\Classes\$ext", "HKCU:\Software\Classes\SystemFileAssociations\$ext") {
        $k = "$base\shellex\$previewKey"
        if (-not (Test-Path $k)) { New-Item -Force $k | Out-Null }
        Set-ItemProperty $k "(default)" $clsid
    }
}

# limpa a instalacao antiga, apenas por usuario
Remove-Item "HKCU:\Software\Classes\CLSID\$clsid" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers" $clsid -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:LOCALAPPDATA "Bambu3DPreview") -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "Instalado. No Explorer, ative o painel de visualizacao (Alt+P) e selecione um arquivo 3D."
