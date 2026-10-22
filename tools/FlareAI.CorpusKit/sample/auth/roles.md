# Roles and permissions

Every account has one role. The role decides what the account can see and change.

- **Viewer** accounts can read jobs and history.
  - They cannot start or stop a job.
  - They cannot see tokens.
- **Operator** accounts can start and stop jobs.
- **Admin** accounts can also manage workers and roles.

| Role | Start jobs | Manage workers | Manage roles |
| --- | --- | --- | --- |
| Viewer | No | No | No |
| Operator | Yes | No | No |
| Admin | Yes | Yes | Yes |

An admin can change a role from the **Accounts** page. The change takes effect the next time the person signs in. Read [Sign in to Zorbit](login.html) for the sign-in steps.
