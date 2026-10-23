param(
    [string]$Snapshot = (Join-Path $PSScriptRoot 'snapshot-20261023.json')
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$seedRoot = Join-Path $repoRoot 'src/FieldTrader/SeedData'
$imagesRoot = Join-Path $seedRoot 'images'
$samplesRoot = Join-Path $seedRoot 'samples'
$catalogPath = Join-Path $seedRoot 'catalog.json'
$sourceBase = 'https://aqtwwx.net'
$sourcePage = "$sourceBase/listen-treasure"
$sourceBatch = 'aqtwwx-listen-treasure-20261023'
$seedRevision = 'aqtwwx-listen-treasure-20261023-v1'

if (-not (Test-Path -LiteralPath $Snapshot)) { throw "Missing snapshot: $Snapshot" }
$snapshotData = Get-Content -LiteralPath $Snapshot -Raw | ConvertFrom-Json

function Get-ShortHash([string]$value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant().Substring(0, 10)
}

function Save-RemoteFile([string]$url, [string]$destination, [switch]$AllowNotFound) {
    $directory = Split-Path $destination -Parent
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Invoke-WebRequest -Uri $url -Headers @{
                'User-Agent' = 'Mozilla/5.0'
                'Referer' = $sourcePage
            } -UseBasicParsing -OutFile $destination
            if ((Get-Item -LiteralPath $destination).Length -le 0) { throw 'Downloaded file is empty.' }
            $stream = [IO.File]::OpenRead($destination)
            try {
                $header = [byte[]]::new(12)
                [void]$stream.Read($header, 0, $header.Length)
            }
            finally {
                $stream.Dispose()
            }
            $isPng = $header[0] -eq 0x89 -and $header[1] -eq 0x50 -and $header[2] -eq 0x4E -and $header[3] -eq 0x47
            $isWav = $header[0] -eq 0x52 -and $header[1] -eq 0x49 -and $header[2] -eq 0x46 -and $header[3] -eq 0x46
            if ($destination.EndsWith('.png', [StringComparison]::OrdinalIgnoreCase) -and -not $isPng) { throw 'Server returned a non-PNG response.' }
            if ($destination.EndsWith('.wav', [StringComparison]::OrdinalIgnoreCase) -and -not $isWav) { throw 'Server returned a non-WAV response.' }
            Start-Sleep -Milliseconds 120
            return $true
        }
        catch {
            $statusCode = $_.Exception.Response.StatusCode.value__
            if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }
            if ($AllowNotFound -and $statusCode -eq 404) { return $false }
            if ($attempt -eq 5) { throw "Download failed after retries: $url`n$($_.Exception.Message)" }
            Start-Sleep -Seconds $attempt
        }
    }
}

New-Item -ItemType Directory -Force -Path $imagesRoot, $samplesRoot | Out-Null
Get-ChildItem -LiteralPath $imagesRoot -File | Remove-Item -Force
Get-ChildItem -LiteralPath $samplesRoot -File | Remove-Item -Force

$items = [Collections.Generic.List[object]]::new()
$groups = [Collections.Generic.List[object]]::new()
$samples = [Collections.Generic.List[object]]::new()
$imageCount = 0
$placeholderCount = 0

foreach ($group in @($snapshotData.groups)) {
    $soundId = [string]$group.soundId
    $number = [string]$group.no
    $groupId = "aqtwwx-listen-sound-$number"
    $groups.Add([pscustomobject]@{
        Id = $groupId
        Name = "行商拾取音效 $number"
        Evidence = "aqtwwx listen-treasure codex; soundId=$soundId; captured=$($snapshotData.capturedAt)"
    })

    $sampleRelative = "samples/aqtwwx-listen-sound-$number.wav"
    $soundUrl = "$sourceBase/static/listen-treasure/sounds/$soundId.wav"
    [void](Save-RemoteFile $soundUrl (Join-Path $seedRoot $sampleRelative))
    $samples.Add([pscustomobject]@{
        Id = "aqtwwx-listen-sample-$number"
        SoundGroupId = $groupId
        FilePath = $sampleRelative
        Description = "暗区情报站行商听声图鉴；拾取音效 $number"
        SourceRecording = "listen-treasure/sounds/$soundId.wav"
        StartSeconds = $null
        EndSeconds = $null
        SourceBatch = $sourceBatch
    })

    foreach ($sourceItem in @($group.items)) {
        $name = [string]$sourceItem.name
        $itemHash = Get-ShortHash "listen-treasure|$name"
        $imagePath = $null
        if ([string]$sourceItem.imagePath -notlike '*/item-placeholder.png') {
            $imageRelative = "images/aqtwwx-listen-$itemHash.png"
            if (Save-RemoteFile "$sourceBase$($sourceItem.imagePath)" (Join-Path $seedRoot $imageRelative) -AllowNotFound) {
                $imagePath = $imageRelative
                $imageCount++
            }
            else {
                $placeholderCount++
            }
        }
        else {
            $placeholderCount++
        }

        $rows = [int]$sourceItem.rows
        $cols = [int]$sourceItem.cols
        $orientation = if ($rows -eq $cols) { 'square' } elseif ($cols -gt $rows) { 'horizontal' } else { 'vertical' }
        $items.Add([pscustomobject]@{
            Id = "aqtwwx-listen-item-$itemHash"
            Name = $name
            ImagePath = $imagePath
            SoundGroupId = $groupId
            GridWidth = $cols
            GridHeight = $rows
            Orientation = $orientation
            ImageCrop = $null
            SourceName = $name
            SourceBatch = $sourceBatch
            Price = [int]$sourceItem.price
        })
    }
}

$catalog = [pscustomobject]@{
    Version = 1
    SeedRevision = $seedRevision
    ReplaceLegacySeeds = $true
    Items = @($items)
    SoundGroups = @($groups)
    SoundSamples = @($samples)
}

$catalog | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $catalogPath -Encoding utf8NoBOM

$audit = [pscustomobject]@{
    Source = $sourcePage
    CapturedAt = $snapshotData.capturedAt
    SeedRevision = $seedRevision
    Items = $items.Count
    Images = $imageCount
    PlaceholderImages = $placeholderCount
    SoundGroups = $groups.Count
    SoundSamples = $samples.Count
}
$audit | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $seedRoot 'aqtwwx-listen-treasure-audit.json') -Encoding utf8NoBOM
$audit | Format-List
