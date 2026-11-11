@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "TARGET=%LOCALAPPDATA%\Programs\QuotaBar"

echo.
echo === 安装 QuotaBar ===
echo.

if not exist "QuotaBar.exe" (
    echo 还没有 QuotaBar.exe，先编译一次...
    call build.cmd
    if errorlevel 1 exit /b 1
)

echo [1/4] 退出正在运行的 QuotaBar ...
taskkill /im QuotaBar.exe /f >nul 2>&1

echo [2/4] 复制到 %TARGET%
if not exist "%TARGET%" mkdir "%TARGET%"
copy /y "QuotaBar.exe" "%TARGET%\QuotaBar.exe" >nul

echo [3/4] 创建桌面快捷方式
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$w = New-Object -ComObject WScript.Shell; $s = $w.CreateShortcut([Environment]::GetFolderPath('Desktop') + '\QuotaBar.lnk'); $s.TargetPath = '%TARGET%\QuotaBar.exe'; $s.WorkingDirectory = '%TARGET%'; $s.Description = 'AI 额度'; $s.Save()" >nul 2>&1
if errorlevel 1 echo      跳过（不影响使用）

echo [4/4] 启动
start "" "%TARGET%\QuotaBar.exe"

echo.
echo [√] 安装完成。
echo.
echo     程序位置：%TARGET%\QuotaBar.exe
echo     配置文件：%APPDATA%\QuotaBar\config.json
echo.
echo     关掉挂件窗口不会退出，程序会留在右下角托盘里继续刷新。
echo     开机自动启动：右键托盘图标 -^> 设置 -^> 勾上「开机自动启动」。
echo     卸载：运行 uninstall.cmd
echo.
endlocal
