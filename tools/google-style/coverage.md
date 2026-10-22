# Google style guide: compile coverage

3133 source units in compiled pages: 2749 covered by rules, 380 not applicable (with reasons), 4 uncovered, 6 rules dropped by the validator. 1991 rules: 1565 prose (AI), 320 lint rows, 106 patterns.

## Pages

| Page | Scope | Units | Rules | Not applicable | Uncovered + dropped | Errors / note |
|---|---|---|---|---|---|---|
| about | reference | 23 | — | — | — | How the guide works and which sources win; no writing rules. |
| highlights | reference | 21 | — | — | — | Summary; each item links to the page that holds the rule. |
| whats-new | reference | 636 | — | — | — | Change log: drives updates of this layer, not reviews. |
| philosophy | reference | 16 | — | — | — | Why the guide exists; no writing rules. |
| word-list | lint | 619 | 753 | 38 | 8 |  |
| product-names | core | 25 | 14 | 4 | 0 |  |
| text-formatting | reference | 10 | — | — | — | Summary table of formatting rules on other pages. |
| accessibility | core | 87 | 67 | 14 | 0 |  |
| excessive-claims | core | 15 | 7 | 1 | 0 |  |
| future | core | 2 | 2 | 1 | 0 |  |
| translation | core | 63 | 47 | 10 | 0 |  |
| inclusive-documentation | core | 71 | 43 | 9 | 0 |  |
| jargon | core | 8 | 7 | 2 | 0 |  |
| prescriptive-documentation | core | 25 | 12 | 3 | 0 |  |
| other-sources | pack:links | 9 | 5 | 0 | 0 |  |
| timeless-documentation | core | 28 | 20 | 4 | 0 |  |
| tone | core | 34 | 26 | 6 | 0 |  |
| abbreviations | pack:abbreviations | 74 | 26 | 19 | 0 |  |
| voice | core | 16 | 6 | 1 | 0 |  |
| anthropomorphism | core | 6 | 2 | 0 | 0 |  |
| articles | core | 7 | 1 | 4 | 0 |  |
| capitalization | core | 26 | 19 | 4 | 0 |  |
| contractions | pack:apostrophes | 5 | 7 | 0 | 0 |  |
| pluralization | core | 46 | 17 | 6 | 0 |  |
| possessives | pack:apostrophes | 26 | 9 | 2 | 0 |  |
| prepositions | core | 6 | 4 | 1 | 0 |  |
| tense | core | 12 | 4 | 0 | 0 |  |
| pronouns | core | 33 | 14 | 5 | 0 |  |
| person | core | 16 | 13 | 0 | 0 |  |
| sentence-structure | core | 5 | 1 | 1 | 0 |  |
| reference-verbs | pack:table | 5 | 1 | 1 | 0 |  |
| colons | pack:colons | 10 | 2 | 4 | 0 |  |
| commas | core | 28 | 10 | 5 | 0 |  |
| dashes | pack:dashes | 21 | 6 | 11 | 0 |  |
| ellipses | pack:ellipses | 14 | 12 | 0 | 0 |  |
| hyphens | pack:hyphens | 80 | 33 | 6 | 0 |  |
| parentheses | pack:parentheses | 9 | 3 | 0 | 0 |  |
| periods | core | 42 | 19 | 11 | 2 |  |
| quotation-marks | pack:quotes | 34 | 14 | 2 | 0 |  |
| semicolons | pack:semicolons | 4 | 4 | 0 | 0 |  |
| slashes | pack:slashes | 27 | 14 | 1 | 0 |  |
| dates-times | pack:numbers | 52 | 38 | 6 | 0 |  |
| format-examples | pack:code | 5 | 5 | 2 | 0 |  |
| images | pack:images | 98 | 70 | 28 | 0 |  |
| footnotes | pack:footnotes | 8 | 3 | 0 | 0 |  |
| headings | pack:headings | 45 | 31 | 1 | 0 |  |
| italics-terms | pack:emphasis | 7 | 2 | 2 | 0 |  |
| lists | pack:lists | 150 | 36 | 9 | 0 |  |
| mathematical-notation | pack:math | 71 | 21 | 6 | 0 |  |
| notices | pack:notes | 20 | 16 | 3 | 0 |  |
| numbers | pack:numbers | 72 | 51 | 10 | 0 |  |
| paragraph-structure | core | 6 | 8 | 0 | 0 |  |
| phone-numbers | pack:numbers | 14 | 10 | 2 | 0 |  |
| procedures | pack:steps | 111 | 46 | 8 | 0 |  |
| tables | pack:table | 43 | 36 | 9 | 0 |  |
| units-of-measure | pack:numbers | 53 | 24 | 6 | 0 |  |
| cross-references | pack:links | 73 | 29 | 5 | 0 |  |
| headings-targets | pack:links | 31 | 5 | 15 | 0 |  |
| api-reference-comments | pack:api | 71 | 46 | 7 | 0 |  |
| code-in-text | pack:code | 110 | 58 | 12 | 0 |  |
| code-samples | pack:code | 28 | 9 | 14 | 0 |  |
| code-syntax | pack:code | 115 | 45 | 32 | 0 |  |
| placeholders | pack:code | 99 | 27 | 13 | 0 |  |
| ui-elements | pack:ui | 159 | 74 | 7 | 0 |  |
| semantic-tagging | pack:code | 9 | 10 | 1 | 0 |  |
| html-formatting | reference | 13 | — | — | — | Source-HTML formatting (indentation, quoting attributes); Flare writes the XHTML, writers don't. |
| markdown | reference | 3 | — | — | — | When to use Markdown vs HTML in Google's own docs; not applicable to Flare topics. |
| examples | pack:examples | 82 | 27 | 2 | 0 |  |
| filenames | pack:code | 53 | 14 | 2 | 0 |  |
| trademarks | core | 10 | 6 | 2 | 0 |  |

