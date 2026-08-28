FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

# The tracer home is downloaded here rather than pulled in as a NuGet package: the bundle package
# ships every runtime identifier and would add several hundred MB to the published output, while
# this tarball is the linux-x64 build alone. Keep DD_TRACER_VERSION >= the Datadog.Trace package
# version in TrmnlApi.csproj; bump the two together.
ARG DD_TRACER_VERSION=3.52.0
RUN mkdir -p /opt/datadog \
 && curl -sSL "https://github.com/DataDog/dd-trace-dotnet/releases/download/v${DD_TRACER_VERSION}/datadog-dotnet-apm-${DD_TRACER_VERSION}.tar.gz" \
      | tar -xz -C /opt/datadog

# The bundled geographic dataset, built by tools/GeoDataBuilder and published as a release asset.
# Pinned by URL and verified by sha256, the same way the tracer tarball above is. Leave the URL
# empty and the image still runs: local geocoding and the on-screen location switch off, every
# query goes to the vendor geocoder, and the service logs a warning at boot. See
# docs/geographic-telemetry.md.
ARG GEO_DATA_URL=
ARG GEO_DATA_SHA256=
RUN mkdir -p /opt/geo \
 && if [ -n "$GEO_DATA_URL" ]; then \
      curl -sSL "$GEO_DATA_URL" -o /opt/geo/geo.sqlite \
   && echo "$GEO_DATA_SHA256  /opt/geo/geo.sqlite" | sha256sum -c -; \
    fi

WORKDIR /src
COPY src/TrmnlApi/TrmnlApi.csproj src/TrmnlApi/
COPY src/TrmnlApi.Geo/TrmnlApi.Geo.csproj src/TrmnlApi.Geo/
RUN dotnet restore src/TrmnlApi/TrmnlApi.csproj
COPY src/ src/
RUN dotnet publish src/TrmnlApi/TrmnlApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY --from=build /opt/geo/ /app/
# GeoNames is CC BY 4.0 and requires attribution. Unconditional, because the data is user-facing.
COPY NOTICE /app/NOTICE

COPY --from=build /opt/datadog /opt/datadog
# Creates the log directory the profiler writes attach diagnostics to.
RUN /opt/datadog/createLogPath.sh
ENV CORECLR_ENABLE_PROFILING=1 \
    CORECLR_PROFILER={846F5F1C-F9AE-4B07-969E-05C26BC060D8} \
    CORECLR_PROFILER_PATH=/opt/datadog/Datadog.Trace.ClrProfiler.Native.so \
    DD_DOTNET_TRACER_HOME=/opt/datadog

# Empty when no dataset was baked in, which is exactly the "no dataset" path GeoOptions describes.
ENV Geo__DatabasePath=/app/geo.sqlite

# Override with ASPNETCORE_HTTP_PORTS or ASPNETCORE_URLS to listen elsewhere.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# A deployment may override this with its own start command, in which case changing the line below
# is not enough on its own. See "DD_VERSION and the commit SHA" in docs/observability.md.
ENTRYPOINT ["dotnet", "TrmnlApi.dll"]
