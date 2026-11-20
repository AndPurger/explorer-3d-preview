# Remove a previa 3D do Explorer (pede admin uma vez).
$clsid = "{7A3E5B1D-4C2F-4E8A-9B61-3D0F5C2A8E47}"
$previewKey = "{8895b1c6-b41f-4c1c-a562-0d564250836f}"
foreach ($ext in ".3mf", ".stl", ".obj") {
    foreach ($base in "HKCU:\Software\Classes\$ext", "HKCU:\Software\Classes\SystemFileAssociations\$ext") {
        $k = "$base\shellex\$previewKey"
        if ((Test-Path $k) -and ((Get-Item $k).GetValue("") -eq $clsid)) { Remove-Item $k -Force }
    }
}
$script = Join-Path $PSScriptRoot "register-machine.ps1"
Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $script + '"'), "-Remove"
Write-Host "Previa 3D removida."
