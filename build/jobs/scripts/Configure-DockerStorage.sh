#!/usr/bin/env bash

set -euo pipefail

readonly EXPECTED_STORAGE_MOUNT='/mnt/storage/sdc'
readonly DOCKER_CONFIG='/etc/docker/daemon.json'
readonly DATA_DISK_LINK='/dev/disk/azure/scsi1/lun0'
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

if mountpoint --quiet "${EXPECTED_STORAGE_MOUNT}"; then
  storage_mount="${EXPECTED_STORAGE_MOUNT}"
else
  echo "Managed DevOps Pools did not mount ${EXPECTED_STORAGE_MOUNT}; locating the requested Azure data disk."
  lsblk --output NAME,PATH,SIZE,TYPE,FSTYPE,MOUNTPOINTS
  findmnt --real --output TARGET,SOURCE,FSTYPE,OPTIONS

  if [[ ! -e "${DATA_DISK_LINK}" ]]; then
    echo "Azure data disk ${DATA_DISK_LINK} was not attached." >&2
    exit 1
  fi

  data_disk="$(readlink --canonicalize-existing "${DATA_DISK_LINK}")"
  filesystem_output="$(lsblk --paths --noheadings --raw --output PATH,FSTYPE "${data_disk}")"
  filesystem_rows="$(awk 'NF == 2 { print $1 "|" $2 }' <<< "${filesystem_output}")"
  filesystems=()
  if [[ -n "${filesystem_rows}" ]]; then
    mapfile -t filesystems <<< "${filesystem_rows}"
  fi
  if (( ${#filesystems[@]} > 1 )); then
    echo "Azure data disk ${data_disk} contains multiple filesystems; refusing to choose one." >&2
    exit 1
  fi

  if (( ${#filesystems[@]} == 0 )); then
    echo "Azure data disk ${data_disk} has no filesystem." >&2
    exit 1
  fi

  IFS='|' read -r mount_device filesystem_type <<< "${filesystems[0]}"
  if ! storage_mount="$(findmnt --first-only --raw --noheadings --output TARGET --source "${mount_device}")"; then
    echo "Azure data disk filesystem ${mount_device} is not mounted." >&2
    exit 1
  fi

  echo "Using Azure data disk filesystem ${mount_device} (${filesystem_type}) mounted at ${storage_mount}."
fi

if [[ "${storage_mount}" == '/' ]] || ! mountpoint --quiet "${storage_mount}"; then
  echo "Resolved Docker storage path ${storage_mount} is not a valid data disk mount." >&2
  exit 1
fi

readonly DOCKER_ROOT="${storage_mount}/docker"
echo "Configuring Docker storage at ${DOCKER_ROOT}..."
sudo mkdir --parents "${DOCKER_ROOT}"

docker_config_backup="$(mktemp)"
docker_config_candidate="$(mktemp)"
if sudo test -f "${DOCKER_CONFIG}"; then
  docker_config_existed=true
  sudo cat "${DOCKER_CONFIG}" > "${docker_config_backup}"
fi

python3 - "${docker_config_backup}" "${docker_config_existed}" "${DOCKER_ROOT}" "${docker_config_candidate}" <<'PYTHON'
import json
import sys

config_path, config_exists, docker_root, output_path = sys.argv[1:]
config = {}

if config_exists == "true":
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
sudo mkdir --parents "$(dirname "${DOCKER_CONFIG}")"
sudo install --mode=0644 "${docker_config_candidate}" "${DOCKER_CONFIG}"
sudo systemctl start docker.socket docker.service

actual_docker_root="$(docker info --format '{{.DockerRootDir}}')"
if [[ "${actual_docker_root}" != "${DOCKER_ROOT}" ]]; then
  echo "Docker root is ${actual_docker_root}; expected ${DOCKER_ROOT}." >&2
  exit 1
fi

df --human-readable "${DOCKER_ROOT}"
docker_restart_needed=false