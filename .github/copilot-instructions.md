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
| `.github/trellis-api-*.md` | API references for the Trellis packages — context for editing the template itself. |

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
- **Trellis runtime package versions** are pinned in `template/Directory.Packages.props` — bump in lock-step with `xavierjohn/Trellis.Microservices` releases that change consumed APIs.

## Don't

- Don't break the round-trip. If your change builds inside `template/` but fails after `dotnet new`, fix it before merging.
- Don't add cross-project `<ProjectReference>` between `template/` content and code outside `template/` — the pack only ships `template/**`.
- Don't add private NuGet sources to `template/nuget.config` — it must work for any developer running `dotnet new trellis-microservices` on a stock machine.
