#!/usr/bin/env bash
# docs/TASKS.md Task 23 — registers a signed package (the output of sign-package.sh) with a running
# Aqsat.Api. Requires a Platform.Owner JWT — get one the same way the panel does (POST /api/auth/login
# for a user whose role carries Platform.Owner), or a long-lived CI credential if you've set one up.
set -euo pipefail

usage() {
  cat >&2 <<'USAGE'
Usage: register-package.sh <api-base-url> <bearer-token> <manifest.json> <release-notes-fa> [minimum-from-version] [--db-migration] [--security]

  manifest.json: the JSON sign-package.sh produced (version/imageTag/sha256/signatureBase64).
  release-notes-fa: Persian changelog text shown in the panel.
USAGE
  exit 1
}

[[ $# -ge 4 ]] || usage

api_base="$1"
token="$2"
manifest_path="$3"
release_notes="$4"
minimum_from_version="${5:-}"
shift $(( $# < 5 ? $# : 5 ))

has_db_migration=false
is_security_update=false
for arg in "$@"; do
  case "$arg" in
    --db-migration) has_db_migration=true ;;
    --security) is_security_update=true ;;
    *) echo "Unknown flag: $arg" >&2; usage ;;
  esac
done

version=$(jq -r '.version' "$manifest_path")
image_tag=$(jq -r '.imageTag' "$manifest_path")
sha256=$(jq -r '.sha256' "$manifest_path")
signature=$(jq -r '.signatureBase64' "$manifest_path")

body=$(jq -n \
  --arg version "$version" \
  --arg imageTag "$image_tag" \
  --arg sha256 "$sha256" \
  --arg signatureBase64 "$signature" \
  --arg releaseNotesFa "$release_notes" \
  --arg minimumFromVersion "$minimum_from_version" \
  --argjson hasDbMigration "$has_db_migration" \
  --argjson isSecurityUpdate "$is_security_update" \
  '{version: $version, imageTag: $imageTag, sha256: $sha256, signatureBase64: $signatureBase64,
    releaseNotesFa: $releaseNotesFa,
    minimumFromVersion: (if $minimumFromVersion == "" then null else $minimumFromVersion end),
    hasDbMigration: $hasDbMigration, isSecurityUpdate: $isSecurityUpdate}')

curl -sf -X POST "$api_base/api/platform/updates/register" \
  -H "Authorization: Bearer $token" \
  -H "Content-Type: application/json" \
  -d "$body" | jq .

echo "Registered $version. It now appears in the panel's package list — staged rollout (docs/RELEASE-RUNBOOK.md) starts from there." >&2
