param(
    [Parameter(Mandatory = $true)]
    [string]$Source
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path $PSScriptRoot -Parent
$seedRoot = Join-Path $repoRoot 'src/FieldTrader/SeedData'
$catalogPath = Join-Path $seedRoot 'catalog.json'
$databasePath = Join-Path $Source 'assets/database.json'
$imagesRoot = Join-Path $Source 'assets/images'
$soundsRoot = Join-Path $Source 'assets/sounds'
$sourceRevision = 'merchanthelper-5a0f5249'

foreach ($path in @($catalogPath, $databasePath, $imagesRoot, $soundsRoot)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing import input: $path" }
}

function Get-NormalizedName([string]$value) {
    return -join ($value.ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })
}

function Get-ShortHash([string]$value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($value)
    $hash = [Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant().Substring(0, 10)
}

function Get-Layout([int]$slot, [string]$orientation) {
    $key = "$slot|$orientation"
    switch ($key) {
        '1|正' { return @{ Width = 1; Height = 1; Orientation = 'square' } }
        '2|横' { return @{ Width = 2; Height = 1; Orientation = 'horizontal' } }
        '2|竖' { return @{ Width = 1; Height = 2; Orientation = 'vertical' } }
        '4|正' { return @{ Width = 2; Height = 2; Orientation = 'square' } }
        '4|横' { return @{ Width = 4; Height = 1; Orientation = 'horizontal' } }
        '4|竖' { return @{ Width = 1; Height = 4; Orientation = 'vertical' } }
        '6|横' { return @{ Width = 3; Height = 2; Orientation = 'horizontal' } }
        '6|竖' { return @{ Width = 2; Height = 3; Orientation = 'vertical' } }
        default { throw "Unsupported MerchantHelper layout: slot=$slot orientation=$orientation" }
    }
}

$database = Get-Content -LiteralPath $databasePath -Raw | ConvertFrom-Json
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$sourceItems = @($database.items) + @($database.special_items)
$sourceSounds = @{}
foreach ($sound in @($database.sounds)) { $sourceSounds[[string]$sound.name] = $sound }

# Current names map to MerchantHelper spelling. These aliases are also used by the price catalog.
$currentToSourceNames = @{
    '体外去颤器' = '体外除颤器'
    '摄影机' = '摄像机'
    '金手机' = '金色手机'
    '红外线理疗仪' = '红外线理疗灯'
    '量子2000玩具机器人' = '量子2000'
    '金条' = '金块'
    '维塞尔日记' = '塞维尔日记'
    '手持接收机' = '手持接收器'
    '盘蛇金象' = '盘蛇金像'
    '盛宴雕塑' = '盛宴雕像'
    '石膏雕塑' = '石膏人头'
    '监听音箱' = '监听音响'
    '小金豹' = '金豹雕像'
    '千花书签' = '干花书签'
}

$knownNames = @{}
foreach ($item in @($catalog.Items)) {
    $knownNames[(Get-NormalizedName ([string]$item.Name))] = [string]$item.Name
}
foreach ($alias in $currentToSourceNames.GetEnumerator()) {
    $knownNames[(Get-NormalizedName ([string]$alias.Value))] = [string]$alias.Key
}

$itemsToAdd = [Collections.Generic.List[object]]::new()
foreach ($item in $sourceItems) {
    $normalized = Get-NormalizedName ([string]$item.name)
    if ($knownNames.ContainsKey($normalized)) { continue }
    $knownNames[$normalized] = [string]$item.name
    $itemsToAdd.Add($item)
}

$groupsById = @{}
$samplesById = @{}
$newGroups = [Collections.Generic.List[object]]::new()
$newSamples = [Collections.Generic.List[object]]::new()
$newItems = [Collections.Generic.List[object]]::new()
$copiedImages = [Collections.Generic.List[string]]::new()
$copiedSounds = [Collections.Generic.List[string]]::new()

foreach ($sourceItem in $itemsToAdd) {
    $name = [string]$sourceItem.name
    $isSpecial = $sourceItem.special -eq $true -or [string]$sourceItem.sound_type -eq '特殊'
    if ($isSpecial) {
        $soundFile = [string]$sourceItem.sound_file
        $groupKey = "special|$name"
        $groupName = "MerchantHelper · 特殊 · $name"
    }
    else {
        $soundType = [string]$sourceItem.sound_type
        if (-not $sourceSounds.ContainsKey($soundType)) { throw "Unknown sound type '$soundType' for $name" }
        $soundFile = [string]$sourceSounds[$soundType].file
        $groupKey = "type|$soundType"
        $groupName = "MerchantHelper · $soundType"
    }

    if (-not $groupsById.ContainsKey($groupKey)) {
        $soundSource = Join-Path $soundsRoot $soundFile
        if (-not (Test-Path -LiteralPath $soundSource)) { throw "Missing sound file: $soundSource" }
        $groupHash = Get-ShortHash $groupKey
        $sampleHash = Get-ShortHash "sample|$groupKey"
        $groupId = "merchanthelper-sound-$groupHash"
        $sampleRelative = "samples/merchanthelper-sound-$sampleHash.wav"
        $sampleDestination = Join-Path $seedRoot $sampleRelative
        Copy-Item -LiteralPath $soundSource -Destination $sampleDestination -Force
        $copiedSounds.Add($sampleRelative)

        $group = [pscustomobject]@{
            Id = $groupId
            Name = $groupName
            Evidence = "MerchantHelper source revision $sourceRevision; sound_type=$($sourceItem.sound_type); imported local reference library."
        }
        $sample = [pscustomobject]@{
            Id = "merchanthelper-sample-$sampleHash"
            SoundGroupId = $groupId
            FilePath = $sampleRelative
            Description = "$groupName；MerchantHelper 声音类型样例"
            SourceRecording = $soundFile
            StartSeconds = $null
            EndSeconds = $null
            SourceBatch = $sourceRevision
        }
        $groupsById[$groupKey] = $groupId
        $samplesById[$groupId] = $sample
        $newGroups.Add($group)
        $newSamples.Add($sample)
    }

    $imageSource = Join-Path $imagesRoot ([string]$sourceItem.image)
    if (-not (Test-Path -LiteralPath $imageSource)) { throw "Missing image file: $imageSource" }
    $itemHash = Get-ShortHash "item|$name"
    $imageRelative = "images/merchanthelper-item-$itemHash.png"
    Copy-Item -LiteralPath $imageSource -Destination (Join-Path $seedRoot $imageRelative) -Force
    $copiedImages.Add($imageRelative)
    $layout = Get-Layout ([int]$sourceItem.slot) ([string]$sourceItem.orientation)

    $newItems.Add([pscustomobject]@{
        Id = "merchanthelper-item-$itemHash"
        Name = $name
        ImagePath = $imageRelative
        SoundGroupId = $groupsById[$groupKey]
        GridWidth = $layout.Width
        GridHeight = $layout.Height
        Orientation = $layout.Orientation
        ImageCrop = $null
        SourceName = $name
        SourceBatch = $sourceRevision
    })
}

$catalog.SoundGroups = @($catalog.SoundGroups) + @($newGroups | Sort-Object Name)
$catalog.SoundSamples = @($catalog.SoundSamples) + @($newSamples | Sort-Object Id)
$catalog.Items = @($catalog.Items) + @($newItems | Sort-Object Name)
$catalog.SeedRevision = "items-20261023-$sourceRevision"

$duplicateIds = @($catalog.Items | Group-Object Id | Where-Object Count -gt 1)
$duplicateNames = @($catalog.Items | Group-Object Name | Where-Object Count -gt 1)
$duplicateGroups = @($catalog.SoundGroups | Group-Object Id | Where-Object Count -gt 1)
if ($duplicateIds.Count -gt 0 -or $duplicateNames.Count -gt 0 -or $duplicateGroups.Count -gt 0) {
    throw 'Import would create duplicate item names, item IDs or sound group IDs.'
}

$catalog | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $catalogPath -Encoding utf8NoBOM
[pscustomobject]@{
    SourceItems = $sourceItems.Count
    ExistingOrAliasedItems = $sourceItems.Count - $itemsToAdd.Count
    AddedItems = $itemsToAdd.Count
    AddedSoundGroups = $newGroups.Count
    AddedSoundSamples = $newSamples.Count
    CopiedImages = $copiedImages.Count
    CopiedSounds = $copiedSounds.Count
    CatalogItems = @($catalog.Items).Count
    CatalogSoundGroups = @($catalog.SoundGroups).Count
    CatalogSoundSamples = @($catalog.SoundSamples).Count
    SeedRevision = $catalog.SeedRevision
} | Format-List
