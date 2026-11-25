@echo off
setlocal
set "APP_EXE=%~dp0League Data Recorder.exe"
set "REPORT_DIR=%LOCALAPPDATA%\League Data Recorder\Logs"
if not defined LOCALAPPDATA set "REPORT_DIR=%TEMP%\League Data Recorder\Logs"
if not exist "%REPORT_DIR%" mkdir "%REPORT_DIR%" >nul 2>&1
if not exist "%REPORT_DIR%" set "REPORT_DIR=%TEMP%\League Data Recorder\Logs"
if not exist "%REPORT_DIR%" mkdir "%REPORT_DIR%" >nul 2>&1
if not exist "%REPORT_DIR%" goto no_report_folder
set "REPORT=%REPORT_DIR%\launcher-%RANDOM%-%RANDOM%.txt"
>"%REPORT%" echo League Data Recorder diagnostic launcher
if exist "%REPORT%" goto report_ready
set "REPORT_DIR=%TEMP%\League Data Recorder\Logs"
if not exist "%REPORT_DIR%" mkdir "%REPORT_DIR%" >nul 2>&1
set "REPORT=%REPORT_DIR%\launcher-%RANDOM%-%RANDOM%.txt"
>"%REPORT%" echo League Data Recorder diagnostic launcher
if not exist "%REPORT%" goto no_report_folder

:report_ready
>>"%REPORT%" echo Time: %DATE% %TIME%
>>"%REPORT%" echo Application: "%APP_EXE%"
>>"%REPORT%" ver
if not exist "%APP_EXE%" goto missing_executable
echo Starting League Data Recorder. Keep this window open until the app closes.
echo Report folder: "%REPORT_DIR%"
echo.
start "" /wait /d "%~dp0." "%APP_EXE%" 1>>"%REPORT%" 2>&1
set "APP_EXIT_CODE=%ERRORLEVEL%"
>>"%REPORT%" echo.
>>"%REPORT%" echo Exit code: %APP_EXIT_CODE%
if "%APP_EXIT_CODE%"=="0" goto finished
echo The application exited with code %APP_EXIT_CODE%.
echo Send the launcher report and any matching crash/startup reports to the developer.
echo If no crash report exists, also copy errors from Windows Event Viewer.
goto show_report

:missing_executable
>>"%REPORT%" echo ERROR: The executable was not found. Extract the complete release ZIP first.
echo The executable was not found beside this launcher.
echo Extract the complete release ZIP and leave this file beside League Data Recorder.exe.
set "APP_EXIT_CODE=2"
goto show_report

:finished
echo The application exited with code 0.
echo If no window appeared, check Task Manager for an existing instance and review the startup report.
goto show_report

:show_report
echo.
echo Report: "%REPORT%"
echo.
pause
exit /b %APP_EXIT_CODE%

:no_report_folder
echo The diagnostic report folder could not be created in Local AppData or Temp.
echo Check folder permissions and available disk space.
pause
exit /b 1
