# /// script
# requires-python = ">=3.10"
# ///
"""
Builds a synthetic Flare project for stress tests: "Digital Banking Help", 25 feature areas x 10 topics, five
simulated authors with their own habits, shared snippets, conditions, images and a TOC. Every defect is seeded on
purpose and listed in ground-truth.json (file, excerpt, sentence, expected source and rule), so a run of the
plugin can be scored: what it caught, what it missed, what it flagged that nobody seeded.

    uv run --script scripts/make-corpus.py [out_dir]      (default: corpus)

Deterministic: same seed, same corpus. The clean text is written to pass the style guide; anything the linter
or the AI flags in it shows up as "unseeded" in the bench report, to be read by a person.
"""
import json
import random
import sys
from pathlib import Path
from xml.sax.saxutils import escape

SEED = 20261009
NS = 'xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"'
PRODUCT = "Acme Online Banking"
COMPANY = "Acme Financial"
PHONE = "1-800-555-0100"

# ---- authors: who writes what, and which habits they bring -------------------------------------------------
AUTHORS = {
    "priya":  {"rate": 0.6, "habits": ["passive", "will", "latin", "undefined_abbr"]},
    "marcus": {"rate": 2.2, "habits": ["claims_soon", "claims_free", "claims_guaranteed", "marketing", "exclaim", "we", "timeline"]},
    "dana":   {"rate": 1.8, "habits": ["customer", "bank", "whitelist", "blacklist", "sso_spelling", "idp", "oidc", "saml2", "the_user"]},
    "tom":    {"rate": 2.4, "habits": ["utilize", "in_order_to", "prior_to", "is_able_to", "please", "simply", "please_note", "wordy", "passive", "vague"]},
    "lee":    {"rate": 1.6, "habits": ["alt_missing", "heading_skip", "second_h1", "table_header", "click_here", "phone", "url", "variable_product", "variable_company", "ui_not_bold"]},
}

