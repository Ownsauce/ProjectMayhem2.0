param(
    [string]$ProjectRoot = "."
)

$root = Resolve-Path $ProjectRoot
$playfieldDir = Join-Path $root "AO.Unity\Assets\StreamingAssets\AOData\Playfields"

if (-not (Test-Path $playfieldDir)) {
    throw "Playfield directory not found: $playfieldDir"
}

$results = New-Object System.Collections.Generic.List[object]

Get-ChildItem -Path $playfieldDir -Filter "*.json" -File |
    Where-Object { $_.BaseName -match '^\d+$' } |
    ForEach-Object {
        try {
            $pf = Get-Content $_.FullName -Raw | ConvertFrom-Json
            $statels = @($pf.Statels)
            if ($statels.Count -eq 0) {
                return
            }

            $zeroCount = 0
            foreach ($statel in $statels) {
                $isZeroPos = ($statel.Position.X -eq 0.0) -and ($statel.Position.Y -eq 0.0) -and ($statel.Position.Z -eq 0.0)
                $isZeroRot = ($statel.Rotation.x -eq 0.0) -and ($statel.Rotation.y -eq 0.0) -and ($statel.Rotation.z -eq 0.0)
                $isDefaultScale = ($statel.Scale -eq 1.0) -and ($statel.ScaleRaw -eq 100)
                $hasNoOverrides = (@($statel.TextureOverrides).Count -eq 0)

                if ($isZeroPos -and $isZeroRot -and $isDefaultScale -and $hasNoOverrides) {
                    $zeroCount++
                }
            }

            $ratio = [math]::Round(($zeroCount / [double]$statels.Count) * 100.0, 2)
            if ($zeroCount -gt 0) {
                $results.Add([pscustomobject]@{
                    PlayfieldId      = [int]$pf.PlayfieldId
                    Name             = [string]$pf.Name
                    StatelCount      = $statels.Count
                    ZeroFallbackRows = $zeroCount
                    ZeroFallbackPct  = $ratio
                    File             = $_.FullName
                }) | Out-Null
            }
        }
        catch {
            Write-Warning "Failed to inspect $($_.FullName): $($_.Exception.Message)"
        }
    }

$results = @($results | Sort-Object ZeroFallbackPct, PlayfieldId -Descending)
$reportPath = Join-Path $playfieldDir "zeroed_playfields_report.json"

if ($results.Count -eq 0) {
    "[]" | Set-Content $reportPath -Encoding UTF8
}
else {
    $results | ConvertTo-Json -Depth 4 | Set-Content $reportPath -Encoding UTF8
}

Write-Host "Found $($results.Count) playfield JSONs with at least one zero/default statel row."
Write-Host "Report: $reportPath"
$results | Select-Object -First 20 | Format-Table PlayfieldId, Name, StatelCount, ZeroFallbackRows, ZeroFallbackPct -AutoSize
