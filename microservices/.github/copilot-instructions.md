# Copilot instructions for Trellis.Microservices.Template

This repository ships the `Trellis.Microservices.Templates` NuGet template pack — `dotnet new trellis-microservices` scaffolds a multi-tenant microservices topology using the [Trellis](https://github.com/xavierjohn/Trellis) and [Trellis.Microservices](https://github.com/xavierjohn/Trellis.Microservices) packages.

## Layout

| Path | Role |
|---|---|
| `templatepack.csproj` | The NuGet template pack. Packs everything under `template/` into `content/` inside the .nupkg. |
| `template/` | The actual scaffolded project content. **Edits here become the user's starting point.** |
| `template/.template.config/template.json` | Template engine config — parameters, sourceName, post-actions. |
| `.github/workflows/build.yml` | Builds the template content, packs it, instantiates the result, and rebuilds it end-to-end. |
| `.github/workflows/publish.yml` | Manual-dispatch gate to push the pack to nuget.org with a dry-run option. |
| `template/.agentdocs/` | AgentDocs-managed policy, project-aware index, context, and version-aligned framework, microservices, ResourceNaming, and SLI references. Start at `README.md`. |
| `template/.config/dotnet-tools.json` | Pinned Trellis.AgentDocs local tool. |
| `template/AGENTS.md`, `template/.github/copilot-instructions.md` | Managed pointers to the AgentDocs index, alongside curated template conventions. |

## When you change `template/` content

Always verify the round-trip locally:

```powershell
cd C:\GitHub\Trellis\Trellis.Microservices.Template
./build.cmd
# then in a clean shell:
dotnet new uninstall Trellis.Microservices.Templates
dotnet new install .\nupkg\Trellis.Microservices.Templates.*.nupkg
mkdir C:\Temp\trellis-smoke
cd C:\Temp\trellis-smoke
dotnet new trellis-microservices -n SmokeTest
cd SmokeTest
dotnet build SmokeTest.slnx -c Release   # MUST produce 0 warnings, 0 errors
```

The CI `build.yml` runs this exact loop on every PR. A change that builds locally inside `template/` but breaks the instantiation will fail CI.

## Key conventions

- **Aspire project type names use the csproj BASE NAME** — `Projects.Projects`, `Projects.Members`, `Projects.Gateway`. The repeated `Projects.Projects` is intentional (outer namespace, inner type).
- **`TEMPLATE_*` tokens** in template content are replaced by template.json `symbol.replaces`. Add new tokens by following the `TEMPLATE_GATEWAY_ISSUER_URL` pattern.
- **`sourceName: "ProjectTrackerTemplate"`** — replaced everywhere by the user's `-n` argument. The token must appear as `ProjectTrackerTemplate` (no spaces); the `ValueWithoutSpaces` form handles spaces in user input.
- **`<Using Include="Trellis" />` causes Unit/IResult ambiguity** with Mediator/Microsoft.AspNetCore.Http when scoped globally. Use per-file `using Trellis;` and fully-qualify `Mediator.Unit` and `Trellis.IResult` where ambiguous.
- **IDE0005 is a build error** in the template content (matches upstream `xavierjohn/Trellis.Microservices`). Don't leave unused usings.
- **Trellis runtime package versions** are pinned in `template/Directory.Packages.props` via two MSBuild properties: `$(TrellisVersion)` (framework — `xavierjohn/Trellis`) and `$(TrellisMicroservicesVersion)` (`xavierjohn/Trellis.Microservices`). Respect the microservices packages' minimum framework version, then regenerate AgentDocs (see below).

## Syncing the API reference

`template/.agentdocs/` ships version-aligned references installed by the pinned AgentDocs tool.
Core covers framework guidance. Approve `Trellis.Microservices.Abstractions`,
`Trellis.Microservices.AspNetCore`, `Trellis.Yarp`, ResourceNaming, and the SLI packages separately in
`policy.json`; they publish guidance outside Core.

After changing package versions, run the following in an isolated copy of `template/` initialized
with `git init`. AgentDocs requires a Git root, not a nested directory in this enclosing repository:

```powershell
dotnet tool restore
dotnet restore ProjectTrackerTemplate.slnx
dotnet tool run agentdocs sync --strict
dotnet tool run agentdocs check --strict
```

Copy the generated `.agentdocs/`, `AGENTS.md`, and `.github/copilot-instructions.md` back into
`template/`. Keep `.config/dotnet-tools.json` with them if the tool version changed. Review and commit
the managed output with the package update; do not copy package Markdown manually or edit managed files.

For a newly generated project, initialize its Git root, restore the local tool and solution, then run
`agentdocs sync` and `agentdocs check --strict` to refresh the recorded restore graph for its new name.
Trellis builds and runs without the tool; only guidance maintenance requires it.

## Don't

- Don't break the round-trip. If your change builds inside `template/` but fails after `dotnet new`, fix it before merging.
- Don't add cross-project `<ProjectReference>` between `template/` content and code outside `template/` — the pack only ships `template/**`.
- Don't add private NuGet sources to `template/nuget.config` — it must work for any developer running `dotnet new trellis-microservices` on a stock machine.
