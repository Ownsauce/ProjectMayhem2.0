Add-Type -AssemblyName System.IO.Compression

function Analyze-Layer {
    param(
        [byte[]]$Data,
        [int]$Depth,
        [long]$BaseOffset,
        [int]$MaxDepth,
        [System.Collections.Generic.List[object]]$Output
    )

    if ($Depth -ge $MaxDepth -or $Data.Length -lt 4) { return }

    $i = 0
    while ($i -lt $Data.Length) {
        $found = $false
        foreach ($mode in @('gzip','zlib','deflate')) {
            $input = $null; $ds = $null; $temp = $null
            try {
                $slice = New-Object byte[] ($Data.Length - $i)
                [Array]::Copy($Data, $i, $slice, 0, $slice.Length)
                $input = New-Object System.IO.MemoryStream(,$slice)
                switch ($mode) {
                    'gzip' { $ds = New-Object System.IO.Compression.GZipStream($input, [System.IO.Compression.CompressionMode]::Decompress, $true) }
                    'zlib' { $ds = New-Object System.IO.Compression.ZLibStream($input, [System.IO.Compression.CompressionMode]::Decompress, $true) }
                    'deflate' { $ds = New-Object System.IO.Compression.DeflateStream($input, [System.IO.Compression.CompressionMode]::Decompress, $true) }
                }
                $temp = New-Object System.IO.MemoryStream
                $ds.CopyTo($temp)
                $decompressed = $temp.ToArray()
                $used = [int]$input.Position
                if ($used -le 0) { $used = 1 }

                $Output.Add([pscustomobject]@{
                    Offset = $BaseOffset + $i
                    Format = $mode
                    CompressedSize = $used
                    DecompressedSize = $decompressed.Length
                    Depth = $Depth
                }) | Out-Null

                if ($decompressed.Length -gt 0 -and ($Depth + 1) -lt $MaxDepth) {
                    Analyze-Layer -Data $decompressed -Depth ($Depth + 1) -BaseOffset ($BaseOffset + $i) -MaxDepth $MaxDepth -Output $Output
                }

                $i += $used
                $found = $true
                break
            } catch {
            } finally {
                if ($ds) { $ds.Dispose() }
                if ($temp) { $temp.Dispose() }
                if ($input) { $input.Dispose() }
            }
        }

        if (-not $found) {
            $Output.Add([pscustomobject]@{
                Offset = $BaseOffset + $i
                Format = 'raw'
                CompressedSize = 1
                DecompressedSize = 1
                Depth = $Depth
            }) | Out-Null
            $i++
        }
    }
}

function Analyze-File {
    param([string]$Path)
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $out = New-Object 'System.Collections.Generic.List[object]'
    Analyze-Layer -Data $bytes -Depth 0 -BaseOffset 0 -MaxDepth 3 -Output $out
    return @($bytes.Length, $out)
}

$base = 'C:\Other\PrivateAO\cd_image\data\statels'
foreach ($pf in 545,560,565) {
    $path = Join-Path $base ("$pf.pf")
    $result = Analyze-File -Path $path
    $fileLen = $result[0]
    $blocks = $result[1]
    Write-Output ("=== PF {0} ===" -f $pf)
    Write-Output ("FileBytes={0}" -f $fileLen)
    $blocks | Group-Object Depth,Format | Sort-Object Name | ForEach-Object {
        $parts = $_.Name -split ', '
        $depth = $parts[0]
        $format = $parts[1]
        $sumC = ($_.Group | Measure-Object CompressedSize -Sum).Sum
        $sumD = ($_.Group | Measure-Object DecompressedSize -Sum).Sum
        Write-Output ("Depth={0} Format={1} Count={2} TotalCompressed={3} TotalDecompressed={4}" -f $depth,$format,$_.Count,$sumC,$sumD)
    }
    Write-Output 'First10'
    $blocks | Select-Object -First 10 | ForEach-Object {
        Write-Output ('[{0}] {1} C={2} D={3}' -f $_.Offset,$_.Format,$_.CompressedSize,$_.DecompressedSize)
    }
    Write-Output ''
}
