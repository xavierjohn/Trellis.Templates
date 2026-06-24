# Get started

## Prerequisites

- **.NET 10 SDK** or later. Check with `dotnet --version`.
- That's it. Both templates restore everything else from NuGet.org — no private feeds, no extra tooling.

## 1. Install the templates

```bash
dotnet new install Trellis.AspTemplate
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

The `-n` value becomes your project/namespace name everywhere.

## 3. Build and run

### ASP.NET service

```bash
cd MyService
dotnet build MyService.slnx -c Release    # 0 warnings, 0 errors
dotnet run --project Api/src
```

Then open the **Scalar API reference** (printed in the console) to explore the API, or hit
`/health` for a readiness check, and `/openapi/{version}.json` for the OpenAPI document.

### Microservices

The microservices template is orchestrated by **.NET Aspire**. Run the AppHost and it brings up the
gateway and every service together, with the Aspire dashboard for traces, logs, and metrics:

```bash
cd MyPlatform
dotnet run --project AppHost
```

The dashboard URL is printed on startup. From there you can reach the gateway and each service, and watch
requests flow across services in the trace view.

## 4. Make it yours

Both templates ship with a **working reference implementation** (a small Todo / Project-Tracker domain) so
you can see the patterns in context before you replace them. The recommended path:

1. Read the reference domain to see how aggregates, commands, handlers, and endpoints fit together.
2. Replace it with your own domain, one layer at a time, building as you go.
3. Lean on the shipped `.github/` instructions and API references — they tell an AI assistant exactly how
   to build with Trellis, so scaffolding new features stays consistent.

## Where to next

- **[Capabilities](capabilities.md)** — what every Trellis service gets for free, and how to use each one.
- **[ASP.NET service template](asp-template.md)** / **[Microservices template](microservices-template.md)** — a tour of each template's structure.
- **[Capability parity](capability-parity.md)** — how the two templates are kept in lock-step.
