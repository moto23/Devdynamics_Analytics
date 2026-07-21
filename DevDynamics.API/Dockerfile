# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY . ./
RUN dotnet restore
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

COPY --from=build /app/out .

# Render injects PORT at runtime; Program.cs binds to it explicitly.
# 5236 is only the local fallback.
ENV PORT=5236
EXPOSE 5236

ENTRYPOINT ["dotnet", "DevDynamics.API.dll"]
