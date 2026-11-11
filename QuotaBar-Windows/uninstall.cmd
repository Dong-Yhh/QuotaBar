@echo off
chcp 65001 >nul
setlocal

set "TARGET=%LOCALAPPDATA%\Programs\QuotaBar"

echo.
echo === 卸载 QuotaBar ===
echo.

echo [1/3] 退出正在运行的程序
taskkill /im QuotaBar.exe /f >nul 2>&1
ping -n 2 127.0.0.1 >nul 2>&1

echo [2/3] 删除程序文件
if exist "%TARGET%" rmdir /s /q "%TARGET%"

echo [3/3] 删除桌面快捷方式和开机启动项
del /q "%USERPROFILE%\Desktop\QuotaBar.lnk" >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v QuotaBar /f >nul 2>&1

echo.
echo [√] 已卸载。
echo     配置文件保留在 %APPDATA%\QuotaBar（里面是 API Key），
echo     确认不再需要的话可以手动删掉那个文件夹。
echo.
endlocal
