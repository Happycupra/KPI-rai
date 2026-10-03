FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/SolutionCompakt.Server/SolutionCompakt.Server.csproj src/SolutionCompakt.Server/
COPY src/SolutionCompakt.Core/SolutionCompakt.Core.csproj src/SolutionCompakt.Core/
COPY src/SolutionCompakt.Data/SolutionCompakt.Data.csproj src/SolutionCompakt.Data/
RUN dotnet restore src/SolutionCompakt.Server/SolutionCompakt.Server.csproj
COPY src/SolutionCompakt.Core/ src/SolutionCompakt.Core/
COPY src/SolutionCompakt.Data/ src/SolutionCompakt.Data/
COPY src/SolutionCompakt.Server/ src/SolutionCompakt.Server/
RUN dotnet publish src/SolutionCompakt.Server/SolutionCompakt.Server.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "SolutionCompakt.Server.dll"]
