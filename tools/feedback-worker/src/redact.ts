import type { SettingValue } from "./validate";

// Server-side redaction. The plugin already strips secrets; this is the second net, so it errs on the side of
// removing too much.

/** Setting names that hold credentials, whatever their value. */
const SECRET_KEY = /(key|token|secret|password|passwd|pwd|credential|auth|cookie|session|signature|cert)/i;

/** Values that look like credentials wherever they appear. */
const SECRET_VALUE_PATTERNS: RegExp[] = [
	/\bsk-[A-Za-z0-9_-]{8,}/g, // Anthropic / OpenAI style keys (sk-ant-..., sk-proj-...)
	/\bgh[pousr]_[A-Za-z0-9]{20,}/g, // GitHub tokens
	/\bgithub_pat_[A-Za-z0-9_]{20,}/g,
	/\bxox[abprs]-[A-Za-z0-9-]{10,}/g, // Slack
	/\bAKIA[0-9A-Z]{16}\b/g, // AWS access key id
	/\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{5,}/g, // JWT
	/\b(bearer|basic)\s+[A-Za-z0-9._~+/=-]{8,}/gi, // Authorization header values
];

/** `name=value`, `name: value` and `"name": "value"` pairs whose name says secret. */
const SECRET_ASSIGNMENT =
	/((?:x-)?(?:api[_-]?key|access[_-]?token|auth[_-]?token|token|secret|password|passwd|pwd|client[_-]?secret)["']?\s*[:=]\s*["']?)([^\s"',;&]{4,})/gi;

export const REDACTED = "[redacted]";

/** Replaces anything that looks like a credential in free text. */
export function redactText(text: string): string {
	let out = text;
	for (const p of SECRET_VALUE_PATTERNS) out = out.replace(p, REDACTED);
	out = out.replace(SECRET_ASSIGNMENT, (_m, name: string) => `${name}${REDACTED}`);
	return out;
}

function looksSecret(value: string): boolean {
	return SECRET_VALUE_PATTERNS.some((p) => {
		p.lastIndex = 0;
		const hit = p.test(value);
		p.lastIndex = 0;
		return hit;
	});
}

/**
 * Drops any setting whose name suggests a secret or whose value looks like one. Returns the kept settings and the
 * number dropped (the names of dropped settings are not reported).
 */
export function redactSettings(settings: Record<string, SettingValue>): { kept: Record<string, SettingValue>; dropped: number } {
	const kept: Record<string, SettingValue> = {};
	let dropped = 0;
	for (const [k, v] of Object.entries(settings)) {
		if (SECRET_KEY.test(k) || (typeof v === "string" && (looksSecret(v) || redactText(v) !== v))) {
			dropped++;
			continue;
		}
		kept[k] = v;
	}
	return { kept, dropped };
}
