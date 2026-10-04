# syntax=docker/dockerfile:1
# One multi-stage build produces both HuntOps runtime images:
#   docker build --target web    -t huntops-web .
#   docker build --target worker -t huntops-worker .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, from project files only, so the layer is cached until dependencies change.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/HuntOps.Domain/HuntOps.Domain.csproj                 src/HuntOps.Domain/
COPY src/HuntOps.Application/HuntOps.Application.csproj       src/HuntOps.Application/
COPY src/HuntOps.Infrastructure/HuntOps.Infrastructure.csproj src/HuntOps.Infrastructure/
COPY src/HuntOps.Api/HuntOps.Api.csproj                       src/HuntOps.Api/
COPY src/HuntOps.Mcp/HuntOps.Mcp.csproj                       src/HuntOps.Mcp/
COPY src/HuntOps.Web/HuntOps.Web.csproj                       src/HuntOps.Web/
COPY src/HuntOps.Worker/HuntOps.Worker.csproj                 src/HuntOps.Worker/
RUN dotnet restore src/HuntOps.Web/HuntOps.Web.csproj \
 && dotnet restore src/HuntOps.Worker/HuntOps.Worker.csproj

COPY src/ src/
RUN dotnet publish src/HuntOps.Web/HuntOps.Web.csproj       -c Release -o /out/web    --no-restore \
 && dotnet publish src/HuntOps.Worker/HuntOps.Worker.csproj -c Release -o /out/worker --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime-base
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
WORKDIR /app
USER $APP_UID

FROM runtime-base AS web
COPY --from=build --chown=$APP_UID /out/web .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD ["dotnet", "HuntOps.Web.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "HuntOps.Web.dll"]

FROM runtime-base AS worker
COPY --from=build --chown=$APP_UID /out/worker .
ENV ASPNETCORE_HTTP_PORTS=8081
EXPOSE 8081
HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
  CMD ["dotnet", "HuntOps.Worker.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "HuntOps.Worker.dll"]
