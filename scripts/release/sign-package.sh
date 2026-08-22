#!/usr/bin/env bash
# docs/TASKS.md Task 23 — signs {Version}|{ImageTag}|{Sha256} with RSA-PSS/SHA-256, salt length
# pinned to 32 bytes (the SHA-256 digest size) to match .NET's RSASignaturePadding.Pss default,
# which is NOT configurable from the .NET side — src/Aqsat.Updater/Security/PackageSignatureVerifier.cs
# and src/Aqsat.Api's own copy both verify with that exact default, so the salt length on this side
# must match it explicitly rather than relying on whatever OpenSSL's own default happens to be.
set -euo pipefail

usage() {
  echo "Usage: $0 <version> <image-tag> <sha256-digest> <private-key.pem>" >&2
  echo "  sha256-digest: with or without the 'sha256:' prefix — both accepted." >&2
  exit 1
}

[[ $# -eq 4 ]] || usage

version="$1"
image_tag="$2"
sha256_raw="$3"
private_key="$4"

sha256="${sha256_raw#sha256:}"
sha256="sha256:$sha256"

payload="${version}|${image_tag}|${sha256}"

signature_base64=$(printf '%s' "$payload" \
  | openssl dgst -sha256 -sign "$private_key" -sigopt rsa_padding_mode:pss -sigopt rsa_pss_saltlen:32 \
  | base64 -w0)

cat <<EOF
{
  "version": "$version",
  "imageTag": "$image_tag",
  "sha256": "$sha256",
  "signatureBase64": "$signature_base64"
}
EOF
