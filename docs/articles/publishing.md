# Publishing

The templates are published as NuGet packages from this repository through two channels, plus a build gate
that protects both.

## Channels

| Channel | Feed | Workflow | Auth | Use for |
| --- | --- | --- | --- | --- |
| **Stable** | nuget.org | `Publish template` | Trusted Publishing (OIDC) | Public releases |
| **Alpha** | GitHub Packages | `Publish to GitHub Packages` | built-in `GITHUB_TOKEN` | Internal / pre-release builds |

Both are **manual** (`workflow_dispatch`): you pick a template and run. Package versions are stamped from git
history by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning), so each commit produces
a unique prerelease version.

## The build gate

Before anything is published, the **Build templates** workflow runs on every push and pull request. For each
template it builds and tests the content, packs the template, then **installs the pack, scaffolds a project
from it, and builds that** — the end-to-end check that catches "the template installs but produces code that
doesn't compile". If the round-trip fails, nothing ships.

## Stable releases — NuGet Trusted Publishing

The stable channel uses **[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing)**:
there is **no long-lived API key**. The workflow requests a GitHub OIDC token, exchanges it with nuget.org for
a one-hour key via `NuGet/login`, and pushes with that.

One-time setup:

1. On nuget.org, go to your username → **Trusted Publishing** and add a policy for repository owner
   `xavierjohn`, repository `Trellis.Templates`, workflow file `publish-templates.yml`.
2. Add a `NUGET_USER` secret holding your nuget.org profile name.

Then run **Actions → Publish template**, choose the template, and set `dry_run = false`. The default dry run
packs and reports the version without pushing — a safe way to confirm what would publish.

## Alpha builds — GitHub Packages

The alpha channel publishes to this account's GitHub Packages NuGet feed using the workflow's built-in
`GITHUB_TOKEN` — **no extra secret**. Run **Actions → Publish to GitHub Packages**, choose a template, and it
pushes a prerelease build to `https://nuget.pkg.github.com/xavierjohn`.
