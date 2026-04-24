@echo off
powershell -ExecutionPolicy Bypass -File "E:\vscode Claude\PortableCDriveCleaner\tools\Record-Checkpoint.ps1" -Mode Interrupt -Task "手动保存当前进度" -Summary "用户手动触发保存当前进度入口，优先保留当前代码和文档状态" -IncludeCode -Push
pause
