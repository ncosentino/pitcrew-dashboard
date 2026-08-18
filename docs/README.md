# PitCrew Dashboard documentation

This map is the canonical entry point for maintained Dashboard documentation.

## Overview

- [Project overview and quick start](../README.md)

## Architecture

- [Architecture decision process](architecture/decisions.md)
- [Architecture decision records](adr/README.md)
- [.NET project and feature structure](architecture/project-structure.md)
- [Data access and repositories](architecture/data-access.md)
- [HTTP clients and options](architecture/http-clients-and-options.md)
- [Frontend architecture](frontend-architecture.md)

## Fleet operations and evidence

- [Capacity operations](capacity-operations.md)
- [Manager recovery](manager-recovery.md)
- [Connector health journal](connector-health.md)
- [Host hardware inventory](hardware-inventory.md)
- [Runner correlation assignments](runner-correlation.md)
- [Noninteractive read-only diagnostics](noninteractive-diagnostics.md)
- [Database operations](database-operations.md)
- [Support plane v1](support-plane.md)

## Deployment

- [Hosted deployment](hosted-deployment.md)
- [Caddy ingress](hosting/caddy.md)
- [Cloudflare Tunnel ingress](hosting/cloudflare-tunnel.md)
- [Hosted support relay](hosting/support-relay.md)
- [Custom ingress contract](hosting/custom-ingress.md)
- [Container packaging](container/README.md)
- [ASP.NET Core container](container/aspnet.md)
- [Publishing to GitHub Container Registry](container/ghcr.md)

## Development

- [.NET engineering conventions](development/dotnet-engineering.md)
- [Node dependency installation](development/node-dependencies.md)
- [.NET performance](performance/dotnet-performance.md)
- [Testing strategy](testing.md)
- [Roslyn analyzers](development/roslyn-analyzers.md)
- [Evaluations and benchmarks](testing/evaluations-and-benchmarks.md)
- [Browser UX evidence harness](testing/browser-ux.md)
- [Physical device evidence procedure](testing/physical-device-evidence.md)
- [Request validation and job scheduling](development/request-validation-and-jobs.md)
- [Blazor extensibility](ui/blazor-extensibility.md)

## UX and design

- [Product context](../PRODUCT.md)
- [Dashboard design system](../DESIGN.md)
- [UX and design resilience](ux-design.md)
- [UX terminology and status language](ux-terminology.md)
- [Settings navigation and form composition](ui/settings-composition.md)
- [Impeccable design workflow](impeccable-design.md)
