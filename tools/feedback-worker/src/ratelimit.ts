import { HttpError, sha256Hex } from "./http";

// Fixed-window counters in D1. The Workers rate limiting binding only supports 10 s and 60 s periods, and the
// per-IP limit here is hourly, so a D1 counter is used for every limit. IPs and client ids are stored only as
// SHA-256 hashes inside the bucket name, and rows are pruned by the weekly cron once they expire.

export interface Limit {
	scope: string;
	max: number;
	windowSeconds: number;
}

export const LIMITS = {
	reportPerIp: { scope: "report-ip", max: 20, windowSeconds: 3600 },
	reportPerClient: { scope: "report-client", max: 5, windowSeconds: 60 },
	fpPerIp: { scope: "fp-ip", max: 60, windowSeconds: 3600 },
	fpPerClient: { scope: "fp-client", max: 30, windowSeconds: 3600 },
} satisfies Record<string, Limit>;

/** Counts one hit against `limit` for `identity`; throws 429 once the window's budget is spent. */
export async function hit(db: D1Database, limit: Limit, identity: string, now = Date.now()): Promise<void> {
	const nowS = Math.floor(now / 1000);
	const window = Math.floor(nowS / limit.windowSeconds);
	const expiresAt = (window + 1) * limit.windowSeconds;
	const bucket = `${limit.scope}:${(await sha256Hex(identity)).slice(0, 32)}:${window}`;
	const row = await db
		.prepare(
			`INSERT INTO rate_limits (bucket, count, expires_at) VALUES (?1, 1, ?2)
			 ON CONFLICT (bucket) DO UPDATE SET count = count + 1
			 RETURNING count`,
		)
		.bind(bucket, expiresAt)
		.first<{ count: number }>();
	if (row && row.count > limit.max) {
		throw new HttpError(429, "Too many reports from this computer. Try again later.", Math.max(1, expiresAt - nowS));
	}
}

export async function pruneExpired(db: D1Database, now = Date.now()): Promise<void> {
	await db.prepare("DELETE FROM rate_limits WHERE expires_at < ?1").bind(Math.floor(now / 1000)).run();
}
