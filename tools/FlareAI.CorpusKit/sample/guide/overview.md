# Zorbit overview

Zorbit runs jobs on a schedule and shows the result of each run in one place. You can use it for backups, reports, and data imports.

## How it works

A *job* is a command plus a schedule. When a job is due, Zorbit picks a free worker, runs the command, and records the exit code. The run appears in the history within a few seconds.

- Jobs run on workers that you register.
- Each worker reports its status every minute.
- A failed job can retry up to three times before it raises an alert.

> **Warning:** Do not delete a worker while a job is running on it. The job stops and does not retry.

## Next steps

Install the agent by following the [quickstart](quickstart.md), and then read how to [sign in](../auth/login.html) for the first time. For example, you can import a sample job from the **Jobs** page.

Need more help? Contact the Zorbit support team through the customer portal, and include your account name and the steps you tried.
