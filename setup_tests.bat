@echo off
echo Building Main Project...
cd DropDetect
dotnet build
if %errorlevel% neq 0 exit /b %errorlevel%

cd ..
echo Creating xUnit project...
if not exist DropDetect.Tests (
    dotnet new xunit -n DropDetect.Tests
)

cd DropDetect.Tests
echo Adding Reference...
dotnet add reference ../DropDetect/DropDetect.csproj

echo Building Test Project...
dotnet build
exit /b 0