# ---- feature areas: (slug, name, object, labels, settings) --------------------------------------------------
AREAS = [
    ("accounts", "Account overview", "account nickname", "Accounts", [("Display order", "Sets the order of accounts on the dashboard."), ("Hidden accounts", "Hides closed or unused accounts from the dashboard.")]),
    ("transfers", "Internal transfers", "transfer", "Transfers", [("Frequency", "Sets a one-time or recurring schedule."), ("Memo", "Adds a note that appears in the account history.")]),
    ("billpay", "Bill pay", "payee", "Bill Pay", [("Delivery method", "Shows whether a payment goes out electronically or by check."), ("Reminder", "Sends an alert before a bill is due.")]),
    ("deposit", "Mobile deposit", "check deposit", "Deposit", [("Deposit limit", "Shows the most a member can deposit each day."), ("Endorsement", "Shows the text to write on the back of the check.")]),
    ("alerts", "Alerts", "alert", "Alerts", [("Channel", "Sends the alert by email, text message, or push notification."), ("Threshold", "Sets the balance that triggers a low-balance alert.")]),
    ("cards", "Card controls", "card lock", "Cards", [("Lock card", "Stops new purchases on the card."), ("Travel notice", "Lists the dates and places a member plans to travel.")]),
    ("statements", "Statements", "statement", "Statements", [("Delivery", "Switches between paper and electronic statements."), ("Format", "Downloads a statement as a PDF or CSV file.")]),
    ("p2p", "Person-to-person payments", "contact", "Pay a Person", [("Daily limit", "Shows the most a member can send each day."), ("Contact method", "Sends money by email address or mobile number.")]),
    ("budget", "Budgeting", "budget", "Budget", [("Category", "Groups spending for the monthly summary."), ("Goal", "Sets a spending target for a category.")]),
    ("messages", "Secure messages", "message", "Messages", [("Attachment", "Adds a file of up to 10 MB to a message."), ("Topic", "Routes the message to the right team.")]),
    ("login", "Sign-in and security", "trusted device", "Security", [("Remember device", "Skips the extra check on a trusted device."), ("Session timeout", "Signs a member out after a period of inactivity.")]),
    ("sso", "Single sign-on", "identity provider connection", "Sign-On Settings", [("Metadata URL", "Points to the identity provider's metadata."), ("Certificate", "Verifies the identity provider's signature.")]),
    ("mfa", "Two-step verification", "verification method", "Verification", [("Method", "Sends a code by text message or an authenticator app."), ("Backup codes", "Lists one-time codes for when a phone isn't at hand.")]),
    ("profile", "Profile settings", "contact detail", "Profile", [("Mailing address", "Sets the address for statements and cards."), ("Language", "Sets the language of the app and its emails.")]),
    ("business-users", "Business users", "sub-user", "Users", [("Role", "Sets what a sub-user can see and do."), ("Approval limit", "Sets the largest payment a sub-user can approve.")]),
    ("ach", "ACH payments", "ACH batch", "ACH", [("Effective date", "Sets the day the batch settles."), ("Template", "Saves a batch to reuse for later payrolls.")]),
    ("wires", "Wire transfers", "wire", "Wires", [("Cutoff time", "Shows the last time a wire can go out the same day."), ("Beneficiary", "Stores the details of the person or company paid.")]),
    ("positive-pay", "Positive pay", "exception", "Positive Pay", [("Decision", "Pays or returns a check that doesn't match the issue file."), ("Issue file", "Lists the checks a business wrote.")]),
    ("loans", "Loan payments", "loan payment", "Loans", [("Payment amount", "Sets the regular payment or an extra principal payment."), ("Autopay", "Pays the loan from a chosen account each month.")]),
    ("savings", "Savings goals", "savings goal", "Goals", [("Target date", "Sets the date to reach the goal."), ("Round-ups", "Moves spare change from purchases into the goal.")]),
    ("disputes", "Disputes", "dispute", "Disputes", [("Reason", "Records why a member disputes a transaction."), ("Status", "Shows where the dispute is in review.")]),
    ("documents", "Documents", "document", "Documents", [("Document type", "Groups tax forms, notices, and agreements."), ("Retention", "Shows how long a document stays available.")]),
    ("admin", "Administration", "feature setting", "Administration", [("Feature flag", "Turns a feature on or off for all members."), ("Audit log", "Lists the changes that staff made.")]),
    ("branding", "Branding", "theme", "Branding", [("Logo", "Sets the logo in the header and emails."), ("Colors", "Sets the primary and accent colors.")]),
    ("reports", "Reports", "report", "Reports", [("Date range", "Sets the period a report covers."), ("Export", "Downloads a report as a CSV file.")]),
]

TASKS = ["Add a {o}", "Change a {o}", "Remove a {o}", "Review {o} history", "Turn on {o} alerts", "Download {o} details"]
VERBS = {"Add": ("Add", "add"), "Change": ("Edit", "change"), "Remove": ("Remove", "remove"), "Review": ("History", "review"), "Turn": ("Alerts", "turn on alerts for"), "Download": ("Download", "download")}

