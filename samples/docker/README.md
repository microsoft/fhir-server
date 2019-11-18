# Running Azure FHIR Server with Docker

*IMPORTANT: This configuration is not suitable for production scenarios. This sample has been provided to enable Dev/Test scenarios on a development machine.*

The following instructions detail how to build an azure-fhir-server image and run it with SQL server with Docker on Linux using Docker Compose.

## Build and run using Docker Compose

The quickest way to get the Azure FHIR Server up and running on Docker is to build and run the Azure FHIR Server with a SQL server container using docker compose. Run  the following command from the root of the `microsoft/fhir-server` repository:

```bash
docker-compose -f samples/docker/docker-compose.yaml up -d
```

## Build a Docker Image

If you wish to run using a different SQL instance, such as Azure SQL Database you may want to build a custom image.

The application settings, including SQL server connection string are configured in the file ```samples/docker/appsettings.docker.json``` which is copied in when the Docker image is built. You may alternatively wish to mount the configuration file in at run time.

To build an `azure-fhir-api` image run the following command from the root of the `microsoft/fhir-server`repository:

```docker build -f samples/docker/Dockerfile -t azure-fhir-api .```

To run the container use the command:

```docker run -d -e ASPNETCORE_ENVIRONMENT=docker azure-fhir-api azure-fhir-api```

### TODO

- password for sql
- Split restore and build
- can config be done using env vars?
- Which projects actually need building?
