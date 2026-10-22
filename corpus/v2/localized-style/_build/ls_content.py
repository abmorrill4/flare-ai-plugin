"""Synthetic topics for the localized-style corpus project (original text).
Each entry: (path, markdown, plants) where plants = [(quote, kind, fix)] are deliberate company-guide violations written into the text."""

BOIL = "If you cannot log in, contact your organization's administrator."

T = []


def topic(path, md, plants=()):
    T.append((path, md.strip() + '\n', list(plants)))


topic('getting-started/about-acme-sign-on.md', """
# About VAR_PRODUCT

VAR_PRODUCT is the administration console for single sign-on. Administrators use it to decide who can reach each application, and users use it to reach every application with one login.

## What you can do

- Add users, groups, and roles.
- Connect applications with SAML 2.0 or OpenID Connect.
- Require multi-factor authentication for sensitive applications.
- Review who logged in, when, and from where.

## Who uses the console

Administrators manage users and applications. Auditors have read-only access to the audit log. Users see only the applications that an administrator assigned to them.

> [!NOTE]
> The console runs in your browser. You do not install anything on a user's computer.

VAR_COMPANY publishes a new console release every month. Each release is described in the release notes.
""")

topic('getting-started/log-in-to-the-console.md', """
# Log in to the console

Use this procedure to log in to the VAR_PRODUCT console for the first time.

## Before you start

You need the console address and the email address that your administrator used to create your account. You receive both in a welcome message.

## Log in

1. Open the console address in your browser.
2. In the **E-mail address** field, enter your email address.
3. Click **Next**, and then enter your password.
4. If your organization requires multi-factor authentication, enter the 6-digit code from your authenticator app.
5. Click **Log in**.

The console opens to the **Applications** page.

## Log in with your company account

If your organization connects the console to a company directory, click **Log in with company account** instead. The console redirects you to your company's login page, and you sign in there with your usual credentials.

> [!TIP]
> Bookmark the login page so that you can return to it later.

BOIL_LINE
""", [('sign in there', 'bank', 'log in there')])

topic('getting-started/log-out-and-session-timeouts.md', """
# Log out and session timeouts

The console logs you out automatically when your session ends. You can also log out at any time.

## Log out

1. Click your name in the upper-right corner.
2. Click **Log out**.

## Session timeouts

A session ends when you are inactive for the idle timeout, or when it reaches the maximum length. Administrators set both values.

| Setting | Default | Allowed range |
|---|---|---|
| Idle timeout | 30 min | 5 min to 8 hr |
| Maximum session length | 12 hr | 1 hr to 24 hr |

When a session is about to end, the console shows a warning for 60 seconds. Click **Stay logged in** to extend the session.

BOIL_LINE
""")

topic('getting-started/console-tour.md', """
# Tour of the console

The console has four areas. This tour describes each one.

## Navigation bar

The navigation bar runs along the left side. It has these entries:

- **Applications** lists the applications that you can reach.
- **Users** lists the people in your organization.
- **Security** has the multi-factor, **Whitelist**, and session settings.
- **Reports** has the audit log and exports.

## Search

Use the search box at the top of every page to find a user or an application by name. Press <kbd>/</kbd> to move the cursor to the search box.

## The detail pane

When you select an item in a list, its details open in the pane on the right. Click **Edit** to change the item, or click the **X** to close the pane.

## Help

Click **?** to open this documentation in a new tab.
""")

topic('users/add-a-user.md', """
# Add a user

An administrator adds each user before that person can log in. You can add users one at a time or import many users from a file.

## Add one user

1. Select **Users** > **Add user**.
2. Enter the user's name and email address.
3. In the **Role** list, select a role.
4. Click **Save**.

The user receives a welcome message with a link to set a password. The link expires after 7 days.

## Import users from a file

1. Select **Users** > **Import**.
2. Click **Choose file**, and then select a CSV file.
3. Click **Import**.

The CSV file needs the columns `name`, `email`, and `role`. The import skips rows that have an email address that already exists.

> [!IMPORTANT]
> A file can have at most 5,000 rows. Split a larger file into several files.

BOIL_LINE
""")

