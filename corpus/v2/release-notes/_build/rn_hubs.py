"""Synthetic (original) hub topics for the release-notes project."""
import json, os, sys
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import SP
from rn_build import pretty_date, slug, SRC

summary = []


def w(name, text):
    open(SRC + '/index/' + name + '.md', 'w', encoding='utf-8').write(text.strip() + '\n')


def main():
    global summary
    summary = json.load(open(SP + '/rn-summary.json'))
    w('about-release-notes', """
---
title: About these release notes
---
# About these release notes

These release notes describe every change in VAR_PRODUCT from version 1.0.0 to VAR_CURRENT_RELEASE. Each release has its own topic, and the larger releases also have topics for new features, fixes, and breaking changes.

The notes are now organized by release instead of by feature. If you are looking for a specific change, start with the [release history](release-history.md), or search for the issue id.

## How the notes are organized

- Release topics give the date, a short summary, and links to the details.
- "New in" topics list features and improvements, grouped by area.
- "Fixes in" topics list the problems that a release corrects.
- "Breaking changes" topics list the changes that can stop an existing integration from working.

> [!NOTE]
> Features that appear here are available in the release that lists them. A feature is currently supported only in the versions that are listed in [Supported versions](supported-versions.md).

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    rows = ['| Version | Release date | Features | Fixes | Breaking changes |', '|---|---|---|---|---|']
    for r in summary:
        rows.append('| [%s](../releases/release-%s.md) | %s | %d | %d | %s |' % (r['v'], slug(r['v']), pretty_date(r['date']), r['nf'], r['nx'], 'Yes' if r['bc'] else 'No'))
    w('release-history', """
---
title: Release history
---
# Release history

This table lists each release in the notes, newest first. The counts show the items that are described in the release topic.

%s

The latest release is VAR_CURRENT_RELEASE. Releases before VAR_OLDEST_SUPPORTED no longer receive fixes.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""" % '\n'.join(rows))

    w('versioning-policy', """
---
title: Versioning policy
---
# Versioning policy

VAR_PRODUCT uses three-part version numbers: major.minor.patch. The numbers tell you how much changes between two releases.

| Part | Changes when | Example |
|---|---|---|
| Major | A change can break an existing integration | 2.8.0 to 3.0.0 |
| Minor | A release adds features and stays compatible | 3.1.0 to 3.2.0 |
| Patch | A release fixes problems only | 3.9.0 to 3.9.1 |

Minor releases are published about every 3 months. Patch releases are published when a fix is ready, usually within 2 weeks of the report.

## What stays the same

Within a major version, the Admin API keeps its paths, field names, and response codes. A field that you rely on can still gain new values, so your client should ignore values it does not recognize.

## What can change

A major release can remove a deprecated setting, rename a field, or change a default value. Each of these changes is listed in the breaking changes topic for the release. Read that topic before you upgrade.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('upgrade-guide', """
---
title: Upgrade guide
---
# Upgrade guide

Use this procedure to move a running VAR_PRODUCT cluster to a newer release. The steps are the same for minor and patch releases. For a major release, also read the breaking changes topic first.

## Before you start

1. Read the topic for the release that you are upgrading to.
2. Back up your configuration and your data store.
3. Confirm that your version is listed in [Supported versions](supported-versions.md).

## Upgrade a node

1. Select **Admin** > **Upgrade** in the console.
2. Click **Check for updates**, and then select the release.
3. Click **Download**, and wait for the status to change to **Ready**.
4. Run the migration command on one node:

   ```
   orbit migrations up
   ```

5. Restart the node, and then check the status page.
6. Repeat steps 4 and 5 on each remaining node.

> [!WARNING]
> Do not run the migration command on more than one node at a time. Two concurrent migrations can leave the data store in an inconsistent state.

## After the upgrade

Check the logs for warnings about deprecated settings. A setting that is deprecated in this release is removed in a future major release, so change it soon.

If a node does not start, see [Known issues](known-issues.md) and then contact support.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('deprecation-policy', """
---
title: Deprecation policy
---
# Deprecation policy

A deprecated feature still works, but it is scheduled for removal. This topic explains how VAR_PRODUCT announces and removes deprecated features.

- A deprecation is announced in the release notes of a minor release.
- The feature keeps working for at least one full major version.
- The removal is listed under breaking changes in the release that removes it.

## Deprecated settings

The following settings are deprecated as of VAR_CURRENT_RELEASE. Each one has a replacement that you can use today.

| Setting | Replacement | Removed in |
|---|---|---|
| `node_id` | The cluster node identity | 4.0.0 |
| `legacy_router` | The expressions router | 4.0.0 |
| `admin_gui_url` | `admin_gui_listen` | 4.0.0 |

If you must keep a deprecated setting for now, plan the change before the next major release. We will publish a migration note a month before each removal.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('supported-versions', """
---
title: Supported versions
---
# Supported versions

Each minor version receives fixes for a limited time. After that time, the version is end of life, and support can only help you upgrade.

| Version | Status | Fixes until | Minimum memory | Request timeout |
|---|---|---|---|---|
| VAR_CURRENT_RELEASE | Current | Next minor release plus 12 months | 2 GB | 60 s |
| 3.8 | Supported | 12 months after release | 2 GB | 60 s |
| 3.6 | Supported, ends soon | 12 months after release | 1 GB | 30 s |
| 3.0 | End of life | Ended | 1 GB | 30 s |
| 2.8 | End of life | Ended | 512 MB | 30 s |

Versions before VAR_OLDEST_SUPPORTED are no longer maintained. If you run an older version, upgrade to a supported release before you report a problem.

> [!TIP]
> The status column is updated with each release, so check this page after every upgrade.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('known-issues', """
---
title: Known issues
---
# Known issues

This topic lists problems that are open in VAR_CURRENT_RELEASE. Each entry has an issue id and a workaround when one exists. We remove an entry in the release that fixes it.

## Startup

- A node fails to start with the message "Invalid configuration: duplicate listener" when two listeners use the same port (ORB-16021). Remove the duplicate entry from the configuration file.
- The status page shows **Starting** for up to 30 seconds after a restart on a node with a large data set (ORB-15990). This is expected, and the node still serves traffic.

## Admin API

- A request that sets `tags` to an empty list returns a 400 response (ORB-15877). Omit the field instead of sending an empty list.
- The `/status` endpoint now returns the memory field only for the node that serves the request.

## Console

- The **Export** button is currently inactive on the plugins page (ORB-16102). Use the Admin API to export plugin configuration until a fix is released.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('issue-ids', """
---
title: Issue ids
---
# Issue ids

Items in the release notes end with an issue id, for example ORB-12345. The id links the note to the discussion and to the change that implemented it.

## Read an issue id

1. Find the id at the end of the item.
2. Open the public tracker and enter the id in the search box.
3. Read the description and the comments to see why the change was made.

An item can have more than one id when several changes combine into one note. The ids are listed in the order that the changes were merged.

## Report an issue

Search the tracker first. If you cannot find your problem, open a new issue and include the version, the configuration, and the steps that reproduce the problem. Do not post secrets or customer data.

Issue ids before ORB-1000 come from the earlier tracker and are not searchable. For those, ask support.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('change-types', """
---
title: Types of change
---
# Types of change

The notes group changes by type so that you can find what matters to you.

| Type | What it means |
|---|---|
| Feature | Adds something new, such as a field, an endpoint, or a setting. |
| Fix | Corrects behavior that did not match the documentation. |
| Performance improvement | Makes an existing operation faster or lighter on memory. |
| Deprecation | Announces that a feature will be removed in a later major release. |
| Breaking change | Changes behavior in a way that can stop an existing integration from working. |

## Which types need action

You do not need to do anything for features, fixes, and performance improvements. Deprecations ask you to plan a change. Breaking changes ask you to change your integration before you upgrade.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('subscribe', """
---
title: Get release announcements
---
# Get release announcements

You can get a message when a new release is published. There are three ways to subscribe.

## Feed

Add the release feed to your reader: https://docs.example.com/releases/feed.xml. The feed has one entry for each release, and a new entry appears within an hour of the release.

## Email

1. Open the support portal and sign in.
2. Select **Profile** > **Notifications**.
3. Select **Release announcements**, and then click **Save**.

## Chat

Join the releases channel in the community workspace. Announcements for security fixes are posted there first.

Call 1-800-555-0100 or email releases@example.com if you need a printed copy of the notes for an audit.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('faq', """
---
title: Release notes FAQ
---
# Release notes FAQ

## Why does a release have no date?

A release without a date has not been published yet. The date appears when the release is final.

## Why are some changes missing?

The notes describe changes that affect users. Internal changes, such as test updates, are not listed.

## How do I find out if a fix affects me?

Search the fixes topic for the name of the plugin or the area that you use. If a note says that a fix applies to a specific setting, check that setting in your configuration.

## Can I skip a release?

You can skip minor and patch releases, but you must read the notes for every release that you skip. A deprecation in a skipped release can become a removal in the next major version.

## Where are the notes for older releases?

The notes for releases before 1.0.0 are not included. They are available from support.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")

    w('roll-back', """
---
title: Roll back an upgrade
---
# Roll back an upgrade

If an upgrade causes a problem that you cannot resolve quickly, you can return to the previous release. Roll back before you change any data, because newer migrations can alter the data store.

1. Stop the node.
2. Restore the backup that you made before the upgrade.
3. Install the previous release.
4. Start the node, and then check the status page.

> [!IMPORTANT]
> A roll back after a major upgrade can lose data that was written after the migration. Contact support before you roll back a major version.

After you roll back, report the problem so that it can be fixed in the next patch release. Include the version numbers and the log output from the failed upgrade.

Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal.
""")


if __name__ == '__main__':
    main()
