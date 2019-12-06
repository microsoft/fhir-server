FROM mcr.microsoft.com/dotnet/core/sdk:2.2-bionic AS build

ARG FHIR_VERSION=R4

WORKDIR /build

COPY ./*.* ./
COPY ./samples/docker/docker_global.json ./global.json
COPY ./src/ ./src/
COPY ./tools/Microsoft.Health.Extensions.BuildTimeCodeGenerator/ ./tools/Microsoft.Health.Extensions.BuildTimeCodeGenerator/

RUN dotnet add "./tools/Microsoft.Health.Extensions.BuildTimeCodeGenerator/Microsoft.Health.Extensions.BuildTimeCodeGenerator.csproj" package Microsoft.CodeAnalysis.Analyzers  --version 2.9.4

RUN dotnet publish "./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Web/Microsoft.Health.Fhir.${FHIR_VERSION}.Web.csproj" -c Release -o "/build"

FROM mcr.microsoft.com/dotnet/core/aspnet:2.2-alpine AS runtime

ARG FHIR_VERSION=R4

# See https://www.abhith.net/blog/docker-sql-error-on-aspnet-core-alpine/
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /build .

RUN ln -s "Microsoft.Health.Fhir.${FHIR_VERSION}.Web.dll" "Microsoft.Health.Fhir.Web.dll"

ENTRYPOINT ["dotnet", "Microsoft.Health.Fhir.Web.dll"]