#tool "nuget:?package=xunit.runner.console&version=2.1.0"

#addin "Cake.FileHelpers"

var target          = Argument("target", "Default");
var configuration   = Argument("configuration", "Release");
var artifactsDir    = Directory("./artifacts");
var solution        = "./src/OwinHttpMessageHandler.sln";
var buildNumber     = string.IsNullOrWhiteSpace(EnvironmentVariable("BUILD_NUMBER")) ? "0" : EnvironmentVariable("BUILD_NUMBER");

Task("Clean")
    .Does(() =>
{
    CleanDirectory(artifactsDir);
});

Task("RestorePackages")
    .IsDependentOn("Clean")
    .Does(() =>
{
    DotNetCoreRestore("./src");
    //NuGetRestore(solution);
});

Task("Build")
    .IsDependentOn("RestorePackages")
    .Does(() =>
{
    MSBuild(solution, settings => settings
        .SetConfiguration(configuration)
        .SetVerbosity(Verbosity.Minimal)
        .UseToolVersion(MSBuildToolVersion.VS2017)
    );
});

Task("RunTests")
    .IsDependentOn("Build")
    .Does(() =>
{
        var dll = "./src/OwinHttpMessageHandler.Tests/bin/" + configuration + "/OwinHttpMessageHandler.Tests.dll";
        var settings =  new XUnitSettings 
        { 
            ToolPath = "./tools/xunit.runner.console/tools/xunit.console.exe"
        };
        XUnit(dll,settings);
});

Task("NuGetPack")
    .IsDependentOn("Build")
    .Does(() =>
{
    var settings = new DotNetCorePackSettings
    {
        Configuration = "Release",
        OutputDirectory = "./artifacts/",
        NoBuild = true,
    };
    DotNetCorePack("./src/OwinHttpMessageHandler", settings);
});

Task("Default")
    .IsDependentOn("RunTests")
    .IsDependentOn("NuGetPack");

RunTarget(target);