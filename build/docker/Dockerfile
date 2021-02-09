FROM mcr.microsoft.com/dotnet/sdk:5.0.102-1-alpine3.12@sha256:fb1a43b50c7047e5f28e309268a8f5425abc9cb852124f6828dcb0e4f859a4a1 AS build

ARG FHIR_VERSION

WORKDIR /repo

COPY .editorconfig \
     CustomAnalysisRules.ruleset \
     CustomAnalysisRules.Test.ruleset \
     Directory.Build.props \
     global.json \
     nuget.config \
     stylecop.json \
     ./

# Copy csproj and projitem files first, then do a dotnet restore. These layers are only invalidated
# when these project files change.

COPY ./src/Microsoft.Health.Fhir.ValueSets/Microsoft.Health.Fhir.ValueSets.csproj \
     ./src/Microsoft.Health.Fhir.ValueSets/Microsoft.Health.Fhir.ValueSets.csproj

COPY ./src/Microsoft.Health.Fhir.Core/Microsoft.Health.Fhir.Core.csproj \
     ./src/Microsoft.Health.Fhir.Core/Microsoft.Health.Fhir.Core.csproj

COPY ./src/Microsoft.Health.Fhir.Azure/Microsoft.Health.Fhir.Azure.csproj \
     ./src/Microsoft.Health.Fhir.Azure/Microsoft.Health.Fhir.Azure.csproj

COPY ./src/Microsoft.Health.Fhir.SqlServer/Microsoft.Health.Fhir.SqlServer.csproj \
     ./src/Microsoft.Health.Fhir.SqlServer/Microsoft.Health.Fhir.SqlServer.csproj

COPY ./src/Microsoft.Health.Fhir.Api/Microsoft.Health.Fhir.Api.csproj \
     ./src/Microsoft.Health.Fhir.Api/Microsoft.Health.Fhir.Api.csproj

COPY ./src/Microsoft.Health.Fhir.CosmosDb/Microsoft.Health.Fhir.CosmosDb.csproj \
     ./src/Microsoft.Health.Fhir.CosmosDb/Microsoft.Health.Fhir.CosmosDb.csproj

COPY ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Core/Microsoft.Health.Fhir.${FHIR_VERSION}.Core.csproj \
     ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Core/Microsoft.Health.Fhir.${FHIR_VERSION}.Core.csproj

COPY ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Api/Microsoft.Health.Fhir.${FHIR_VERSION}.Api.csproj \
     ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Api/Microsoft.Health.Fhir.${FHIR_VERSION}.Api.csproj

COPY ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Web/Microsoft.Health.Fhir.${FHIR_VERSION}.Web.csproj \
     ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Web/Microsoft.Health.Fhir.${FHIR_VERSION}.Web.csproj

COPY ./src/Microsoft.Health.Fhir.Shared.Web/Microsoft.Health.Fhir.Shared.Web.projitems \
     ./src/Microsoft.Health.Fhir.Shared.Web/Microsoft.Health.Fhir.Shared.Web.projitems

COPY ./src/Microsoft.Health.Fhir.Shared.Core/Microsoft.Health.Fhir.Shared.Core.projitems \
     ./src/Microsoft.Health.Fhir.Shared.Core/Microsoft.Health.Fhir.Shared.Core.projitems

COPY ./src/Microsoft.Health.Fhir.Shared.Client/Microsoft.Health.Fhir.Shared.Client.projitems \
     ./src/Microsoft.Health.Fhir.Shared.Client/Microsoft.Health.Fhir.Shared.Client.projitems

COPY ./src/Microsoft.Health.Fhir.Shared.Api/Microsoft.Health.Fhir.Shared.Api.projitems \
     ./src/Microsoft.Health.Fhir.Shared.Api/Microsoft.Health.Fhir.Shared.Api.projitems

RUN dotnet restore ./src/Microsoft.Health.Fhir.${FHIR_VERSION}.Web/Microsoft.Health.Fhir.${FHIR_VERSION}.Web.csproj

# now copy over everything else and publish

COPY . .

RUN dotnet publish /repo/src/Microsoft.Health.Fhir.${FHIR_VERSION}.Web/Microsoft.Health.Fhir.${FHIR_VERSION}.Web.csproj -c Release -o "/build" --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:5.0.2-alpine3.12@sha256:efd03222416e60b50eb86183a6018c698eebcf496dfa427711ebd7d5c50b427f AS runtime

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
