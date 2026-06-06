# Supabase Password Migration Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent legacy plaintext account passwords from being copied to Supabase by upgrading them to salted PBKDF2 during migration.

**Architecture:** Add a focused password normalizer inside the migration tool. The users table mapping invokes it only for `password_hash`; valid current PBKDF2 strings remain byte-for-byte unchanged, non-empty legacy values receive a new random salt and PBKDF2 hash, and missing values abort migration before writes.

**Tech Stack:** .NET 10, C#, `Rfc2898DeriveBytes.Pbkdf2`, PowerShell regression tests, SQL Server, PostgreSQL/Supabase.

---

### Task 1: Password normalization regression test

**Files:**
- Create: `Tools/SupabaseDataMigrator/PasswordMigrationNormalizer.cs`
- Create: `Tools/SupabaseDataMigrator/PasswordMigrationNormalizerTest.cs`
- Modify: `Tools/SupabaseDataMigrator/Program.cs`

- [x] Add an internal self-test mode that verifies a current PBKDF2 value is preserved exactly.
- [x] Verify two normalizations of the same legacy value produce valid but different salted PBKDF2 strings.
- [x] Verify both generated hashes validate against the original legacy password.
- [x] Verify null, empty, and whitespace-only values are rejected.
- [x] Run the self-test before implementation and confirm it fails because the normalizer does not exist.

### Task 2: Minimal password normalizer

**Files:**
- Create: `Tools/SupabaseDataMigrator/PasswordMigrationNormalizer.cs`
- Modify: `Tools/SupabaseDataMigrator/Program.cs`

- [x] Implement PBKDF2-SHA256 with 100,000 iterations, a 16-byte random salt, and a 32-byte derived key.
- [x] Preserve strings matching the validated four-part `pbkdf2-sha256` format.
- [x] Reject empty values with `InvalidOperationException`.
- [x] Run the self-test and require `SUPABASE_PASSWORD_MIGRATION_TEST=PASS`.

### Task 3: Integrate users migration

**Files:**
- Modify: `Tools/SupabaseDataMigrator/Program.cs`

- [x] Add an optional value normalizer to `ColumnMap`.
- [x] Attach the password normalizer only to `Users.Password -> app_users.password_hash`.
- [x] Count legacy password upgrades during preflight without logging values.
- [x] Print only `LEGACY_PASSWORDS_TO_UPGRADE=<count>`.
- [x] Apply normalization immediately before parameter binding inside the existing PostgreSQL transaction.

### Task 4: Verification and delivery

**Files:**
- Modify: `Tools/CloudReadinessCheck.ps1`
- Modify: `Tools/SupabaseDataMigrator/README.md`

- [x] Add the password migration self-test to the cloud readiness gate.
- [ ] Run the real dry run and confirm 575 source rows, 12 `PREFLIGHT_OK` tables, and one legacy password upgrade.
- [x] Run build, schema/RLS contract, architecture, secret scan, publish, and NuGet vulnerability checks.
- [ ] Commit and push the verified change before giving the staging Apply command.
