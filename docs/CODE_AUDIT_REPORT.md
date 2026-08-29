# Complete Code Audit Report

> Scope: `src/` (.NET backend, Vue dashboard), `tests/`, `eng/`, `shell/`. Performed against the
> repository state at the time this report was written, covering architecture, coding style,
> comment standard, dead-code, and the three-dimensional vulnerability scan (network / low-level
> runtime / business logic).

## 1. Overall Code Quality Score (0-100)

**Score: 92 / 100**

Deductions:
- -4: Auth token/payload persisted in `localStorage` on the dashboard, which is readable by any
  script executing in the page context if an XSS bug is ever introduced elsewhere (see 3.1). This
  is the only remaining tracked item; it requires an auth-transport redesign out of scope for a
  surgical, low-risk fix (see rationale in 3.1).
- -2: Response headers previously carried no defense-in-depth hardening (no `X-Frame-Options`,
  `X-Content-Type-Options`, `Referrer-Policy`); **fixed in this PR** (see 3.1).
- -2: A few silently-swallowed exception branches existed without telemetry, relying only on
  inline comments to explain intent; **fixed in this PR** by adding debug-level structured
  logging that preserves the original fail-open behavior (see 2.3).

No glue code, no god classes/functions, no circular dependencies, and no floating package versions
were found. Path handling, authentication, and password storage already follow strong practices
(BCrypt with dummy-hash timing equalization, `CryptographicOperations.FixedTimeEquals`, realpath-based
symlink-safe path resolution, parameterized SQL). This is a comparatively mature codebase; findings
below are refinements rather than a rewrite mandate.

## 2. Code Design & Specification Issues

### 2.1 Architecture Design Defects

| # | Location | Issue | Remediation |
|---|----------|-------|--------------|
| A1 | `src/FortOS.Agent/Catalog/AgentCatalog.cs` (906 lines) | Large aggregate of catalog models/mappers in one file. Not a god *class* (mostly records/DTOs), but file size hampers navigation. | Split by concern into `AgentCatalog.Models.cs` / `AgentCatalog.Mapping.cs` partial files, or separate files per DTO group, in a follow-up refactor. Not urgent — no behavioral risk. |
| A2 | `src/FortOS.Core/Models/CoreModels.cs` (865 lines) | Same pattern: many unrelated DTOs aggregated in a single file. | Group by bounded context (Storage, Share, Network, ...) into separate files under `Models/`. |
| A3 | `src/FortOS.Installer.Core/Steps/ChrootStep.cs` (~564 lines) | Sizeable orchestration step, but each private method is small and single-purpose; acceptable given the sequential nature of an install step. | No action required; keep an eye on growth. |

No SRP/OCP/DIP violations, hardcoded singleton dependencies, or circular project references were
found (`FortOS.slnx` project graph is a clean DAG: Core → Platform/Security → Modules.* → Api).
Dependency injection is used consistently via `AddFortOS*` extension methods.

### 2.2 Coding Style & Comment Defects

| # | Location | Issue | Remediation |
|---|----------|-------|--------------|
| B1 | `src/FortOS.Api/Middleware/IdempotencyMiddleware.cs:89` (pre-fix) | Magic number `81920` for the read buffer size, undocumented. | **Fixed in this PR**: extracted to `RequestBodyCopyBufferBytes` constant. |
| B2 | `src/FortOS.Modules.Update/Services/OtaUpdateService.cs:45` (pre-fix) | Same magic buffer size `81920` duplicated. | **Fixed in this PR**: extracted to `DownloadCopyBufferBytes` constant. |
| B3 | `src/FortOS.Cli/Program.cs:27` (pre-fix) | Magic delay `1200` (ms) for banner display with only a one-line comment. | **Fixed in this PR**: extracted to `BannerDisplayDelayMilliseconds` constant. |
| B4 | `src/FortOS.Modules.Share/Services/FilePathResolver.cs:74` (pre-fix) | Magic `TimeoutSeconds = 5` for the `realpath` subprocess call. | **Fixed in this PR**: extracted to `RealpathTimeoutSeconds` constant. |

Naming throughout the codebase is semantic (no Pinyin, no single-letter identifiers found in
business logic); XML doc comments consistently describe intent rather than restating code. No
useless/redundant comments were found during sampling of the security, API, and module layers.

### 2.3 Redundant & Dead Code Defects

- No unused imports, unreachable branches, or large commented-out code blocks were found via
  repository-wide search.
- No `TODO`/`FIXME`/`HACK` markers or `#pragma warning disable` suppressions were found in `.cs`
  sources.
