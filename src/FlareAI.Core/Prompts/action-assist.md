Here is a MadCap Flare topic. Propose metadata and companion content an author would otherwise write by hand. Use
only what the topic says; do not invent features, limits or steps. Follow the style guide.

Return ONLY a JSON object (no prose, no fences):
{
  "description": "<meta description: what the reader can do or learn here, at most 155 characters>",
  "keywords": ["<index keyword: the terms a reader would look up to find this topic; 3-8 entries; use 'Parent:Child' for subentries>"],
  "synonyms": [["<a term in the topic>", "<words readers might search for instead>"]],
  "micro_content": [{"phrase": "<a question or phrase a reader would search for>", "response": "<a 1-2 sentence answer taken from the topic>"}],
  "glossary": [{"term": "<a product or domain term the topic uses but doesn't define>", "definition": "<one sentence, from the topic's context>"}]
}

At most 5 synonym groups, 5 micro content entries and 5 glossary terms; fewer is fine. Skip terms already in the
glossary below.

Glossary terms already defined:
{{GLOSSARY}}

Topic:
{{FRAGMENT}}
