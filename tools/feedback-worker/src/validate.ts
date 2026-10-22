import { HttpError } from "./http";

// Strict schemas for the two POST bodies. Unknown fields are rejected; `null` counts as "absent" for optional
// fields because Newtonsoft.Json writes nulls by default.

export type ReportKind = "error" | "problem";
export const FP_REASONS = ["text_correct", "change_wrong", "rule_doesnt_fit"] as const;
export type FpReason = (typeof FP_REASONS)[number];

export interface ExceptionInfo {
	type: string;
	message: string;
	stack: string;
}

export type SettingValue = string | number | boolean | null;

export interface Report {
	kind: ReportKind;
	client_id: string;
	plugin_version: string;
	flare_version?: string;
	os?: string;
	automatic: boolean;
	what_happened?: string;
	expected?: string;
	user_note?: string;
	exception?: ExceptionInfo;
	diag_log_tail?: string;
	settings?: Record<string, SettingValue>;
}

export interface FalsePositive {
	client_id: string;
	plugin_version: string;
	rule_id: string;
	rule_name: string;
	reason: FpReason;
	flagged_text: string;
	context?: string;
	note?: string;
}

export const LIMITS = {
	version: 32,
	flareVersion: 64,
	os: 128,
	text: 4000, // what_happened, expected, user_note
	exceptionType: 256,
	exceptionMessage: 2000,
	stack: 32_000,
	diagLog: 64 * 1024,
	settingsKeys: 100,
	settingKey: 64,
	settingValue: 500,
	ruleId: 128,
	ruleName: 200,
	flaggedText: 200,
	fpContext: 1000,
	fpNote: 1000,
} as const;

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const VERSION = /^[0-9A-Za-z][0-9A-Za-z.+-]*$/;
const RULE_ID = /^[A-Za-z0-9][A-Za-z0-9._:/-]*$/;
const SETTING_KEY = /^[A-Za-z0-9_.-]+$/;

function bad(message: string): never {
	throw new HttpError(400, message);
}

function isObject(v: unknown): v is Record<string, unknown> {
	return typeof v === "object" && v !== null && !Array.isArray(v);
}

function onlyKeys(obj: Record<string, unknown>, allowed: readonly string[], where: string): void {
	for (const k of Object.keys(obj)) if (!allowed.includes(k)) bad(`Unknown field "${where}${k}".`);
}

/** Control characters other than tab, newline and carriage return. */
function hasControlChars(s: string): boolean {
	return /[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f]/.test(s);
}

function str(obj: Record<string, unknown>, key: string, max: number, required: true, opts?: { pattern?: RegExp; multiline?: boolean }): string;
function str(obj: Record<string, unknown>, key: string, max: number, required: false, opts?: { pattern?: RegExp; multiline?: boolean }): string | undefined;
function str(
	obj: Record<string, unknown>,
	key: string,
	max: number,
	required: boolean,
	opts: { pattern?: RegExp; multiline?: boolean } = {},
): string | undefined {
	const v = obj[key];
	if (v === undefined || v === null || v === "") {
		if (required) bad(`"${key}" is required.`);
		return undefined;
	}
	if (typeof v !== "string") bad(`"${key}" must be a string.`);
	if (v.length > max) bad(`"${key}" is too long (limit ${max} characters).`);
	if (!opts.multiline && /[\r\n]/.test(v)) bad(`"${key}" must be a single line.`);
	if (hasControlChars(v)) bad(`"${key}" contains control characters.`);
	if (opts.pattern && !opts.pattern.test(v)) bad(`"${key}" has an invalid format.`);
	return v;
}

function common(body: Record<string, unknown>): { client_id: string; plugin_version: string } {
	return {
		client_id: str(body, "client_id", 36, true, { pattern: GUID }).toLowerCase(),
		plugin_version: str(body, "plugin_version", LIMITS.version, true, { pattern: VERSION }),
	};
}

