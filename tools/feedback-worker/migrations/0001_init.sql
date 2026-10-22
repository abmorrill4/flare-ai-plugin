-- House Style feedback receiver: initial schema.

-- Fixed-window rate-limit counters. bucket = "<scope>:<sha256 of ip or client>:<window index>".
CREATE TABLE rate_limits (
  bucket      TEXT PRIMARY KEY,
  count       INTEGER NOT NULL,
  expires_at  INTEGER NOT NULL          -- unix seconds; pruned by the weekly cron
);
CREATE INDEX rate_limits_expires ON rate_limits (expires_at);

-- One row per error signature -> the GitHub issue that tracks it.
CREATE TABLE error_signatures (
  signature     TEXT PRIMARY KEY,       -- sha256(type | top 3 House Style frames | version), 16 hex chars
  issue_number  INTEGER NOT NULL,
  title         TEXT NOT NULL,
  report_count  INTEGER NOT NULL DEFAULT 1,
  first_seen    INTEGER NOT NULL,
  last_seen     INTEGER NOT NULL
);

-- Which installs have reported a signature (hashed client ids), so a comment can say "another install".
CREATE TABLE signature_clients (
  signature    TEXT NOT NULL,
  client_hash  TEXT NOT NULL,
  PRIMARY KEY (signature, client_hash)
);

-- Every accepted report (error or problem), for reference lookups. No report content is stored here.
CREATE TABLE reports (
  id            TEXT PRIMARY KEY,
  kind          TEXT NOT NULL,
  client_hash   TEXT NOT NULL,
  plugin_version TEXT NOT NULL,
  signature     TEXT,
  issue_number  INTEGER NOT NULL,
  duplicate     INTEGER NOT NULL DEFAULT 0,
  created_at    INTEGER NOT NULL
);

-- False-positive reports. Stored only here; summarised weekly into one issue per rule.
CREATE TABLE false_positives (
  id             TEXT PRIMARY KEY,
  created_at     INTEGER NOT NULL,
  client_hash    TEXT NOT NULL,
  plugin_version TEXT NOT NULL,
  rule_id        TEXT NOT NULL,
  rule_name      TEXT NOT NULL,
  reason         TEXT NOT NULL CHECK (reason IN ('text_correct', 'change_wrong', 'rule_doesnt_fit')),
  flagged_text   TEXT NOT NULL,
  context        TEXT,
  note           TEXT,
  summarised_at  INTEGER
);
CREATE INDEX false_positives_pending ON false_positives (summarised_at, rule_id);

-- The summary issue that tracks each rule's false positives. since = when this issue started counting.
CREATE TABLE fp_rule_issues (
  rule_id       TEXT PRIMARY KEY,
  issue_number  INTEGER NOT NULL,
  since         INTEGER NOT NULL
);
