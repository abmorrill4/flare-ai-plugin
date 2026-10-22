# Diagrams

Two self-contained SVGs: no external requests, system fonts only, light and dark mode, 360 x 548 or smaller viewBox so they read at phone width (vertical layout, scales with the container up to 520px).

- `how-it-works.svg`: the flow in Flare, with the local/network boundary.
- `data-flow.svg`: what stays on the machine and what leaves on an AI action (privacy page).

## Embed

Inline (recommended; picks up the site colours). Paste the file's contents where the diagram goes:

    <figure class="diagram"> ...contents of how-it-works.svg... </figure>

The SVG reads `--text`, `--text-muted`, `--border`, `--surface`, `--bg-alt`, `--accent`, `--accent-text`, `--note-bg` and `--note-border` from `style.css`, with built-in fallbacks. It follows the site's `prefers-color-scheme` rules and needs no extra CSS. It already has `role="img"`, `<title>` and `<desc>`.

As an image (fallback colours only, still follows the OS light/dark setting; the `alt` text must repeat the meaning):

    <img src="diagrams/how-it-works.svg" alt="..." width="360" height="548">

Put a visible caption or the surrounding text next to it; don't rely on the diagram alone.