# ---- defect catalog: sentence template, excerpt, expected source and rule ----------------------------------
# source: lint (docstyle), ai (judgment), structure (StructureCheck). {o} = the area's object.
DEFECTS = {
    "utilize":       ("lint", "company:plain-language", "You can utilize the {o} list to confirm the details.", "utilize"),
    "in_order_to":   ("lint", "company:plain-language", "In order to change a {o}, open its details.", "In order to"),
    "prior_to":      ("lint", "company:plain-language", "Review the amount prior to saving the {o}.", "prior to"),
    "is_able_to":    ("lint", "company:plain-language", "A member is able to cancel a {o} until midnight.", "is able to"),
    "please":        ("lint", "company:plain-language", "Please check each {o} before you continue.", "Please"),
    "simply":        ("lint", "company:plain-language", "Simply select a {o} to see its details.", "Simply"),
    "please_note":   ("lint", "company:plain-language", "Please note that a {o} can take one business day to appear.", "Please note that"),
    "whitelist":     ("lint", "company:plain-language", "Whitelist the IP ranges of your branch network.", "Whitelist"),
    "blacklist":     ("lint", "company:plain-language", "Accounts on the blacklist can't receive a {o}.", "blacklist"),
    "the_user":      ("lint", "company:plain-language", "If the user deletes the {o}, it can't be restored.", "the user"),
    "customer":      ("lint", "project:terminology", "Customers can see each {o} for 18 months.", "Customers"),
    "bank":          ("lint", "project:terminology", "Contact your bank if a {o} is missing.", "bank"),
    "sso_spelling":  ("lint", "company:terminology", "Turn on Single Sign-On for business members first.", "Single Sign-On"),
    "idp":           ("lint", "company:terminology", "Ask your Identity Provider for the metadata file.", "Identity Provider"),
    "oidc":          ("lint", "company:terminology", "The app also supports OpenId Connect.", "OpenId Connect"),
    "saml2":         ("lint", "company:terminology", "Business sign-in supports SAML2.", "SAML2"),
    "claims_soon":   ("lint", "company:claims", "Shared {o} access is coming soon.", "coming soon"),
    "claims_free":   ("lint", "company:claims", "Every {o} is free of charge.", "free of charge"),
    "claims_guaranteed": ("lint", "company:claims", "Delivery of each {o} is guaranteed within one hour.", "guaranteed"),
    "variable_product": ("lint", "project:variables", "Acme Online Banking lists each {o} on the dashboard.", PRODUCT),
    "variable_company": ("lint", "project:variables", "Acme Financial reviews each {o} within one business day.", COMPANY),
    "latin":         ("lint", "google", "Add a short label, e.g. the purpose of the {o}.", "e.g."),
    "exclaim":       ("lint", "google", "Your new {o} is ready to go!", "go!"),
    "will":          ("lint", "google", "The {o} will appear in your history.", "will"),
    "we":            ("lint", "google", "We recommend that you check each {o} weekly.", "We"),
    "passive":       ("ai", "google", "Each {o} is reviewed by the financial institution before it is released.", "is reviewed by the financial institution"),
    "wordy":         ("ai", "company:plain-language", "At this point in time, it is important to be aware of the fact that each {o} has a daily limit.", "At this point in time"),
    "vague":         ("ai", "general", "Configure the remaining options as appropriate for your situation.", "as appropriate for your situation"),
    "ui_not_bold":   ("ai", "company:ui-references", "Click the save button to keep your changes.", "the save button"),
    "marketing":     ("ai", "company:claims", "Our best-in-class {o} experience delights members.", "best-in-class"),
    "undefined_abbr": ("ai", "company:terminology", "Members with MFA turned on get a code for each new {o}.", "MFA"),
    "timeline":      ("ai", "company:claims", "Bulk editing of each {o} arrives in a future update.", "arrives in a future update"),
    # structure: handled by placement, sentence is the trigger text
    "click_here":    ("structure", "Structure.LinkText", "For the fee schedule, <a href=\"https://www.example.com/fees\">click here</a>.", "click here"),
    "phone":         ("structure", "Structure.HardCoded", "For help, call " + PHONE + ".", PHONE),
    "url":           ("structure", "Structure.HardCoded", "The full terms are at https://www.example.com/terms.", "https://www.example.com/terms"),
    "alt_missing":   ("structure", "Structure.AltText", None, None),
    "heading_skip":  ("structure", "Structure.HeadingOrder", None, None),
    "second_h1":     ("structure", "Structure.OneH1", None, None),
    "table_header":  ("structure", "Structure.TableHeader", None, None),
}

V_PRODUCT = '<MadCap:variable name="General.ProductName" />'
V_COMPANY = '<MadCap:variable name="General.CompanyName" />'
V_PHONE = '<MadCap:variable name="General.SupportPhone" />'


class Topic:
    def __init__(self, rel, title, author, area):
        self.rel, self.title, self.author, self.area = rel, title, author, area
        self.blocks = []        # list of [kind, html]; kind p | step-list | table | other
        self.defects = []

    def p(self, html, cond=None):
        attr = f' MadCap:conditions="{cond}"' if cond else ""
        self.blocks.append(["p", f"<p{attr}>{html}</p>"])

    def html(self):
        body = "\n        ".join(b[1] for b in self.blocks)
        return (f'<?xml version="1.0" encoding="utf-8"?>\n<html {NS}>\n    <head>\n        <title>{escape(self.title)}</title>\n'
                f'    </head>\n    <body>\n        <h1>{escape(self.title)}</h1>\n        {body}\n    </body>\n</html>\n')


