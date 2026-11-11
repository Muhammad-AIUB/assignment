#!/bin/sh
# Runs when the nginx container starts (the nginx image executes every script in
# /docker-entrypoint.d). Writes /usr/share/nginx/html/config.js from API_BASE_URL, so the
# same image can be pointed at any backend without a rebuild. The app reads the value
# from window.__APP_CONFIG__ (src/config.ts).
#
# API_BASE_URL must be reachable from the user's BROWSER, not from this container:
# http://localhost:8080 under docker compose, never http://backend:8080.
set -eu

: "${API_BASE_URL:=http://localhost:8080}"

# Write the value as a JSON string: line breaks are dropped, then backslashes and double
# quotes are escaped, so a stray character cannot break out of the string literal.
escaped=$(printf '%s' "$API_BASE_URL" | tr -d '\r\n' | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g')

cat > /usr/share/nginx/html/config.js <<EOF
// Written at container start from API_BASE_URL by docker-entrypoint.d/40-runtime-config.sh.
window.__APP_CONFIG__ = { apiBaseUrl: "${escaped}" };
EOF

echo "40-runtime-config.sh: apiBaseUrl set to ${API_BASE_URL}"
