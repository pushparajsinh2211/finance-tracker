FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore dependencies
COPY ["FamilyLedger.Api/FamilyLedger.Api.csproj", "FamilyLedger.Api/"]
RUN dotnet restore "FamilyLedger.Api/FamilyLedger.Api.csproj"

# Build the payload
COPY . .
WORKDIR "/src/FamilyLedger.Api"
RUN dotnet publish "FamilyLedger.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Serve locally
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .

# Expose port and bind specifically to 0.0.0.0 for Render's edge routing
EXPOSE 8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080

ENTRYPOINT ["dotnet", "FamilyLedger.Api.dll"]