def articles(s):
    import re
    return re.sub(r"\b([Aa]) (?=[aeiouAEIOU])", r"\1n ", s)


def plain(html):
    import re
    return re.sub(r"<[^>]+>", "", html).replace("&amp;", "&")


def seed(topic, kind, o, rnd):
    source, rule, sentence, excerpt = DEFECTS[kind]
    d = {"id": f"{topic.rel}#{len(topic.defects) + 1}", "file": "Content/" + topic.rel, "author": topic.author, "kind": kind,
         "source": source, "rule": rule, "excerpt": excerpt, "sentence": None}
    if sentence:
        text = articles(sentence.replace("{o}", o))
        # Append to a paragraph or a step: defects live inside real text, not in paragraphs of their own.
        targets = [b for b in topic.blocks if b[0] in ("p", "steps")]
        b = rnd.choice(targets)
        if b[0] == "p":
            b[1] = b[1][:-4] + " " + text + "</p>"
        else:
            items = b[1].split("</li>")
            k = rnd.randrange(len(items) - 1)
            items[k] = items[k] + " " + text
            b[1] = "</li>".join(items)
        d["sentence"] = plain(text)
        d["excerpt"] = plain(excerpt)
    elif kind == "alt_missing":
        topic.blocks.insert(1, ["other", f'<p><img src="../Resources/Images/{topic.area}.png" /></p>'])
    elif kind == "heading_skip":
        # Right under the h1, so h1 -> h3 skips a level whatever the topic's own headings are.
        topic.blocks.insert(0, ["other", "<h3>Before you start</h3>"])
        topic.blocks.insert(1, ["p", "<p>Make sure you have the account number at hand.</p>"])
    elif kind == "second_h1":
        topic.blocks.append(["other", "<h1>Related tasks</h1>"])
        topic.blocks.append(["p", "<p>See the other topics in this section.</p>"])
    elif kind == "table_header":
        topic.blocks.append(["table", "<table><tr><td>Limit</td><td>Amount</td></tr><tr><td>Daily</td><td>$2,500</td></tr></table>"])
    topic.defects.append(d)


def build_topics(rnd):
    topics = []
    authors = list(AUTHORS)
    for n, (slug, name, o, label, settings) in enumerate(AREAS):
        author = authors[n % len(authors)]
        folder = f"{slug}/"
        os_ = o + "s" if not o.endswith("s") else o
        # concept
        lower = name if name.startswith("ACH") else name[0].lower() + name[1:]
        t = Topic(folder + "about.htm", f"About {lower}", author, slug)
        t.p(f"{name} gives members one place to manage each {o} in {V_PRODUCT}.")
        t.p(f"Members can add, change, and remove a {o} from the <b>{label}</b> page. Each change appears in the account history.")
        t.p(f"Staff at {V_COMPANY} choose which of these features members see.")
        t.blocks.append(["other", f'<MadCap:snippetBlock src="../Resources/Snippets/{"LegacyApp" if n % 3 == 0 else "SupportContact"}.flsnp" />'])
        t.p("Printed copies of this page can be out of date.", cond="Default.Print")
        topics.append(t)
        # tasks
        for task in TASKS:
            title = task.replace("{o}", o)
            first = task.split()[0]
            button, verb = VERBS[first]
            t = Topic(folder + title.lower().replace(" ", "-") + ".htm", title, author, slug)
            t.p(f"You can {verb} a {o} at any time.")
            steps = [f"On the main menu, select <b>{label}</b>.",
                     f"Find the {o}, and then select <b>{button}</b>.",
                     "Check the details on the screen.",
                     "Select <b>Save</b>."]
            t.blocks.append(["steps", "<ol>" + "".join(f"<li>{s}</li>" for s in steps) + "</ol>"])
            t.p(f"The {o} list shows the change right away.")
            if rnd.random() < 0.3:
                t.p(f'<img src="../Resources/Images/{slug}.png" alt="The {label} page with the {button} button." />')
            topics.append(t)
        # reference
        t = Topic(folder + "settings.htm", f"{name} settings", author, slug)
        t.p(f"These settings control how each {o} works.")
        rows = "".join(f"<tr><td>{escape(s)}</td><td>{escape(d)}</td></tr>" for s, d in settings)
        t.blocks.append(["table", f"<table><thead><tr><th>Setting</th><th>What it does</th></tr></thead><tbody>{rows}</tbody></table>"])
        t.p("Staff can change the defaults on the <b>Settings</b> page.")
        topics.append(t)
        # troubleshooting
        t = Topic(folder + "troubleshoot.htm", f"Troubleshoot {lower}", author, slug)
        t.p(f"Try these steps when a {o} doesn't look right.")
        t.blocks.append(["other", f"<h2>A {o} is missing</h2>"])
        t.p(f"Refresh the page. If the {o} still doesn't appear, sign out and sign in again.")
        t.blocks.append(["other", "<h2>A change didn't save</h2>"])
        t.p(f"Check that every required box has a value, and then select <b>Save</b> again.")
        t.blocks.append(["other", f'<MadCap:snippetBlock src="../Resources/Snippets/SupportContact.flsnp" />'])
        topics.append(t)
        # faq
        t = Topic(folder + "faq.htm", f"{name} questions", author, slug)
        t.blocks.append(["other", f"<h2>How long does a {o} take?</h2>"])
        t.p(f"Most changes to a {o} appear within a few minutes.")
        t.blocks.append(["other", f"<h2>Who can see a {o}?</h2>"])
        t.p(f"Only the account owner and the people they share access with can see {os_}.")
        topics.append(t)
    return topics


