FROM mcr.microsoft.com/dotnet/core/sdk:2.2-bionic AS build

WORKDIR /build

# should split restore and build - had problems so did one layer
COPY ./*.* ./
COPY ./src/ ./src/
COPY ./test/ ./test/
COPY ./tools/ ./tools/
COPY ./samples/ ./samples/

COPY ./samples/docker/appsettings.docker.json  ./src/Microsoft.Health.Fhir.Stu3.Web
COPY ./samples/docker/appsettings.docker.json  ./src/Microsoft.Health.Fhir.R4.Web

RUN dotnet build -c Release

RUN dotnet publish "./src/Microsoft.Health.Fhir.Stu3.Web/Microsoft.Health.Fhir.Stu3.Web.csproj" -c Release -o "/build" --no-restore


FROM mcr.microsoft.com/dotnet/core/aspnet:2.2-alpine AS runtime

# See https://www.abhith.net/blog/docker-sql-error-on-aspnet-core-alpine/
RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

WORKDIR /app
COPY --from=build /build .

ENTRYPOINT ["dotnet", "Microsoft.Health.Fhir.Stu3.Web.dll"]