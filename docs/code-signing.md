# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

## What is signed
Releases on GitHub (`HouseStyle-<version>.zip`): `FlareAI.Core.dll`, `FlareAI.Flare.dll` and `FlareAI.Mcp.exe`.
They are built by GitHub Actions (`.github/workflows/build.yml`) from the tagged commit in this repository and signed
only through that workflow. Third-party files in the zip (`Newtonsoft.Json.dll`, already signed by its author) are not
re-signed. MadCap's DLLs are never included.

## Roles
- Committers and reviewers: [Arthur Morrill](https://github.com/abmorrill4)
- Approvers: [Arthur Morrill](https://github.com/abmorrill4)

Every signing request is approved by hand. Maintainers use multi-factor authentication on GitHub and SignPath.

## Privacy
This program transfers document data to other networked systems only when the user asks for an AI action: the text
being checked (and, for alt text, the image) and the relevant style rules go to the Anthropic API, using the user's own
API key, or to a gateway the user configures. Once a day it asks GitHub for the latest release, sending no text, key or
identifier; the user or IT can turn this off. It sends no telemetry. See the Privacy section of the README.

---

## Maintainer setup (one time)

1. Make the repository public and publish a first (unsigned) release by pushing a tag: `git tag v0.1.0 && git push --tags`.
2. Apply at <https://signpath.org/apply> with the repository URL. Mention that the plugin calls MadCap Flare's
   Plug-In API but ships no MadCap code, and that CI builds against open-source declarations in `ref/`.
3. When approved, in SignPath create:
   - project `house-style`, linked to this GitHub repository (trusted build system: GitHub.com);
   - signing policy `release-signing` (manual approval);
   - artifact configuration `plugin-zip`:

     ```xml
     <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
       <zip-file>
         <directory path="FlareAI">
           <pe-file path="FlareAI.Core.dll"><authenticode-sign/></pe-file>
           <pe-file path="FlareAI.Flare.dll"><authenticode-sign/></pe-file>
           <pe-file path="FlareAI.Mcp.exe"><authenticode-sign/></pe-file>
         </directory>
       </zip-file>
     </artifact-configuration>
     ```
4. Add repository secrets `SIGNPATH_API_TOKEN` (a CI user's token) and `SIGNPATH_ORGANIZATION_ID`.
5. From then on every `v*` tag builds, waits for your approval in SignPath, and releases the signed zip.
