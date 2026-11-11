@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo.
echo === QuotaBar Windows 构建 ===
echo.

rem 用 Windows 自带的 .NET Framework 编译器，不需要安装 .NET SDK / Visual Studio。
set "FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319"
if not exist "%FW%\csc.exe" set "FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319"

if not exist "%FW%\csc.exe" (
    echo [×] 找不到系统自带的 C# 编译器 csc.exe。
    echo     期望位置：%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
    echo     Windows 10 / 11 默认都有；如果没有，说明 .NET Framework 被精简过，
    echo     到「启用或关闭 Windows 功能」里勾上 .NET Framework 4.x 即可。
    exit /b 1
)

echo [1/2] 编译器：%FW%\csc.exe

rem 编译前先确认 WPF 程序集都在，缺哪个直接说清楚，别让用户去看 csc 的天书报错。
for %%R in (WindowsBase.dll PresentationCore.dll PresentationFramework.dll System.Xaml.dll System.Windows.Forms.dll System.Drawing.dll) do (
    if not exist "%FW%\%%R" (
        echo [×] 缺少 %%R，位置：%FW%\%%R
        echo     .NET Framework 可能被精简过。到「启用或关闭 Windows 功能」里勾上
        echo     「.NET Framework 4.x」的完整安装，或者装一次 .NET Framework 4.8 运行时。
        exit /b 1
    )
)

echo [2/2] 正在编译 src\*.cs ...
echo.

"%FW%\csc.exe" ^
    /nologo ^
    /target:winexe ^
    /platform:anycpu ^
    /optimize+ ^
    /utf8output ^
    /nowarn:1701,1702 ^
    /out:"QuotaBar.exe" ^
    /reference:"%FW%\WindowsBase.dll" ^
    /reference:"%FW%\PresentationCore.dll" ^
    /reference:"%FW%\PresentationFramework.dll" ^
    /reference:"%FW%\System.Xaml.dll" ^
    /reference:"%FW%\System.Windows.Forms.dll" ^
    /reference:"%FW%\System.Drawing.dll" ^
    src\*.cs

if errorlevel 1 (
    echo.
    echo [×] 编译失败，请把上面的错误信息发给我。
    exit /b 1
)

echo.
echo [√] 编译成功：%~dp0QuotaBar.exe
echo.
echo     直接双击 QuotaBar.exe 就能运行。
echo     想装到「开始菜单 / 桌面」并创建快捷方式，运行 install.cmd。
echo     想验证解析逻辑是否正确，运行：QuotaBar.exe --selftest
echo.
endlocal
