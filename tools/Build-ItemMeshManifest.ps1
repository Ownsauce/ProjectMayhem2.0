param(
    [string]$ItemsPath = "C:\Other\codeprojects\ProjectMayhem\AO.Unity\Assets\StreamingAssets\AOData\items.json",
    [string]$AbiffMapPath = "C:\Other\codeprojects\ProjectMayhem\AO.Unity\Assets\Resources\ItemMeshes\AbiffNames.json",
    [string]$CirMapPath = "C:\Other\codeprojects\ProjectMayhem\AO.Unity\Assets\Resources\ItemMeshes\CirNames.json",
    [string]$OutputManifestPath = "C:\Other\codeprojects\ProjectMayhem\AO.Unity\Assets\StreamingAssets\AOData\item_mesh_manifest.json",
    [string]$OutputReferencesPath = "C:\Other\codeprojects\ProjectMayhem\AO.Unity\Assets\StreamingAssets\AOData\item_mesh_references.json",
    [int[]]$VisualStatIds = @(12, 38, 39, 64, 209),
    [switch]$PreferCir,
    [switch]$IncludeUnresolvedReport
)

$ErrorActionPreference = 'Stop'

function Load-JsonMap {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        return @{}
    }

    $raw = Get-Content $Path -Raw | ConvertFrom-Json
    $map = @{}
    $raw.PSObject.Properties | ForEach-Object {
        $map[[int]$_.Name] = [string]$_.Value
    }

    return $map
}

if (-not (Test-Path $ItemsPath)) {
    throw "Items file not found: $ItemsPath"
}

$abiffMap = Load-JsonMap -Path $AbiffMapPath
$cirMap = Load-JsonMap -Path $CirMapPath
$items = Get-Content $ItemsPath -Raw | ConvertFrom-Json

$manifestById = @{}
$references = New-Object System.Collections.Generic.List[object]
$unresolved = New-Object System.Collections.Generic.List[object]

foreach ($item in $items) {
    if ($null -eq $item -or $null -eq $item.StatValues) {
        continue
    }

    foreach ($statValue in $item.StatValues) {
        if ($null -eq $statValue) {
            continue
        }

        $statId = [int]$statValue.Stat
        if ($VisualStatIds -notcontains $statId) {
            continue
        }

        $meshId = [int]$statValue.RawValue
        if ($meshId -le 0) {
            continue
        }

        $abiffName = $abiffMap[$meshId]
        $cirName = $cirMap[$meshId]

        $sourceKind = $null
        $meshName = $null
        if ($PreferCir -and $cirName) {
            $sourceKind = 'cir'
            $meshName = $cirName
        }
        elseif ($abiffName) {
            $sourceKind = 'abiff'
            $meshName = $abiffName
        }
        elseif ($cirName) {
            $sourceKind = 'cir'
            $meshName = $cirName
        }

        $references.Add([pscustomobject]@{
            AOID = [int]$item.AOID
            Name = [string]$item.Name
            StatId = $statId
            MeshId = $meshId
            SourceKind = $sourceKind
            MeshKey = $meshName
        }) | Out-Null

        if (-not $meshName) {
            $unresolved.Add([pscustomobject]@{
                AOID = [int]$item.AOID
                Name = [string]$item.Name
                StatId = $statId
                MeshId = $meshId
            }) | Out-Null
            continue
        }

        if (-not $manifestById.ContainsKey($meshId)) {
            $extension = if ($sourceKind -eq 'cir') { '.cir' } else { '.abiff' }
            $manifestById[$meshId] = [pscustomobject]@{
                StatelId = $meshId
                MeshName = "$meshName$extension"
                FileName = "${meshName}_${meshId}${extension}"
            }
        }
    }
}

$manifest = $manifestById.Values | Sort-Object StatelId
$manifest | ConvertTo-Json -Depth 5 | Set-Content $OutputManifestPath -Encoding UTF8
$references | Sort-Object MeshId, AOID | ConvertTo-Json -Depth 5 | Set-Content $OutputReferencesPath -Encoding UTF8

Write-Host "Manifest written: $OutputManifestPath"
Write-Host "Reference report written: $OutputReferencesPath"
Write-Host ("Resolved meshes: {0}" -f $manifest.Count)

if ($IncludeUnresolvedReport) {
    $unresolvedPath = [System.IO.Path]::ChangeExtension($OutputManifestPath, '.unresolved.json')
    $unresolved | Sort-Object MeshId, AOID | ConvertTo-Json -Depth 5 | Set-Content $unresolvedPath -Encoding UTF8
    Write-Host "Unresolved report written: $unresolvedPath"
    Write-Host ("Unresolved entries: {0}" -f $unresolved.Count)
}
