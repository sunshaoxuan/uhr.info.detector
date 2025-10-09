$lines = Get-Content 'c:\workspace\uhr.info.detector\frmMain.cs'

# Delete lines 87-91 (encoding constants and FilePrepareLogName)
# Delete lines 93-118 (DecodeSvnText method)
# Delete line 3024 (Trim400)
# Delete lines 3028-3034 (SafeLog)

# Create array to track which lines to keep
$linesToDelete = @()
$linesToDelete += 87..91  # Encoding constants
$linesToDelete += 93..118 # DecodeSvnText method
$linesToDelete += 3024    # Trim400
$linesToDelete += 3028..3034 # SafeLog

# Filter out the lines (1-indexed to 0-indexed)
$result = @()
for ($i = 0; $i -lt $lines.Count; $i++) {
    if (-not ($linesToDelete -contains ($i + 1))) {
        $result += $lines[$i]
    }
}

# Save the result
$result | Set-Content 'c:\workspace\uhr.info.detector\frmMain.cs'
Write-Host "Deleted $($linesToDelete.Count) lines successfully"
