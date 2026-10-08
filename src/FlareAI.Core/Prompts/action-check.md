Review the XHTML fragment below as a senior technical editor. Do not rewrite it.

An automatic linter has already flagged these issues — do NOT repeat them:
{{LINT}}

Report only what a linter cannot judge, ranked by impact on the reader: instructions that are not imperative, passive or unclear sentences, wrong audience terms, missing first-use expansions, sentences doing two jobs, vague or promissory claims, steps out of order. Skip anything trivial or subjective. At most 8 findings; fewer is better. If the text is already fine, return [].

Return ONLY a JSON array (no prose, no fences). Each item:
{"rule": "<2-4 word rule name>", "severity": "error|warning|suggestion", "excerpt": "<exact contiguous text copied from the fragment>", "replacement": "<exact text to put in place of excerpt, or null>", "why": "<max 15 words>"}

Excerpt rules — findings that break these are discarded:
- Copy it character-for-character from the fragment, as plain text, never including tags, and never spanning a tag (for example, text on both sides of a <b> or a MadCap element).
- Make it as short as possible while still unique: the words that change, plus a few around them if needed.
- Never quote text inside MadCap elements other than xref link text.

Replacement rules:
- Plain text only, same tense and grammar as the surrounding sentence, so it can be swapped in verbatim.
- Use null when the fix needs restructuring beyond the excerpt; say what to do in "why".

Fragment:
{{FRAGMENT}}
