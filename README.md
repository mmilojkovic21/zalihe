# Lagerko

**Inventory and stock management SaaS for webshops and small businesses.**
Live: [lagerko.com](https://lagerko.com)

Lagerko keeps stock in sync with WooCommerce webshops, records every stock movement as an immutable ledger, and gives small teams a fast web app (with barcode scanning) for receiving goods, processing orders and tracking inventory.

> Source is published for review only. See [LICENSE](LICENSE).

---

## Features

- **Items & stock ledger.** Stock is never edited directly: it is always the sum of append-only movements (receipt, sale, return, adjustment to counted quantity).
- **WooCommerce integration.** Connect a shop, import products, and process orders and stock updates via webhooks through a channel abstraction (`ISalesChannel`).
- **Goods receipts and orders.** Receive deliveries, handle incoming orders and returns.
- **CSV import.** Bulk import of items.
- **Low-stock alerts, dashboard and reports.**
- **Real-time updates** across open sessions via SignalR.
- **Barcode scanning** in the browser.
- **Multi-tenant.** Many companies share one database with strict isolation.
- **Localization** (i18next).

## Tech stack

| Area | Technologies |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core, ASP.NET Core Identity, SignalR, Hangfire, MailKit, OpenAPI |
| Database | PostgreSQL |
| Frontend | React, TypeScript, Vite, Mantine, TanStack Query, React Router, Orval (typed API client generated from OpenAPI) |
| Testing | xUnit, Testcontainers (real PostgreSQL), WebApplicationFactory, Vitest, React Testing Library |
| DevOps | Docker, Docker Compose, Nginx, GitHub Actions CI/CD, self-hosted Linux server |

## Architecture

A pragmatic Clean Architecture ("light"): clear layers, no MediatR/CQRS ceremony.

```
Zalihe.Web  ──►  Zalihe.Infrastructure  ──►  Zalihe.Application  ──►  Zalihe.Domain
(HTTP, SignalR)  (EF Core, Identity,         (use cases,              (entities,
                  WooCommerce, jobs)          interfaces)              business rules)
```

Inner layers never depend on outer ones. The domain has no framework dependencies, so core rules (stock calculation, order processing) are tested in milliseconds without a database.

### Tenant isolation

Data isolation does not rely on developers remembering a `WHERE TenantId = …`:

1. The tenant id is stored as a claim in the auth cookie.
2. Middleware sets a per-request `TenantContext`.
3. An EF Core **global query filter** is applied automatically to every tenant-owned entity.
4. Queries without a tenant throw, and `SaveChanges` rejects writes for a different tenant.

A dedicated test fails if anyone adds a tenant-owned entity without the filter.

### Concurrency-safe stock

Each movement runs in a transaction that locks the item's stock row (`SELECT … FOR UPDATE`), so simultaneous movements for the same item (e.g. a webhook and a user) are serialized and the stock always equals the sum of movements.

## Testing

- ~550 backend tests across domain, application and integration test projects
- Integration tests run against a **real PostgreSQL** in Docker (Testcontainers), not an in-memory fake
- ~50 frontend test files (Vitest + React Testing Library)

## CI/CD

Every push to `main` or `dev` runs backend and frontend tests, the frontend build and the linter in GitHub Actions. On success it deploys over SSH to the matching environment (beta / dev) on a self-hosted server running Docker Compose behind Nginx.

---

**Author:** Miljan Milojković · [miljanmilojkovic.com](https://miljanmilojkovic.com) · [LinkedIn](https://www.linkedin.com/in/miljan-milojkovic-682878200/)
