# Pinned images for the test dependencies that CI runs as containers. This file is never built:
# build/templates/steps/start-*.yml read the image reference from the "FROM <image> AS <stage>" line
# whose stage name they own (sql, azurite). Keep one image per line and pin by digest.
FROM mcr.microsoft.com/mssql/server:2025-latest@sha256:05644dad1266ca3d445d1e8f6d2ebb9a6a03df9a39376e0f7b48917356dca9d9 AS sql
FROM mcr.microsoft.com/azure-storage/azurite@sha256:830430c1da1a2d537e08f3e6764dd1f5ae00cf0346bcaf625b968ec3f0971fd5 AS azurite