topic('users/disable-a-user.md', """
# Disable a user

Disable a user when that person leaves your organization or when you suspect that an account is compromised. A disabled user cannot log in, but the account and its history remain.

## Disable an account

1. Select **Users**, and then select the user.
2. Click **Disable**.
3. In the confirmation box, click **Disable** again.

The user's active sessions end within a minute.

## Enable an account again

1. Select **Users**, and then select the filter **Disabled**.
2. Select the user, and then click **Enable**.

> [!WARNING]
> Disabling an account does not remove the user from groups. If the user returns, the account has the same access as before.

To remove the account and its history, delete the user instead. Deleted accounts cannot be restored.
""")

topic('users/reset-a-password.md', """
# Reset a password

A user who forgets a password can reset it without help. An administrator can also send a reset link.

## Reset your own password

1. On the login page, click **Forgot password**.
2. Enter your email address, and then click **Send link**.
3. Open the link in the message. The link works for 30 minutes.
4. Enter a new password twice, and then click **Save**.

## Send a reset link to a user

1. Select **Users**, and then select the user.
2. Click **Send reset link**.

> [!NOTE]
> An end user who has a company account resets the password in the company directory, not in the console.

Passwords must have at least 12 characters. They cannot match any of the last 5 passwords.

BOIL_LINE
""", [('end user', 'customer', 'user')])

topic('users/roles-and-permissions.md', """
# Roles and permissions

A role is a set of permissions. Every user has exactly one role.

| Role | Can manage users | Can manage applications | Can read the audit log |
|---|---|---|---|
| Administrator | Yes | Yes | Yes |
| Application owner | No | Yes | No |
| Auditor | No | No | Yes |
| User | No | No | No |

## Choose a role

Give people the smallest role that lets them do their work. Most people need the **User** role.

## Change a role

1. Select **Users**, and then select the user.
2. In the **Role** list, select the new role.
3. Click **Save**.

The change takes effect the next time the user logs in. To apply it immediately, end the user's sessions.
""")

topic('users/directory-sync.md', """
# Synchronize users from a directory

Directory sync copies users and groups from your company directory into the console. It runs every 15 minutes.

## Requirements

- A directory that supports the LDAP protocol.
- A service account with read access to the users and groups that you want to copy.
- A network path from the console to the directory.

## Connect a directory

1. Select **Security** > **Directory sync**.
2. Click **Add directory**.
3. Enter the server address, the port, and the service account.
4. Click **Test connection**. If the test succeeds, click **Save**.

## Map attributes

Directory sync maps the `mail` attribute to the user's email address and `cn` to the user's name. You can map other attributes on the **Mapping** tab.

> [!NOTE]
> Ask your Identity Provider administrator for the names of the attributes that your directory uses.

If a user is missing after a sync, see the troubleshooting topic for directory sync.
""", [('Identity Provider', 'idp', 'identity provider')])

topic('apps/add-an-application.md', """
# Add an application

Add an application so that users can reach it from the console with one login. The console includes templates for common applications and a custom option for the rest.

1. Select **Applications** > **Add application**.
2. Search for the application, and then select its template. If no template exists, select **Custom**.
3. Enter a name and an icon.
4. Select how users log in: **SAML 2.0** or **OpenID Connect**.
5. Click **Save**.

After you save, the console shows the values that you must enter in the application. Keep the page open until you finish.

## Test the connection

Click **Test login**. The console opens the application in a new window and logs you in as yourself. If the test fails, the console shows the step that failed.

> [!TIP]
> Test with a user who has the **User** role, because an administrator might see extra pages.
""")

topic('apps/assign-users-to-an-application.md', """
# Assign users to an application

A user can reach an application only when an administrator or application owner assigns it. You can assign users one at a time or assign a whole group.

## Assign a group

1. Select **Applications**, and then select the application.
2. Click the **Assignments** tab.
3. Click **Assign group**, and then select the group.
4. Click **Save**.

Everyone in the group can reach the application the next time they log in. People who join the group later get access automatically.

## Remove an assignment

Select the group or user on the **Assignments** tab, and then click **Remove**. The person can no longer open the application from the console. Active sessions in the application continue until they end.
""")

topic('apps/configure-sign-on-settings.md', """
# Configure the login settings of an application

Each application has its own **Sign-On Settings** page. The values on the page control how the console sends users to the application.

## Open the page

1. Select **Applications**, and then select the application.
2. Click **Sign-On Settings**.

## Settings

| Setting | Description | Example |
|---|---|---|
| Entry URL | The address where users start | `https://app.example.com/start` |
| Session length | How long the application session lasts | 8 hr |
| Attribute mapping | Which user fields the application receives | `email` |

Changes apply to new logins. Users who are already logged in keep their current settings until their sessions end.

> [!NOTE]
> Some applications overwrite these values when they refresh their metadata. Check the page after you update an application.
""")

