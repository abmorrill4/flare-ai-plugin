import { createExecutionContext, createScheduledController, waitOnExecutionContext } from "cloudflare:test";
import { env } from "cloudflare:workers";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import worker from "../src/index";
import { CLIENT_A, CLIENT_B, clearDb, FakeGitHub, post } from "./helpers";

let gh: FakeGitHub;
beforeEach(async () => {
	await clearDb();
	gh = new FakeGitHub();
	gh.install();
});
afterEach(() => vi.restoreAllMocks());

function fp(over: Record<string, unknown> = {}): Record<string, unknown> {
	return {
		client_id: CLIENT_A,
		plugin_version: "0.2.0",
		rule_id: "google:quotation-marks/2",
		rule_name: "Put periods inside quotation marks",
		reason: "text_correct",
		flagged_text: "“Care and feeding”.",
		context: null,
		note: null,
		...over,
	};
}

async function runCron(when: number): Promise<void> {
	const ctx = createExecutionContext();
	await worker.scheduled(createScheduledController({ scheduledTime: when, cron: "0 8 * * 1" }), env, ctx);
	await waitOnExecutionContext(ctx);
}

describe("POST /v1/false-positive", () => {
	it("stores the report in D1 without touching GitHub", async () => {
		const res = await post("/v1/false-positive", fp({ note: "British style here.", context: "The section's title is “Care and feeding”." }));
		expect(res.status).toBe(200);
		const json = await res.json<{ ok: boolean; reference: string }>();
		expect(json.ok).toBe(true);
		expect(json.reference).toMatch(/^fp-[0-9a-f]{8}$/);
		expect(gh.requests).toHaveLength(0);

		const row = await env.DB.prepare("SELECT * FROM false_positives").first<Record<string, unknown>>();
		expect(row).toMatchObject({ rule_id: "google:quotation-marks/2", reason: "text_correct", note: "British style here.", summarised_at: null });
		expect(row?.client_hash).not.toBe(CLIENT_A);
	});

	it("validates reason and lengths", async () => {
		expect((await post("/v1/false-positive", fp({ reason: "wrong" }))).status).toBe(400);
		expect((await post("/v1/false-positive", fp({ flagged_text: "x".repeat(201) }))).status).toBe(400);
		expect((await post("/v1/false-positive", fp({ context: "x".repeat(1001) }))).status).toBe(400);
		expect((await post("/v1/false-positive", fp({ note: "x".repeat(1001) }))).status).toBe(400);
		expect((await post("/v1/false-positive", fp({ rule_id: "bad id with spaces" }))).status).toBe(400);
		expect((await post("/v1/false-positive", fp({ extra: 1 }))).status).toBe(400);
	});

	it("redacts secrets in notes", async () => {
		await post("/v1/false-positive", fp({ note: "token=abcd1234efgh" }));
		const row = await env.DB.prepare("SELECT note FROM false_positives").first<{ note: string }>();
		expect(row?.note).toBe("token=[redacted]");
	});

	it("rate limits per client", async () => {
		for (let i = 0; i < 30; i++) expect((await post("/v1/false-positive", fp())).status).toBe(200);
		expect((await post("/v1/false-positive", fp())).status).toBe(429);
	});
});

describe("weekly summary", () => {
	it("creates one issue per rule, then updates it the next week", async () => {
		await post("/v1/false-positive", fp());
		await post("/v1/false-positive", fp({ client_id: CLIENT_B, reason: "rule_doesnt_fit", note: "We use @docs-team style." }));
		await post("/v1/false-positive", fp({ reason: "text_correct", flagged_text: "“Done”." }));
		await post("/v1/false-positive", fp({ rule_id: "company:latin/1", rule_name: "Avoid Latin abbreviations", reason: "change_wrong", flagged_text: "e.g." }));

		await runCron(Date.now() + 1000);

		expect(gh.issues.size).toBe(2);
		const quote = [...gh.issues.values()].find((i) => i.title.includes("quotation-marks"))!;
		expect(quote.title).toBe("False positives: google:quotation-marks/2 Put periods inside quotation marks");
		expect(quote.labels).toEqual(["false-positive"]);
		expect(quote.body).toContain("| The text is correct as written | 2 |");
		expect(quote.body).toContain("| This rule doesn't fit our docs | 1 |");
		expect(quote.body).toContain("| **Total** | **3** |");
		expect(quote.comments).toHaveLength(1);
		expect(quote.comments[0]).toContain("3 reports from 2 installs");
		expect(quote.comments[0]).toContain("“Done”.");
		expect(quote.comments[0]).toContain("@​docs-team");

		const pending = await env.DB.prepare("SELECT COUNT(*) AS n FROM false_positives WHERE summarised_at IS NULL").first<{ n: number }>();
		expect(pending?.n).toBe(0);

		// Nothing new: the next run does nothing.
		const before = gh.requests.length;
		await runCron(Date.now() + 2000);
		expect(gh.requests.length).toBe(before);

		// Next week, one more report for the same rule: same issue, updated totals, a new comment.
		await post("/v1/false-positive", fp({ reason: "change_wrong", flagged_text: "“Again”." }));
		await runCron(Date.now() + 3000);
		expect(gh.issues.size).toBe(2);
		expect(quote.body).toContain("| The suggested change is wrong | 1 |");
		expect(quote.body).toContain("| **Total** | **4** |");
		expect(quote.comments).toHaveLength(2);
		expect(quote.comments[1]).toContain("1 report from 1 install");
	});

	it("starts a new issue when the rule's issue was closed", async () => {
		await post("/v1/false-positive", fp());
		await runCron(Date.now() + 1000);
		gh.issues.get(1)!.state = "closed";
		await post("/v1/false-positive", fp());
		await runCron(Date.now() + 2000);
		expect(gh.issues.size).toBe(2);
		expect(gh.issues.get(2)!.body).toContain("| **Total** | **1** |");
	});

	it("leaves rows pending when GitHub fails, so the next run retries", async () => {
		await post("/v1/false-positive", fp());
		gh.failures.push(new Response("", { status: 500 }));
		await runCron(Date.now() + 1000);
		const pending = await env.DB.prepare("SELECT COUNT(*) AS n FROM false_positives WHERE summarised_at IS NULL").first<{ n: number }>();
		expect(pending?.n).toBe(1);
		await runCron(Date.now() + 2000);
		expect(gh.issues.size).toBe(1);
	});

	it("prunes expired rate-limit rows", async () => {
		await env.DB.prepare("INSERT INTO rate_limits (bucket, count, expires_at) VALUES ('old', 1, 1)").run();
		await runCron(Date.now());
		const row = await env.DB.prepare("SELECT COUNT(*) AS n FROM rate_limits WHERE bucket = 'old'").first<{ n: number }>();
		expect(row?.n).toBe(0);
	});
});
