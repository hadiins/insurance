#!/usr/bin/env bash
# docs/TASKS.md Task 23 — run once, ever (or when rotating keys). The private key never leaves your
# machine/CI secret store; the public key half is what UPDATER_SIGNING_PUBLIC_KEY_PEM in
# docker-compose.prod.yml gets set to (see README.md and docs/RELEASE-RUNBOOK.md).
set -euo pipefail

out_dir="${1:-.}"
private_key="$out_dir/release-signing-key.pem"
public_key="$out_dir/release-signing-key.pub.pem"

if [[ -f "$private_key" ]]; then
  echo "Refusing to overwrite an existing private key at $private_key" >&2
  echo "Generating a new one invalidates every package signed with the old one — Aqsat.Updater would reject them all." >&2
  exit 1
fi

openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -out "$private_key"
openssl pkey -in "$private_key" -pubout -out "$public_key"
chmod 600 "$private_key"

echo "Private key: $private_key (keep this secret, never commit it)"
echo "Public key:  $public_key (this is UPDATER_SIGNING_PUBLIC_KEY_PEM)"
