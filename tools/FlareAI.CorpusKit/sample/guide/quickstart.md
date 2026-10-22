---
title: Zorbit quickstart
---

# Zorbit quickstart

This page walks you through your first scheduled job. It takes about ten minutes.

## Install the agent

1. Download the agent for your system.
2. Open a terminal, and then run the installer:

   ```bash
   sudo ./zorbit-agent install --token YOUR_TOKEN
   ```

3. Select **Workers** on the main menu.
4. Confirm that your machine appears in the list before you continue.

> **Tip:** Keep the terminal open until the worker shows the status `Ready`.

## Create a job

1. On the **Jobs** page, click **New job**.
2. In the **Command** box, enter `echo "hello"`.
3. Choose a schedule, and then click **Save**. The job runs at the next interval.

The run appears in the history. Read how to [create an access token](../auth/tokens.md) if you want to start jobs from a script.

Need more help? Contact the Zorbit support team through the customer portal, and include your account name and the steps you tried.
