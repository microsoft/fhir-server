FROM mcr.microsoft.com/dotnet/core/sdk:3.1.201-bionic AS build

ARG FHIR_VERSION

WORKDIR /src

COPY ./ ./

RUN dotnet build --configuration Release
RUN dotnet publish "./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Web/Microsoft.Health.Fhir.${FHIR_VERSION}.Web.csproj" -c Release -o "/build" --no-build

FROM mcr.microsoft.com/dotnet/core/aspnet:3.1-alpine AS runtime

ARG FHIR_VERSION

RUN set -x && \
    # See https://www.abhith.net/blog/docker-sql-error-on-aspnet-core-alpine/
    apk add --no-cache icu-libs && \
    addgroup nonroot && \
    adduser -S -D -H -s /sbin/nologin -G nonroot -g nonroot nonroot

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    ASPNETCORE_URLS=http://+:8080

WORKDIR /app
COPY --from=build /build .

RUN ln -s "Microsoft.Health.Fhir.${FHIR_VERSION}.Web.dll" "Microsoft.Health.Fhir.Web.dll"

USER nonroot
EXPOSE 8080
ENTRYPOINT ["dotnet", "Microsoft.Health.Fhir.Web.dll"]
