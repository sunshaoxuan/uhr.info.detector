$content = Get-Content 'c:\workspace\uhr.info.detector\frmMain.cs' -Raw

# First, mark method declarations with a temporary marker so we don't replace them
$content = $content -replace '(private\s+static\s+(void|string|int)\s+)(SafeLog|Trim400|DecodeSvnText|CompareVersionStringSmartAsc|FindSmartVersionIndex)(\s*\()', '${1}TEMP_METHOD_${3}${4}'

# Mark field/property declarations
$content = $content -replace '(private\s+(static\s+)?(readonly\s+)?(Encoding|const\s+string)\s+)(SvnConsoleEncoding|EncUtf8|EncSjis|FilePrepareLogName)(\s*=)', '${1}TEMP_FIELD_${5}${6}'

# Now replace all usages
$content = $content -replace '(?<![\w.])SafeLog\(', 'LogHelper.SafeLog('
$content = $content -replace '(?<![\w.])Trim400\(', 'LogHelper.TruncateString('
$content = $content -replace '(?<![\w.])FilePrepareLogName', 'LogHelper.FilePrepareLogName'

$content = $content -replace '(?<![\w.])SvnConsoleEncoding', 'EncodingHelper.SvnConsoleEncoding'
$content = $content -replace '(?<![\w.])EncUtf8', 'EncodingHelper.EncUtf8'
$content = $content -replace '(?<![\w.])DecodeSvnText\(', 'EncodingHelper.DecodeSvnText('

$content = $content -replace '(?<![\w.])CompareVersionStringSmartAsc\(', 'VersionCompareHelper.CompareVersionStringSmartAsc('
$content = $content -replace '(?<![\w.])FindSmartVersionIndex\(', 'VersionCompareHelper.FindSmartVersionIndex('

# Restore the marked declarations back to original
$content = $content -replace 'TEMP_METHOD_(SafeLog|Trim400|DecodeSvnText|CompareVersionStringSmartAsc|FindSmartVersionIndex)', '${1}'
$content = $content -replace 'TEMP_FIELD_(SvnConsoleEncoding|EncUtf8|EncSjis|FilePrepareLogName)', '${1}'

Set-Content 'c:\workspace\uhr.info.detector\frmMain.cs' -Value $content
Write-Host "Refactoring completed successfully"
