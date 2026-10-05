# Observability

Both templates instrument traces, metrics and structured logs, including Trellis mediator spans
and service-level indicators. Exporter registration is conditional:

| Configuration | Destination |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP for all three signals |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure Monitor |
| Both | Both destinations |
| Neither | No exporter; normal console logging remains |

OTLP endpoints must be absolute HTTP/HTTPS URLs. `OTEL_EXPORTER_OTLP_PROTOCOL` defaults to `grpc`;
`http/protobuf` appends the signal-specific paths. Unsupported protocols fail composition.
The ASP dashboard is opt-in; Aspire injects its dashboard endpoint into local microservices.

Azure Bicep provisions workspace-based Application Insights. ASP's regional stack injects the
connection string into App Service. The microservices dependency stack returns it for deployment
configuration; AppHost also forwards its configured value to the gateway and both services.
Platform diagnostic settings do not replace application telemetry.

Composition tests cover no destination, OTLP, Azure Monitor and both. Loopback OTLP tests verify
actual trace, metric and log delivery rather than merely looking for registration strings.
