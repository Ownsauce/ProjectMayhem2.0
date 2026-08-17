param(
    [string]$ProjectRoot = ".",
    [string]$PlayfieldsDir = "AO.Unity/Assets/StreamingAssets/AOData/Playfields",
    [string]$ModelInfoMapPath = "AO.Unity/Assets/StreamingAssets/AOData/model_info_map.json",
    [switch]$OverwriteExisting
)

$ErrorActionPreference = "Stop"

function Resolve-AbsolutePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,
        [Parameter(Mandatory = $true)]
        [string]$ChildPath
    )

    $combined = Join-Path $BasePath $ChildPath
    return [System.IO.Path]::GetFullPath($combined)
}

$root = [System.IO.Path]::GetFullPath($ProjectRoot)
$playfieldsPath = Resolve-AbsolutePath -BasePath $root -ChildPath $PlayfieldsDir
$modelInfoPath = Resolve-AbsolutePath -BasePath $root -ChildPath $ModelInfoMapPath

if (-not (Test-Path -LiteralPath $playfieldsPath)) {
    throw "Playfields directory not found: $playfieldsPath"
}

if (-not (Test-Path -LiteralPath $modelInfoPath)) {
    throw "model_info_map.json not found: $modelInfoPath"
}

Write-Host "Loading model info map from $modelInfoPath"
$modelEntries = Get-Content -LiteralPath $modelInfoPath -Raw | ConvertFrom-Json

$meshMap = @{}
foreach ($entry in $modelEntries) {
    if ($null -eq $entry) {
        continue
    }

    if ([int]$entry.TypeId -ne 1010001) {
        continue
    }

    $meshMap[[int]$entry.InstanceId] = [string]$entry.Name
}

Write-Host ("Indexed {0} statel mesh names from model_info_map.json" -f $meshMap.Count)

$playfieldFiles = Get-ChildItem -LiteralPath $playfieldsPath -File |
    Where-Object {
        $_.Extension -eq ".json" -and
        $_.BaseName -match '^\d+$'
    } |
    Sort-Object {
        [int]$_.BaseName
    }

if (-not $playfieldFiles) {
    Write-Warning "No numeric playfield JSON files were found under $playfieldsPath"
    exit 0
}

$summary = New-Object System.Collections.Generic.List[object]

foreach ($playfieldFile in $playfieldFiles) {
    $playfieldId = [int]$playfieldFile.BaseName
    $outputPath = Join-Path $playfieldsPath ("{0}_statel_mesh_map.json" -f $playfieldId)
    $unresolvedPath = Join-Path $playfieldsPath ("{0}_statel_mesh_map.unresolved.json" -f $playfieldId)

    if ((-not $OverwriteExisting) -and (Test-Path -LiteralPath $outputPath)) {
        Write-Host ("Skipping {0}: map already exists" -f $playfieldId)
        continue
    }

    Write-Host ("Building statel mesh map for playfield {0}" -f $playfieldId)
    $playfield = Get-Content -LiteralPath $playfieldFile.FullName -Raw | ConvertFrom-Json

    $statels = @($playfield.Statels)
    if (-not $statels -or $statels.Count -eq 0) {
        Write-Warning ("Playfield {0} has no statels; writing empty map" -f $playfieldId)
        @() | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $outputPath -Encoding UTF8
        if (Test-Path -LiteralPath $unresolvedPath) {
            Remove-Item -LiteralPath $unresolvedPath -Force
        }
        $summary.Add([pscustomobject]@{
            PlayfieldId = $playfieldId
            Entries = 0
            Unresolved = 0
            Output = $outputPath
        }) | Out-Null
        continue
    }

    $result = $statels |
        Group-Object -Property StatelId |
        ForEach-Object {
            $statelId = [int]$_.Name
            [pscustomobject]@{
                StatelId = $statelId
                MeshName = $meshMap[$statelId]
                CountInPlayfield = $_.Count
            }
        } |
        Sort-Object -Property StatelId

    $result | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $outputPath -Encoding UTF8

    $unresolved = @($result | Where-Object { [string]::IsNullOrWhiteSpace($_.MeshName) })
    if ($unresolved.Count -gt 0) {
        $unresolved | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $unresolvedPath -Encoding UTF8
        Write-Warning ("Playfield {0} has {1} unresolved statel ids" -f $playfieldId, $unresolved.Count)
    }
    elseif (Test-Path -LiteralPath $unresolvedPath) {
        Remove-Item -LiteralPath $unresolvedPath -Force
    }

    $summary.Add([pscustomobject]@{
        PlayfieldId = $playfieldId
        Entries = $result.Count
        Unresolved = $unresolved.Count
        Output = $outputPath
    }) | Out-Null
}

$summaryPath = Join-Path $playfieldsPath "statel_mesh_map_build_summary.json"
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

Write-Host ""
Write-Host ("Processed {0} playfields" -f $summary.Count)
Write-Host ("Summary written to {0}" -f $summaryPath)
