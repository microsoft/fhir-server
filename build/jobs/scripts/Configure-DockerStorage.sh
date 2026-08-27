#!/usr/bin/env bash

set -euo pipefail

readonly STORAGE_MOUNT='/mnt/storage/sdc'
readonly DOCKER_ROOT="${STORAGE_MOUNT}/docker"
readonly DOCKER_CONFIG='/etc/docker/daemon.json'
docker_config_backup=''
docker_config_candidate=''
docker_config_existed=false
docker_config_changed=false
docker_restart_needed=false

cleanup() {
  exit_code=$?
  trap - EXIT

  if (( exit_code != 0 )); then
    if [[ "${docker_config_changed}" == true ]]; then
      if sudo systemctl stop docker.service docker.socket; then
        if [[ "${docker_config_existed}" == true ]]; then
          config_restored=false
          if sudo cp "${docker_config_backup}" "${DOCKER_CONFIG}"; then
            config_restored=true
          fi
        else
          config_restored=false
          if sudo rm --force "${DOCKER_CONFIG}"; then
            config_restored=true
          fi
        fi

        if [[ "${config_restored}" == true ]]; then
          sudo systemctl start docker.socket docker.service || echo "Failed to restart Docker after restoring ${DOCKER_CONFIG}." >&2
        else
          echo "Failed to restore ${DOCKER_CONFIG}; Docker remains stopped." >&2
        fi
      else
        echo "Failed to stop Docker; ${DOCKER_CONFIG} was not restored." >&2
      fi
    elif [[ "${docker_restart_needed}" == true ]]; then
      sudo systemctl start docker.socket docker.service || true
    fi
  fi

  if [[ -n "${docker_config_backup}" ]]; then
    rm --force "${docker_config_backup}" || true
  fi

  if [[ -n "${docker_config_candidate}" ]]; then
    rm --force "${docker_config_candidate}" || true
  fi

  exit "${exit_code}"
}

trap cleanup EXIT

if ! mountpoint --quiet "${STORAGE_MOUNT}"; then
  echo "Expected Docker storage disk is not mounted at ${STORAGE_MOUNT}." >&2
  exit 1
fi

echo "Configuring Docker storage at ${DOCKER_ROOT}..."
sudo mkdir --parents "${DOCKER_ROOT}"

docker_config_backup="$(mktemp)"
docker_config_candidate="$(mktemp)"
if sudo test -f "${DOCKER_CONFIG}"; then
  docker_config_existed=true
  sudo cp "${DOCKER_CONFIG}" "${docker_config_backup}"
fi

sudo python3 - "${DOCKER_CONFIG}" "${DOCKER_ROOT}" "${docker_config_candidate}" <<'PYTHON'
import json
import os
import sys

config_path, docker_root, output_path = sys.argv[1:]
config = {}

if os.path.exists(config_path):
    with open(config_path, encoding="utf-8") as config_file:
        config = json.load(config_file)

config["data-root"] = docker_root

with open(output_path, "w", encoding="utf-8") as config_file:
    json.dump(config, config_file, indent=2)
    config_file.write("\n")
PYTHON

docker_restart_needed=true
sudo systemctl stop docker.service docker.socket
docker_config_changed=true
sudo install --mode=0644 "${docker_config_candidate}" "${DOCKER_CONFIG}"
sudo systemctl start docker.socket docker.service

actual_docker_root="$(docker info --format '{{.DockerRootDir}}')"
if [[ "${actual_docker_root}" != "${DOCKER_ROOT}" ]]; then
  echo "Docker root is ${actual_docker_root}; expected ${DOCKER_ROOT}." >&2
  exit 1
fi

df --human-readable "${DOCKER_ROOT}"
docker_restart_needed=false