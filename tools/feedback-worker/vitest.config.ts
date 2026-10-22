import path from "node:path";
import { cloudflareTest, readD1Migrations } from "@cloudflare/vitest-plugin";
import { defineConfig } from "vitest/config";

// Never let a real GITHUB_TOKEN from the developer's shell reach the tests (GitHub is mocked anyway).
process.env.GITHUB_TOKEN = "test-token-not-real";

export default defineConfig(async () => {
	const migrations = await readD1Migrations(path.join(import.meta.dirname, "migrations"));
	return {
		plugins: [
			cloudflareTest({
				wrangler: { configPath: "./wrangler.jsonc" },
				miniflare: {
					// Test-only bindings: a fake token (GitHub is mocked) and the migrations for the setup file.
					bindings: { GITHUB_TOKEN: "test-token-not-real", TEST_MIGRATIONS: migrations },
				},
			}),
		],
		test: {
			setupFiles: ["./test/apply-migrations.ts"],
		},
	};
});
