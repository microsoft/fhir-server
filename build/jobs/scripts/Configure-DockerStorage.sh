#!/usr/bin/env bash

set -euo pipefail

readonly STORAGE_MOUNT='/mnt/storage/sdc'
readonly DOCKER_ROOT="${STORAGE_MOUNT}/docker"
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

if ! mountpoint --quiet "${STORAGE_MOUNT}"; then
  echo "Managed DevOps Pools did not mount ${STORAGE_MOUNT}; inspecting the requested Azure data disk."
  lsblk --output NAME,PATH,SIZE,TYPE,FSTYPE,MOUNTPOINTS
  findmnt --real --output TARGET,SOURCE,FSTYPE,OPTIONS

  if [[ ! -e "${DATA_DISK_LINK}" ]]; then
    echo "Azure data disk ${DATA_DISK_LINK} was not attached." >&2
    exit 1
  fi

  data_disk="$(readlink --canonicalize-existing "${DATA_DISK_LINK}")"
  root_device_id="$(findmnt --raw --noheadings --output MAJ:MIN /)"
  if lsblk --noheadings --raw --output MAJ:MIN "${data_disk}" | grep --fixed-strings --line-regexp --quiet "${root_device_id}"; then
    echo "Refusing to use ${data_disk} because it backs the root filesystem." >&2
    exit 1
  fi

  mapfile -t data_devices < <(lsblk --paths --noheadings --raw --output PATH "${data_disk}")
  for device in "${data_devices[@]}"; do
    existing_mount="$(findmnt --raw --noheadings --output TARGET --source "${device}" 2>/dev/null || true)"
    if [[ -n "${existing_mount}" ]]; then
      echo "Azure data disk device ${device} is already mounted at ${existing_mount}, not ${STORAGE_MOUNT}." >&2
      exit 1
    fi
  done

  mapfile -t filesystems < <(lsblk --paths --noheadings --raw --output PATH,FSTYPE "${data_disk}" | awk 'NF == 2 { print $1 "|" $2 }')
  if (( ${#filesystems[@]} > 1 )); then
    echo "Azure data disk ${data_disk} contains multiple filesystems; refusing to choose one." >&2
    exit 1
  fi

  if (( ${#filesystems[@]} == 1 )); then
    IFS='|' read -r mount_device filesystem_type <<< "${filesystems[0]}"
  else
    mount_device="${data_disk}"
    filesystem_type=''
  fi

  if [[ -z "${filesystem_type}" ]]; then
    if (( ${#data_devices[@]} != 1 )); then
      echo "Azure data disk ${data_disk} is partitioned but has no filesystem; refusing to format it." >&2
      exit 1
    fi

    echo "Creating an ext4 filesystem on empty Azure data disk ${mount_device}."
    sudo mkfs.ext4 -F "${mount_device}"
    filesystem_type='ext4'
  fi

  sudo mkdir --parents "${STORAGE_MOUNT}"
  sudo mount --types "${filesystem_type}" "${mount_device}" "${STORAGE_MOUNT}"
fi

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