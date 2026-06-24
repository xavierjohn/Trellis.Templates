# Contributing

This repository holds two `dotnet new` templates and the contract that keeps them at parity. Here's how to
work in it.

## Layout

```
asp/            # the Trellis.AspTemplate ASP.NET template
microservices/  # the Trellis.Microservices.Templates Aspire template
shared/
  capability-parity-manifest.yaml   # the required capabilities
  contract-tests/                   # the runner that enforces them
  conventions/                      # cross-template conventions
docs/           # this documentation site (DocFX)
```

Each template keeps its own `.github/copilot-instructions.md` with the authoritative, template-specific rules
(TDD, encoding, build commands). Read a template's instructions before editing it.

## Run the parity contract locally

The same check CI runs, against a template's source:

```bash
dotnet run --project shared/contract-tests -- \
  shared/capability-parity-manifest.yaml asp asp/template

dotnet run --project shared/contract-tests -- \
  shared/capability-parity-manifest.yaml microservices microservices/template
```

It exits non-zero and prints what's missing if a required capability regressed.

## Build a template end-to-end

Building the template content isn't enough — verify the **round-trip** (the same gate CI runs):

```bash
cd microservices
dotnet build template/ProjectTrackerTemplate.slnx -c Release
# pack, install, scaffold, and build the result — see the template's build.cmd
```

A change that builds inside `template/` but breaks after `dotnet new` will fail CI.

## The parity rule

When you add or change a **cross-cutting** capability:

1. Update `shared/capability-parity-manifest.yaml`.
2. Implement it in **both** templates.
3. Let CI run the contract against both.

Never satisfy the contract by weakening a check. Fix the template. See [Capability parity](capability-parity.md).

## Git conventions

- Commit with the GitHub **no-reply** email (`<id>+<username>@users.noreply.github.com`), never a real address.
- **Never** commit to `main` directly, and never force-push `main` or a shared branch. All changes go through
  a PR branch; after a PR opens, add new commits on top rather than amending and force-pushing.
- Commit messages and PR text state only **the issue** and **the fix** — no test counts, no internal
  references, no process narrative, and no `Co-authored-by:` trailers.
- Build with **`-c Release`** before pushing; Release turns code-style diagnostics into errors, which is what
  CI enforces.

## Editing these docs

This site is built with [DocFX](https://dotnet.github.io/docfx/). Preview it locally:

```bash
docfx docs/docfx.json --serve
```

Then open the printed URL. Content lives under `docs/` as Markdown; the navigation is in `docs/toc.yml` and
`docs/articles/toc.yml`.
