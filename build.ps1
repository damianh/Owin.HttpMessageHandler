dotnet restore .\src\OwinHttpMessageHandler.sln
dotnet build .\src\OwinHttpMessageHandler.sln -c Release
dotnet pack .\src\OwinHttpMessageHandler\OwinHttpMessageHandler.csproj -c Release -o .\..\..\artifacts