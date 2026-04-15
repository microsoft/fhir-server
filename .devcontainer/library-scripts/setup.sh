#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "$0")" && pwd)"

if [ -f "${script_dir}/fix-cert.sh" ]; then
    /bin/bash "${script_dir}/fix-cert.sh"
else
    /bin/bash "${script_dir}/library-scripts/fix-cert.sh"
fi
