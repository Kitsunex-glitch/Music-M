# Скрипт сборки четырёх вариантов Setup (single-file publish):
#   Setup-FULL.X86.exe     - автономная (self-contained), встроен .NET runtime
#   Setup-SLIM.X86.exe     - зависит от платформы (framework-dependent), использует системный .NET
#   Setup-FULL.ARM64.exe   - автономная (self-contained), встроен .NET runtime
#   Setup-SLIM.ARM64.exe   - зависит от платформы (framework-dependent), использует системный .NET
#
# FULL  -> SelfContained = true  (в exe встроен .NET runtime, работает без установленного .NET)
# SLIM  -> SelfContained = false (использует установленный на системе .NET Desktop Runtime)
#
# Готовые exe складываются в одну общую папку без лишних файлов.

$ErrorActionPreference = "Stop"

$Project = Join-Path $PSScriptRoot "Setup.csproj"
$Configuration = "Release"
$OutRoot = Join-Path $PSScriptRoot "bin\SetupBuilds"

# Варианты: имя выходного файла -> RID + SelfContained
$variants = @(
    @{ Name = "Setup-FULL.X86";     Rid = "win-x86";   SelfContained = $true  },
    @{ Name = "Setup-SLIM.X86";     Rid = "win-x86";   SelfContained = $false },
    @{ Name = "Setup-FULL.ARM64";   Rid = "win-arm64"; SelfContained = $true  },
    @{ Name = "Setup-SLIM.ARM64";   Rid = "win-arm64"; SelfContained = $false }
)

# Очищаем выходную папку перед сборкой
if (Test-Path $OutRoot) { Remove-Item -Path $OutRoot -Recurse -Force }
New-Item -ItemType Directory -Path $OutRoot -Force | Out-Null

foreach ($v in $variants) {
    $tmp = Join-Path $env:TEMP "SetupBuild_$($v.Name)"
    $mode = if ($v.SelfContained) { "SELF-CONTAINED" } else { "FRAMEWORK-DEPENDENT" }
    Write-Host ""
    Write-Host "=== Building $($v.Name) (RID: $($v.Rid), $mode) ===" -ForegroundColor Cyan

    dotnet publish $Project `
        -c $Configuration `
        -r $v.Rid `
        --self-contained:$($v.SelfContained) `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $tmp

    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $($v.Name)" -ForegroundColor Red
        exit $LASTEXITCODE
    }

    # Копируем только сам exe под нужным именем в общую папку
    $srcExe = Join-Path $tmp "Setup.exe"
    $dstExe = Join-Path $OutRoot "$($v.Name).exe"
    Copy-Item -Path $srcExe -Destination $dstExe -Force
    Write-Host "OK -> $dstExe" -ForegroundColor Green

    # Очищаем временную папку
    Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Get-ChildItem $OutRoot -Filter "*.exe" | ForEach-Object {
    Write-Host ("{0,-25} {1,12:N0} bytes" -f $_.Name, $_.Length)
}