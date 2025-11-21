# GitHub Actions Build Workflow

This directory contains GitHub Actions workflows for automating the build process of the Spark project.

## Workflows

### build.yml

The main build workflow that builds the Spark Windows executable. This workflow runs on:
- Push to `main`, `master`, or `develop` branches
- Pull requests targeting `main`, `master`, or `develop` branches  
- Manual trigger via workflow dispatch

#### Jobs

1. **build**: Creates a framework-dependent build
   - Faster build time
   - Smaller artifact size
   - Requires .NET 8.0 runtime to be installed on the target machine
   - Output artifact: `Spark-Windows-x64`

2. **build-self-contained**: Creates a self-contained build
   - Includes .NET runtime
   - Larger artifact size (~150MB+)
   - No .NET runtime installation required on target machine
   - Single-file executable with native libraries
   - Output artifact: `Spark-Windows-x64-SelfContained`

#### Build Artifacts

Build artifacts are automatically uploaded and retained for 30 days. You can download them from the Actions tab of the repository.

## Requirements

- Windows runner (due to WPF/Windows Forms dependencies)
- .NET 8.0 SDK
- No additional secrets required for basic builds

## MSIX Packaging

MSIX packaging is not included in the automated workflow because it requires:
- Code signing certificate (must be stored as a GitHub secret)
- Additional configuration for certificate thumbprint
- Windows Store association (if distributing through Microsoft Store)

To add MSIX packaging in the future, you would need to:
1. Store the signing certificate as a GitHub secret
2. Import the certificate in the workflow
3. Build the `SparkMSIX.wapproj` project with appropriate signing configuration

## Local Testing

To test the build locally on Windows:

```powershell
# Framework-dependent build
dotnet restore Spark.csproj
dotnet build Spark.csproj --configuration Release
dotnet publish Spark.csproj --configuration Release --output ./publish

# Self-contained build  
dotnet publish Spark.csproj --configuration Release --runtime win-x64 --self-contained true --output ./publish-self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## Troubleshooting

**Build fails on non-Windows runner**: This is expected. The project uses WPF and Windows Forms, which require a Windows environment.

**Missing dependencies**: Ensure all NuGet packages are restored. The workflow automatically runs `dotnet restore` before building.

**Self-contained build is very large**: This is normal. Self-contained builds include the entire .NET runtime (~150MB+). If size is a concern, use the framework-dependent build instead.
