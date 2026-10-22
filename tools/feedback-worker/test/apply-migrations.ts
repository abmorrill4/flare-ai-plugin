import { applyD1Migrations } from "cloudflare:test";
import { env } from "cloudflare:workers";

// Setup files may run more than once; applyD1Migrations only applies migrations that haven't been applied yet.
await applyD1Migrations(env.DB, env.TEST_MIGRATIONS);
