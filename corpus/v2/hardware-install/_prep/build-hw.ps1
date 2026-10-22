param([string]$Out = 'C:\Users\arthu\AppData\Local\Temp\corpus-src\hw-proj', [int]$Seed = 20261021, [double]$Density = 0.10)
$ErrorActionPreference = 'Stop'
$s = 'C:\Users\arthu\AppData\Local\Temp\claude\C--Users-arthu--claude-spares-spare-hb2wzX\c6ad24e8-5d27-444b-83f8-74c51c4eb661\scratchpad'
$repo = 'E:\flare-ai-plugin\.claude\worktrees\agent-a101244617958dc2d'
$exe = "$repo\tools\FlareAI.CorpusKit\bin\Release\net48\FlareAI.CorpusKit.exe"
$md = 'C:\Users\arthu\AppData\Local\Temp\corpus-src\hw-md'
python -I "$s\addnames.py" | Out-Null
python -I "$s\adoc2html.py" "$s\hw-curated.json"
uv run --no-project --with pillow python "$s\shrink_images.py" "$md\images" | Select-Object -Last 1
if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
& $exe import --src "$md\src" --out $Out --name hardware-install --seed $Seed --alt-drop-rate 0 --snippet-min 3 --var "Lumora Devices=General.CompanyName:Lumora Devices" --var "Raspberry Pi=General.ProductName:Lumora" | Select-Object -First 3
& $exe scaffold --project $Out --name hardware-install --conditions "Print,ScreenOnly" --glossary "GPIO=General-purpose input/output.;PCIe=Peripheral Component Interconnect Express.;FFC=Flat flexible cable.;PoE=Power over Ethernet.;PMIC=Power management integrated circuit.;TOPS=Tera-operations per second." | Select-Object -First 1
python -I "$s\enrich_hw.py" $Out "$md\topics.tsv"
$rest = 'in_order_to,prior_to,is_able_to,please,simply,please_note,whitelist,blacklist,the_user,latin,will,we,claims_soon,claims_free,claims_guaranteed,marketing,passive,wordy,vague,ui_not_bold,undefined_abbr,click_here,phone,url,alt_missing,unit_space'
& $exe inject --project $Out --seed ($Seed + 2) --density 0.03 --project-id hardware-install --defects "$s\hw-defects.yaml" --kinds heading_skip | Select-Object -First 1
& $exe inject --project $Out --seed ($Seed + 3) --density 0.02 --project-id hardware-install --defects "$s\hw-defects.yaml" --kinds utilize | Select-Object -First 1
& $exe inject --project $Out --seed $Seed --density $Density --project-id hardware-install --defects "$s\hw-defects.yaml" --kinds $rest | Select-Object -First 1
& $exe inject --project $Out --seed ($Seed + 1) --density 0.02 --project-id hardware-install --defects "$s\hw-defects.yaml" --kinds exclaim | Select-Object -First 1
& $exe contexts --project $Out | Select-Object -First 1
python -I "$s\natural_labels.py" $Out hardware-install hardware
& $exe contexts --project $Out --update | Select-Object -First 1
Remove-Item "$Out\contexts.jsonl"
& $exe check --project $Out
