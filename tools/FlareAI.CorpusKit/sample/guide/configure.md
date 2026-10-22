# Configure Zorbit

You can change how Zorbit behaves in the settings file. Edit the file before you restart the agent.

| Setting | What it does | Default |
| --- | --- | --- |
| `retries` | Sets how many times a failed job retries. | 3 |
| `interval` | Sets how often a worker reports its status. | 60 seconds |
| `region` | Chooses the region that runs your jobs. | `us-east` |

!!! warning "Restart required"
    A change to `region` takes effect only after you restart the agent. Running jobs finish first.

To rotate the access token, follow [create an access token](../auth/tokens.md).

Select **Settings** > **Advanced** to see the full list of options.
