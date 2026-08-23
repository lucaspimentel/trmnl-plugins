FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TrmnlApi/TrmnlApi.csproj src/TrmnlApi/
RUN dotnet restore src/TrmnlApi/TrmnlApi.csproj
COPY src/ src/
RUN dotnet publish src/TrmnlApi/TrmnlApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
# Override with ASPNETCORE_HTTP_PORTS or ASPNETCORE_URLS to listen elsewhere.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrmnlApi.dll"]
