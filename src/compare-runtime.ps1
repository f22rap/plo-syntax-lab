if ($CsvPaths.Count -lt 2 -or $CsvPaths.Count -gt 3) { throw 'Select two or three CSV files.' }
if ($CsvLabels.Count -eq 0) { $CsvLabels = @(1..$CsvPaths.Count | ForEach-Object { 'CSV'+$_ }) }
if ($CsvLabels.Count -ne $CsvPaths.Count) { throw 'CSV label count must match CSV file count.' }
for ($labelIndex=0; $labelIndex -lt $CsvLabels.Count; $labelIndex++) {
    $CsvLabels[$labelIndex] = if ([string]::IsNullOrWhiteSpace($CsvLabels[$labelIndex])) { 'CSV'+($labelIndex+1) } else { $CsvLabels[$labelIndex].Trim() }
}
$resolved = @($CsvPaths | ForEach-Object { (Resolve-Path -LiteralPath $_).ProviderPath })
if (@($resolved | Select-Object -Unique).Count -ne $resolved.Count) { throw 'The same CSV cannot be selected twice.' }
if ($OutputPath) {
    $OutputPath = [IO.Path]::GetFullPath($OutputPath)
    if ($resolved -contains $OutputPath -or $OutputPath -eq $PSCommandPath) { throw 'Output cannot overwrite an input CSV or this script.' }
    if ((Test-Path -LiteralPath $OutputPath) -and -not $Force) { throw 'Output already exists. Choose another path or use -Force.' }
}
$boardCards = @(for ($offset=0; $offset -lt $Board.Length; $offset+=2) { $Board.Substring($offset,2) })
$culture = [Globalization.CultureInfo]::InvariantCulture
# Card masks give each unordered four-card hand a unique key without sorting.
$cardBits = [Collections.Generic.Dictionary[string,long]]::new([StringComparer]::Ordinal)
$cardNumber = 0
foreach ($rank in 'AKQJT98765432'.ToCharArray()) {
    foreach ($suit in 'shdc'.ToCharArray()) {
        $cardBits.Add([string]$rank + [string]$suit, ([long]1 -shl $cardNumber))
        $cardNumber++
    }
}
[long]$boardMask = 0
foreach ($card in $boardCards) { $boardMask = $boardMask -bor $cardBits[$card] }
$validHand = [regex]::new('^(?:[AKQJT2-9][shdc]){4}$', [Text.RegularExpressions.RegexOptions]::Compiled)
$datasets = @(for ($i=0; $i -lt $resolved.Count; $i++) {
    $path = $resolved[$i]
    $nameBoard = [regex]::Match([IO.Path]::GetFileNameWithoutExtension($path), '(?i)(?:^|_)((?:[AKQJT2-9][shdc]){3,5})(?=_|$)')
    if ($nameBoard.Success) {
        $nameCards = @(for ($offset=0; $offset -lt $nameBoard.Groups[1].Value.Length; $offset+=2) { $nameBoard.Groups[1].Value.Substring($offset,2).ToLowerInvariant() })
        if (($nameCards.Count -ne $boardCards.Count) -or ((($nameCards[0..2] | Sort-Object) -join '') -cne ((($boardCards[0..2] | ForEach-Object { $_.ToLowerInvariant() }) | Sort-Object) -join '')) -or ($boardCards.Count -ge 4 -and $nameCards[3] -cne $boardCards[3].ToLowerInvariant()) -or ($boardCards.Count -eq 5 -and $nameCards[4] -cne $boardCards[4].ToLowerInvariant())) { throw "CSV$($i+1): board differs from $Board." }
    }
    $seen = [Collections.Generic.HashSet[long]]::new()
    $inputRows = @(Import-Csv -LiteralPath $path -Encoding UTF8)
    if ($inputRows.Count -eq 0) { throw "CSV$($i+1): no data rows." }
    # Import-Csv uses one header schema for every row, so check it once per file.
    $columns = $inputRows[0].PSObject.Properties.Name
    if ($columns -notcontains 'weight') { throw "CSV$($i+1): weight column is required. Frequency is not substituted." }
    $handColumn = if ($columns -contains 'hand') { 'hand' } elseif ($columns -contains 'combo') { 'combo' } else { throw "CSV$($i+1): hand or combo column is required." }
    $line = 1
    [decimal]$allSum = 0
    $rows = @(foreach ($inputRow in $inputRows) {
        $line++
        $hand = [string]$inputRow.$handColumn
        if (-not $validHand.IsMatch($hand)) { throw "CSV$($i+1), line ${line}: invalid PLO4 hand." }
        [long]$mask = 0
        for ($offset=0; $offset -lt 8; $offset+=2) {
            [long]$bit = $cardBits[$hand.Substring($offset,2)]
            if (($mask -band $bit) -ne 0) { throw "CSV$($i+1), line ${line}: repeated card." }
            $mask = $mask -bor $bit
        }
        if (($mask -band $boardMask) -ne 0) { throw "CSV$($i+1), line ${line}: hand contains a board card." }
        if (-not $seen.Add($mask)) { throw "CSV$($i+1), line ${line}: duplicate hand." }
        [decimal]$value = 0
        if (-not [decimal]::TryParse([string]$inputRow.weight, [Globalization.NumberStyles]::Float, $culture, [ref]$value) -or $value -lt 0 -or $value -gt 1) { throw "CSV$($i+1), line ${line}: invalid weight; expected 0 to 1." }
        $allSum += $value
        [pscustomobject]@{ Hand=$hand; Mask=$mask; Value=$value }
    })
    $inputRows = $null
    [pscustomobject]@{ Path=$path; Rows=$rows; AllSum=$allSum }
})
$results = @(foreach ($filter in $filters) {
    $matcher = if ($filter.Cell -ne 'ALL') { [PloSyntax.SyntaxMatcher]::new($filter.Syntax) } else { $null }
    $stats = @(foreach ($dataset in $datasets) {
        [decimal]$sum = 0; [int]$count = 0
        if ($filter.Cell -eq 'ALL') { $sum=$dataset.AllSum; $count=$dataset.Rows.Count }
        else {
            $matched = $dataset.Rows.Where({ $matcher.Matches($_.Mask) })
            $count = $matched.Count
            foreach ($match in $matched) { $sum += $match.Value }
        }
        [pscustomobject]@{ Count=$count; Sum=$sum }
    })
    [decimal]$total = 0
    foreach ($stat in $stats) { $total += $stat.Sum }
    $row = [ordered]@{ board=$Board; cell=$filter.Cell; label=$filter.Label; syntax=$filter.Syntax; csv_count=$datasets.Count; total_weight=$total }
    for ($i=0; $i -lt $datasets.Count; $i++) {
        $prefix = 'csv' + ($i+1)
        $row[$prefix+'_label'] = $CsvLabels[$i]
        $row[$prefix+'_path'] = $datasets[$i].Path
        $row[$prefix+'_source_rows'] = $datasets[$i].Rows.Count
        $row[$prefix+'_matched_hands'] = $stats[$i].Count
        $row[$prefix+'_weight_sum'] = $stats[$i].Sum
        $row[$prefix+'_percent'] = if ($total -gt 0) { $stats[$i].Sum / $total * 100 } else { $null }
    }
    $row['status'] = if ($total -gt 0) { 'OK' } else { 'ZeroTotal' }
    $row['note'] = if ($total -gt 0) { '' } else { 'Total weight is zero; percentages are undefined.' }
    [pscustomobject]$row
})
if ($OutputPath) {
    $directory = Split-Path -Parent $OutputPath
    if (-not (Test-Path -LiteralPath $directory)) { New-Item -ItemType Directory -Path $directory | Out-Null }
    if ($PSVersionTable.PSEdition -eq 'Desktop') { $results | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding UTF8 }
    else { $results | Export-Csv -LiteralPath $OutputPath -NoTypeInformation -Encoding utf8BOM }
}
$results
