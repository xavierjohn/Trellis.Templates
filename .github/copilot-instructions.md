# Copilot instructions — Trellis.Templates

This repository holds the official Trellis project templates **and** the contract that keeps them at parity.
Read this before editing.

## Layout

- `asp/` — the `Trellis.AspTemplate` ASP.NET single-app template (`dotnet new trellis-asp`).
- `microservices/` — the `Trellis.Microservices.Templates` Aspire multi-service template (`dotnet new trellis-microservices`).
- `shared/capability-parity-manifest.yaml` — the single source of truth for required cross-cutting capabilities.
- `shared/contract-tests/` — the runner that asserts each template implements the manifest.
- `shared/conventions/` — cross-template conventions (e.g. Azure resource naming).

**Before editing a template, read its own instructions:** `asp/.github/copilot-instructions.md` and
`microservices/.github/copilot-instructions.md`. Those are the authoritative, template-specific rules
(TDD, encoding, build/round-trip commands). The imported `asp/.github/workflows/*` and
`microservices/.github/workflows/*` are inert reference copies — CI runs only from the repo-root
`.github/workflows/`.

## The parity contract — the whole point of this repo

A capability is "cross-cutting" if a developer would expect it regardless of which template they picked.
Every such capability is listed in `shared/capability-parity-manifest.yaml` with `requiredFor: [asp, microservices]`.

When you add or change a cross-cutting capability:

1. Update `shared/capability-parity-manifest.yaml` (add the capability and its verification checks).
2. Implement it in **both** templates.
3. CI instantiates each template and runs `shared/contract-tests` against it. A missing/regressed
   required capability fails the build.

Never satisfy the manifest by weakening a check to match a template. Fix the template.

## Git conventions (apply to every change here)

- Author identity uses the GitHub no-reply email (`<id>+<username>@users.noreply.github.com`), never a real email.
- Never commit to `main`; never force-push `main` or a shared branch. All changes go through a PR branch.
- After a PR opens, add new commits on top — do not amend + force-push (the only exceptions are
  pre-first-push message polish and redacting personal data).
- PR titles, descriptions, commit messages, and review replies state only **the issue** and **the fix**.
  No test counts / "X tests pass", no internal references (lab runs, finding IDs, session names, model
  attribution), no process narrative, and **no `Co-authored-by:` trailers**.
- The template solutions build in **Release** in CI, which enforces code-style (IDE00xx) as errors —
  build with `-c Release` before pushing.
