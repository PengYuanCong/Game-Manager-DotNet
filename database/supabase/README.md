# Supabase Migration SQL

These files are staging-first migration artifacts for the ARAM Mayhem MVC app.
They do not switch the running app away from SQL Server by themselves.

## Order

1. Back up SQL Server `LOL`.
2. Create a Supabase staging project.
3. Initialize the staging schema with
   `Tools\RunSupabaseStagingMigration.ps1 -InitializeSchema -ConfirmStaging`.
   This executes `0001_schema.sql`, verifies all expected tables and RLS, and
   continues with a dry run. The Supabase SQL editor remains a manual fallback.
4. Run `Tools\RunSupabaseStagingMigration.ps1` in dry-run mode and confirm
   source row counts, target row counts, target schema, RLS status, and the
   legacy-password upgrade count look reasonable. Legacy account passwords are
   converted to salted PBKDF2 during copy and are never printed.
5. Run `Tools\RunSupabaseStagingMigration.ps1 -Apply -ConfirmStaging` against
   staging only. Apply mode writes inside a PostgreSQL transaction and rolls
   back on copy failure.
6. Run `0002_seed_aram_starter_data.sql` only if the staging database has no
   real ARAM data yet. Do not seed over curated production-like data unless you
   intentionally want the starter rows to update matching keys.
7. Set the app to `Database__Provider=Supabase` and smoke test login, AI
   recommendation, heroes, augments, equipment, calculator, and profile pages.
8. Run `Tools\CloudReadinessCheck.ps1` before Render or Azure deployment. The
   script does not apply migrations; it only checks build, publish, config,
   antiforgery coverage, Supabase schema/repository contracts, UI/server data
   access boundaries, Azure container preflight, high-confidence secret leaks,
   and package vulnerabilities.
9. Run `Tools\RunLocalSmoke.ps1 -Port 5226` for a local no-write smoke test.
   It starts a temporary app instance, checks `/healthz`, public pages,
   unauthenticated redirects, exception-page markers, and security headers,
   then stops the temporary app.
   Use `Tools\RunLocalSmoke.ps1 -Port 5226 -Gate CloudPreview` to exercise the
   cloud preview wrapper locally without requiring an external URL.
10. After a Render/Azure preview URL exists and Supabase secrets are configured
    on the platform, run `Tools\RunCloudPreviewSmoke.ps1 -Target Render
    -BaseUrl <preview-url>` or `Tools\RunCloudPreviewSmoke.ps1 -Target Azure
    -BaseUrl <preview-url>`. It verifies public pages, unauthenticated
    redirects, exception-page markers, baseline security headers, `/healthz`,
    and `/readyz` with `databaseProvider=Supabase`.
11. Run `Tools\DeploymentCompletionAudit.ps1 -RunRenderPreviewSmoke` as the
    final evidence gate. It reports `READY` only when local readiness, local
    smoke, Supabase apply evidence, and Render preview smoke are all proven.
    The audit also writes a secret-free status report to
    `Reports\deployment-completion-last.json`.

## Security Notes

- Keep Supabase database passwords and `service_role` keys only in server-side
  environment variables.
- Do not expose the direct database URL, database password, or `service_role`
  key in Razor views, JavaScript, static files, Git, or browser localStorage.
- The schema enables RLS and intentionally creates no public policies yet.
  The current MVC app should use a server-side PostgreSQL connection. Browser
  access through Supabase REST/JS needs a separate Supabase Auth/RLS design.
- `user_activity_logs.link_url` accepts only local relative URLs beginning with
  `/`, matching the app-side validation.

## Naming

The SQL Server runtime tables use names such as `LolAramGuides` and
`AiRecommendationCache`. The Supabase schema uses snake_case table and column
names. PostgreSQL repository implementations should map the existing C# models
to this schema instead of exposing quoted PascalCase identifiers.

## Migration Tool

Use `Tools\RunSupabaseStagingMigration.ps1` for SQL Server to Supabase staging
copies. It runs the schema contract check first, is dry-run by default, and
writes only with `-Apply -ConfirmStaging`.
Successful dry-run/apply writes a secret-free evidence report to
`Reports\supabase-staging-migration-last.json`; final completion requires this
report to show `APPLY_PASS`.

## Contract Check

`Tools/SupabaseContractCheck.ps1` is a static guard for migration drift. It
parses `0001_schema.sql`, confirms the required tables have RLS enabled, and
checks the `Postgres*.cs` repositories for references to missing Supabase tables
or common schema columns.

Run it directly when changing PostgreSQL repositories or schema:

```powershell
powershell -ExecutionPolicy Bypass -File Tools\SupabaseContractCheck.ps1
```

`Tools\ArchitectureDependencyCheck.ps1` verifies that controllers, Razor views,
and client assets do not directly open database connections or expose server
secrets. `Tools\AzurePreflightCheck.ps1` verifies the Docker/Azure settings,
including `WEBSITES_PORT=8080`, production mode, health/readiness endpoints,
and no production fallback admin user.
