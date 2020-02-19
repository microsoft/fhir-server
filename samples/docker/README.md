# Running Azure FHIR Server with Docker

*IMPORTANT:* This sample has been created to enable Dev/Test scenarios and is not suitable for production scenarios. Passwords are contained in deployment files, the SQL server connection is not encrypted and authentication on the FHIR Server has been disabled.

The following instructions detail how to build and run the FHIR Server in Docker on Linux.

## Build and run with SQL Server using Docker Compose

The quickest way to get the Azure FHIR Server up and running on Docker is to build and run the Azure FHIR Server with a SQL server container using docker compose. Run the following command, replacing `<SA_PASSWORD>` with your chosen password (be sure to follow the [SQL server password complexity requirements](https://docs.microsoft.com/en-us/sql/relational-databases/security/password-policy?view=sql-server-ver15#password-complexity)), from the root of the `microsoft/fhir-server` repository:

```bash
env SAPASSWORD='<SA_PASSWORD>' docker-compose -f samples/docker/docker-compose.yaml up -d
```

Given the FHIR API is likely to start before the SQL server is ready, you may need to restart the API container once the SQL server is healty. This can be done using `docker restart <container-name>`, i.e. docker restart `docker restart docker_fhir-api_1`.

Once deployed the FHIR Server metadata endpoint should be avaialble at `http://localhost/metadata/`.

## Run in Docker with a custom configuration

To build the `azure-fhir-api` image run the following command from the root of the `microsoft/fhir-server`repository:

The default configuration builds an image with the FHIR R4 API:

```bash
docker build -f samples/docker/Dockerfile -t azure-fhir-api .
```

For STU3 use the following command:

```bash
docker build -f samples/docker/Dockerfile -t azure-fhir-api --build-arg FHIR_VERSION=Stu3 .
```

The container can then be run, specifying configuration details such as:

```bash
docker run -d \
    -e FHIRServer__Security__Enabled="false"
    -e SqlServer__ConnectionString="Server=tcp:<sql-server-fqdn>,1433;Initial Catalog=FHIR;Persist Security Info=False;User ID=sa;Password=<sql-sa-password>;MultipleActiveResultSets=False;Connection Timeout=30;" \
    -e SqlServer__AllowDatabaseCreation="true" \
    -e SqlServer__Initialize="true" \
    -e DataStore="SqlServer" \
    -p 80:80
    azure-fhir-api azure-fhir-api
```
