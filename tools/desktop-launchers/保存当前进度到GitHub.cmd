@echo off
powershell -ExecutionPolicy Bypass -File "E:\vscode Claude\PortableCDriveCleaner\tools\Save-Handoff.ps1" -Message "checkpoint: save docs and code progress" -IncludeCode -Push
pause

