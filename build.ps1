# Compila a DLL do preview handler e o visualizador avulso com o compilador do .NET Framework (ja vem no Windows).
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$out = Join-Path $root "bin"
New-Item -ItemType Directory -Force $out | Out-Null

$refs = @(
    "$fw\System.dll", "$fw\System.Core.dll", "$fw\System.Xml.dll", "$fw\System.Xaml.dll",
    "$fw\System.IO.Compression.dll", "$wpf\WindowsBase.dll", "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll"
) | ForEach-Object { "/r:$_" }

$lib = @("$root\src\ModelLoader.cs", "$root\src\PreviewView.cs", "$root\src\PreviewHandler.cs")
& "$fw\csc.exe" /nologo /target:library /platform:anycpu /optimize+ /out:"$out\Bambu3DPreview.dll" $refs $lib
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar a DLL" }

& "$fw\csc.exe" /nologo /target:winexe /platform:anycpu /optimize+ /out:"$out\Viewer.exe" $refs "/r:$out\Bambu3DPreview.dll" "$root\src\Viewer.cs"
if ($LASTEXITCODE -ne 0) { throw "Falha ao compilar o Viewer" }
Write-Host "Compilado em $out"
