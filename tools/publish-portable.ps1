<#
.SYNOPSIS
    Publica la versión portátil de Radial: un único .exe autocontenido para Windows x64.

.DESCRIPTION
    Aplicación de archivo único (Radial.App.exe) que lleva dentro el runtime de .NET y las bibliotecas nativas de WPF: no hace falta
    instalar nada. No es un instalador y NO está firmada (la firma y la confianza de Windows se evalúan en la fase 8; Smart App Control
    puede bloquear un .exe sin firmar). No usa recorte (WPF no lo admite de forma segura).

    Es la ÚNICA definición del comando de publicación: la usan a mano el desarrollador y el trabajo de CI.

.PARAMETER Output
    Carpeta de salida. Por defecto: artifacts\portable\win-x64 (la carpeta artifacts\ está excluida de Git).

.EXAMPLE
    pwsh -File tools\publish-portable.ps1
    powershell -ExecutionPolicy Bypass -File tools\publish-portable.ps1 -Output C:\temp\radial
#>
[CmdletBinding()]
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\portable\win-x64')
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\src\Radial.App\Radial.App.csproj'

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish termino con el codigo $LASTEXITCODE."
}

$exe = Join-Path $Output 'Radial.App.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "No se genero $exe."
}

$item = Get-Item -LiteralPath $exe
$hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
Write-Host ("Radial portatil: {0} ({1:N1} MiB)" -f $item.FullName, ($item.Length / 1MB))
Write-Host "SHA-256: $hash"
