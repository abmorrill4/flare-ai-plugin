# Lint Topic, close the pane, Reopen Issues: the same results come back without a new lint (0.3.4, reports #5).
param($Fixture)
$run = Start-FlareE2E -Fixture $Fixture -Force
Send-E2E open (Join-Path $run.Project "Content\billpay\faq.htm") | Out-Null
Invoke-Ribbon "Lint Topic"
$first = (Wait-PaneDone).pane.panel
Assert-E2E ($first.rows.Count -gt 0) "Lint Topic found issues in faq.htm"

Assert-E2E (Test-PaneOnScreen) "the issues pane is on screen after Lint Topic"
Send-E2E click "Close" | Out-Null
Wait-Until { -not (Get-E2EState).pane -and -not (Test-PaneOnScreen) } 10 "the pane's Close button to take the pane off screen" | Out-Null

Invoke-Ribbon "Reopen Issues"
$again = (Wait-PaneDone).pane.panel
Assert-E2E (Test-PaneOnScreen) "Reopen Issues put the pane back on screen"
Assert-E2E ($again.id -eq $first.id) "Reopen Issues showed the last results (a new lint makes a new panel)"
Assert-E2E (($again.rows.text -join "`n") -eq ($first.rows.text -join "`n")) "the same issues came back"
