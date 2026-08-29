#!/usr/bin/env bash
# docs/UPDATE-TEST-RUNBOOK.md — one-command release for a single-server (Ubuntu) deployment.
# Does, in order, what .github/workflows/release.yml does on CI (build → push → sign → register),
# but entirely on this machine and against a LOCAL container registry on 127.0.0.1 — no external
# registry, no CI secrets. docs/RELEASE-RUNBOOK.md remains the authoritative procedure; this
# script just removes the toil for the one-server topology docker-compose.prod.yml actually ships.
#
# Why a local registry at all: Aqsat.Updater pulls the package's image via the Docker Engine API
# (POST /images/create), and the DAEMON executes that pull — so the tag must resolve from a
# registry the host daemon can reach. 127.0.0.0/8 is an implicitly-insecure registry range for
# dockerd, so plain HTTP on loopback needs no TLS configuration. And because the pull runs
# daemon-side on the host, 127.0.0.1:5000 works even though the updater itself sits in a container.
#
# Prerequisites on this machine: docker, openssl, jq, curl — all standard on Ubuntu.
# Sibling release scripts are invoked via `bash scripts/release/…` on purpose: git stores them
# non-executable (100644), so a direct ./name.sh would hit "Permission denied" on a fresh clone.
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

registry="127.0.0.1:5000"
api_base="http://127.0.0.1:8080"
version=""
notes=""
minimum_from=""
db_migration=false
security=false
image_only=false
owner_mobile="${PLATFORM_OWNER_MOBILE:-}"
owner_password="${PLATFORM_OWNER_PASSWORD:-}"

usage() {
  cat >&2 <<'USAGE'
Usage: publish-local.sh --version X.Y.Z [--notes "..."] [options]

  --version X.Y.Z        release version (required, semver) — also stamped into the image
  --notes "..."          Persian release notes shown in the panel (required unless --image-only)
  --api-base URL         running Aqsat.Api base URL (default: http://127.0.0.1:8080)
  --registry HOST:PORT   registry address for the image tag (default: 127.0.0.1:5000)
  --minimum-from X.Y.Z   oldest version this package may upgrade (optional version gate)
  --db-migration         this release changes the schema (migration badge in the panel)
  --security             marks the package as a security update
  --image-only           build + push + sign only, skip API registration (for the first 1.0.0 deploy)
  --owner-mobile M / --owner-password P   Platform.Owner login (default: $PLATFORM_OWNER_MOBILE /
                         $PLATFORM_OWNER_PASSWORD env vars)
USAGE
  exit 1
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="${2:?}"; shift 2 ;;
    --notes) notes="${2:?}"; shift 2 ;;
    --api-base) api_base="${2:?}"; shift 2 ;;
    --registry) registry="${2:?}"; shift 2 ;;
    --minimum-from) minimum_from="${2:?}"; shift 2 ;;
    --db-migration) db_migration=true; shift ;;
    --security) security=true; shift ;;
    --image-only) image_only=true; shift ;;
    --owner-mobile) owner_mobile="${2:?}"; shift 2 ;;
    --owner-password) owner_password="${2:?}"; shift 2 ;;
    *) echo "Unknown option: $1" >&2; usage ;;
  esac
done

