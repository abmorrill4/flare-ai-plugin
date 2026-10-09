A reviewer proposed a change to one passage of a technical document. The author wants it refined.

Passage (plain text of the paragraph):
{{CONTEXT}}

The text to change (exact, inside the passage):
{{EXCERPT}}

The reviewer's proposal and reason:
{{PROPOSAL}}

The author's instruction:
{{INSTRUCTION}}

Follow the author's instruction while still applying the style guide. Return ONLY a JSON object (no prose, no fences):
{"replacement": "<exact plain text to put in place of the text to change>", "why": "<max 15 words>"}

The replacement must read correctly when swapped in for exactly the text to change, with the same tense and grammar
as the rest of the passage. If the instruction means the text should stay as it is, return the text to change unchanged.