## Uncovered units and dropped rules

- periods#34
- dropped in periods: pattern '(?<=[.?!]) {2,}(?=[A-Z])': The Example doesn't trigger this rule: 'The VM is created. Next, connect to it.'.
- word-list#341
- word-list#343
- word-list#534
- dropped in word-list: row 'anti-pattern': The Example doesn't trigger this rule: 'Avoid these five SQL anti-patterns.'.
- dropped in word-list: row 'touch and hold': The Example doesn't trigger this rule: 'To open the menu, touch and hold the app icon.'.
- dropped in word-list: row 'long press': The Example doesn't trigger this rule: 'To open the menu, long press the app icon.'.
- dropped in word-list: row 'lockscreen, lock-screen': The Example doesn't trigger this rule: 'Swipe up on the lockscreen.'.
- dropped in word-list: row 'Tap & hold, Tap and hold, tap & hold, tap and hold': The Example doesn't trigger this rule: 'To open the menu, tap and hold the icon.'.

## Compiler passes (canonical owner, Product tags, Term scope, fixes)

- numbers: google:numbers/32 dropped (\b\d+(?:\.\d+)?\s+%): owners.json: Units-Of-Measure owns the space before %
- numbers: google:numbers/35 dropped (\d\s*(?:–|&ndash;)\s*\d): owners.json: Hyphens owns en dashes in number ranges
- procedures: google:procedures/39 dropped (please, Please): duplicate of google:tone/28
- ellipses: google:ellipses/10 Use '...' → '' (fixes.json: three periods are still an ellipsis; Google says not to use ellipses in text or UI references)
- ellipses: google:ellipses/10 severity → 'suggestion' (fixes.json)
- ellipses: google:ellipses/10 why → 'Don't use ellipses; leave them out of UI labels.' (fixes.json)
- periods: google:periods/14 dropped (\b(?:[A-Z]\.){2,}): duplicate of google:abbreviations/25, google:word-list/719
- dashes: google:dashes/4 pattern → '(?<!\d)\u2013|\u2013(?!\d)' (fixes.json)
- dashes: google:dashes/4 example → 'Install the app – then restart it.' (fixes.json)
- pluralization: google:pluralization/13 dropped (\b\d+(?:\.\d+)?(?:KB|MB|GB|TB|PB)\b): duplicate of google:units-of-measure/4
- accessibility: google:accessibility/30 drops 'click here' (owned by google:word-list/136)
- accessibility: google:accessibility/30 Example → 'Don't use click here or read this document.'
- tone: google:tone/19 dropped (let's, Let's): duplicate of google:word-list/432
- tone: google:tone/20 drops 'simply' (owned by google:inclusive-documentation/20)
- tone: google:tone/20 drops 'Simply' (owned by google:inclusive-documentation/20)
- tone: google:tone/21 dropped (tl;dr, TL;DR, ymmv, YMMV): duplicate of google:word-list/694, google:word-list/765
- slashes: google:slashes/3 dropped ((?<![\w/.])\d{1,2}/\d{1,2}/(?:\d{4}|\d{2})(?![\w/])): duplicate of google:dates-times/33
- hyphens: google:hyphens/19 drops 'web page' (fixes.json: inferred by the model, not in the source (Google itself writes web page))
- hyphens: google:hyphens/19 drops 'trade-off' (fixes.json: inferred by the model, not in the source (Google itself writes web page))
- hyphens: google:hyphens/19 drops 'work-around' (fixes.json: inferred by the model, not in the source (Google itself writes web page))
- hyphens: google:hyphens/19 Example → 'Open the host name in a browser.'
- hyphens: google:hyphens/19 dropped (host name): duplicate of google:word-list/362
- abbreviations: google:abbreviations/18 dropped (i.e., e.g.): duplicate of google:word-list/244, google:word-list/377
- abbreviations: google:abbreviations/19 dropped (tl;dr, TL;DR, ymmv, YMMV, RTFM): duplicate of google:word-list/606, google:word-list/694, google:word-list/765
- abbreviations: google:abbreviations/24 drops 'U.S.' (owned by google:word-list/719)
- abbreviations: google:abbreviations/24 Example → 'The service is available in the U.K. only.'
- abbreviations: google:abbreviations/28 drops 'an SQL' (owned by google:word-list/283, google:word-list/613, google:word-list/653)
- abbreviations: google:abbreviations/28 drops 'an FHIR' (owned by google:word-list/283, google:word-list/613, google:word-list/653)
- abbreviations: google:abbreviations/28 drops 'An FHIR' (owned by google:word-list/283, google:word-list/613, google:word-list/653)
- abbreviations: google:abbreviations/28 drops 'a SAP' (owned by google:word-list/283, google:word-list/613, google:word-list/653)
- abbreviations: google:abbreviations/28 Example → 'Run An SQL query against the table.'
- parentheses: google:parentheses/4 dropped (\b[A-Za-z]+\((s|es)\)): owners.json: pluralization/18 is the broader (s)/(es)/(ren) rule
- code-syntax: google:code-syntax/32 Product.GoogleCloud (products.json lists the rule)
- code-syntax: google:code-syntax/34 Product.GoogleCloud (its section is about it)
- code-syntax: google:code-syntax/36 Product.GoogleCloud (products.json lists the rule)
- cross-references: google:cross-references/15 dropped (click here, Click here): duplicate of google:word-list/136
- inclusive-documentation: google:inclusive-documentation/2 drops 'man-hours' (owned by google:word-list/450)
- inclusive-documentation: google:inclusive-documentation/2 Example → 'Build AI that benefits mankind.'
- inclusive-documentation: google:inclusive-documentation/10 drops 'crazy' (owned by google:word-list/100, google:word-list/169, google:word-list/172, google:word-list/612)
- inclusive-documentation: google:inclusive-documentation/10 drops 'insane' (owned by google:word-list/100, google:word-list/169, google:word-list/172, google:word-list/612)
- inclusive-documentation: google:inclusive-documentation/10 drops 'cripple' (owned by google:word-list/100, google:word-list/169, google:word-list/172, google:word-list/612)
- inclusive-documentation: google:inclusive-documentation/10 drops 'cripples' (owned by google:word-list/100, google:word-list/169, google:word-list/172, google:word-list/612)
- inclusive-documentation: google:inclusive-documentation/10 drops 'crippled' (owned by google:word-list/100, google:word-list/169, google:word-list/172, google:word-list/612)
- inclusive-documentation: google:inclusive-documentation/10 drops 'blind to' (owned by google:word-list/100, google:word-list/169, google:word-list/172, google:word-list/612)
- inclusive-documentation: google:inclusive-documentation/10 drops 'sanity check' (owned by google:word-list/100, google:word-list/169, google:word-list/172, google:word-list/612)
- inclusive-documentation: google:inclusive-documentation/29 drops 'blacklist' (owned by google:word-list/292, google:word-list/95)
- inclusive-documentation: google:inclusive-documentation/29 drops 'first-class citizen' (owned by google:word-list/292, google:word-list/95)
- inclusive-documentation: google:inclusive-documentation/29 Example → 'For example, avoid terms such as blacklist and native feature, and don't use terms like first-class citizen, even though such terms might still be widely used.'
- jargon: google:jargon/7 dropped (blast radius): duplicate of google:word-list/98
- jargon: google:jargon/9 dropped (off-the-shelf): duplicate of google:word-list/519
- api-reference-comments: google:api-reference-comments/17 dropped (e.g.): duplicate of google:word-list/244
- ui-elements: google:ui-elements/13 dropped (hamburger icon, zippy, expando): duplicate of google:word-list/273, google:word-list/340
- examples: google:examples/27 drops 'foo' (owned by google:word-list/294)
- examples: google:examples/27 Example → 'Don't use unclear components like foo, bar, and baz in names.'
- examples: google:examples/18 dropped (\b(?!800[-‑.]555[-‑.]01\d\d\b)\d{3}[-‑.]\d{3}[-‑.]\d{4}\b): owners.json: Phone-Numbers owns phone formats; the Examples page's 555 advice stays prose
- word-list: google:word-list/23 claims units of several entries (action bar; app bar); its Term is its own entry, 'action bar'
- word-list: google:word-list/23 Term 'action bar, app bar' → 'action bar'
- word-list: google:word-list/144 claims units of several entries (Cloud console; console); its Term is its own entry, 'Cloud console'
- word-list: google:word-list/144 Term 'Cloud console, console' → 'Cloud console'
- word-list: google:word-list/145 claims units of several entries (Cloud console; console); its Term is its own entry, 'Cloud console'
- word-list: google:word-list/145 Term 'Cloud console, console' → 'Cloud console'
- word-list: google:word-list/232 claims units of several entries (drop-down; downscope); its Term is its own entry, 'drop-down'
- word-list: google:word-list/232 Term 'drop-down, downscope' → 'drop-down'
- word-list: google:word-list/233 claims units of several entries (drop-down; downscope); its Term is its own entry, 'drop-down'
- word-list: google:word-list/233 Term 'drop-down, downscope' → 'drop-down'
- word-list: google:word-list/278 Term 'fail over (verb), failover (noun, adjective)' → 'fail over (verb), failover (noun or adjective)'
- word-list: google:word-list/315 Term 'Google API Client Library for LANGUAGE (Java, .NET, etc.)' → 'Google API Client Library for LANGUAGE (Java or .NET or etc.)'
- word-list: google:word-list/349 claims units of several entries (health check; healthy); its Term is its own entry, 'health check'
- word-list: google:word-list/349 Term 'health check, healthy' → 'health check'
- word-list: google:word-list/479 claims units of several entries (mobile device; mobile phone); its Term is its own entry, 'mobile device'
- word-list: google:word-list/479 Term 'mobile device, mobile phone' → 'mobile device'
- word-list: google:word-list/548 claims units of several entries (personally identifiable information (PII); persistent disk); its Term is its own entry, 'personally identifiable information (PII)'
- word-list: google:word-list/548 Term 'personally identifiable information (PII), persistent disk' → 'personally identifiable information (PII)'
- word-list: google:word-list/550 claims units of several entries (plain text; persistent disk); its Term is its own entry, 'plain text'
- word-list: google:word-list/550 Term 'plain text, persistent disk' → 'plain text'
- word-list: google:word-list/551 claims units of several entries (please; persistent disk); its Term is its own entry, 'please'
- word-list: google:word-list/551 Term 'please, persistent disk' → 'please'
- word-list: google:word-list/552 claims units of several entries (please; persistent disk); its Term is its own entry, 'please'
- word-list: google:word-list/552 Term 'please, persistent disk' → 'please'
- word-list: google:word-list/554 claims units of several entries (plugin (noun), plug-in (adjective), plug in (verb); persistent disk; please); its Term is its own entry, 'plugin (noun), plug-in (adjective), plug in (verb)'
- word-list: google:word-list/554 Term 'plugin (noun), plug-in (adjective), plug in (verb), persistent disk, please' → 'plugin (noun), plug-in (adjective), plug in (verb)'
- word-list: google:word-list/555 claims units of several entries (plugin (noun), plug-in (adjective), plug in (verb); persistent disk; please); its Term is its own entry, 'plugin (noun), plug-in (adjective), plug in (verb)'
- word-list: google:word-list/555 Term 'plugin (noun), plug-in (adjective), plug in (verb), persistent disk, please' → 'plugin (noun), plug-in (adjective), plug in (verb)'
- word-list: google:word-list/556 claims units of several entries (plugin (noun), plug-in (adjective), plug in (verb); persistent disk; please); its Term is its own entry, 'plugin (noun), plug-in (adjective), plug in (verb)'
- word-list: google:word-list/556 Term 'plugin (noun), plug-in (adjective), plug in (verb), persistent disk, please' → 'plugin (noun), plug-in (adjective), plug in (verb)'
- word-list: google:word-list/557 claims units of several entries (point to; persistent disk; please); its Term is its own entry, 'point to'
- word-list: google:word-list/557 Term 'point to, persistent disk, please' → 'point to'
- word-list: google:word-list/558 claims units of several entries (point to; persistent disk; please); its Term is its own entry, 'point to'
- word-list: google:word-list/558 Term 'point to, persistent disk, please' → 'point to'
- word-list: google:word-list/559 claims units of several entries (POJO; persistent disk; please); its Term is its own entry, 'POJO'
- word-list: google:word-list/559 Term 'POJO, persistent disk, please' → 'POJO'
- word-list: google:word-list/562 claims units of several entries (populate; persistent disk; please); its Term is its own entry, 'populate'
- word-list: google:word-list/562 Term 'populate, persistent disk, please' → 'populate'
- word-list: google:word-list/564 claims units of several entries (portal; persistent disk; please); its Term is its own entry, 'portal'
- word-list: google:word-list/564 Term 'portal, persistent disk, please' → 'portal'
- word-list: google:word-list/565 claims units of several entries (possible; persistent disk; please); its Term is its own entry, 'possible'
- word-list: google:word-list/565 Term 'possible, persistent disk, please' → 'possible'
- word-list: google:word-list/566 claims units of several entries (PostgreSQL; persistent disk; please); its Term is its own entry, 'PostgreSQL'
- word-list: google:word-list/566 Term 'PostgreSQL, persistent disk, please' → 'PostgreSQL'
- word-list: google:word-list/567 claims units of several entries (postmortem; persistent disk; please); its Term is its own entry, 'postmortem'
- word-list: google:word-list/567 Term 'postmortem, persistent disk, please' → 'postmortem'
- word-list: google:word-list/568 claims units of several entries (postmortem; persistent disk; please); its Term is its own entry, 'postmortem'
- word-list: google:word-list/568 Term 'postmortem, persistent disk, please' → 'postmortem'
- word-list: google:word-list/569 claims units of several entries (practitioner; persistent disk; please); its Term is its own entry, 'practitioner'
- word-list: google:word-list/569 Term 'practitioner, persistent disk, please' → 'practitioner'
- word-list: google:word-list/578 claims units of several entries (press; persistent disk; please); its Term is its own entry, 'press'
- word-list: google:word-list/578 Term 'press, persistent disk, please' → 'press'
- word-list: google:word-list/579 claims units of several entries (press; persistent disk; please); its Term is its own entry, 'press'
- word-list: google:word-list/579 Term 'press, persistent disk, please' → 'press'
- word-list: google:word-list/581 claims units of several entries (primitive; persistent disk; please); its Term is its own entry, 'primitive'
- word-list: google:word-list/581 Term 'primitive, persistent disk, please' → 'primitive'
- word-list: google:word-list/582 claims units of several entries (project; persistent disk; please); its Term is its own entry, 'project'
- word-list: google:word-list/582 Term 'project, persistent disk, please' → 'project'
- word-list: google:word-list/583 claims units of several entries (proper, properly; persistent disk; please); its Term is its own entry, 'proper, properly'
- word-list: google:word-list/583 Term 'proper, properly, persistent disk, please' → 'proper, properly'
- word-list: google:word-list/625 claims units of several entries (service level agreement; service level indicator; service level objective); its Term is its own entry, 'service level agreement'
- word-list: google:word-list/625 Term 'service level agreement, service level indicator, service level objective' → 'service level agreement'
- word-list: google:word-list/626 claims units of several entries (service level indicator; service level objective); its Term is its own entry, 'service level indicator'
- word-list: google:word-list/626 Term 'service level indicator, service level objective' → 'service level indicator'
- word-list: google:word-list/637 claims units of several entries (sign-in (noun or adjective), sign in (verb); sign-out (noun or adjective), sign out (verb)); its Term is its own entry, 'sign-in (noun or adjective), sign in (verb)'
- word-list: google:word-list/637 Term 'sign-in (noun or adjective), sign in (verb), sign-out (noun or adjective), sign out (verb)' → 'sign-in (noun or adjective), sign in (verb)'
- word-list: google:word-list/677 claims units of several entries (target; tap); its Term is its own entry, 'target'
- word-list: google:word-list/677 Term 'target, tap' → 'target'
- word-list: google:word-list/678 claims units of several entries (terminate; tap); its Term is its own entry, 'terminate'
- word-list: google:word-list/678 Term 'terminate, tap' → 'terminate'
- word-list: google:word-list/679 claims units of several entries (text box, textbox; terminate); its Term is its own entry, 'text box, textbox'
- word-list: google:word-list/679 Term 'text box, textbox, terminate' → 'text box, textbox'
- word-list: google:word-list/681 claims units of several entries (then; terminate; tap); its Term is its own entry, 'then'
- word-list: google:word-list/681 Term 'then, terminate, tap' → 'then'
- word-list: google:word-list/682 claims units of several entries (they (singular); terminate; tap); its Term is its own entry, 'they (singular)'
- word-list: google:word-list/682 Term 'they (singular), terminate, tap' → 'they (singular)'
- word-list: google:word-list/684 claims units of several entries (third party (noun), third-party (adjective); terminate; tap); its Term is its own entry, 'third party (noun), third-party (adjective)'
- word-list: google:word-list/684 Term 'third party (noun), third-party (adjective), terminate, tap' → 'third party (noun), third-party (adjective)'
- word-list: google:word-list/686 claims units of several entries (this, that; terminate; tap); its Term is its own entry, 'this, that'
- word-list: google:word-list/686 Term 'this, that, terminate, tap' → 'this, that'
- word-list: google:word-list/689 claims units of several entries (timeout (noun), time out (verb); terminate; tap); its Term is its own entry, 'timeout (noun), time out (verb)'
- word-list: google:word-list/689 Term 'timeout (noun), time out (verb), terminate, tap' → 'timeout (noun), time out (verb)'
- word-list: google:word-list/693 claims units of several entries (time zone (noun), time-zone (adjective); terminate; tap); its Term is its own entry, 'time zone (noun), time-zone (adjective)'
- word-list: google:word-list/693 Term 'time zone (noun), time-zone (adjective), terminate, tap' → 'time zone (noun), time-zone (adjective)'
- word-list: google:word-list/696 claims units of several entries (touch; terminate; tap); its Term is its own entry, 'touch'
- word-list: google:word-list/696 Term 'touch, terminate, tap' → 'touch'
- word-list: google:word-list/699 claims units of several entries (traditional; terminate; tap); its Term is its own entry, 'traditional'
- word-list: google:word-list/699 Term 'traditional, terminate, tap' → 'traditional'
- word-list: google:word-list/702 claims units of several entries (trojan; terminate; tap); its Term is its own entry, 'trojan'
- word-list: google:word-list/702 Term 'trojan, terminate, tap' → 'trojan'
- word-list: google:word-list/703 claims units of several entries (turn on; terminate; tap); its Term is its own entry, 'turn on'
- word-list: google:word-list/703 Term 'turn on, terminate, tap' → 'turn on'
- word-list: google:word-list/705 claims units of several entries (type; terminate; tap); its Term is its own entry, 'type'
- word-list: google:word-list/705 Term 'type, terminate, tap' → 'type'
- word-list: google:word-list/720 claims units of several entries (user; unselect); its Term is its own entry, 'user'
- word-list: google:word-list/720 Term 'user, unselect' → 'user'
- word-list: google:word-list/723 claims units of several entries (using; unselect); its Term is its own entry, 'using'
- word-list: google:word-list/723 Term 'using, unselect' → 'using'
- word-list: google:word-list/725 claims units of several entries (utilize, utilization; UTF; unselect); its Term is its own entry, 'utilize, utilization'
- word-list: google:word-list/725 Term 'utilize, utilization, UTF, unselect' → 'utilize, utilization'
- word-list: google:word-list/62 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/141 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/142 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/143 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/144 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/145 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/146 Product.GoogleCloud (its fix writes the product name)
- word-list: google:word-list/147 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/176 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/217 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/242 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/243 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/275 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/276 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/317 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/318 Product.GoogleCloud (its fix writes the product name)
- word-list: google:word-list/321 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/322 Product.GoogleCloud (its fix writes the product name)
- word-list: google:word-list/388 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/391 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/392 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/393 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/395 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/500 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/538 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/546 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/547 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/599 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/697 Product.Android (products.json lists the entry)
- word-list: google:word-list/733 Product.GoogleCloud (products.json lists the entry)
- word-list: google:word-list/450 Use 'person hours' → 'person-hours' (fixes.json: one fix for man-hours: Inclusive-Documentation and Hyphens write person-hours)
- word-list: google:word-list/340 takes over 'Hamburger icon' from google:ui-elements/13
- word-list: google:word-list/26 dropped (omnibox): duplicate of google:word-list/523
- word-list: google:word-list/57 dropped (as of this writing, As of this writing): duplicate of google:timeless-documentation/15
- word-list: google:word-list/95 drops 'whitelist' (owned by google:word-list/333, google:word-list/752)
- word-list: google:word-list/95 drops 'graylist' (owned by google:word-list/333, google:word-list/752)
- word-list: google:word-list/95 drops 'greylist' (owned by google:word-list/333, google:word-list/752)
- word-list: google:word-list/129 dropped (Uncheck, uncheck): duplicate of google:word-list/711
- word-list: google:word-list/202 dropped (Uncheck, uncheck): duplicate of google:word-list/711
- word-list: google:word-list/223 dropped (does not yet): duplicate of google:timeless-documentation/17
- word-list: google:word-list/260 drops 'allows you to' (owned by google:word-list/39)
- word-list: google:word-list/277 drops 'unarchive' (owned by google:word-list/709)
- word-list: google:word-list/277 drops 'Unarchive' (owned by google:word-list/709)
- word-list: google:word-list/301 dropped (in the future, In the future): duplicate of google:timeless-documentation/19
- word-list: google:word-list/303 dropped (Gb/s): duplicate of google:units-of-measure/23
- word-list: google:word-list/306 dropped (he/she, (s)he, s/he, him/her, his/her): duplicate of google:pronouns/6
- word-list: google:word-list/368 dropped (hover over, Hover over): duplicate of google:inclusive-documentation/15
- word-list: google:word-list/478 dropped (cellular data): duplicate of google:word-list/121
- word-list: google:word-list/480 dropped (cellular network): duplicate of google:word-list/122
- word-list: google:word-list/577 dropped (presently, Presently, at present, At present): duplicate of google:timeless-documentation/20
- word-list: google:word-list/709 drops 'uncompress' (owned by google:word-list/277)
- word-list: google:word-list/709 drops 'untar' (owned by google:word-list/277)
- word-list: google:word-list/709 drops 'unzip' (owned by google:word-list/277)
- word-list: google:word-list/709 Example → 'To get the files, unarchive the downloaded archive.'
- word-list: google:word-list/752 drops 'whitelisted' (owned by google:word-list/96, google:word-list/97)
- word-list: google:word-list/752 drops 'whitelisting' (owned by google:word-list/96, google:word-list/97)
- word-list: google:word-list/752 drops 'Whitelisted' (owned by google:word-list/96, google:word-list/97)
- word-list: google:word-list/752 drops 'Whitelisting' (owned by google:word-list/96, google:word-list/97)
- word-list: google:word-list/42 dropped (\b\d{1,2}(?::\d{2})?(?:\s?(?:[ap]\.m\.|[ap]m\b|[AP]\.M\.)|(?:AM|PM)\b)): owners.json: Dates-Times owns time formats (dates-times/8 and /9 cover every form; word-list/42 differs only by a trailing period)
- word-list: google:word-list/512 dropped (noops): owners.json: same entry (NoOps); 511 has the fix
- word-list: google:word-list/244 Use 'for example, For example' → 'for example,' (the fix 'Use a supported format, for example JSON.' is flagged by google:word-list/296 ('for example'))
- word-list: google:word-list/269 flag-only, was Use 'etc.' (the fix 'If your app experiences instability, high latency, etc., follow these steps:' is flagged by google:lists/36 (', etc.'))

