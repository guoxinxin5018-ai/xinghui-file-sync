@echo off
chcp 65001 >nul
cd /d "%~dp0"
if not exist "dist" mkdir "dist"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo 未找到 .NET Framework C# 编译器。
  echo 请在 Windows 功能中启用 .NET Framework 4.x 后重试。
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:"assets\星汇.ico" /win32manifest:"星汇.manifest" /out:"dist\星汇-文件同步助手.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Xml.dll "FolderSyncAssistant.cs"
if errorlevel 1 (
  echo.
  echo 编译失败，请查看上面的错误信息。
) else (
  echo.
  echo 编译完成：dist\星汇-文件同步助手.exe
)
pause
