# GNAS API 网关容器镜像
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 5000 5001

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/GNAS.Api/GNAS.Api.csproj
RUN dotnet publish src/GNAS.Api/GNAS.Api.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "GNAS.Api.dll"]
