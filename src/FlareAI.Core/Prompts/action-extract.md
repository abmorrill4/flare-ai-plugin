Below are an author's notes about their team's writing style: rambling thoughts, an old style guide, a prompt they
used with another AI tool, or a terminology list. Turn them into style-guide rules.

Return ONLY a JSON array (no prose, no fences). One item per rule, in one of two shapes:

A word or phrase to replace or avoid (a linter can enforce it):
{"title": "<short name>", "avoid": "<word or phrase to avoid; several separated by commas>", "use": "<replacement; as many comma-separated entries as avoid, or \"(delete)\", or \"\" to only flag it>", "severity": "error|warning|suggestion", "why": "<max 12 words>", "example": "<a short realistic sentence that contains the avoided word>", "quote": "<the words in the notes this comes from>"}

A writing rule that needs judgment (the AI reviewer applies it):
{"title": "<short name>", "text": "<the rule, one imperative sentence>", "bad": "<a short sentence that breaks it>", "good": "<the same sentence fixed>", "quote": "<the words in the notes this comes from>"}

Rules:
- Only rules the notes actually state or clearly imply. Do not add general writing advice of your own.
- Skip rules the Google developer documentation style guide already covers, unless the notes contradict it.
- Keep each rule to one point. Split combined rules.
- The quote must be copied from the notes.

Notes:
{{NOTES}}