- Exception handling review:
  - `src/FortOS.Api/Grpc/ShareGrpcService.cs` and `src/FortOS.Api/Services/AiAssistantService.cs`
    each contain a `catch (JsonException) { /* comment */ }` used to skip a single malformed
    streamed event without aborting the whole stream. This is a deliberate, well-documented
    design choice (partial/heartbeat data is expected on those wire formats), not a bug. **Fixed
    in this PR**: both catch blocks now emit a debug-level structured log (`ILogger.LogDebug`)
    identifying the event being skipped, so the fail-open behavior remains but is now observable;
    the raw payload/SSE line content is intentionally not logged to avoid leaking event data.
  - `src/FortOS.Modules.Share/Services/FilePathResolver.cs` silently fell back to a normalized
    path when the `realpath` subprocess failed. **Fixed in this PR**: added a debug-level log
    call documenting the fallback and the path involved.
  - `src/FortOS.Api/Services/StartupOrchestrator.cs` logs a warning on failure and continues
    (graceful degradation by design); acceptable, no change needed.
  - No empty `catch {}` blocks or catch-all blocks with zero logging remain.

## 3. Classified Security Vulnerability List

### 3.1 Network Security Layer Vulnerabilities

| Severity | Location | Attack Principle | Fix Status / Recommendation |
|----------|----------|-------------------|------------------------------|
| Medium | `src/FortOS.Dashboard/src/stores/auth.ts` | JWT access token and payload are persisted in `localStorage`. Any future XSS vulnerability elsewhere in the SPA would let an attacker read `localStorage` synchronously and exfiltrate the token, achieving full account takeover without needing to defeat CSRF/token-replay protections. | Not changed in this PR (would require a broader auth-transport redesign to HttpOnly, `SameSite=Strict` cookies plus CSRF-token issuance, which is out of scope for a surgical fix and carries regression risk to the whole auth flow — there is no automated frontend test harness in this repository to validate such a change end-to-end). Recommended as a medium-term iteration: migrate token storage to an HttpOnly cookie set by the API, with a separate readable CSRF token for state-changing requests. **Partial mitigation applied in this PR**: added `SecurityHeadersMiddleware` (see next row) as defense-in-depth to reduce the likelihood/impact of the XSS precondition this finding depends on. |
| Low | Missing HTTP security headers | No response carried `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, or `Permissions-Policy`, leaving the dashboard without baseline clickjacking/MIME-sniffing protection. | **Fixed in this PR**: added `src/FortOS.Api/Middleware/SecurityHeadersMiddleware.cs`, registered first in the pipeline, setting `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: same-origin`, and a restrictive `Permissions-Policy`. Covered by a new integration test (`ApiGatewayTests.AnyResponse_IncludesSecurityHeaders`). A strict `Content-Security-Policy` was intentionally not added in this pass: the dashboard has no frontend test harness to verify it would not break the Vite/Naive-UI bundle (e.g. its inline `<style>` FOUC guard), so it is called out as a follow-up requiring frontend verification rather than risked here. |
| — | CORS | `builder.Services.AddCors` explicitly restricts to `allowedOrigins` (no `AllowAnyOrigin`); confirmed no wildcard origin. | No action needed. |
| — | Transport | No plaintext transmission of credentials found; `NasTokenMiddleware` reads bearer tokens from the `Authorization` header, not cookies, and TLS termination is expected at the reverse-proxy/hosting layer per `docker-compose.yml`. | No action needed. |
| — | Replay/CSRF | `IdempotencyMiddleware` (Idempotency-Key + request fingerprint) and `RateLimitMiddleware` already provide replay and abuse mitigations for state-changing requests. | No action needed. |
| — | File upload/path traversal | `FilePathResolver`/`PathSafety` canonicalize via `realpath` before any allowed-root check, closing the TOCTOU/symlink-escape window that a naive string-prefix check would miss. `UploadSessionService` and `RecycleBinService` route through this same resolver. | No action needed. |

### 3.2 Low-Level Code Layer Vulnerabilities

| Severity | Location | Attack Principle | Fix Status / Recommendation |
|----------|----------|-------------------|------------------------------|
| — | `src/FortOS.Security/Services/IdentityService.cs:349` | `HMACSHA1` is used, but only as the HMAC primitive for RFC 6238/4226 TOTP code generation, which mandates SHA-1 for algorithm compatibility with standard authenticator apps. This is not a password/signature hash and is not weakened by SHA-1's collision properties (HMAC-SHA1 remains unbroken as a MAC). | No action needed; flagged and cleared as a false positive during audit. |
| — | Password storage | `BCrypt.Net.BCrypt.HashPassword(password, 12)` used consistently for all password storage (`IdentityService.cs`, `ChrootStep.cs`), with a fixed dummy-hash comparison to equalize timing on unknown-user login. | No action needed — meets best practice. |
| — | Randomness | No `System.Random` usage found for tokens/session identifiers; `Guid.NewGuid()` usages found are for non-security event/correlation IDs only. | No action needed. |
| — | Deserialization | No `BinaryFormatter`, unchecked `XmlSerializer`, or unrestricted `JsonConvert.DeserializeObject` usage found; the codebase uses `System.Text.Json` with typed deserialization throughout. | No action needed. |
| — | Dependencies | All NuGet package references are pinned to explicit versions (no floating/wildcard versions). `FortOS.Installer.Core.csproj` and `FortOS.Core.csproj` explicitly pin `SQLitePCLRaw.bundle_e_sqlite3` to `3.0.4` with an inline comment documenting the CVE (`GHSA-2m69-gcr7-jv3q`) being avoided. | No action needed. |
| Low | `src/FortOS.Api/Middleware/IdempotencyMiddleware.cs`, `src/FortOS.Modules.Update/Services/OtaUpdateService.cs` | Hardcoded `81920`-byte copy buffers appeared coincidentally identical in two unrelated files with no named constant, making it unclear whether the sizing was an intentional shared decision or two independent choices that happened to match. | **Fixed in this PR** — each class now owns its own named constant (`RequestBodyCopyBufferBytes`, `DownloadCopyBufferBytes`). The two classes have unrelated responsibilities (HTTP request-body hashing vs. update-package download streaming), so independent per-class constants are intentional; they are not meant to share a single value, and either can be tuned independently in the future. |
| Medium | `src/FortOS.Modules.Share/Services/FilePathResolver.cs` (`ResolveRealPathAsync`) | Log forging: the newly-added debug log for the `realpath` fallback path wrote the caller-supplied path directly into the log message. `RecycleController` calls `ResolveRealPathAsync` with a decoded, attacker-influenced identifier before any newline-stripping validation runs, so a crafted path containing CR/LF could forge additional fake log lines. Found by CodeQL (`cs/log-forging`) during this PR's own review, not present before this PR's logging addition. | **Fixed in this PR** — added a `SanitizeForLog` helper that replaces control characters (including CR/LF) with `_` before the value is passed to `ILogger`, applied at the one call site that logs a caller-supplied path. |

### 3.3 Business Logic Layer Vulnerabilities

| Severity | Location | Attack Principle | Fix Status / Recommendation |
|----------|----------|-------------------|------------------------------|
| — | Authentication/authorization | Controllers rely on a global `CapabilityAuthorizationFilter` + `CapabilityConvention` (registered in `Program.cs`) plus `NasTokenMiddleware`/`GrpcAuthorizationInterceptor`, rather than per-controller `[Authorize]` attributes. Verified this filter is registered globally for all controllers/gRPC services, so there is no route left unauthenticated by omission. | No action needed; documented here to avoid a future false-positive re-flag of "missing `[Authorize]`". |
| — | Brute force / lockout | `IdentityService` tracks `FailedAttempts`/`LockedUntil` per user and equalizes timing for unknown users. | No action needed. |
| — | TOTP replay window | `VerifyTotp` checks a ±1 time-step window (RFC 6238 standard tolerance) — bounded, not an unbounded replay window. | No action needed. |
| — | Overflow / idempotency | Update/backup/upload flows validate declared vs. actual byte counts (`MaxPackageBytes` dual-checked against header and streamed count) and use `IdempotencyMiddleware` for state-changing requests. | No action needed. |

## 4. Overall Refactoring & Optimization Suggestions

**Emergency Fixes** (none identified — no critical/high vulnerabilities found).

**Medium-Term Iteration Optimization**
- Migrate dashboard auth-token storage from `localStorage` to an HttpOnly cookie + CSRF-token
  pair to remove the token-theft-via-XSS blast radius described in 3.1.
- Extract the remaining large DTO-aggregation files (`AgentCatalog.cs`, `CoreModels.cs`) into
  per-bounded-context files to ease navigation as the catalog grows.

**Long-Term Architecture Adjustment**
- Add a frontend test harness (component/e2e) to the dashboard so a strict `Content-Security-Policy`
  can be introduced and verified safely, further hardening the localStorage-token exposure.

## 5. Qualified Code Acceptance Standard Summary

| Requirement | Status |
|-------------|--------|
| 1. No glue-style stacked logic; clear layered separation, single responsibility, no oversized monolithic functions | **Met** — clean project/module layering, no function exceeded ~40 lines in sampling. |
| 2. Standard semantic naming; valid comments for all critical logic/inputs/exceptions; no useless redundant comments | **Met** — see 2.2. |
| 3. No unreachable dead code, no excessive global state; complete exception capture with persistent log records | **Met** — best-effort/degrade-gracefully paths now emit debug-level logs in addition to their documenting comments (see 2.3). |
| 4. Zero critical/high-risk vulnerabilities in network, runtime, and business logic layers; strict backend validation of all external input | **Met** — no critical/high findings; one **Medium** finding (localStorage token storage) tracked as a recommendation, not blocking, since it requires an auth-transport redesign outside this PR's minimal-change scope. |
| 5. All magic numbers/hardcoded static strings extracted into unified constants | **Met** after this PR's fixes (see 2.2/3.2). |

**Overall verdict:** the codebase is **qualified**, with one tracked medium-severity
recommendation (auth token storage) for a follow-up iteration.
