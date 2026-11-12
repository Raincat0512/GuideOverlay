@echo off
setlocal
cd /d "%~dp0"
rem 统一 936 代码页：本文件按 GBK 保存，保证中文提示在双击/脚本调用下都不乱码
chcp 936 >nul 2>nul

rem ==== 构建加固（2026-11 产品审查 05 批次）====
rem 1) 先编译到 obj\GuideOverlay.exe，成功替换后才产生新 exe（输出名保持最终名，版本资源正确）；
rem 2) 任何失败路径都不会删除旧 GuideOverlay.exe；
rem 3) 旧 exe 被占用（悬浮窗还在运行）时给出中文提示。

set "TARGET=GuideOverlay.exe"
rem 编译到 obj\ 子目录、输出名仍用最终名：这样 exe 的版本资源 OriginalFilename 才是 GuideOverlay.exe
set "OUTDIR=obj"
set "TMPEXE=%OUTDIR%\GuideOverlay.exe"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" goto :no_csc

rem 清掉上次中断残留的临时产物（只删 obj\ 里的半成品，绝不动旧 exe）
if exist "%TMPEXE%" del /q "%TMPEXE%" >nul 2>nul
if not exist "%OUTDIR%" mkdir "%OUTDIR%" >nul 2>nul

"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /platform:anycpu /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:"%TMPEXE%" "Program.cs"
if errorlevel 1 goto :build_failed
if not exist "%TMPEXE%" goto :build_failed

rem 同一分区内 move 是原子替换：目标被占用时失败，旧 exe 保持原样
move /y "%TMPEXE%" "%TARGET%" >nul 2>nul
if exist "%TMPEXE%" goto :target_busy
if not exist "%TARGET%" goto :target_busy

if exist "%OUTDIR%" rmdir "%OUTDIR%" >nul 2>nul
echo.
echo BUILD OK
for %%F in ("%TARGET%") do echo %%~fF  %%~zF bytes
exit /b 0

:build_failed
if exist "%TMPEXE%" del /q "%TMPEXE%" >nul 2>nul
if exist "%OUTDIR%" rmdir "%OUTDIR%" >nul 2>nul
echo.
echo BUILD FAILED - compile error, old GuideOverlay.exe left untouched.
echo [失败] 编译错误，旧 GuideOverlay.exe 未改动
pause
exit /b 1

:target_busy
if exist "%TMPEXE%" del /q "%TMPEXE%" >nul 2>nul
if exist "%OUTDIR%" rmdir "%OUTDIR%" >nul 2>nul
echo.
echo BUILD FAILED - GuideOverlay.exe is in use. Close the overlay and retry.
echo [失败] 程序正在运行，请先关闭悬浮窗后再构建
echo        若确认程序未运行，请检查旧 exe 是否被杀软/同步盘/预览窗口占用。
pause
exit /b 1

:no_csc
echo csc.exe not found. Windows .NET Framework 4.x is required.
echo [失败] 未找到 csc.exe，请先安装 .NET Framework 4.x（Windows 10/11 自带）
pause
exit /b 1