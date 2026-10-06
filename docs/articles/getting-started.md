# Get started

## Prerequisites

- **.NET 10 SDK** or later. Check with `dotnet --version`.
- Docker or Podman for microservices and ASP server-database profiles. Both templates restore packages
  from NuGet.org without private feeds.

## 1. Install the templates

```bash
dotnet new install Trellis.Asp.Templates
dotnet new install Trellis.Microservices.Templates
```

You can confirm they're installed with:

```bash
dotnet new list trellis
```

## 2. Scaffold a project

Pick the template that fits what you're building:

```bash
# A single, focused service
dotnet new trellis-asp -n MyService

# A platform of services behind a gateway
dotnet new trellis-microservices -n MyPlatform
```

The `-n` value names the solution and supplies the default namespace. Use `--root-namespace` for a
different valid C# namespace and `--author-name` for project authorship.

Both templates default to unversioned APIs, external JWT/OIDC authentication, OTLP, and no deployment
scaffold. `--api-versioning`, `--database postgres|sqlserver`, `--auth entra`,
`--telemetry-exporters azure-monitor|both`, and `--deployment container|azure` customize the output.
ASP defaults to SQLite; microservices defaults to SQL Server. Azure requires an explicit server
provider and rejects SQLite. `--skip-restore` suppresses automatic restore.

## 3. Build and run

### ASP.NET service

```bash
cd MyService
dotnet build MyService.slnx -c Release    # 0 warnings, 0 errors
dotnet run --project Api/src
```

Then open the **Scalar API reference** (printed in the console) to explore the API, or hit
the generated HTTP requests for the sample endpoints. Unversioned output publishes `/openapi/v1.json`;
versioned output publishes a document for each API version.

### Microservices

The microservices template is orchestrated by **.NET Aspire**. Run the AppHost and it brings up the
gateway and every service together, with the Aspire dashboard for traces, logs, and metrics:

```bash
cd MyPlatform
dotnet run --project AppHost/src
```

The dashboard URL is printed on startup. From there you can reach the gateway and each service, and watch
requests flow across services in the trace view.

## 4. Make it yours

Both templates ship with a **working reference implementation** (a small Todo / Project-Tracker domain) so
you can see the patterns in context before you replace them. The recommended path:

1. Read the reference domain to see how aggregates, commands, handlers, and endpoints fit together.
2. Replace it with your own domain, one layer at a time, building as you go.
3. Start coding agents at `AGENTS.md` for architectural rules and coding conventions. Its managed
   pointer routes to `.agentdocs/README.md` and the version-aligned package references.
   `.github/copilot-instructions.md` delegates to the same canonical instructions for Copilot.

To maintain the optional AgentDocs setup, run these commands from the generated project's Git root
after creating it or upgrading packages:

```powershell
dotnet tool restore
dotnet restore
dotnet tool run agentdocs sync
dotnet tool run agentdocs check --strict
```

Initialize a new repository with `git init` first if needed. Commit the tool manifest, managed
instruction pointers, policy, and `.agentdocs/` with package updates. The application builds and runs
without the tool.

## Where to next

- **[Capabilities](capabilities.md)** — what every Trellis service gets for free, and how to use each one.
- **[ASP.NET service template](asp-template.md)** / **[Microservices template](microservices-template.md)** — a tour of each template's structure.
- **[Capability parity](capability-parity.md)** — how the two templates are kept in lock-step.