# Same strict validation release.yml applies to GITHUB_REF_NAME — this value ends up inside the
# signed payload and the image tag, so no free-form text, ever.
[[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "ERROR: --version must be X.Y.Z semver (got '$version')" >&2; usage; }
$image_only || [[ -n "$notes" ]] || { echo "ERROR: --notes is required (Persian changelog shown in the panel)" >&2; usage; }

for tool in docker openssl jq curl; do
  command -v "$tool" >/dev/null 2>&1 || { echo "ERROR: '$tool' is not installed on this machine." >&2; exit 1; }
done

echo "==> [1/6] Release signing keypair"
private_key="secrets/release-signing-key.pem"
public_key="secrets/release-signing-key.pub.pem"
mkdir -p secrets
if [[ ! -f "$private_key" ]]; then
  bash scripts/release/generate-signing-key.sh secrets
  echo
  echo "    NEW keypair generated at secrets/."
  echo "    Put the PUBLIC key into .env as a quoted multi-line value (compose v2 parses it):"
  echo "      printf '\\nUPDATER_SIGNING_PUBLIC_KEY_PEM=\"%s\"\\n' \"\$(cat $public_key)\" >> .env"
  echo "    then recreate api+updater BEFORE applying any update — otherwise registration and"
  echo "    apply-time signature verification both reject every package."
  echo
else
  echo "    using existing $private_key"
fi

echo "==> [2/6] Local registry on $registry"
registry_container="aqsat-registry"
if ! docker inspect "$registry_container" >/dev/null 2>&1; then
  # Bound to loopback only — nothing outside this host can ever see it, and both the daemon-side
  # push (this script) and pull (the updater, via the host daemon) resolve 127.0.0.1:5000 locally.
  docker run -d --name "$registry_container" --restart unless-stopped -p 127.0.0.1:5000:5000 registry:2
fi
for _ in $(seq 1 30); do
  curl -sf "http://$registry/v2/" >/dev/null 2>&1 && break
  sleep 1
done
curl -sf "http://$registry/v2/" >/dev/null 2>&1 || {
  echo "ERROR: local registry did not become ready on http://$registry (is the registry:2 image pullable from this host?)" >&2
  exit 1
}
echo "    registry ready"

image_tag="$registry/aqsat-api:$version"
echo "==> [3/6] Building $image_tag (version stamped into the image)"
docker build -f src/Aqsat.Api/Dockerfile --build-arg APP_VERSION="$version" -t "$image_tag" .

echo "==> [4/6] Pushing"
docker push "$image_tag"
# Same extraction release.yml performs: RepoDigests[0] is "repo@sha256:<manifest-digest>" — keep
# the digest. This (NOT the image config ID) is what both verifiers check the pull against.
sha256="$(docker inspect --format='{{index .RepoDigests 0}}' "$image_tag" | sed 's/^[^@]*@//')"

echo "==> [5/6] Signing manifest"
out_dir="release-out"
mkdir -p "$out_dir"
manifest="$out_dir/manifest-$version.json"
bash scripts/release/sign-package.sh "$version" "$image_tag" "$sha256" "$private_key" > "$manifest"
echo "    wrote $manifest"

if $image_only; then
  echo "==> [6/6] Registration skipped (--image-only). Bring the stack up with this version stamp:"
  echo "    APP_VERSION=$version docker compose -f docker-compose.prod.yml --env-file .env up -d --build"
  exit 0
fi

echo "==> [6/6] Registering with $api_base"
[[ -n "$owner_mobile" && -n "$owner_password" ]] || {
  echo "ERROR: Platform.Owner credentials required — pass --owner-mobile/--owner-password or export" >&2
  echo "       PLATFORM_OWNER_MOBILE / PLATFORM_OWNER_PASSWORD." >&2
  exit 1
}
token="$(curl -sf -X POST "$api_base/api/auth/login" \
  -H "Content-Type: application/json" \
  -d "$(jq -n --arg m "$owner_mobile" --arg p "$owner_password" '{mobile: $m, password: $p}')" \
  | jq -r '.token // empty')"
[[ -n "$token" ]] || { echo "ERROR: login failed — check the owner credentials and that $api_base is reachable." >&2; exit 1; }

register_args=("$api_base" "$token" "$manifest" "$notes" "$minimum_from")
$db_migration && register_args+=(--db-migration)
$security && register_args+=(--security)
bash scripts/release/register-package.sh "${register_args[@]}"

echo
echo "Done — $version is now in the update panel's package list (بهروزرسانی سیستم)."
echo "Next: log in as Platform.Owner → صفحهٔ «بهروزرسانی سیستم» → درخواست کد تأیید → شروع بهروزرسانی."
echo "The panel backs up the database first and rolls the container back automatically if the health check fails."
