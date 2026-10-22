p = 'pol-defects.yaml'
s = open('E:/flare-ai-plugin/.claude/worktrees/agent-a101244617958dc2d/tools/FlareAI.CorpusKit/Inject/defects.default.yaml', encoding='utf-8').read()
R = [
 ("texts: ['This feature is coming soon.', 'Support for more formats is coming soon.']", "texts: ['Support for additional reporting is coming soon.', 'A revised template is coming soon.']"),
 ("texts: ['Setup is free of charge.']", "texts: ['Awareness training is free of charge.']"),
 ("texts: ['Delivery is guaranteed within one hour.']", "texts: ['Compliance is guaranteed for every covered entity.']"),
 ("texts: ['You get a best-in-class experience.']", "texts: ['You get a best-in-class compliance program.']"),
 ("texts: ['Each change is reviewed by the support team before it is applied.'], marks: ['is reviewed by the support team']", "texts: ['Each request is reviewed by the privacy office before it is approved.'], marks: ['is reviewed by the privacy office']"),
 ("texts: ['At this point in time, it is important to be aware of the fact that results can vary.']", "texts: ['At this point in time, it is important to be aware of the fact that requirements can vary.']"),
 ("texts: ['Configure the remaining options as appropriate for your situation.'], marks: ['as appropriate for your situation']", "texts: ['Review the records as appropriate for your situation.'], marks: ['as appropriate for your situation']"),
 ("texts: ['Click the save button to keep your changes.'], marks: ['the save button']", "texts: ['Select the submit button to send the report.'], marks: ['the submit button']"),
 ("texts: ['Members with MFA turned on get a code each time.'], marks: ['MFA']", "texts: ['Report the incident to the CISO within the required period.'], marks: ['CISO']"),
 ("texts: ['Add the address to the whitelist.', 'Only addresses on the whitelist can connect.']", "texts: ['Add the vendor to the whitelist.', 'Only vendors on the whitelist can receive records.']"),
 ("texts: ['Items on the blacklist cannot be selected.']", "texts: ['Vendors on the blacklist cannot receive records.']"),
 ("texts: ['We recommend that you review this setting regularly.']", "texts: ['We recommend that you review this policy each year.']"),
 ("texts: ['Add a short label, e.g. the purpose of the item.']", "texts: ['Add a short note, e.g. the reason for the request.']"),
]
for a, b in R:
    if a not in s:
        print('MISSING', a[:60])
    s = s.replace(a, b)
months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']
extra = "  - kind: date_format\n    source: lint\n    rules: ['google:*']\n    ops:\n"
for i, m in enumerate(months, 1):
    extra += "      - {op: replace, find: '\\b%s (\\d{1,2}), (\\d{4})\\b', bad: '%02d/$1/$2', fix: '%s $1, $2'}\n" % (m, i, m)
s = s.rstrip('\n') + '\n' + extra
head = "# Policy defect catalog: the CorpusKit default (ops) with sentences rewritten for a policy set, plus date_format.\n# Used with: corpuskit inject --defects <this file>. Kinds are the ids of corpus/schema/defects.yaml.\n"
open(p, 'w', encoding='utf-8', newline='\n').write(head + s)
print('ok')
