# Lagerko

**Stock management for small webshops and businesses.** Lagerko shows how much stock a business actually has, where it went, and what needs reordering, wherever the business sells: its WooCommerce shop, Instagram, phone orders or a physical store.

Live: [lagerko.com](https://lagerko.com) · Built and run by one person, from the data model to the server it runs on.

> Working name in the code is **Zalihe** (Serbian for "stock").

## What it does

- **Items and variations:** SKU, barcode, categories, purchase and sales prices, minimum stock levels
- **Stock movements:** receipts, sales, adjustments (stocktaking, write-offs) and returns, with a full history for every item
- **WooCommerce integration:** connect a shop without copying API keys, import products and variations, deduct stock automatically when an order arrives, restore it when the order is cancelled, and push stock levels back to the shop
- **CSV import** with column mapping, a preview and per-row validation errors
- **Low-stock alerts** in the app and by email (at most one digest per company per day)
- **Reports** for reordering, and real-time updates on open screens via SignalR
- **Multi-tenant** with team invitations and roles; UI in Serbian (Latin) and English

## Tech stack

| Layer | |
|---|---|
| Backend | .NET 10, ASP.NET Core, EF Core, PostgreSQL, Hangfire (Postgres storage), SignalR, ASP.NET Core Identity |
| Frontend | React, TypeScript, Vite, TanStack Query, React Router, Mantine, react-i18next |
| API contract | OpenAPI; the TypeScript client is generated with Orval, never written by hand |
| Testing | xUnit, NSubstitute, Shouldly, Testcontainers, WebApplicationFactory, Vitest, React Testing Library |
| Infrastructure | Docker Compose, Caddy, GitHub Actions CI/CD to a self-hosted server |

## Design decisions

- **Stock is never edited directly.** Every change is an append-only `StockMovement`, and `StockLevel` is updated in the same transaction. Movements are never changed or deleted; mistakes are fixed with a correcting movement. This keeps a full audit trail and makes the current stock always explainable.
- **Tenant isolation in one shared database.** Every company-owned table has a `TenantId`, filtered through EF Core global query filters, never by hand in queries. Integration tests check that company A can never see company B's data.
- **Clean Architecture, kept light.** `Domain`, `Application`, `Infrastructure` and `Web` projects, but no MediatR, CQRS or event sourcing: plain services with clear methods.
- **API first, ready for mobile.** All business logic lives in the API, lists are paginated and filtered on the server, errors use `ProblemDetails` with error codes the frontend translates, and auth is set up to accept bearer tokens later for a React Native app.
- **Privacy by default.** No customer personal data from orders is stored. Shop API keys are encrypted with ASP.NET Data Protection.
- **Sales channels behind an interface** (`ISalesChannel`), so WooCommerce is the first integration, not a hard dependency.

## Testing

- **~550 backend tests:** fast unit tests for domain rules (stock calculation, order processing) and integration tests against a real PostgreSQL database via Testcontainers
- **Frontend tests** with Vitest for components with logic: forms, validation, number formatting
- WooCommerce is never called in tests; a fake `ISalesChannel` or HTTP handler is used instead
- Every push to `main` or `dev` runs all tests, builds and lints the frontend, then deploys `main` to production and `dev` to a separate dev environment

## Project structure

```
src/
  Zalihe.Domain/          entities and domain rules, no dependencies
  Zalihe.Application/     services and use cases, interfaces (e.g. ISalesChannel)
  Zalihe.Infrastructure/  EF Core, WooCommerce client, email, Hangfire jobs
  Zalihe.Web/             API controllers, webhook endpoints, serves the React build
  web/                    React app (Vite)
tests/
  Zalihe.Domain.Tests/
  Zalihe.Application.Tests/
  Zalihe.IntegrationTests/
deploy/                   production Docker Compose, Caddy, backup and deploy scripts
docs/                     spec, architecture, design and deployment notes (Serbian)
```

## Running locally

Requirements: .NET 10 SDK, Node.js 24, Docker.

```bash
docker compose up -d --wait                                # Postgres (5433), WordPress + WooCommerce (8080), Mailpit (8025)
dotnet run --project src/Zalihe.Web --launch-profile http  # API on http://localhost:5131
cd src/web && npm install && npm run dev                   # React on http://localhost:5173

# Optional: set up a WooCommerce test shop with sample products
docker compose run --rm wpcli bash /scripts/setup.sh

dotnet test                                                # all .NET tests
cd src/web && npm test                                     # frontend tests
```

## Author

Miljan Milojković: full-stack engineer (C# / .NET, React / TypeScript, PostgreSQL) from Niš, Serbia.
[miljanmilojkovic.com](https://miljanmilojkovic.com) · [LinkedIn](https://linkedin.com/in/miljan-milojkovic-682878200)
