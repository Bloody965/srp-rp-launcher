param(
    [Parameter(Mandatory = $true)]
    [string]$ModpackRoot,

    [Parameter(Mandatory = $true)]
    [string]$BaseUrl,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = ".\modpack-delta-manifest.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ModpackRoot -PathType Container)) {
    throw "ModpackRoot not found: $ModpackRoot"
}

$resolvedRoot = (Resolve-Path -LiteralPath $ModpackRoot).Path
$normalizedBaseUrl = $BaseUrl.Trim().TrimEnd('/')

if (-not [Uri]::TryCreate($normalizedBaseUrl, [UriKind]::Absolute, [ref]([Uri]$null))) {
    throw "BaseUrl must be an absolute URL"
}

$rootPrefix = $resolvedRoot
if (-not $rootPrefix.EndsWith([IO.Path]::DirectorySeparatorChar)) {
    $rootPrefix += [IO.Path]::DirectorySeparatorChar
}

$files = Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File | ForEach-Object {
    $fullPath = $_.FullName
    $relativePath = $fullPath.Substring($rootPrefix.Length).Replace('\', '/')
    $sha = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToLowerInvariant()

    [pscustomobject]@{
        path = $relativePath
        url = "$normalizedBaseUrl/$relativePath"
        sha256Hash = $sha
        fileSizeBytes = [int64]$_.Length
    }
}

$manifest = [ordered]@{
    version = $Version
    baseUrl = $normalizedBaseUrl
    files = $files
    deletePaths = @()
}

$outputDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDir) -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
Write-Host "Delta manifest generated: $OutputPath"
