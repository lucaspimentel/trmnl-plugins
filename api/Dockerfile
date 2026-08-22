FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/TrmnlApi/TrmnlApi.csproj src/TrmnlApi/
RUN dotnet restore src/TrmnlApi/TrmnlApi.csproj
COPY src/ src/
RUN dotnet publish src/TrmnlApi/TrmnlApi.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "TrmnlApi.dll"]
