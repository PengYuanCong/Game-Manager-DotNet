# Portfolio Finalization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add repeatable tests, an offline ARAM AI quality dataset, CI security gates, and portfolio evidence, then verify and push the completed work to `main`.

**Architecture:** Keep production behavior unchanged. Add a focused xUnit project for deterministic domain and MVC security conventions, a JSON dataset plus PowerShell validator for AI quality contracts, and a GitHub Actions workflow that composes the repository's existing validation scripts.

**Tech Stack:** .NET 10, xUnit, ASP.NET Core MVC reflection tests, PowerShell, GitHub Actions, JSON.

---

### Task 1: Add the test project

**Files:**
- Create: `Proposal.Tests/Proposal.Tests.csproj`
- Create: `Proposal.Tests/AiRecommendationCacheKeyTests.cs`
- Create: `Proposal.Tests/Pbkdf2PasswordHashServiceTests.cs`
- Create: `Proposal.Tests/LolAramAugmentTagNormalizerTests.cs`
- Create: `Proposal.Tests/MvcSecurityConventionTests.cs`
- Modify: `Proposal.slnx`

- [ ] Create the xUnit project with `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `coverlet.collector`.
- [ ] Add the project to `Proposal.slnx` and reference `Proposal/Proposal.csproj`.
- [ ] Write cache-key tests for trimming, case normalization, cache scope separation, input changes, and 64-character lowercase hexadecimal output.
- [ ] Run the cache-key test filter and confirm the intentionally incorrect first assertion fails.
- [ ] Correct the assertion and confirm the filter passes.
- [ ] Write password tests for random salts, valid login, invalid login, legacy password upgrade, malformed hashes, and blank stored values.
- [ ] Run the password test filter and confirm an intentionally incorrect first assertion fails.
- [ ] Correct the assertion and confirm the filter passes.
- [ ] Write tag normalizer tests for series aliases, simplified/traditional aliases, deduplication, effect inference, unknown tags, and blank input.
- [ ] Run the normalizer test filter and confirm an intentionally incorrect first assertion fails.
- [ ] Correct the assertion and confirm the filter passes.
- [ ] Write MVC security convention tests that require authentication on protected controllers, admin role on equipment CRUD, and antiforgery on every `HttpPost` action.
- [ ] Run the MVC test filter and confirm an intentionally incomplete protected-controller list fails.
- [ ] Complete the list and confirm the filter passes.

### Task 2: Add the offline AI quality contract

**Files:**
- Create: `evaluation/aram-recommendation-cases.json`
- Create: `Tools/ValidateAiEvaluationDataset.ps1`
- Create: `Proposal.Tests/AiEvaluationDatasetTests.cs`

- [ ] Write a failing dataset test that expects exactly 30 unique cases before the dataset exists.
- [ ] Run the dataset test and verify it fails because the file is missing.
- [ ] Add 30 cases covering AP, AD, tank, fighter, support, mixed damage, all four choice stages, prior augments, forbidden content, and Traditional Chinese item names.
- [ ] Add typed test-side records and assertions for IDs, champion, stage, three offered augments, allowed item pools, forbidden terms, and required concepts.
- [ ] Run the dataset test and fix data errors until it passes.
- [ ] Add a PowerShell validator that performs the same portable checks without requiring the test runtime.
- [ ] Run `powershell -ExecutionPolicy Bypass -File Tools/ValidateAiEvaluationDataset.ps1` and require `AI_EVALUATION_DATASET_STATUS=PASS`.

### Task 3: Add continuous integration

**Files:**
- Create: `.github/workflows/ci.yml`

- [ ] Configure `push` and `pull_request` triggers for `main`.
- [ ] Grant only `contents: read`.
- [ ] Pin `actions/checkout` and `actions/setup-dotnet` to major stable versions.
- [ ] Restore, build Release with warnings as errors disabled only where the project already permits warnings, and run tests with TRX output.
- [ ] Run the AI dataset validator, Supabase contract, architecture check, and `CloudReadinessCheck.ps1 -SkipNetworkChecks`.
- [ ] Run `dotnet list Proposal.slnx package --vulnerable --include-transitive` and fail when vulnerable packages are reported.
- [ ] Validate workflow syntax by parsing the file shape and inspecting all commands locally.

### Task 4: Document portfolio quality evidence

**Files:**
- Modify: `README.md`
- Create: `docs/INTERVIEW_DEMO.md`

- [ ] Add a CI badge linked to the workflow.
- [ ] Add a concise quality section listing unit tests, MVC security conventions, the 30-case AI quality contract, schema checks, secret scanning, and smoke tests.
- [ ] Explain that the dataset is an offline contract and does not claim live-model accuracy.
- [ ] Add a three-minute interview script covering the user problem, recommendation flow, RAG evidence, player feedback, and deployment security.
- [ ] Check README links and referenced local files.

### Task 5: Run the completion audit

**Files:**
- Modify only if verification exposes a defect.

- [ ] Run `dotnet restore Proposal.slnx`.
- [ ] Run `dotnet build Proposal.slnx -c Release --no-restore`.
- [ ] Run `dotnet test Proposal.slnx -c Release --no-build`.
- [ ] Run `Tools/ValidateAiEvaluationDataset.ps1`.
- [ ] Run `Tools/SupabaseContractCheck.ps1`.
- [ ] Run `Tools/ArchitectureDependencyCheck.ps1`.
- [ ] Run `Tools/CloudReadinessCheck.ps1 -SkipNetworkChecks`.
- [ ] Run NuGet vulnerability scanning and confirm zero vulnerable packages.
- [ ] Run `Tools/RunLocalSmoke.ps1` on an unused port.
- [ ] Run cloud preview smoke against the configured Render URL.
- [ ] Inspect `git diff --check`, secret-sensitive patterns, and final status.

### Task 6: Commit and publish main

**Files:**
- Commit all validated files from Tasks 1-5.

- [ ] Confirm the current branch is `main` and the worktree contains only intended changes.
- [ ] Commit with a portfolio-finalization message.
- [ ] Pull remote references and verify `origin/main` has not diverged.
- [ ] Push `main`.
- [ ] Verify `git ls-remote origin refs/heads/main` matches local `HEAD`.
- [ ] Re-open the GitHub README or raw README and verify the new quality section is present.

