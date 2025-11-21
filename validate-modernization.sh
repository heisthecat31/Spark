#!/bin/bash

# Spark Project Modernization Validation Script
# This script validates that the modernization changes are working correctly

echo "=== Spark Project Modernization Validation ==="
echo ""

# Check .NET version
echo "1. Checking .NET SDK version..."
dotnet --version
if [ $? -eq 0 ]; then
    echo "✅ .NET SDK is available"
else
    echo "❌ .NET SDK not found"
    exit 1
fi
echo ""

# Validate global.json
echo "2. Validating global.json configuration..."
if [ -f "global.json" ]; then
    echo "✅ global.json exists"
    if grep -q "8.0" global.json; then
        echo "✅ global.json specifies .NET 8.0"
    else
        echo "❌ global.json does not specify .NET 8.0"
        exit 1
    fi
else
    echo "❌ global.json not found"
    exit 1
fi
echo ""

# Validate project file
echo "3. Validating project file modernization..."
if [ -f "Spark.csproj" ]; then
    echo "✅ Spark.csproj exists"
    
    if grep -q "net8.0-windows" Spark.csproj; then
        echo "✅ Target framework is net8.0-windows"
    else
        echo "❌ Target framework is not modernized"
        exit 1
    fi
    
    if grep -q "<LangVersion>12</LangVersion>" Spark.csproj; then
        echo "✅ C# language version is 12"
    else
        echo "❌ C# language version is not modernized"
        exit 1
    fi
    
    if ! grep -q "WindowsAPICodePack-Shell" Spark.csproj; then
        echo "✅ Deprecated WindowsAPICodePack-Shell removed"
    else
        echo "❌ Deprecated WindowsAPICodePack-Shell still present"
        exit 1
    fi
else
    echo "❌ Spark.csproj not found"
    exit 1
fi
echo ""

# Test package restore
echo "4. Testing package restore..."
dotnet restore Spark.csproj --disable-build-servers --verbosity quiet
if [ $? -eq 0 ]; then
    echo "✅ Package restore successful"
else
    echo "❌ Package restore failed"
    exit 1
fi
echo ""

# Check for modern package versions
echo "5. Validating package modernization..."
if grep -q "Microsoft.Data.Sqlite.*8.0.0" Spark.csproj; then
    echo "✅ Microsoft.Data.Sqlite updated to 8.0.0"
else
    echo "❌ Microsoft.Data.Sqlite not properly updated"
    exit 1
fi

if grep -q "Microsoft.Win32.SystemEvents.*8.0.0" Spark.csproj; then
    echo "✅ Microsoft.Win32.SystemEvents updated to 8.0.0"
else
    echo "❌ Microsoft.Win32.SystemEvents not properly updated"
    exit 1
fi
echo ""

# Validate code modernization
echo "6. Validating code modernization..."
if [ -f "Windows/Settings/UnifiedSettingsWindow.xaml.cs" ]; then
    if ! grep -q "WindowsAPICodePack" "Windows/Settings/UnifiedSettingsWindow.xaml.cs"; then
        echo "✅ WindowsAPICodePack usage removed from code"
    else
        echo "❌ WindowsAPICodePack usage still present in code"
        exit 1
    fi
    
    if grep -q "OpenFolderDialog" "Windows/Settings/UnifiedSettingsWindow.xaml.cs"; then
        echo "✅ Modern OpenFolderDialog API implemented"
    else
        echo "❌ Modern OpenFolderDialog API not found"
        exit 1
    fi
else
    echo "❌ UnifiedSettingsWindow.xaml.cs not found"
    exit 1
fi
echo ""

echo "=== Validation Complete ==="
echo "✅ All modernization checks passed!"
echo ""
echo "The project has been successfully modernized to:"
echo "  • .NET 8.0"
echo "  • C# 12"
echo "  • Modern package versions"
echo "  • Updated APIs"
echo ""
echo "Note: This is a Windows-specific WPF application."
echo "Full build requires Windows environment with appropriate workloads."