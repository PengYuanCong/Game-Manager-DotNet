# Supabase Data Migrator

This tool copies allowlisted application tables from local SQL Server to a
Supabase/PostgreSQL staging database.

It is intentionally conservative:

- Default mode is dry-run row counting only.
- It writes only when `--apply` is passed.
- Apply mode also requires `--confirm-staging` or
  `MIGRATION_CONFIRM_STAGING=true`.
- Apply mode writes inside one PostgreSQL transaction and rolls back on copy
  failure.
- Dry-run and apply both verify that the target Supabase schema exists, has the
  mapped columns, and has RLS enabled before writing.
- It logs table names and counts, not connection strings or secrets.
- It should be used against staging before production.

## Dry Run

```powershell
$env:MIGRATION_SQLSERVER_CONNECTION='Data Source=...;Initial Catalog=LOL;Integrated Security=True;Encrypt=True;TrustServerCertificate=True'
$env:MIGRATION_POSTGRES_CONNECTION='Host=...;Database=postgres;Username=postgres;Password=<postgres-password>;SSL Mode=Require;Trust Server Certificate=true'
powershell -ExecutionPolicy Bypass -File Tools\RunSupabaseStagingMigration.ps1
```

If the dry run reports that every `public.*` target table is missing, initialize
the staging schema once. This command requires explicit confirmation, executes
the checked-in `database/supabase/0001_schema.sql`, verifies every table and
RLS setting, then continues with the normal dry run:

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunSupabaseStagingMigration.ps1 -InitializeSchema -ConfirmStaging
```

## Apply To Staging

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunSupabaseStagingMigration.ps1 -Apply -ConfirmStaging
```

To limit the scope:

```powershell
powershell -ExecutionPolicy Bypass -File Tools\RunSupabaseStagingMigration.ps1 -Apply -ConfirmStaging -Tables users,equipments,lol_aram_guides,lol_aram_augments
```

Recommended order:

1. Run
   `Tools\RunSupabaseStagingMigration.ps1 -InitializeSchema -ConfirmStaging`
   once, or execute `database/supabase/0001_schema.sql` in the Supabase SQL
   editor.
2. Dry-run this tool and inspect row counts, target row counts, missing source
   columns, target schema status, and RLS status.
3. Apply this tool to staging with `--apply --confirm-staging`.
4. Run `database/supabase/0002_seed_aram_starter_data.sql` only when staging is
   intentionally empty/demo-like.