topic('apps/set-up-saml.md', """
# Set up SAML 2.0

Use SAML 2.0 when an application asks for an identity provider metadata file. The console is the identity provider.

## Get the metadata

1. Select **Applications**, and then select the application.
2. Click **Sign-On Settings** > **SAML 2.0**.
3. Click **Download metadata**.

## Send the metadata to the application

Upload the file in the application's SAML settings, or copy the values that the console shows: the issuer, the single sign-on URL, and the signing certificate.

## Check the setup

Click **Test login**. If the application rejects the response, compare the audience and the name ID format with the values that the application expects.

> [!NOTE]
> Some older applications support only SAML2 with SHA-1 signatures. The console does not allow SHA-1 signatures. Ask the application's vendor for an update.
""", [('SAML2', 'saml2', 'SAML 2.0')])

topic('apps/set-up-openid-connect.md', """
# Set up OpenID Connect

Use OpenID Connect when an application asks for a client ID and a client secret. The console is the provider.

1. Select **Applications**, and then select the application.
2. Click **Sign-On Settings** > **OpenID Connect**.
3. Copy the **Client ID** and the **Client secret**.
4. In the application, paste both values, and then enter the redirect address that the application shows.
5. In the console, add the same redirect address in the **Redirect URLs** list, and then click **Save**.

## Scopes

The console supports the `openid`, `profile`, and `email` scopes. An application that asks for other scopes receives an error.

> [!WARNING]
> Treat the client secret like a password. If it is exposed, select **Rotate secret** and update the application.

The first version of this feature supported OpenId Connect only in the authorization code flow. Other flows are not available.
""", [('OpenId Connect', 'oidc', 'OpenID Connect')])

topic('apps/single-sign-on-overview.md', """
# Single sign-on overview

With single sign-on, a user logs in once and then reaches every assigned application without entering a password again. The next sections explain how it works.

## How it works

1. The user opens an application.
2. The application sends the user to the console.
3. The console checks the user's session. If there is none, the user logs in.
4. The console sends a signed message to the application.
5. The application trusts the message and lets the user in.

## Why use it

Single sign on reduces password fatigue and gives administrators one place to end access. It also gives auditors one log of every login.

## What it does not do

Single sign-on does not manage permissions inside an application. A user can reach an application, but the application still decides what that user can do.

Some vendors call this feature Single Sign-On or SSO. This guide uses single sign-on.
""", [('Single sign on', 'sso_spelling', 'Single sign-on')])

topic('security/multi-factor-authentication.md', """
# Multi-factor authentication

Multi-factor authentication asks users for a second proof of identity when they log in. The console supports authenticator apps and hardware keys.

## Require multi-factor authentication

1. Select **Security** > **Multi-factor**.
2. Turn on **Require for all users**, or select a group in **Require for groups**.
3. Click **Save**.

Users set up their second factor the next time they log in.

## Set up an authenticator app

1. Log in to the console.
2. When the console shows a QR code, scan it with your authenticator app.
3. Enter the 6-digit code, and then click **Verify**.

Save the recovery codes that the console shows. Each code works once.

BOIL_LINE
""")

topic('security/manage-the-ip-allowlist.md', """
# Manage the IP allowlist

An IP allowlist limits where users can log in from. Requests from other addresses are refused, even with a correct password.

> [!NOTE]
> In the console, this feature is named **Whitelist**. This guide uses allowlist in running text and the console's name for buttons and tabs.

## Add an address range

1. Select **Security** > **Whitelist**.
2. Click **Add range**.
3. Enter the range in CIDR notation, for example, `203.0.113.0/24`.
4. Click **Save**.

## Test a range

Select a range and click **Test**. Enter an address, and the console tells you whether the address is allowed.

## Turn the allowlist off

Click the switch labeled **Enforce whitelist**. The console keeps the ranges, but all addresses can log in.
""")

