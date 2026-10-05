# Copilot Instructions — Trellis Template Repository

This repository contains the **Trellis ASP.NET template** (`dotnet new`). There are two sets of copilot instructions:

1. **`.github/copilot-instructions.md`** (this file) — Instructions for working on the template repository itself.
2. **`template/.github/copilot-instructions.md`** — Instructions shipped with the template. When a user installs the template, this file guides AI in building their service. Edit this file when updating Trellis conventions, patterns, or architectural guidance.

---

## Repository Structure

```
Trellis.AspTemplate/
├── templatepack.csproj            ← NuGet template pack project
├── version.json                   ← Nerdbank.GitVersioning
├── template/                      ← The actual template content (installed by `dotnet new`)
│   ├── .github/
│   │   └── copilot-instructions.md   ← AI instructions + managed AgentDocs pointer
│   ├── .config/dotnet-tools.json     ← Pinned Trellis.AgentDocs tool
│   ├── .agentdocs/                   ← Managed policy, index, context, and package references
│   ├── AGENTS.md                     ← Managed pointer to .agentdocs/README.md
│   ├── Directory.Build.props
│   ├── Directory.Packages.props      ← Trellis + dependency versions
│   ├── Domain/
│   ├── Application/
│   ├── Acl/
│   ├── Api/
│   └── build/
└── .github/
    └── copilot-instructions.md       ← THIS FILE (template repo instructions)
```

## Key Files

- **`template/.github/copilot-instructions.md`** — The most important file for Trellis conventions. This is what AI agents see when building services from the template. Keep it focused on architectural rules and conventions; defer API details to the template API reference.
- **`template/.agentdocs/README.md`** — AgentDocs' project-aware index of required and on-demand package references.
- **`template/.agentdocs/policy.json`** — Approved guidance packages. Core covers the framework; ResourceNaming and SLI require separate approvals.
- **`template/Directory.Packages.props`** — Central package version management. The `TrellisVersion` property controls all Trellis package versions.

## Working on the Template

- Template content lives entirely under `template/`. Files added there are included when a user runs `dotnet new`.
- Do NOT modify `Directory.Build.props`, `global.json`, or `build/test.props` — these are pre-configured for template users. Exception: updating the placeholder service name (e.g., `TodoSample`) in `Directory.Build.props` is allowed when changing the template's sample identity.
- Add new NuGet packages to `template/Directory.Packages.props` (version) and the relevant `.csproj` (reference without version).
- The template uses `TodoSample` as a placeholder service name.

## Building & Testing the Template Pack

```powershell
# Build the template NuGet package
dotnet pack templatepack.csproj

# Install locally for testing
dotnet new install ./nupkg/Trellis.AspTemplate.*.nupkg

# Create a new project from the template
dotnet new trellis-asp -n MyService

# Uninstall
dotnet new uninstall Trellis.AspTemplate
```

## Upgrading Trellis Packages

After upgrading `TrellisVersion` in `template/Directory.Packages.props`, regenerate the guidance with
the pinned `Trellis.AgentDocs` tool. AgentDocs operates at the Git root, so run the following in an
isolated copy of `template/` initialized with `git init`, not inside this enclosing template repository:

```powershell
dotnet tool restore
dotnet restore TrellisAspTemplate.slnx
dotnet tool run agentdocs sync --strict
dotnet tool run agentdocs check --strict
```

Copy the generated `.agentdocs/`, `AGENTS.md`, and `.github/copilot-instructions.md` back into
`template/`. Keep `.config/dotnet-tools.json` with them if the tool version changed. Review and commit
the managed output with the package update; do not copy package Markdown manually or edit managed files.

For a newly generated project, initialize its Git root, restore the local tool and solution, then run
`agentdocs sync` and `agentdocs check --strict` to refresh the recorded restore graph for its new name.
Trellis builds and runs without the tool; only guidance maintenance requires it.

## Updating Trellis Conventions

When updating how AI should build services with Trellis:

1. Edit `template/.github/copilot-instructions.md` for architectural rules and conventions.
2. For API reference changes, update the upstream package and regenerate `template/.agentdocs/` with AgentDocs; never hand-edit package-owned guides.
3. Keep instructions DRY — the copilot instructions should reference the template API reference by section number (e.g., "See §12") rather than duplicating API details.