## Not applicable (reasons)

- numbers: Section introduction (numbers#4, numbers#11)
- numbers: List introduction (numbers#6, numbers#13)
- numbers: Cross-reference pointer (numbers#46, numbers#72)
- numbers: Cross-reference link (numbers#47, numbers#48)
- numbers: Table column labels (numbers#60)
- numbers: External reference pointer (numbers#64)
- procedures: Introduction and definition (procedures#1)
- procedures: Cross-reference pointer (procedures#9, procedures#21, procedures#64, procedures#88, procedures#107, procedures#108)
- procedures: Table header (procedures#91)
- prescriptive-documentation: Introduction to list (prescriptive-documentation#3)
- prescriptive-documentation: Example pointer, not a rule (prescriptive-documentation#7)
- prescriptive-documentation: Cross-reference to word list (prescriptive-documentation#25)
- dates-times: Introduction (dates-times#1)
- dates-times: Lead-in to list (dates-times#2)
- dates-times: Rationale example, not instruction (dates-times#33, dates-times#34, dates-times#35)
- dates-times: Table header (dates-times#49)
- pronouns: Historical background, not an instruction (pronouns#14)
- pronouns: External reference pointer (pronouns#15, pronouns#30)
- pronouns: Cross-reference pointer (pronouns#20)
- pronouns: Section introduction (pronouns#26)
- sentence-structure: Example table column headers only (sentence-structure#2)
- reference-verbs: Pointer to further information (reference-verbs#5)
- product-names: Page introduction (product-names#1)
- product-names: Cross-reference pointer (product-names#8, product-names#12, product-names#23)
- colons: Introductory definition (colons#1)
- colons: Cross-reference pointer (colons#2, colons#9, colons#10)
- phone-numbers: Introduction and scope (phone-numbers#1)
- phone-numbers: For-more-information pointer (phone-numbers#11)
- capitalization: Pointer to word list (capitalization#6)
- capitalization: Pointer to Product names page (capitalization#7)
- capitalization: Pointer to another page (capitalization#12, capitalization#13)
- headings: Table column labels (headings#3)
- italics-terms: Page introduction (italics-terms#1)
- italics-terms: Pointer to other page (italics-terms#2)
- commas: Page introduction; content repeated in other units (commas#1)
- commas: Lead-in to list of cases (commas#19)
- commas: Table header row (commas#23)
- commas: Cross-reference pointer (commas#27, commas#28)
- timeless-documentation: Rationale introduction (timeless-documentation#8)
- timeless-documentation: Rationale, not an instruction (timeless-documentation#9, timeless-documentation#10)
- timeless-documentation: List introduction (timeless-documentation#11)
- periods: Introduction to list guidance (periods#2)
- periods: Pointer to Lists page (periods#3)
- periods: Introduction to techniques (periods#5)
- periods: Explanation of link formatting, no instruction (periods#9)
- periods: Pointer to Parentheses page (periods#24)
- periods: Pointer to Headings page (periods#26)
- periods: Pointer to Numbers page (periods#28)
- periods: Pointer to Figure captions page (periods#29)
- periods: Pointer to Alt text page (periods#30)
- periods: Pointer to Abbreviations page (periods#33)
- periods: Applies to blog posts, not help topics (periods#38)
- dashes: Introduction and pointer to other pages (dashes#1)
- dashes: Cross-reference link (dashes#2, dashes#3, dashes#4, dashes#13, dashes#14)
- dashes: How to type the character, not a writing rule (dashes#6, dashes#7, dashes#8, dashes#9, dashes#10)
- code-in-text: Markdown markup; Flare topics use HTML (code-in-text#7)
- code-in-text: Pointer to another page (code-in-text#8, code-in-text#10, code-in-text#11, code-in-text#12, code-in-text#13, code-in-text#14)
- code-in-text: Introduction and pointer to resources (code-in-text#9)
- code-in-text: Table introduction (code-in-text#15)
- code-in-text: Table header (code-in-text#16, code-in-text#48)
- code-in-text: List introduction (code-in-text#55)
- pluralization: Cross-reference pointer (pluralization#2, pluralization#15, pluralization#34, pluralization#40, pluralization#46)
- pluralization: Table header only (pluralization#42)
- code-samples: Introduction and pointer (code-samples#1)
- code-samples: Pointer to other page (code-samples#2, code-samples#3, code-samples#4, code-samples#20)
- code-samples: Introduction (code-samples#5)
- code-samples: Introduction to resource list (code-samples#21)
- code-samples: Pointer to external style guide (code-samples#22, code-samples#23, code-samples#24, code-samples#25, code-samples#26)
- code-samples: Pointer to external style guides (code-samples#27)
- code-samples: Informational note about project style guides (code-samples#28)
- quotation-marks: Aggregate duplicate of units 6–34 (quotation-marks#5)
- quotation-marks: Table header (quotation-marks#6)
- accessibility: Introduction (accessibility#1)
- accessibility: Pointer to other resources (accessibility#2)
- accessibility: Resource link (accessibility#3, accessibility#4, accessibility#5, accessibility#83, accessibility#84, accessibility#85, accessibility#86, accessibility#87)
- accessibility: For more information pointer (accessibility#31, accessibility#44, accessibility#59)
- accessibility: Definition and pointer (accessibility#49)
- tone: Pointer to other resources (tone#5)
- tone: Link to other resource (tone#6, tone#7)
- tone: Writing process advice, not checkable (tone#21, tone#22)
- tone: Example table column headers (tone#31)
- excessive-claims: Introductory lead-in sentence. (excessive-claims#6)
- lists: Pointer to another page (lists#1)
- lists: Introduction to table (lists#3)
- lists: Table header row (lists#4)
- lists: Cross-reference pointer (lists#69, lists#70, lists#74, lists#150)
- lists: Section introduction (lists#76)
- lists: Discretionary choice, no requirement (lists#122)
- possessives: Introduction (possessives#1)
- possessives: Cross-reference pointer (possessives#26)
- future: Cross-reference pointer to other pages (future#2)
- prepositions: Cross-reference pointer to another page. (prepositions#6)
- slashes: Cross-reference pointer to another page (slashes#3)
- hyphens: Introduction to guidelines (hyphens#2)
- hyphens: Explanatory factor, not instruction (hyphens#3, hyphens#4)
- hyphens: Pointer to Break the rules (hyphens#10)
- hyphens: Definition of compound (hyphens#24)
- hyphens: For more information pointer (hyphens#51)
- format-examples: Introduction (format-examples#1)
- format-examples: Table header (format-examples#2)
- tables: Introduction (tables#1)
- tables: Introduction to decision table (tables#2)
- tables: Table header row (tables#3)
- tables: Example introduction (tables#15)
- tables: Example table header (tables#16)
- tables: Example table row (tables#17, tables#18)
- tables: For more information pointer (tables#21)
- tables: Note about site CSS (tables#28)
- abbreviations: Definition (abbreviations#1, abbreviations#3, abbreviations#6, abbreviations#10)
- abbreviations: Terminology note (abbreviations#2)
- abbreviations: Definition example (abbreviations#4, abbreviations#5, abbreviations#7, abbreviations#8, abbreviations#9, abbreviations#11, abbreviations#12, abbreviations#13, abbreviations#14)
- abbreviations: Pointer to another page (abbreviations#15, abbreviations#68, abbreviations#74)
- abbreviations: Introductory rationale (abbreviations#22)
- abbreviations: Introduction (abbreviations#63)
- mathematical-notation: Introduction (mathematical-notation#1)
- mathematical-notation: Pointer to another page (mathematical-notation#2)
- mathematical-notation: Section introduction (mathematical-notation#25)
- mathematical-notation: More resources link (mathematical-notation#69, mathematical-notation#70, mathematical-notation#71)
- voice: Pointer to more information (voice#8)
- translation: Introduction (translation#1)
- translation: Introduction to term definitions (translation#2)
- translation: Term definition (translation#3, translation#4, translation#5)
- translation: Cross-reference pointer (translation#6, translation#7)
- translation: Cross-reference link (translation#8, translation#9, translation#10)
- code-syntax: Introduction (code-syntax#1)
- code-syntax: Pointer to other page (code-syntax#2, code-syntax#3, code-syntax#4)
- code-syntax: Section introduction (code-syntax#5, code-syntax#13, code-syntax#78)
- code-syntax: Markdown not used in Flare topics (code-syntax#12)
- code-syntax: For more information pointer (code-syntax#75, code-syntax#76, code-syntax#77, code-syntax#97)
- code-syntax: Syntax illustration (code-syntax#82)
- code-syntax: Illustrative example (code-syntax#85, code-syntax#86, code-syntax#87, code-syntax#99)
- code-syntax: Example explanation (code-syntax#88, code-syntax#89, code-syntax#90, code-syntax#91, code-syntax#92, code-syntax#93, code-syntax#94, code-syntax#95, code-syntax#100, code-syntax#101, code-syntax#104)
- code-syntax: Background explanation (code-syntax#103, code-syntax#105, code-syntax#106)
- code-syntax: Table header (code-syntax#108)
- notices: Introduction to list (notices#4)
- notices: HTML markup recommendation for sites (notices#19)
- notices: HTML code sample (notices#20)
- articles: Pointer to Write for a global audience (articles#4)
- articles: Pointer to Headings and titles (articles#5)
- articles: Pointer to Articles before product names (articles#6)
- articles: Pointer to Indefinite articles before abbreviations (articles#7)
- filenames: Section introduction (filenames#13)
- filenames: Introduction to list (filenames#14)
- units-of-measure: Pointer to Abbreviations page (units-of-measure#3)
- units-of-measure: Pointer to Hyphens page (units-of-measure#4)
- units-of-measure: Pointer to Plural abbreviations (units-of-measure#10)
- units-of-measure: Pointer to another page (units-of-measure#51)
- units-of-measure: More resources link (units-of-measure#52, units-of-measure#53)
- placeholders: Page introduction (placeholders#1)
- placeholders: Pointer to other pages (placeholders#2)
- placeholders: Link to other page (placeholders#3, placeholders#4, placeholders#5)
- placeholders: Definition, not an instruction (placeholders#6)
- placeholders: Illustrative definition example (placeholders#7, placeholders#8)
- placeholders: Section introduction (placeholders#12)
- placeholders: Markdown-specific; Flare topics are HTML (placeholders#16, placeholders#23)
- placeholders: Markdown-specific example (placeholders#24)
- placeholders: Pointer to other page (placeholders#89)
- trademarks: Pointer to more information (trademarks#3, trademarks#10)
- images: Introduction to list (images#2, images#6, images#37, images#49, images#58, images#63, images#89)
- images: Tool choice is free; no writing rule (images#3, images#4)
- images: Example content (images#21, images#22, images#23, images#24, images#25, images#26, images#27)
- images: Example HTML markup (images#28)
- images: Example Markdown markup (images#29)
- images: Pointer to resources (images#70)
- images: Resource link (images#71, images#72, images#73, images#74, images#75)
- images: Background information (images#76, images#86)
- images: For more information pointer (images#88)
- images: Workflow advice about designers, site-specific sizes (images#95)
- cross-references: Introduction (cross-references#1, cross-references#2)
- cross-references: Pointer to another page (cross-references#21)
- cross-references: Pointer to another section (cross-references#64, cross-references#65)
- headings-targets: Introduction and pointer (headings-targets#1)
- headings-targets: Rationale for custom anchors (headings-targets#2, headings-targets#3, headings-targets#4, headings-targets#5)
- headings-targets: Markdown syntax, not Flare (headings-targets#18)
- headings-targets: Markdown example label (headings-targets#19, headings-targets#21, headings-targets#23, headings-targets#25)
- headings-targets: Markdown syntax example (headings-targets#20, headings-targets#22, headings-targets#24, headings-targets#26, headings-targets#30)
- inclusive-documentation: Page introduction (inclusive-documentation#2)
- inclusive-documentation: Pointer to other resources (inclusive-documentation#3)
- inclusive-documentation: Link to other page (inclusive-documentation#4, inclusive-documentation#5, inclusive-documentation#6)
- inclusive-documentation: Pointer to word list (inclusive-documentation#17, inclusive-documentation#32)
- inclusive-documentation: Section introduction and pointers (inclusive-documentation#48)
- inclusive-documentation: List introduction (inclusive-documentation#66)
- jargon: Definition of jargon (jargon#1)
- jargon: Background and pointer to inclusive guide (jargon#2)
- api-reference-comments: Scope note about the guide (api-reference-comments#2)
- api-reference-comments: For more information pointer (api-reference-comments#3)
- api-reference-comments: External resource link (api-reference-comments#4, api-reference-comments#5, api-reference-comments#6)
- api-reference-comments: Introduction to guideline list (api-reference-comments#11, api-reference-comments#47)
- ui-elements: Introductory statement (ui-elements#7)
- ui-elements: Cross-reference pointer (ui-elements#8, ui-elements#33, ui-elements#159)
- ui-elements: Introduction to sections (ui-elements#32)
- ui-elements: Research technique, not writing rule (ui-elements#76)
- ui-elements: Bug-reporting process, not writing rule (ui-elements#77)
- semantic-tagging: Introduction to the list of guidelines. (semantic-tagging#4)
- examples: Pointer to inclusive documentation guidance (examples#52)
- examples: Pointer to phone number formatting (examples#56)
- word-list: Introduction (word-list#1)
- word-list: Research advice for writers (word-list#3)
- word-list: Pointer to other pages (word-list#4)
- word-list: Link to other page (word-list#5, word-list#6, word-list#7, word-list#8, word-list#9)
- word-list: General note with pointer (word-list#10)
- word-list: Describes site link icons (word-list#11)
- word-list: Introduction to guidance labels (word-list#12)
- word-list: Legend for product tag (word-list#15, word-list#16, word-list#17)
- word-list: Cross-reference pointer only. (word-list#46, word-list#52, word-list#62, word-list#67)
- word-list: Term definition, not a writing instruction (word-list#97)
- word-list: Pointer to hyphen guidance (word-list#124, word-list#393)
- word-list: Definition with cross-reference only (word-list#132)
- word-list: Cross-reference to rehost; remaining text duplicates later units. (word-list#334)
- word-list: Pointer to hyphens-with-prefixes guidance. (word-list#363, word-list#378)
- word-list: Term definition, not an instruction. (word-list#367)
- word-list: Permits usage; nothing to check. (word-list#407)
- word-list: Definition, no writing instruction (word-list#413)
- word-list: Cross-reference pointer (word-list#428, word-list#440)
- word-list: Pointer to hyphen prefix guidance (word-list#458)
- word-list: Permission only; nothing to check. (word-list#486)
- word-list: Definition and cross-reference only. (word-list#488)
- word-list: Cross-reference to element entry (word-list#532)
- word-list: Cross-reference to they entry (word-list#539)
- word-list: States term is OK; no instruction (word-list#559)
- word-list: Cross-reference pointer to blind entry (word-list#584)
- word-list: Pointer to external discussions only (word-list#599)