topic('security/end-sessions.md', """
# End sessions

You can end the active sessions of a user when you suspect misuse or when you change the user's role.

## End all sessions for one user

1. Select **Users**, and then select the user.
2. Click **Sessions**.
3. Click **Kill all sessions**.

## End one session

On the **Sessions** tab, find the session by its device and location. Click **Kill session** in that row.

The user must log in again on the next request. Open work in an application is not saved by the console.

## Automatic session endings

The console also ends sessions when an administrator disables the user, when the user changes the password, and when the idle timeout passes.
""")

topic('security/rotate-the-master-key.md', """
# Rotate the master key

The master key encrypts the signing keys that the console uses for SAML and OpenID Connect responses. Rotate it once a year, or at once if you suspect exposure.

## Before you rotate

- Confirm that no one else is changing security settings.
- Export the current configuration.

## Rotate the key

1. Select **Security** > **Keys**.
2. Under **Master key**, click **Rotate**.
3. Type the word ROTATE to confirm, and then click **Rotate key**.

The rotation takes about 2 minutes. Logins continue during the rotation.

> [!WARNING]
> Do not rotate the master key during a directory sync. A sync that is running can fail and leave users half updated.

After the rotation, the console keeps the previous key for 24 hr so that applications can refresh their metadata.
""")

topic('security/audit-log.md', """
# Audit log

The audit log records every login and every change to the settings. Auditors and administrators can read it.

## Open the log

Select **Reports** > **Audit log**.

## Columns

| Column | Description |
|---|---|
| Time | When the event happened, in UTC |
| E-mail address | The person who did it |
| Event | What happened, such as Login or Role changed |
| Source | The IP address of the request |

## Filter the log

Use the filters above the table to limit the log by date, person, or event. The filters combine, so a log entry must match all of them.

The console keeps the audit log for 400 days. To keep it longer, export it each month.
""")

topic('security/api-tokens.md', """
# API tokens

An API token lets a script call the console API without a password. Each token has the permissions of the user who created it.

## Create a token

1. Select your name, and then select **API tokens**.
2. Click **Create token**.
3. Enter a name that describes the script, for example, `nightly-export`.
4. Click **Create**, and then copy the token.

The console shows the token only once. Store it in a secret manager.

## Use a token

Send the token in the `Authorization` header:

```
GET /api/v1/users HTTP/1.1
Host: console.example.com
Authorization: Bearer YOUR_TOKEN
```

## Revoke a token

On the **API tokens** page, click **Revoke** next to the token. Scripts that use it receive a 401 response from then on.
""")

topic('admin/brand-the-login-page.md', """
# Brand the login page

You can change the logo, the colors, and the welcome text on your organization's login page.

## Change the branding

1. Select **Settings** > **Branding**.
2. Click **Upload logo**, and then select a PNG or SVG file. The logo should be at most 400 px wide.
3. Pick an accent color.
4. Enter a welcome message of up to 140 characters.
5. Click **Preview** to see the page, and then click **Publish**.

## Use your own domain

By default, users log in at an address on the console's domain. To use your own address, add a CNAME record for it, and then enter it in **Custom domain**. The console issues a certificate for the address within an hour.

The login page keeps the product name in the footer. You cannot remove it.
""")

topic('admin/languages-and-localization.md', """
# Languages and localization

The console is available in several languages. Each user picks a language, and administrators pick the default for the organization.

## Set the default language

1. Select **Settings** > **Language**.
2. Select the language in the **Default language** list.
3. Click **Save**.

## Set your own language

Click your name, select **Preferences**, and then pick a language. The setting follows you to every device.

## Dates and numbers

The console formats dates and numbers for the selected language. For example, the date 3 April 2026 appears as 04/03/2026 in one language and as 03.04.2026 in another.

## What is not translated

Names that you enter, such as application names and group names, appear exactly as you typed them. The documentation is available in English only.
""")

topic('admin/notifications.md', """
# Notifications

The console can send email and webhook messages when something needs attention. Administrators choose which events send a message.

## Turn on email notifications

1. Select **Settings** > **Notifications**.
2. Under **Email**, select the events that you want.
3. Enter one or more email addresses.
4. Click **Save**.

## Events

- A user fails to log in 5 times in a row.
- An administrator changes a security setting.
- A directory sync fails.
- A certificate will expire in 30 days.

Email messages come from `no-reply@example.com`. Add the address to your mail filter so that the messages are not marked as junk.

> [!TIP]
> Send notifications to a shared mailbox instead of one person, so that the messages are seen when someone is away.
""")

