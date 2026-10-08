#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
node -e 'if (Number(process.versions.node.split(".")[0]) < 20) throw new Error("Node.js 20以上が必要です")'
exec node src/linux/server.cjs "$@"
