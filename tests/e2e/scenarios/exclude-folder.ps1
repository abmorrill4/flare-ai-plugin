# Lint Project, right-click an issue, Exclude Folder: that folder's issues go, Excluded Files (1) appears, and
# Include Again in it lists nothing (0.3.4, reports #6).
param($Fixture)
$run = Start-FlareE2E -Fixture $Fixture -Force
Send-E2E open (Join-Path $run.Project "Content\billpay\faq.htm") | Out-Null
Invoke-Ribbon "Lint Project"
Wait-PaneDone 300 | Out-Null
Send-E2E click "By file" | Out-Null
$rows = (Get-E2EState).pane.panel.rows
$i = [array]::FindIndex([object[]]$rows, [Predicate[object]]{ param($r) $r.file })
Assert-E2E ($i -ge 0) "Lint Project listed issues by file"
$folder = Split-Path $rows[$i].file

$menu = (Send-E2E rowMenu "Exclude Folder" -Extra @{ index = $i }).value
Assert-E2E (@($menu | Where-Object { $_ -like "Exclude Folder * from Lint Project" }).Count -eq 1) "the row menu offers Exclude Folder … from Lint Project (got: $($menu -join ' | '))"

$s = (Get-E2EState).pane
$left = @($s.panel.rows | Where-Object { $_.file -and (Split-Path $_.file) -eq $folder })
Assert-E2E ($left.Count -eq 0) "no issues are left from $folder"
Assert-E2E (@($s.buttons | Where-Object { $_.text -eq "Excluded Files (1)" }).Count -eq 1) "Excluded Files (1) appears (buttons: $($s.buttons.text -join ' | '))"

Send-E2E click "Excluded Files" -NoWait   # a dialog: the click answers when it closes
$d = Wait-Until { (Get-E2EState).dialogs | Where-Object { $_.title -like "*Excluded Files*" } } 10 "the Excluded Files dialog"
Assert-E2E (@($d.items).Count -eq 1) "the dialog lists the excluded folder (items: $($d.items -join ' | '))"
Send-E2E dialogClick "Include Again" -Extra @{ title = "Excluded Files" } | Out-Null
$d = ((Get-E2EState).dialogs | Where-Object { $_.title -like "*Excluded Files*" })
Assert-E2E (@($d.items).Count -eq 0) "Include Again took the folder off the list"
Send-E2E dialogClick "Close" -Extra @{ title = "Excluded Files" } | Out-Null
Wait-Until { $s = Get-E2EState; -not ($s.dialogs | Where-Object { $_.title -like "*Excluded Files*" }) -and -not ($s.pane.buttons | Where-Object { $_.text -like "Excluded Files*" }) } 10 "Excluded Files to close and its pane button to go" | Out-Null
