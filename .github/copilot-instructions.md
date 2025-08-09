# Spark - EchoVR Data Recording and Processing Tool

Spark is a .NET 6 Windows desktop application built with WPF that records, processes, and uploads EchoVR API data. It includes a SvelteKit overlay component for web-based visualizations and overlays.

Always reference these instructions first and fallback to search or bash commands only when you encounter unexpected information that does not match the info here.

## Working Effectively

### Prerequisites and Installation
- Install .NET 6 SDK: `wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb && sudo dpkg -i packages-microsoft-prod.deb && sudo apt-get update && sudo apt-get install -y dotnet-sdk-6.0`
- Node.js and npm are typically pre-installed in most development environments
- Verify installations: `dotnet --version` (should show 6.x.x) and `npm --version`

### Build Process
**CRITICAL: Set appropriate timeouts (60+ minutes) for all build commands. NEVER CANCEL builds that appear to hang.**

#### Main Application (.NET)
1. **Restore packages (2 seconds clean, 1 second cached)**: 
   ```bash
   cd /path/to/Spark
   dotnet restore Spark.sln -p:EnableWindowsTargeting=true
   ```
   - **EnableWindowsTargeting=true** is REQUIRED on non-Windows systems
   - Expect warnings about WindowsAPICodePack compatibility - these are normal

2. **Build main application (17 seconds)**:
   ```bash
   dotnet build Spark.csproj -p:EnableWindowsTargeting=true
   ```
   - **NEVER CANCEL**: Build takes 17 seconds. Set timeout to 60+ seconds.
   - Application builds successfully but CANNOT run on Linux (Windows WPF only)
   - Expect exactly 97 warnings, 0 errors - these are acceptable and should not be treated as build failures

#### Overlay Component (SvelteKit)
1. **Install dependencies (10 seconds clean, 2 seconds cached)**:
   ```bash
   cd Overlay
   npm install
   ```
   - **NEVER CANCEL**: npm install takes 10 seconds. Set timeout to 120+ seconds.
   - SASS dependency is already included in package.json

2. **Build overlay (10 seconds)**:
   ```bash
   npm run build
   ```
   - **NEVER CANCEL**: Build takes 10 seconds. Set timeout to 60+ seconds.
   - Build output goes to `.svelte-kit/output/` and `build/` directories

### Code Quality and Validation
- **Format overlay code**: `cd Overlay && npm run format` (4 seconds)
- **Lint overlay code**: `cd Overlay && npm run lint` (6 seconds) 
  - Expect many linting warnings in third-party library files - these should be ignored
  - Focus only on errors in `src/` files when making changes
- **IMPORTANT**: `dotnet format` does NOT work due to Windows targeting requirements

### Known Issues and Workarounds
1. **Missing SecretKeys.cs**: If build fails with "SecretKeys does not exist", create from SecretKeys.cs.bak:
   ```bash
   cp SecretKeys.cs.bak SecretKeys.cs
   ```

2. **SvelteKit trailingSlash error**: Fixed in current version by removing deprecated config option

3. **Application runtime**: The main application CANNOT run on Linux - it requires Windows Desktop runtime. This is expected behavior.

## Validation Scenarios
**ALWAYS test these scenarios after making changes:**

### Main Application
1. **Build validation**: 
   ```bash
   dotnet build Spark.csproj -p:EnableWindowsTargeting=true
   ```
   - Should complete in ~17 seconds with 0 errors
   - Exactly 97 warnings are expected and acceptable

2. **Package restoration**:
   ```bash
   dotnet restore Spark.sln -p:EnableWindowsTargeting=true  
   ```
   - Should complete in ~2 seconds (or 1 second if cached)
   - Warnings about WindowsAPICodePack are normal

### Overlay Component
1. **Dependency installation and build**:
   ```bash
   cd Overlay
   npm install && npm run build
   ```
   - Should complete in ~20 seconds total
   - No build errors should occur

2. **Code formatting**:
   ```bash
   cd Overlay
   npm run format && npm run lint
   ```
   - Format should complete in ~4 seconds without errors
   - Lint will show warnings in third-party files (ignore these)

## Common Tasks and File Locations

### Key Directories
- `/` - Main .NET application source code
- `/Overlay/` - SvelteKit overlay component  
- `/Windows/` - WPF window definitions and UI code
- `/Controllers/` - Application logic controllers
- `/Data Containers/` - Data models and structures
- `/Properties/` - Application resources and settings

### Important Files
- `Spark.csproj` - Main project file (targets net6.0-windows)
- `Spark.sln` - Solution file (includes installer projects)
- `SecretKeys.cs` - API keys and configuration (create from .bak if missing)
- `Overlay/package.json` - Node.js dependencies for overlay
- `Overlay/svelte.config.js` - SvelteKit configuration

### Build Artifacts and Timing
- **Main build**: 17 seconds → `bin/Debug/net6.0-windows10.0.17763.0/`
- **Overlay build**: 10 seconds → `Overlay/.svelte-kit/output/` and `Overlay/build/`
- **npm install**: 10 seconds → `Overlay/node_modules/`
- **Total from clean**: ~27 seconds for complete build

### PowerShell Integration
- `Overlay/build_and_copy.ps1` - Builds overlay and copies to main app (Windows only)

## Development Workflow
1. **ALWAYS run builds** after making changes to validate compilation
2. **Use EnableWindowsTargeting=true** for all .NET commands on Linux
3. **Test overlay formatting** with `npm run format` before committing
4. **Never attempt to run the main application** on Linux - it will fail as expected
5. **Check build times** - if builds take longer than expected, investigate but do NOT cancel

## Debugging Build Issues
- **"SecretKeys does not exist"**: Copy from SecretKeys.cs.bak
- **"Windows targeting" errors**: Add `-p:EnableWindowsTargeting=true` to dotnet commands  
- **SvelteKit config errors**: Check svelte.config.js for deprecated options
- **npm/Node.js errors**: Ensure Node.js 18+ and latest npm are installed
- **Long build times**: Normal for first builds due to package restoration

**CRITICAL REMINDER**: NEVER CANCEL builds or long-running operations. Set timeouts appropriately and wait for completion.