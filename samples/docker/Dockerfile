FROM mcr.microsoft.com/dotnet/core/sdk:3.1.100-bionic AS build

ARG FHIR_VERSION=R4

WORKDIR /build

COPY ./ ./

RUN dotnet build --configuration Release
RUN dotnet publish "./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Web/Microsoft.Health.Fhir.${FHIR_VERSION}.Web.csproj" -c Release -o "/build" --no-build

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine AS runtime

ARG FHIR_VERSION=R4

# See https://www.abhith.net/blog/docker-sql-error-on-aspnet-core-alpine/
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /build .

RUN ln -s "Microsoft.Health.Fhir.${FHIR_VERSION}.Web.dll" "Microsoft.Health.Fhir.Web.dll"

ENTRYPOINT ["dotnet", "Microsoft.Health.Fhir.Web.dll"]