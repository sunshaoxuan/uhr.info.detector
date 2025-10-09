$content = Get-Content 'c:\workspace\uhr.info.detector\frmMain.cs' -Raw

# Replace SafeLog
$content = $content -replace '(?<![\w.])SafeLog\(', 'LogHelper.SafeLog('
$content = $content -replace '(?<![\w.])FilePrepareLogName', 'LogHelper.FilePrepareLogName'
$content = $content -replace 'Trim400\(', 'LogHelper.TruncateString('

# Replace encoding
$content = $content -replace '(?<![\w.])SvnConsoleEncoding', 'EncodingHelper.SvnConsoleEncoding'
$content = $content -replace '(?<![\w.])EncUtf8', 'EncodingHelper.EncUtf8'
$content = $content -replace '(?<![\w.])DecodeSvnText\(', 'EncodingHelper.DecodeSvnText('

# Replace version compare
$content = $content -replace '(?<![\w.])CompareVersionStringSmartAsc\(', 'VersionCompareHelper.CompareVersionStringSmartAsc('
$content = $content -replace '(?<![\w.])FindSmartVersionIndex\(', 'VersionCompareHelper.FindSmartVersionIndex('

Set-Content 'c:\workspace\uhr.info.detector\frmMain.cs' -Value $content
Write-Host "Refactoring completed successfully"
