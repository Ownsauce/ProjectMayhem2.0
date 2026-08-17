param(
    [string]$MeshDirectory = "AO.Unity\Assets\Resources\WorldMeshes",
    [string]$NamesJsonPath = "",
    [switch]$AllowLegacyNameOnly,
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath {
    param([string]$PathLike)

    if ([string]::IsNullOrWhiteSpace($PathLike)) { return $null }
    if ([System.IO.Path]::IsPathRooted($PathLike)) { return [System.IO.Path]::GetFullPath($PathLike) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $PathLike))
}

function Sanitize-Stem {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) { return "unnamed" }
    $invalid = [System.IO.Path]::GetInvalidFileNameChars()
    $chars = $Name.ToCharArray() | ForEach-Object {
        if ($invalid -contains $_) { "_" } else { $_ }
    }
    return (-join $chars).Trim()
}

function Resolve-NamesJsonPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        $resolved = Resolve-RepoPath $ExplicitPath
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "Names JSON not found: $resolved"
        }
        return $resolved
    }

    $candidates = @(
        "AO.Unity\Assets\Resources\WorldMeshes\AbiffNames.json",
        "AO.Unity\Assets\Resources\ItemMeshes\AbiffNames.json"
    )

    foreach ($candidate in $candidates) {
        $resolved = Resolve-RepoPath $candidate
        if (Test-Path -LiteralPath $resolved) {
            return $resolved
        }
    }

    throw "Could not find AbiffNames.json in default locations."
}

$meshDir = Resolve-RepoPath $MeshDirectory
if (-not (Test-Path -LiteralPath $meshDir)) {
    throw "Mesh directory not found: $meshDir"
}

$namesPath = Resolve-NamesJsonPath $NamesJsonPath

$namesJson = Get-Content -LiteralPath $namesPath -Raw | ConvertFrom-Json
$nameMap = @{}
foreach ($prop in $namesJson.PSObject.Properties) {
    $id = 0
    if (-not [int]::TryParse($prop.Name, [ref]$id)) { continue }
    $nameMap[$id] = [string]$prop.Value
}

if ($nameMap.Count -eq 0) {
    throw "No mesh IDs were read from: $namesPath"
}

$files = Get-ChildItem -LiteralPath $meshDir -File -Filter "*.glb"
$fileByName = @{}
$filesById = @{}

foreach ($file in $files) {
    $fileByName[$file.Name.ToLowerInvariant()] = $file.FullName
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($stem -match "^(.*)_([0-9]+)$") {
        $id = [int]$matches[2]
        if (-not $filesById.ContainsKey($id)) { $filesById[$id] = @() }
        $filesById[$id] += $file.Name
    }
}

$results = New-Object System.Collections.Generic.List[object]
$missing = 0
$matchIdSuffix = 0
$matchLegacy = 0
$wrongNameForId = 0

foreach ($entry in $nameMap.GetEnumerator() | Sort-Object Key) {
    $id = $entry.Key
    $rawName = $entry.Value
    $stem = Sanitize-Stem $rawName

    $expectedWithId = "$stem`_$id.glb"
    $expectedLegacy = "$stem.glb"

    $status = ""
    $resolvedFile = $null

    if ($fileByName.ContainsKey($expectedWithId.ToLowerInvariant())) {
        $status = "ok_id_suffix"
        $resolvedFile = $expectedWithId
        $matchIdSuffix++
    }
    elseif ($AllowLegacyNameOnly -and $fileByName.ContainsKey($expectedLegacy.ToLowerInvariant())) {
        $status = "ok_legacy_name_only"
        $resolvedFile = $expectedLegacy
        $matchLegacy++
    }
    elseif ($filesById.ContainsKey($id) -and $filesById[$id].Count -gt 0) {
        $status = "id_found_name_mismatch"
        $resolvedFile = ($filesById[$id] -join "; ")
        $wrongNameForId++
    }
    else {
        $status = "missing"
        $missing++
    }

    $results.Add([pscustomobject]@{
            Id = $id
            Name = $rawName
            ExpectedFile = $expectedWithId
            Status = $status
            FoundFile = $resolvedFile
        })
}

$orphanFiles = New-Object System.Collections.Generic.List[string]
foreach ($file in $files) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    if ($stem -match "^(.*)_([0-9]+)$") {
        $id = [int]$matches[2]
        if (-not $nameMap.ContainsKey($id)) {
            $orphanFiles.Add($file.Name)
        }
    }
}

$duplicateIdFiles = $filesById.GetEnumerator() | Where-Object { $_.Value.Count -gt 1 } | Sort-Object Key

$summary = [pscustomobject]@{
    NamesJsonPath = $namesPath
    MeshDirectory = $meshDir
    TotalMapEntries = $nameMap.Count
    TotalMeshFiles = $files.Count
    MatchIdSuffix = $matchIdSuffix
    MatchLegacyNameOnly = $matchLegacy
    IdFoundNameMismatch = $wrongNameForId
    Missing = $missing
    DuplicateIdFileGroups = @($duplicateIdFiles).Count
    OrphanFiles = $orphanFiles.Count
}

Write-Host ""
Write-Host "WorldMesh Export Validation"
Write-Host "---------------------------"
$summary | Format-List

if ($wrongNameForId -gt 0) {
    Write-Host ""
    Write-Host "Name Mismatch Samples (ID exists but filename differs):"
    $results | Where-Object { $_.Status -eq "id_found_name_mismatch" } | Select-Object -First 20 | Format-Table -AutoSize
}

if ($missing -gt 0) {
    Write-Host ""
    Write-Host "Missing Samples:"
    $results | Where-Object { $_.Status -eq "missing" } | Select-Object -First 20 | Format-Table -AutoSize
}

if (@($duplicateIdFiles).Count -gt 0) {
    Write-Host ""
    Write-Host "Duplicate ID File Groups (first 20):"
    $duplicateIdFiles | Select-Object -First 20 | ForEach-Object {
        [pscustomobject]@{
            Id = $_.Key
            FileCount = $_.Value.Count
            Files = ($_.Value -join "; ")
        }
    } | Format-Table -AutoSize
}

if ($orphanFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "Orphan File Samples (ID not in AbiffNames):"
    $orphanFiles | Select-Object -First 20
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $resolvedReport = Resolve-RepoPath $ReportPath
    $parent = Split-Path -Parent $resolvedReport
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $payload = [pscustomobject]@{
        Summary = $summary
        Results = $results
        DuplicateIdFileGroups = @($duplicateIdFiles | ForEach-Object {
                [pscustomobject]@{
                    Id = $_.Key
                    Files = $_.Value
                }
            })
        OrphanFiles = @($orphanFiles)
    }
    $payload | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $resolvedReport -Encoding UTF8
    Write-Host ""
    Write-Host "Wrote report: $resolvedReport"
}

exit 0