PROJECT_TERMINOLOGY = """<?xml version="1.0" encoding="utf-8"?>
<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd">
    <head><title>Terminology</title></head>
    <body>
        <h1>Terminology</h1>
        <p>Rules for this project only. House Style applies them on top of the team's style guide when it checks topics in this project.</p>
        <h2>Audience</h2>
        <ul>
            <li id="rule-1">Refer to end users of the product as "members", not "customers" or "users".<ul><li>Bad: Customers can reset their own PIN.</li><li>Good: Members can reset their own PIN.</li></ul></li>
            <li id="rule-2">Refer to the bank or credit union as "the financial institution".<ul><li>Bad: Contact your bank to unlock the account.</li><li>Good: Contact the financial institution to unlock the account.</li></ul></li>
        </ul>
        <table>
            <tr><th>Avoid</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>
            <tr id="rule-3"><td>customer, customers</td><td>member, members</td><td>warning</td><td>We serve members.</td><td>Customers can reset their PIN.</td></tr>
            <tr id="rule-4"><td>bank, credit union</td><td></td><td>suggestion</td><td>Say the financial institution.</td><td>Contact your bank.</td></tr>
        </table>
    </body>
</html>
"""


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else "corpus")
    proj = out / "DigitalBanking"
    rnd = random.Random(SEED)
    topics = build_topics(rnd)

    # Seed defects by each author's habits and rate (Poisson-ish: whole part + chance of one more).
    for t in topics:
        a = AUTHORS[t.author]
        n = int(a["rate"]) + (1 if rnd.random() < a["rate"] - int(a["rate"]) else 0)
        kinds = rnd.sample(a["habits"], min(n, len(a["habits"])))
        o = next(ar[2] for ar in AREAS if ar[0] == t.area)
        for k in kinds:
            if k == "second_h1" and any("<h1>" in b[1] for b in t.blocks): continue
            seed(t, k, o, rnd)

    truth = []
    for t in topics:
        f = proj / "Content" / t.rel
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_text(articles(t.html()), encoding="utf-8")
        truth.extend(t.defects)

    res = proj / "Content" / "Resources"
    (res / "Snippets").mkdir(parents=True, exist_ok=True)
    (res / "Snippets" / "SupportContact.flsnp").write_text(
        f'<?xml version="1.0" encoding="utf-8"?>\n<html {NS}>\n    <head />\n    <body>\n        <p>Still stuck? Contact {V_COMPANY} support at {V_PHONE}.</p>\n    </body>\n</html>\n', encoding="utf-8")
    # One shared snippet with a defect: it reaches every topic that includes it (white-label / snippet impact).
    (res / "Snippets" / "LegacyApp.flsnp").write_text(
        f'<?xml version="1.0" encoding="utf-8"?>\n<html {NS}>\n    <head />\n    <body>\n        <p>Customers who still use the legacy app see a simpler version of this page.</p>\n    </body>\n</html>\n', encoding="utf-8")
    truth.append({"id": "snippet#1", "file": "Content/Resources/Snippets/LegacyApp.flsnp", "author": "shared", "kind": "customer", "source": "lint",
                  "rule": "project:terminology", "excerpt": "Customers", "sentence": "Customers who still use the legacy app see a simpler version of this page."})
    (res / "Images").mkdir(parents=True, exist_ok=True)
    png = bytes.fromhex("89504e470d0a1a0a0000000d4948445200000001000000010806000000"
                        "1f15c4890000000d49444154789c6360000002000154a24f5d0000000049454e44ae426082")
    for a in AREAS:
        (res / "Images" / f"{a[0]}.png").write_bytes(png)
    # The project's own style guide (the project layer): banking terms that don't belong in the bundled starter rules.
    (res / "StyleGuide").mkdir(parents=True, exist_ok=True)
    (res / "StyleGuide" / "Terminology.htm").write_text(PROJECT_TERMINOLOGY, encoding="utf-8", newline="\r\n")

    (proj / "DigitalBanking.flprj").write_text('<?xml version="1.0" encoding="utf-8"?>\n<CatapultProject Version="1" />\n', encoding="utf-8")
    vs = proj / "Project" / "VariableSets"
    vs.mkdir(parents=True, exist_ok=True)
    (vs / "General.flvar").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n<CatapultVariableSet>\n'
        f'    <Variable Name="ProductName" EvaluatedDefinition="{PRODUCT}">{PRODUCT}</Variable>\n'
        f'    <Variable Name="CompanyName" EvaluatedDefinition="{COMPANY}">{COMPANY}</Variable>\n'
        f'    <Variable Name="SupportPhone" EvaluatedDefinition="{PHONE}">{PHONE}</Variable>\n'
        '</CatapultVariableSet>\n', encoding="utf-8")
    cs = proj / "Project" / "ConditionTagSets"
    cs.mkdir(parents=True, exist_ok=True)
    (cs / "Default.flcts").write_text('<?xml version="1.0" encoding="utf-8"?>\n<CatapultConditionTagSet>\n'
                                      '    <ConditionTag Name="Print" BackgroundColor="#ffd700" />\n'
                                      '    <ConditionTag Name="ScreenOnly" BackgroundColor="#7fffd4" />\n</CatapultConditionTagSet>\n', encoding="utf-8")
    toc = proj / "Project" / "TOCs"
    toc.mkdir(parents=True, exist_ok=True)
    entries = []
    for a in AREAS:
        kids = "".join(f'\n        <TocEntry Title="[%=System.LinkedTitle%]" Link="/Content/{t.rel}" />' for t in topics if t.area == a[0])
        entries.append(f'    <TocEntry Title="{escape(a[1])}">{kids}\n    </TocEntry>')
    (toc / "Main.fltoc").write_text('<?xml version="1.0" encoding="utf-8"?>\n<CatapultToc Version="1">\n' + "\n".join(entries) + "\n</CatapultToc>\n", encoding="utf-8")
    tg = proj / "Project" / "Targets"
    tg.mkdir(parents=True, exist_ok=True)
    (tg / "HTML5.fltar").write_text('<?xml version="1.0" encoding="utf-8"?>\n<CatapultTarget Version="2" Type="WebHelp2" '
                                    'MasterToc="/Project/TOCs/Main.fltoc" StartTopic="/Content/accounts/about.htm" ConditionTagExpression="exclude[Default.Print]" />\n', encoding="utf-8")

    (out / "ground-truth.json").write_text(json.dumps({"seed": SEED, "topics": len(topics), "authors": {k: v["habits"] for k, v in AUTHORS.items()},
                                                      "files": {"Content/" + t.rel: t.author for t in topics}, "defects": truth}, indent=1), encoding="utf-8")
    by = {}
    for d in truth: by[d["source"]] = by.get(d["source"], 0) + 1
    print(f"{len(topics)} topics, {len(truth)} seeded defects {by} -> {proj}")


if __name__ == "__main__":
    main()
