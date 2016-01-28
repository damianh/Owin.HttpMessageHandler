properties {
    $projectName = "OwinHttpMessageHandler"
    $buildNumber = 0
    $rootDir  = Resolve-Path .\
    $buildOutputDir = "$rootDir\build"
    $reportsDir = "$buildOutputDir\reports"
    $srcDir = "$rootDir\src"
    $solutionFilePath = "$srcDir\$projectName.sln"
    $assemblyInfoFilePath = "$srcDir\SharedAssemblyInfo.cs"
    $nugetPath = "$srcDir\.nuget\nuget.exe"
}

task default -depends UpdateVersion, CreateNuGetPackages

task Clean {
    Remove-Item $buildOutputDir -Force -Recurse -ErrorAction SilentlyContinue
    exec { msbuild /nologo /verbosity:quiet $solutionFilePath /t:Clean }
}

task UpdateVersion {
    $version = Get-Version $assemblyInfoFilePath
    $oldVersion = New-Object Version $version
    $newVersion = New-Object Version ($oldVersion.Major, $oldVersion.Minor, $oldVersion.Build, $buildNumber)
    Update-Version $newVersion $assemblyInfoFilePath
}

task Compile -depends Clean {
    exec { msbuild /nologo /verbosity:quiet $solutionFilePath /p:Configuration=Release }
}

task RunTests -depends Compile {
    $xunitRunner = "$srcDir\packages\xunit.runner.console.2.1.0\tools\xunit.console.exe"
    gci . -Recurse -Include *Tests.csproj, Tests.*.csproj | % {
        $project = $_.BaseName
        if(!(Test-Path $reportsDir\xUnit\$project)){
            New-Item $reportsDir\xUnit\$project -Type Directory
        }
        .$xunitRunner "$srcDir\$project\bin\Release\$project.dll"
    }
}

task BuildDnx {
    &{$Branch='dev';iex ((new-object net.webclient).DownloadString('https://raw.githubusercontent.com/aspnet/Home/dev/dnvminstall.ps1'))}
    dnvm upgrade
    pushd
    cd .\src
    dnu restore
    dnu build .\OwinHttpMessageHandler --configuration Release
    dnu pack .\OwinHttpMessageHandler --configuration Release --out $buildOutputDir
    dnu build .\OwinHttpMessageHandler.Tests --configuration Release
    dnx .\OwinHttpMessageHandler.Tests test
    popd
}

task CreateNuGetPackages -depends Compile {
    if(!(Test-Path $buildOutputDir)){
        New-Item $buildOutputDir -Type Directory
    }

    $versionString = Get-Version $assemblyInfoFilePath
    $version = New-Object Version $versionString
    $packageVersion = $version.Major.ToString() + "." + $version.Minor.ToString() + "." + $version.Build.ToString() + "-build" + $buildNumber.ToString().PadLeft(5,'0')
    gci $srcDir -Recurse -Include *.nuspec | % {
        exec { .$srcDir\.nuget\nuget.exe pack $_ -o $buildOutputDir -version $packageVersion }
    }
}

function Get-Version
{
    param
    (
        [string]$assemblyInfoFilePath
    )
    Write-Host "path $assemblyInfoFilePath"
    $pattern = '(?<=^\[assembly\: AssemblyVersion\(\")(?<versionString>\d+\.\d+\.\d+\.\d+)(?=\"\))'
    $assmblyInfoContent = Get-Content $assemblyInfoFilePath
    return $assmblyInfoContent | Select-String -Pattern $pattern | Select -expand Matches |% {$_.Groups['versionString'].Value}
}

function Update-Version
{
    param
    (
        [string]$version,
        [string]$assemblyInfoFilePath
    )

    $newVersion = 'AssemblyVersion("' + $version + '")';
    $newFileVersion = 'AssemblyFileVersion("' + $version + '")';
    $tmpFile = $assemblyInfoFilePath + ".tmp"

    Get-Content $assemblyInfoFilePath |
        %{$_ -replace 'AssemblyFileVersion\("[0-9]+(\.([0-9]+|\*)){1,3}"\)', $newFileVersion }  | Out-File -Encoding UTF8 $tmpFile

    Move-Item $tmpFile $assemblyInfoFilePath -force
}
