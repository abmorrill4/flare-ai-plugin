# Create an access token

An access token lets a script start jobs without a password. Keep each token private.

1. Select **Account** > **Tokens**.
2. Click **New token**, and then enter a name.
3. Copy the token before you close the page. It does not appear again.

Pass the token in the `Authorization` header:

```http
GET /v1/jobs HTTP/1.1
Authorization: Bearer YOUR_TOKEN
```

:::tip
Create one token for each script. You can then revoke a single token without breaking the others.
:::

To remove a token, select it in the list and click **Revoke**.
