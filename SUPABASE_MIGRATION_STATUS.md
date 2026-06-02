# Supabase Migration Status

Last updated: 2026-05-26

## Current State

The app still runs on SQL Server by default. This is intentional. PostgreSQL
can now be selected with `Database:Provider=Postgres`, `PostgreSQL`, or
`Supabase` after a Supabase staging database has been prepared.

Supabase/PostgreSQL staging artifacts now exist under `database/supabase`:

- `0001_schema.sql`
- `0002_seed_aram_starter_data.sql`
- `README.md`
- `Tools/SupabaseDataMigrator`

These files prepare a Supabase staging database, but they do not switch the
runtime provider or production connection string.

## Completed Migration Prep

- Controllers are being moved away from direct `SqlConnection` usage.
- Shared SQL Server connection creation exists in `ISqlConnectionFactory`.
- Npgsql 10.0.2 is now referenced by the MVC project.
- Server-side PostgreSQL connection creation exists in `IPostgresConnectionFactory`.
- PostgreSQL repositories/services now exist for app users, calculation history,
  user activity logs, AI recommendation cache, and AI recommendation favorites.
- PostgreSQL repositories/services now also exist for equipment, equipment
  loadouts, calculator data, ARAM champion guides, ARAM augments, and the AI RAG
  knowledge context.
- `Program.cs` now switches repository/service registrations by
  `Database:Provider` while keeping SQL Server as the safe default.
- Admin create/edit duplicate-key handling now recognizes both SQL Server and
  PostgreSQL unique-constraint exceptions.
- AI recommendation cache/favorites now use bounded payloads and full SHA-256
  cache keys.
- User activity logs store only local relative links.
- ARAM guide, augment, and RAG knowledge services use centralized connection
  creation and bounded text inputs.
- PostgreSQL schema now covers users, activity logs, calculation history,
  equipment, loadouts, AI recommendation cache/favorites, ARAM guides,
  augment series, augments, items, and synergy rules.
- `Tools/SupabaseDataMigrator` can dry-run source row counts and copy allowlisted
  tables from SQL Server to Supabase staging only when `--apply` is passed.
- The migrator now validates required vs optional SQL Server source columns
  before copy so older local schemas fail early instead of half-copying data.
- The migrator now also validates target Supabase table/column/RLS readiness
  before writing, requires `--confirm-staging` or
  `MIGRATION_CONFIRM_STAGING=true` with `--apply`, and wraps apply mode in a
  PostgreSQL transaction that rolls back on copy failure.
- `Tools/SupabaseContractCheck.ps1` now validates required Supabase tables,
  RLS enablement, and PostgreSQL repository table/common column references
  before cloud deployment checks pass.
- `Tools\ArchitectureDependencyCheck.ps1` and `Tools\AzurePreflightCheck.ps1`
  are now part of the pre-cloud gate, covering UI/server data-access boundaries,
  client secret exposure checks, production security markers, Docker port
  alignment, `WEBSITES_PORT=8080`, and empty production admin fallback.
- `render.yaml` and `deployment.env.example` now use `Database__Provider=Supabase`
  for cloud staging, while the app still defaults to SQL Server when the setting
  is omitted.
- Production forwarded headers are enabled so Render/Azure reverse-proxy HTTPS
  information is honored before HTTPS redirection.

## Security Decisions

- Supabase credentials must stay server-side only.
- Never expose the database URL, database password, or `service_role` key in
  Razor, JavaScript, static files, localStorage, Git, or screenshots.
- RLS is enabled in the schema, but no public policies are created yet.
- The current MVC app should use a server-side database connection until a
  Supabase Auth/RLS mapping is intentionally designed.

## Next Work

1. Run `0001_schema.sql` in a Supabase staging database.
2. Dry-run `Tools/SupabaseDataMigrator`, then apply it to staging with
   `--apply --confirm-staging` if source/target row counts, target schema, and
   RLS checks look correct.
3. Run `Tools\SupabaseContractCheck.ps1`,
   `Tools\ArchitectureDependencyCheck.ps1`, and
   `Tools\AzurePreflightCheck.ps1` after schema, repository, security, or
   deployment setting changes.
4. Run app smoke tests against staging Supabase with `Database:Provider=Supabase`.
5. Run Docker/Render smoke tests against staging Supabase.
6. Complete final code review, secret scan, and Azure readiness check.
