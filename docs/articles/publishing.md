# Publishing

The templates are published as NuGet packages from this repository through two channels, plus a build gate
that protects both.

## Channels

| Channel | Feed | Workflow | Auth | Use for |
| --- | --- | --- | --- | --- |
| **Stable** | nuget.org | `Publish template` | Trusted Publishing (OIDC) | Public releases |
| **Alpha** | GitHub Packages | `Publish templates to GitHub Packages` | built-in `GITHUB_TOKEN` | Internal / pre-release builds |

Both are **manual** (`workflow_dispatch`): choose `both` (the default), `asp`, or `microservices`.
Selecting `both` validates and publishes the two packages in parallel jobs within one workflow run.
Each job retains its own validation gates and publish result; a failed job does not cancel the other,
so publication is not all-or-nothing. Package versions are stamped from git
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

Then run **Actions → Publish template**, choose `both` or a single template, and set `dry_run = false`.
The default dry run packs and reports each selected package's version without pushing — a safe way
to confirm what would publish.

## Alpha builds — GitHub Packages

The alpha channel publishes to this account's GitHub Packages NuGet feed using the workflow's built-in
`GITHUB_TOKEN` — **no extra secret**. Run **Actions → Publish templates to GitHub Packages**, choose `both`
or a single template, and it pushes the selected prerelease packages to `https://nuget.pkg.github.com/xavierjohn`.
