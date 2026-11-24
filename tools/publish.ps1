# FiveMHater Optimizer - Windows üzerinde tek dosya .exe derleme
# Kullanım (PowerShell, proje kök klasöründe):  .\tools\publish.ps1
$ErrorActionPreference = 'Stop'
dotnet publish .\FiveMHaterOptimizer\FiveMHaterOptimizer.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o .\publish
Get-Item .\publish\FiveMHaterOptimizer.exe | Select-Object Name, Length, LastWriteTime
