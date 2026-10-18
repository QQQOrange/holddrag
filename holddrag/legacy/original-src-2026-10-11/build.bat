@echo off
setlocal
cd /d "%~dp0"

rem 优先用本机用户目录里安装的 .NET SDK，其次用 PATH 里的 dotnet
set "DOTNET=%USERPROFILE%\.dotnet\dotnet.exe"
if not exist "%DOTNET%" set "DOTNET=dotnet"

echo [1/3] 校验 app.manifest ...
rem 清单必须是合法 XML：只要不合法，exe 一启动就会报 14001「并行配置不正确」，
rem 而且报错信息完全不提清单，极难排查。最常见的原因是在注释里写了 "--"。
powershell -NoProfile -ExecutionPolicy Bypass -Command "try { [void][xml](Get-Content -Raw -Encoding UTF8 'app.manifest') } catch { Write-Host ''; Write-Host '  [失败] app.manifest 不是合法 XML。' -ForegroundColor Red; Write-Host ('  ' + $_.Exception.Message) -ForegroundColor Red; Write-Host ''; Write-Host '  exe 启动时会报 14001 并行配置错误。检查注释里是否出现了双连字符 -- 。' -ForegroundColor Yellow; exit 1 }; exit 0"
if errorlevel 1 goto badmanifest

echo [2/3] 编译发布...
"%DOTNET%" publish -c Release
if errorlevel 1 goto fail

echo [3/3] 复制单文件 exe 到项目根目录...
copy /y "bin\Release\net10.0-windows\win-x64\publish\HoldDrag.exe" "HoldDrag.exe" >nul
if errorlevel 1 goto fail

echo.
echo 完成：%CD%\HoldDrag.exe
echo.
pause
exit /b 0

:badmanifest
echo.
echo 构建已中止：清单文件非法，修好 app.manifest 后重新运行本脚本。
echo.
pause
exit /b 1

:fail
echo.
echo 构建失败，请检查上面输出的错误信息。
echo 若提示找不到 dotnet，请先安装 .NET SDK 10：
echo   https://dotnet.microsoft.com/download
echo.
pause
exit /b 1
