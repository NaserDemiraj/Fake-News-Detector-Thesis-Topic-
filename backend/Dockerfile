# Use the official .NET 7.0 SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Set working directory inside the container
WORKDIR /app

# Copy project files and restore dependencies
COPY *.csproj ./

# Restore .NET project dependencies
RUN dotnet restore

# Copy the remaining source code
COPY . ./

# Build the project (Release mode for production)
RUN dotnet publish -c Release -o out

# Use the official .NET 7.0 ASP.NET runtime image for running the app
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Set working directory
WORKDIR /app

# Copy published output from build stage
COPY --from=build /app/out ./

# Expose port (change if your app uses a different port)
EXPOSE 80

# Define the entrypoint
ENTRYPOINT ["dotnet", "FakeNewsDetector.dll"]
