$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$local = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'
$dotnet = if (Test-Path $local) { $local } else { 'dotnet' }

Push-Location $root
try {
    & $dotnet test "$root\ResourcePanel.sln" -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & $dotnet publish "$root\src\ResourcePanel\ResourcePanel.csproj" -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -o "$root\dist"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Portable exe: $root\dist\ResourcePanel.exe"
}
finally {
    Pop-Location
}
