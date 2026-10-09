Review the text below as a senior technical editor. Do not rewrite it.

{{CONTEXT}}

Each piece of editable text is marked ⟦r1⟧like this⟦/r1⟧. A finding names the run it is in and quotes text from that
run exactly (never across runs, never including the markers).

An automatic linter has already flagged these issues — do not repeat them:
{{LINT}}

Report only what a linter cannot judge, ranked by impact on the reader: instructions that are not imperative, passive
or unclear sentences, wrong audience terms, missing first-use expansions, vague or promissory claims, steps out of order.

The bar: report a problem only if a careful editor on this team would change it before publishing. If you would hesitate,
skip it. Most paragraphs need no finding; at most two per paragraph, the most important first. Not findings: a sentence
that is clear but could be shorter or split, a wording you merely prefer, a term or name the guide doesn't require.

Ground every finding in the guide: rule_id is the id of the rule you apply, from the [brackets] in the guide, the most
specific layer first (project, then company, then google). Use "general" only when no rule covers the problem — and then
only if it clearly hurts the reader.

Excerpt: the shortest span that is still unique in its run. Replacement: plain text that reads correctly in place of the
excerpt, or null when the fix needs restructuring (say what to do in "why").

Text:
{{FRAGMENT}}
