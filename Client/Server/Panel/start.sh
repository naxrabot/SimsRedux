#!/bin/sh
# The Sims Redux - server panel. First run installs Electron (once).
cd "$(dirname "$0")"
[ -d node_modules/electron ] || npm install || { echo "Install failed. You need Node.js: https://nodejs.org"; exit 1; }
exec npx electron .
