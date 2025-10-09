$content = Get-Content 'c:\workspace\uhr.info.detector\frmMain.cs' -Raw

# Replace method calls (but NOT declarations)
# Use negative lookbehind to avoid replacing in declarations: not preceded by 'readonly Encoding ' or 'const string '
$content = $content -replace '(?<!readonly Encoding )(?<!const string )(?<![\w.])SafeLog\(', 'LogHelper.SafeLog('
$content = $content -replace '(?<!const string )(?<![\w.])FilePrepareLogName(?![\s]*=)', 'LogHelper.FilePrepareLogName'
$content = $content -replace '(?<![\w.])Trim400\(', 'LogHelper.TruncateString('

# Replace encoding references (but NOT in declarations)
$content = $content -replace '(?<!readonly Encoding )(?<![\w.])SvnConsoleEncoding(?![\s]*=)', 'EncodingHelper.SvnConsoleEncoding'
$content = $content -replace '(?<!readonly Encoding )(?<![\w.])EncUtf8(?![\s]*=)', 'EncodingHelper.EncUtf8'
$content = $content -replace '(?<![\w.])DecodeSvnText\(', 'EncodingHelper.DecodeSvnText('

# Replace version compare methods
$content = $content -replace '(?<![\w.])CompareVersionStringSmartAsc\(', 'VersionCompareHelper.CompareVersionStringSmartAsc('
$content = $content -replace '(?<![\w.])FindSmartVersionIndex\(', 'VersionCompareHelper.FindSmartVersionIndex('

Set-Content 'c:\workspace\uhr.info.detector\frmMain.cs' -Value $content
Write-Host "Refactoring completed successfully"