const REPORT_KEYS = [
	"kind", "client_id", "plugin_version", "flare_version", "os", "automatic", "what_happened", "expected",
	"user_note", "exception", "diag_log_tail", "settings",
] as const;

export function parseReport(body: unknown): Report {
	if (!isObject(body)) bad("The body must be a JSON object.");
	onlyKeys(body, REPORT_KEYS, "");

	const kind = body.kind;
	if (kind !== "error" && kind !== "problem") bad(`"kind" must be "error" or "problem".`);

	const automatic = body.automatic ?? false;
	if (typeof automatic !== "boolean") bad(`"automatic" must be true or false.`);

	const report: Report = {
		kind,
		...common(body),
		flare_version: str(body, "flare_version", LIMITS.flareVersion, false),
		os: str(body, "os", LIMITS.os, false),
		automatic,
		what_happened: str(body, "what_happened", LIMITS.text, false, { multiline: true }),
		expected: str(body, "expected", LIMITS.text, false, { multiline: true }),
		user_note: str(body, "user_note", LIMITS.text, false, { multiline: true }),
		diag_log_tail: str(body, "diag_log_tail", LIMITS.diagLog, false, { multiline: true }),
	};

	if (body.exception !== undefined && body.exception !== null) {
		const ex = body.exception;
		if (!isObject(ex)) bad(`"exception" must be an object.`);
		onlyKeys(ex, ["type", "message", "stack"], "exception.");
		report.exception = {
			type: str(ex, "type", LIMITS.exceptionType, true),
			message: str(ex, "message", LIMITS.exceptionMessage, false, { multiline: true }) ?? "",
			stack: str(ex, "stack", LIMITS.stack, false, { multiline: true }) ?? "",
		};
	}

	if (body.settings !== undefined && body.settings !== null) {
		const s = body.settings;
		if (!isObject(s)) bad(`"settings" must be an object.`);
		const keys = Object.keys(s);
		if (keys.length > LIMITS.settingsKeys) bad(`"settings" has too many entries (limit ${LIMITS.settingsKeys}).`);
		const out: Record<string, SettingValue> = {};
		for (const k of keys) {
			if (k.length > LIMITS.settingKey || !SETTING_KEY.test(k)) bad(`"settings" has an invalid key.`);
			const v = s[k];
			if (typeof v === "string") {
				if (v.length > LIMITS.settingValue) bad(`"settings.${k}" is too long (limit ${LIMITS.settingValue} characters).`);
				if (hasControlChars(v)) bad(`"settings.${k}" contains control characters.`);
			} else if (!(v === null || typeof v === "boolean" || (typeof v === "number" && Number.isFinite(v)))) {
				bad(`"settings.${k}" must be a string, number, true/false or null.`);
			}
			out[k] = v as SettingValue;
		}
		report.settings = out;
	}

	if (kind === "error" && !report.exception) bad(`An error report needs "exception".`);
	if (kind === "problem" && !report.what_happened) bad(`A problem report needs "what_happened".`);
	return report;
}

const FP_KEYS = ["client_id", "plugin_version", "rule_id", "rule_name", "reason", "flagged_text", "context", "note"] as const;

export function parseFalsePositive(body: unknown): FalsePositive {
	if (!isObject(body)) bad("The body must be a JSON object.");
	onlyKeys(body, FP_KEYS, "");
	const reason = body.reason;
	if (typeof reason !== "string" || !(FP_REASONS as readonly string[]).includes(reason))
		bad(`"reason" must be one of: ${FP_REASONS.join(", ")}.`);
	return {
		...common(body),
		rule_id: str(body, "rule_id", LIMITS.ruleId, true, { pattern: RULE_ID }),
		rule_name: str(body, "rule_name", LIMITS.ruleName, true),
		reason: reason as FpReason,
		flagged_text: str(body, "flagged_text", LIMITS.flaggedText, true, { multiline: true }),
		context: str(body, "context", LIMITS.fpContext, false, { multiline: true }),
		note: str(body, "note", LIMITS.fpNote, false, { multiline: true }),
	};
}
