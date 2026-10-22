param([string]$Out = 'C:\Users\arthu\AppData\Local\Temp\corpus-src\pol-proj', [int]$Seed = 20261022, [double]$Density = 0.05)
$ErrorActionPreference = 'Stop'
$s = 'C:\Users\arthu\AppData\Local\Temp\claude\C--Users-arthu--claude-spares-spare-hb2wzX\c6ad24e8-5d27-444b-83f8-74c51c4eb661\scratchpad'
$repo = 'E:\flare-ai-plugin\.claude\worktrees\agent-a101244617958dc2d'
$exe = "$repo\tools\FlareAI.CorpusKit\bin\Release\net48\FlareAI.CorpusKit.exe"
$md = 'C:\Users\arthu\AppData\Local\Temp\corpus-src\pol-md'
python -I "$s\policy_build.py" $md
python -I "$s\fixdefects_pol.py"
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
& $exe import --src "$md\src" --out $Out --name policy --seed $Seed --alt-drop-rate 0 --snippet-min 3 --var "Alderbrook Care Network=General.CompanyName:Alderbrook Care Network" | Select-Object -First 3
& $exe scaffold --project $Out --name policy --conditions "Print,ScreenOnly" --glossary "ePHI=Electronic protected health information.;PHI=Protected health information.;PII=Personally identifiable information.;ODP=Organization-defined parameter.;HHS=Department of Health and Human Services." | Select-Object -First 1
python -I "$s\enrich_pol.py" $Out "$md\topics.tsv"
$rest = 'in_order_to,prior_to,is_able_to,please,simply,please_note,whitelist,blacklist,the_user,latin,will,we,claims_soon,claims_free,claims_guaranteed,marketing,passive,wordy,vague,ui_not_bold,undefined_abbr,click_here,phone,url,alt_missing,date_format'
& $exe inject --project $Out --seed ($Seed + 2) --density 0.03 --project-id policy --defects "$s\pol-defects.yaml" --kinds heading_skip | Select-Object -First 1
& $exe inject --project $Out --seed ($Seed + 3) --density 0.01 --project-id policy --defects "$s\pol-defects.yaml" --kinds utilize | Select-Object -First 1
& $exe inject --project $Out --seed $Seed --density $Density --project-id policy --defects "$s\pol-defects.yaml" --kinds $rest | Select-Object -First 1
& $exe inject --project $Out --seed ($Seed + 1) --density 0.01 --project-id policy --defects "$s\pol-defects.yaml" --kinds exclaim | Select-Object -First 1
& $exe contexts --project $Out | Select-Object -First 1
python -I "$s\natural_labels.py" $Out policy policy
& $exe contexts --project $Out --update | Select-Object -First 1
Remove-Item "$Out\contexts.jsonl"
& $exe check --project $Out
