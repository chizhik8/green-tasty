# Repository Guidelines

These instructions apply to every coding agent working in this repository (Codex, Claude Code and others). This file holds only repository-wide rules; `CLAUDE.md` only imports it, so add or change them here.

Restaurant booking system (table reservations, pre-orders, group pre-orders, feedback, waiter/admin reporting) built as a monorepo with three independently deployed parts:

| Part | Path | Tooling |
|---|---|---|
| Shared AWS infrastructure | `infra/terraform` | Terraform ≥ 1.10 |
| Backend (.NET 10 Lambdas) | `backend/restaurant-backend-app` | `dotnet`, AWS Syndicate (`pip install aws-syndicate`) in a Python venv. The README recommends Python 3.10–3.11; that is a recommendation, not a verified limit — the local venv runs 3.12 |
| Frontend (Angular 22 SPA) | `frontend/restaurant-frontend-app` | Node and npm (version in that folder's `.nvmrc` and `package.json` `engines`) |

## Per-application Instructions

Commands, conventions, tests and verification gates for each application live inside that application's folder, not here. Read them before changing anything in that folder:

- **Frontend:** `frontend/restaurant-frontend-app/CLAUDE.md`. Despite the file name, it applies to every agent; the frontend has no `AGENTS.md` of its own yet.
- **Backend:** `backend/restaurant-backend-app/AGENTS.md` (its `CLAUDE.md` imports it).
- **Infrastructure** has no separate file. From `infra/terraform`, run `terraform fmt -check -recursive` and, after initialization, `terraform validate` and `terraform plan`.

Run commands from the relevant application directory; there is no root npm workspace.

## Required Reading

- **Before implementing or changing functionality**, read `docs/project-overview.md` (product, roles, business rules) and the relevant stories in `docs/requirements-user-stories.md` (currently the Group Pre-order feature: US1, US10–US12, US14–US17).
- **Before changes that cross the frontend/backend/infrastructure boundary**, read `docs/architecture.md`: boundaries, auth, data, API contracts, configuration, deploy dependencies and open questions.

**Current state vs. requirements.** The code and `docs/architecture.md` describe what is implemented now; the user stories describe the expected behavior. When they contradict each other, point out the discrepancy to the user instead of silently changing either side — never rewrite requirements to match the code.

## Commit & Pull Request Guidelines

Per-application commit conventions are in the per-application instructions. Keep PRs focused; describe behavior, link relevant issues, report checks and limitations, and include screenshots for UI changes.

## Security & Configuration

Never commit credentials, local Syndicate configuration (`.syndicate-config*/`), Terraform state/plans or `*.tfvars`, `.env*` files, or generated `environment.production.ts`. Deployment order is Terraform, backend, then frontend; follow the root `README.md` and verify target account and region before deployment.

All configuration targets AWS region `eu-north-1`, the region the project's AWS account works in. The project came from an archive written for other regions; the documentation has been aligned to `eu-north-1`. If you find another region anywhere, treat it as a leftover: point it out instead of copying it. `providers.tf` also declares an `aws.us_east_1` alias, but nothing uses it yet — it is reserved for a future ACM certificate.

`.syndicate-config-*/` (holds `syndicate.yml` and `syndicate_aliases.yml` with account ID, `project_path`, resource names) is machine-local. `account_id` and the Terraform state bucket are hardcoded defaults in `variables.tf` / `backend.tf`.

Deployment commands (`terraform apply`/`destroy`, `syndicate deploy`/`update`/`clean`, `aws s3 sync --delete`) act on whatever the local AWS profile, Syndicate config and Terraform variables point to. The committed configuration describes a single environment. Don't run them unless the user asks.
