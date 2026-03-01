@echo off
cd DropDetect
echo Installing NuGet Packages...
dotnet add package CommunityToolkit.Mvvm
dotnet add package OpenCvSharp4
dotnet add package OpenCvSharp4.runtime.win
dotnet add package Microsoft.ML.OnnxRuntime.DirectML
dotnet add package ClosedXML
echo Building to verify...
dotnet build
exit /b 0
