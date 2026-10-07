@echo off
rem Double-click to playtest: opens each test in playtest\queue.json in turn. See playtest.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0playtest.ps1" %*
