# Rules, on a team guide the writer can change: Edit Rule rewrites a rule in the guide topic and the list shows the new
# wording; Delete Rule removes another after the writer confirms, and the rules after it keep their references (#43).
param($Fixture)
$guide = Join-Path (Get-E2ERoot) ("guide-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
$topic = Join-Path $guide "Content\Resources\StyleGuide\Words.htm"
New-Item -ItemType Directory -Force (Split-Path $topic) | Out-Null
Set-Content (Join-Path $guide "Team.flprj") "<CatapultProject />"
Set-Content $topic -Encoding UTF8 @"
<?xml version="1.0" encoding="utf-8"?>
<html><body><h1>Our words</h1>
<ul><li>Say members when you mean our customers.<ul><li>Bad: Our users can sign in.</li></ul></li><li>Say sign in for the start of a session.</li><li>Spell out numbers below ten in running text.</li></ul>
</body></html>
"@

$run = Start-FlareE2E -Fixture $Fixture -Force -Settings @{ StyleLayers = $guide }
Send-E2E command "rules" -NoWait   # a dialog: the command answers when it closes
$d = Wait-Until { (Get-E2EState).dialogs | Where-Object { $_.title -like "*Rules*" -and @($_.rows).Count -gt 0 } } 60 "the Rules window with its list"
$row = @($d.rows | Where-Object { $_.text -like "Spell out numbers*" })[0]
Assert-E2E ($null -ne $row) "Rules lists the team guide's rules"
$numbersKey = $row.tip

# Edit Rule: new wording and a Good example, saved into the guide topic.
Send-E2E dialogSelect -Extra @{ title = "Rules"; text = "Say members" } | Out-Null
$d = (Get-E2EState).dialogs | Where-Object { $_.title -like "*Rules*" }
Assert-E2E (@($d.buttons | Where-Object { $_.text -eq "Edit Rule" -and $_.enabled }).Count -eq 1) "Edit Rule is enabled for a team rule (buttons: $(($d.buttons | ForEach-Object { "$($_.text)=$($_.enabled)" }) -join ' | '))"
Send-E2E dialogClick "Edit Rule" -Extra @{ title = "Rules" } -NoWait
$e = Wait-Until { (Get-E2EState).dialogs | Where-Object { $_.title -like "*Edit Rule*" } } 15 "the Edit Rule dialog"
Assert-E2E ((@($e.boxes | Where-Object { $_.name -eq "Bad" })[0].text) -eq "Our users can sign in.") "Edit Rule shows the rule's Bad example"
Send-E2E dialogType "Say members, not users, for our customers." -Extra @{ title = "Edit Rule"; name = "Rule" } | Out-Null
Send-E2E dialogType "Our members can sign in." -Extra @{ title = "Edit Rule"; name = "Good" } | Out-Null
Send-E2E dialogClick "Save" -Extra @{ title = "Edit Rule" } | Out-Null
Wait-Until { -not ((Get-E2EState).dialogs | Where-Object { $_.title -like "*Edit Rule*" }) } 15 "Edit Rule to close" | Out-Null
$text = Get-Content $topic -Raw
Assert-E2E ($text -like "*Say members, not users, for our customers.*" -and $text -like "*Good: Our members can sign in.*" -and $text -like "*Bad: Our users can sign in.*") "the guide topic has the new wording and both examples"
$d = Wait-Until { (Get-E2EState).dialogs | Where-Object { $_.title -like "*Rules*" -and @($_.rows | Where-Object { $_.text -like "Say members, not users*" }).Count -eq 1 } } 30 "the list to show the new wording"

# Delete Rule: asks first, then the rule is gone and the rule after it keeps its reference.
Send-E2E dialogSelect -Extra @{ title = "Rules"; text = "Say sign in" } | Out-Null
Send-E2E dialogClick "Delete Rule" -Extra @{ title = "Rules" } -NoWait
Confirm-MessageBox "Delete Rule" "OK"
$d = Wait-Until { (Get-E2EState).dialogs | Where-Object { $_.title -like "*Rules*" -and @($_.rows).Count -gt 0 -and -not @($_.rows | Where-Object { $_.text -like "Say sign in*" }).Count } } 30 "the deleted rule to leave the list"
Assert-E2E ((Get-Content $topic -Raw) -notlike "*Say sign in*") "the guide topic no longer has the deleted rule"
$after = @($d.rows | Where-Object { $_.text -like "Spell out numbers*" })[0]
Assert-E2E ($after.tip -eq $numbersKey) "the next rule keeps its reference ($numbersKey, now $($after.tip))"
Send-E2E dialogClick "Close" -Extra @{ title = "Rules" } | Out-Null
Remove-Item $guide -Recurse -Force -ErrorAction SilentlyContinue
