#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "${BASH_SOURCE[0]}")"
for test in tests.cjs symbolic-tests.cjs straight-draw-tests.cjs draw-regression-tests.cjs pocket-tests.cjs defaults-test.cjs linux-tests.cjs; do
  node "tests/$test"
done
node src/make-offline.cjs
