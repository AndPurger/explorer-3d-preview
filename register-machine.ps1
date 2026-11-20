# Instala em C:\Program Files e registra a classe COM em HKLM (precisa de admin).
# O prevhost.exe roda com permissoes restritas: so carrega handlers registrados na maquina
# e nao consegue ler a pasta AppData do usuario.
# Uso (elevado): register-machine.ps1 -Src "<pasta bin>"   |   register-machine.ps1 -Remove
param([string]$Src, [switch]$Remove)
$ErrorActionPreference = "Stop"
$clsid = "{7A3E5B1D-4C2F-4E8A-9B61-3D0F5C2A8E47}"
$name = "Previa 3D (3MF/STL/OBJ)"
$dest = Join-Path $env:ProgramFiles "Bambu3DPreview"
$c = "HKLM:\Software\Classes\CLSID\$clsid"
$ph = "HKLM:\Software\Microsoft\Windows\CurrentVersion\PreviewHandlers"

$previewKey = "{8895b1c6-b41f-4c1c-a562-0d564250836f}"
$backup = "HKLM:\Software\Bambu3DPreview\OriginalHandlers"

Get-Process prevhost -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
if ($Remove) {
    foreach ($ext in ".3mf", ".stl", ".obj") {
        $k = "HKLM:\Software\Classes\$ext\shellex\$previewKey"
        if ((Test-Path $k) -and ((Get-Item $k).GetValue("") -eq $clsid)) {
            $orig = $null
            if (Test-Path $backup) { $orig = (Get-ItemProperty $backup -ErrorAction SilentlyContinue).$ext }
            if ($orig) { Set-ItemProperty $k "(default)" $orig } else { Remove-Item $k -Force }
        }
    }
    Remove-Item "HKLM:\Software\Bambu3DPreview" -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $c -Recurse -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty $ph $clsid -ErrorAction SilentlyContinue
    Remove-Item $dest -Recurse -Force -ErrorAction SilentlyContinue
    return
}
New-Item -ItemType Directory -Force $dest | Out-Null
Copy-Item (Join-Path $Src "Bambu3DPreview.dll"), (Join-Path $Src "Viewer.exe") $dest -Force
$dll = Join-Path $dest "Bambu3DPreview.dll"

if (-not (Test-Path $c)) { New-Item $c | Out-Null }
Set-ItemProperty $c "(default)" $name
Set-ItemProperty $c "DisplayName" $name
Set-ItemProperty $c "AppID" "{6d2b5079-2f0b-48dd-ab7f-97cec514d30b}"
New-ItemProperty $c "DisableLowILProcessIsolation" -Value 1 -PropertyType DWord -Force | Out-Null
$cat = "$c\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}"
if (-not (Test-Path $cat)) { New-Item -Force $cat | Out-Null }
$ip = "$c\InprocServer32"
if (-not (Test-Path $ip)) { New-Item $ip | Out-Null }
Set-ItemProperty $ip "(default)" "C:\Windows\System32\mscoree.dll"
Set-ItemProperty $ip "ThreadingModel" "Apartment"
Set-ItemProperty $ip "Class" "Bambu3DPreview.PreviewHandler"
Set-ItemProperty $ip "Assembly" "Bambu3DPreview, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null"
Set-ItemProperty $ip "RuntimeVersion" "v4.0.30319"
Set-ItemProperty $ip "CodeBase" ("file:///" + $dll.Replace('\', '/'))
Set-ItemProperty $ph $clsid $name

# o painel de visualizacao do Explorer so usa a associacao de maquina (HKLM)
if (-not (Test-Path $backup)) { New-Item -Force $backup | Out-Null }
foreach ($ext in ".3mf", ".stl", ".obj") {
    $k = "HKLM:\Software\Classes\$ext\shellex\$previewKey"
    if (Test-Path $k) {
        $cur = (Get-Item $k).GetValue("")
        if ($cur -and $cur -ne $clsid) { Set-ItemProperty $backup $ext $cur }
    } else {
        New-Item -Force $k | Out-Null
    }
    Set-ItemProperty $k "(default)" $clsid
}
