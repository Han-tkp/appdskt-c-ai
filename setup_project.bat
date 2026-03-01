@echo off
echo Installing Avalonia Templates...
dotnet new install Avalonia.Templates
echo Creating Avalonia Project...
dotnet new avalonia.app -n DropDetect
echo Installing NuGet Packages...
cd DropDetect
dotnet add package CommunityToolkit.Mvvm
dotnet add package OpenCvSharp4
dotnet add package OpenCvSharp4.runtime.win
dotnet add package Microsoft.ML.OnnxRuntime.DirectML
dotnet add package ClosedXML
echo Done.
exit /b 0