topic('admin/export-reports.md', """
# Export reports

You can export the audit log and the user list as CSV files. Exports run in the background, so you can leave the page.

## Export the audit log

1. Select **Reports** > **Audit log**.
2. Set the filters for the period that you want.
3. Click **Export**.

## Download the file

When the export finishes, the console sends a message with a link. The link works for 24 hr. You can also find the file on the **Exports** page.

## File format

The first row has the column names. Dates use the format YYYY-MM-DD, and times use UTC. Fields that contain commas are enclosed in double quotes.

Large exports can take several minutes. Exports are limited to 1,000,000 rows, so split a larger period into several exports.
""")

topic('admin/webhooks.md', """
# Webhooks

A webhook sends an HTTP request to your system when an event happens in the console. Use webhooks to keep other systems in step.

## Create a webhook

1. Select **Settings** > **Webhooks**.
2. Click **Add webhook**.
3. Enter the address to call, and then select the events.
4. Click **Save**.

## Request format

The console sends a POST request with a JSON body:

```
{
  "event": "user.disabled",
  "time": "2026-04-03T09:15:00Z",
  "user": { "id": "u_1042" }
}
```

## Verify the sender

Each request has a signature in the `X-Signature` header. Compare it with the HMAC of the body, using the secret from the webhook page.

If your system returns an error, the console tries again 3 times, waiting longer each time.
""")

topic('troubleshooting/troubleshoot-log-in-problems.md', """
# Troubleshoot login problems

If a user cannot log in to the console or to an application, read the sections below.

## The user sees "Invalid credentials"

The email address or password is wrong. Ask the user to reset the password. If the account uses a company directory, the password is changed there.

## The user sees "Account disabled"

An administrator disabled the account. Select **Users**, find the person, and check the **Status** column.

## The user is asked for a code that does not work

Authenticator codes depend on the clock. Check that the time on the phone is correct. If the problem continues, an administrator can reset the second factor on the user's page.

## The user cannot sign in from the office

Check the IP allowlist. The office address might not be in the list, or the range might be too narrow.

BOIL_LINE
""", [('sign in from the office', 'bank', 'log in from the office')])

topic('troubleshooting/troubleshoot-directory-sync.md', """
# Troubleshoot directory sync

If users or groups are missing after a directory sync, read the sections below.

## Check the sync status

Select **Security** > **Directory sync**. The **Last sync** column shows the time and the result of the most recent sync.

## Common problems

| Message | Cause | Action |
|---|---|---|
| Connection refused | The console cannot reach the directory | Check the firewall and the port |
| Bind failed | The service account password is wrong | Update the password |
| 0 users found | The search base is wrong | Correct the search base |

## Run a sync now

Click **Sync now**. The console starts a sync at once and shows the progress.

If a user still does not appear, check that the user has an email address in the directory. The console skips entries that have none.
""")

topic('reference/ui-label-reference.md', """
# UI label reference

The console uses a few labels that differ from the wording in this guide. This table lists them so that you can find the right control.

| Label in the console | What this guide calls it | Where to find it |
|---|---|---|
| Whitelist | IP allowlist | **Security** |
| E-mail address | Email address | Login page and **Users** |
| Kill session | End a session | User page, **Sessions** tab |
| Master key | Master key | **Security** > **Keys** |
| Sign-On Settings | Login settings | Application page |
| Disable | Disable | User page |

The labels are part of the product. Do not change them in screenshots or in translated documentation.
""")

topic('reference/keyboard-shortcuts.md', """
# Keyboard shortcuts

You can use the keyboard to move around the console.

| Shortcut | Action |
|---|---|
| <kbd>/</kbd> | Move the cursor to the search box |
| <kbd>g</kbd> then <kbd>a</kbd> | Go to **Applications** |
| <kbd>g</kbd> then <kbd>u</kbd> | Go to **Users** |
| <kbd>Esc</kbd> | Close the detail pane |
| <kbd>?</kbd> | Show the list of shortcuts |

Shortcuts do not work while the cursor is in a text field. Press <kbd>Esc</kbd> first to leave the field.

On some keyboards, you must hold <kbd>Shift</kbd> to type <kbd>?</kbd>.
""")

assert len(T) == 30, len(T)
