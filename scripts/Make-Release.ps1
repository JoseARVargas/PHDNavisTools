<#
.SYNOPSIS
    Gera o instalador do PHDNavisTools e publica uma release no GitHub.

.PARAMETER Version
    Versao da release, ex: "1.7.0". Se omitido, usa a tag git mais recente.

.PARAMETER Title
    Titulo da release no GitHub. Default: "vX.Y.Z"

.PARAMETER NotesFile
    Caminho para arquivo .md com as release notes.

.EXAMPLE
    .\scripts\Make-Release.ps1 -Version "1.7.0" -Title "v1.7.0 - Descricao" -NotesFile "notes.md"
#>
param(
    [string]$Version   = "",
    [string]$Title     = "",
    [string]$NotesFile = ""
)

Set-Location $PSScriptRoot\..
$ErrorActionPreference = "Stop"

# ── Versao ───────────────────────────────────────────────────────────────────
if (-not $Version) {
    $Version = (git describe --tags --abbrev=0 2>$null) -replace '^v', ''
    if (-not $Version) {
        throw "Informe a versao com -Version ou crie uma tag git primeiro (git tag vX.Y.Z)."
    }
}
$tag = "v$Version"
if (-not $Title) { $Title = $tag }

Write-Host ""
Write-Host "=== PHDNavisTools Release $tag ===" -ForegroundColor Cyan

# ── Build Release ─────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "1. Compilando Release..." -ForegroundColor Yellow
dotnet build PHDNavisTools.csproj -c Release --nologo 2>&1 | Select-String "error|warning|exito" -CaseSensitive:$false
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao Release." }
Write-Host "   OK" -ForegroundColor Green

# ── Montar staging ────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "2. Empacotando..." -ForegroundColor Yellow

$outDir  = "bin\Release\net48"
$staging = "dist\staging-$tag"
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging | Out-Null

# DLLs do plugin (exclui os do Navisworks e AdWindows que nao sao redistribuiveis)
$excluded = @("Autodesk.*", "AdWindows.*", "Microsoft.*", "System.*", "PresentationCore.*",
              "PresentationFramework.*", "WindowsBase.*")

Get-ChildItem -Path $outDir -Filter "*.dll" | Where-Object {
    $name = $_.Name
    $keep = $true
    foreach ($pat in $excluded) { if ($name -like $pat) { $keep = $false; break } }
    $keep
} | ForEach-Object {
    Copy-Item $_.FullName -Destination $staging
    Write-Host "   + $($_.Name)"
}

# Scripts de instalacao
Copy-Item "installer\Instalar.bat"    -Destination $staging
Copy-Item "installer\Desinstalar.bat" -Destination $staging
Write-Host "   + Instalar.bat"
Write-Host "   + Desinstalar.bat"

# ── ZIP ───────────────────────────────────────────────────────────────────────
$zipPath = "dist\PHDNavisTools-$tag.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    (Resolve-Path $staging).Path,
    (Join-Path (Get-Location).Path $zipPath))

Remove-Item $staging -Recurse -Force
Write-Host "   ZIP: $zipPath" -ForegroundColor Green

# ── GitHub Release ────────────────────────────────────────────────────────────
Write-Host ""
Write-Host "3. Publicando no GitHub..." -ForegroundColor Yellow

$args = @("release", "create", $tag, $zipPath,
          "--title", $Title)

if ($NotesFile -and (Test-Path $NotesFile)) {
    $args += @("--notes-file", $NotesFile)
}

& gh @args

Write-Host ""
Write-Host "Release $tag publicada com instalador!" -ForegroundColor Green
