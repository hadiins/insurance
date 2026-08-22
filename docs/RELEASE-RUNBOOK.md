# Release Runbook

docs/TASKS.md Task 23's own check: publish a version and apply it to a staging environment,
complete, from build to panel. This is that procedure.

## Prerequisites

- `openssl` (signing)
- `jq` and `curl` (registration script)
- `docker` (build/push — only needed on the machine actually building images, e.g. your CI runner)
- The signing keypair (see below) and a Platform.Owner login on the target environment

## One-time: generate the signing keypair

```bash
scripts/release/generate-signing-key.sh ./secrets
```

Produces `release-signing-key.pem` (private — put it in your CI secret store, never commit it) and
`release-signing-key.pub.pem` (public — this becomes `UPDATER_SIGNING_PUBLIC_KEY_PEM` in every
environment's `.env`, staging and production alike). Losing the private key means every future
release needs a **new** keypair and every environment's `UPDATER_SIGNING_PUBLIC_KEY_PEM` re-set —
plan for that possibility (a second, offline backup copy) accordingly.

## Per-release

### 1. Tests pass

```bash
dotnet test tests/Aqsat.UnitTests/Aqsat.UnitTests.csproj
```

Don't sign a build that hasn't. This isn't optional gate-keeping for its own sake: the signature is
what makes `Aqsat.Updater` trust a package sight-unseen (docs/UPDATE-SYSTEM.md rule 3) — it says
nothing about whether the package is actually *good*, only that it's actually *yours*.

### 2. Build and push the image

```bash
VERSION=1.5.0
IMAGE_TAG="your-registry.ir/aqsat-api:$VERSION"

docker build -f src/Aqsat.Api/Dockerfile -t "$IMAGE_TAG" .
docker push "$IMAGE_TAG"
SHA256=$(docker inspect --format='{{index .RepoDigests 0}}' "$IMAGE_TAG" | sed 's/^[^@]*@//')
```

Registry must be reachable from wherever `Aqsat.Updater` runs (CLAUDE.md: data — and by extension
the images serving it — stays in Iran; use an Iranian-hosted registry, not a foreign one blocked or
throttled at exactly the moment you need to pull a fix).

### 3. Sign

```bash
scripts/release/sign-package.sh "$VERSION" "$IMAGE_TAG" "$SHA256" ./secrets/release-signing-key.pem \
  > manifest.json
```

`manifest.json` now holds `{version, imageTag, sha256, signatureBase64}` — this is the exact shape
`Aqsat.Updater` and `Aqsat.Api` both verify before trusting anything about the release.

### 4. Register with the target environment

```bash
TOKEN=$(curl -sf -X POST "$API_BASE/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "{\"mobile\":\"$PLATFORM_OWNER_MOBILE\",\"password\":\"$PLATFORM_OWNER_PASSWORD\"}" | jq -r .token)

scripts/release/register-package.sh "$API_BASE" "$TOKEN" manifest.json \
  "شرح فارسی تغییرات این نسخه" \
  "" \
  --db-migration   # only if this release actually changes the schema; omit otherwise
```

`register-package.sh` posts to `POST /api/platform/updates/register`, which independently verifies
the signature server-side (`IPackageSignatureVerifier` in `Aqsat.Api` — a **second**, separately
maintained check from the one `Aqsat.Updater` runs at apply time) before writing the `UpdatePackage`
row. A badly signed or tampered manifest never even reaches the catalog a Platform.Owner sees.

### 5. Staged rollout

docs/UPDATE-SYSTEM.md §8's staged-rollout guidance ("staging → 1 volunteer agency → 10% → everyone")
assumes a multi-server SaaS topology. This system's actual deployment shape
(`docker-compose.prod.yml`) is one stack per customer/server group serving every agency on it via
Row-Level Security — there is no "10% of agencies" within a single running container, only "10% of
your deployed stacks." The staging equivalent:

1. **Staging environment first, always.** A `docker-compose.prod.yml` stack pointed at a disposable
   database, registered with the same signing key, applied there before anywhere real.
2. **One volunteer production server next**, if you run more than one. A defective release only
   takes down that one customer's stack, not everyone simultaneously — exactly the failure
   docs/UPDATE-SYSTEM.md §8 warns a same-server rollout risks.
3. **The rest**, once the volunteer server has run the new version through a real day's traffic.

Applying a registered package to any given environment is what the update panel itself does — see
`docs/UPDATE-SYSTEM.md` §5 and the "به‌روزرسانی سیستم" page. There's no separate "deploy" step
beyond what's already documented there: a Platform.Owner logs into that environment, sees the new
version in the panel, and applies it (OTP required).

### 6. Yank, if it turns out broken

```bash
curl -sf -X POST "$API_BASE/api/platform/updates/packages/$PACKAGE_ID/yank" \
  -H "Authorization: Bearer $TOKEN"
```

Or the "لغو این نسخه" button next to the package in the panel. Never deletes the row — an
`UpdateRun` that already applied it still needs a real package to point at, and "this version
existed and was pulled" is itself worth keeping.

## What this doesn't automate

- **The actual `docker build`/`docker push`** — deliberately plain, ordinary Docker commands rather
  than a wrapper script; nothing about them is specific to this system.
- **CI/CD wiring** (GitHub Actions, etc.) — the scripts above are meant to be called *from* whatever
  pipeline you already run tests in, not to replace it.
- **Cross-environment orchestration** ("apply to all servers automatically") — a Platform.Owner
  applies each environment's update individually and deliberately, per docs/UPDATE-SYSTEM.md rule 8
  (out-of-hours windows) and the explicit "no automatic update without a human confirming" rule in
  §9.
