# Portable Disk Cleaner

原生 WinForms 便携式磁盘清理器，支持：

- Windows x64
- Windows ARM64 / MPC

## 特点

- 同时提供 `安装器.exe` 和单文件绿色版 ZIP
- 安装器默认推荐装到最大非 C 固定盘的 `\磁盘清理器`
- 从 C 盘直接运行绿色版时，会提示自搬家到非 C 固定盘
- 启动时主动说明管理员权限用途；拒绝后进入只读浏览模式
- 优先扫描 C 盘，同时检查其他固定盘的常见垃圾目录
- 自动扫描系统垃圾、应用缓存、浏览器缓存、重复安装包 / 压缩包
- 主界面支持 `清理候选 / C盘总览` 双视图
- 支持每小时静默后台清理，用户可随时关闭
- 明确垃圾支持一键安全清理
- 需要确认的内容会展示盘符、路径、类型、大小和说明，并可直接打开位置查看
- 日志写入 `data\logs`

## 使用方式

1. 优先双击 `磁盘清理器安装器.exe`
2. 安装器会默认推荐非 C 固定盘，并可创建桌面快捷方式
3. 首次打开时建议授予管理员权限；如果拒绝，程序会进入只读模式
4. 在 `清理候选` 看可删项，在 `C盘总览` 看 C 盘目录是什么、哪些应迁到 D/E

## 后台自动清理

- 在 `后台自动清理` 页可以开关自动清理
- 默认每小时静默运行一次
- 后台默认只自动处理明确垃圾和完全重复的安装包 / 压缩包

## 打包

运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\publish.ps1
```

生成：

- `dist\磁盘清理器安装器.exe`
- `dist\PortableCDriveCleaner-Windows.zip`
- `dist\PortableCDriveCleaner-MPC-ARM64.zip`

## 跨窗口 / 跨 AI 继续

如果要让不同 AI 窗口继续同一个项目，请优先阅读这些文件：

- `AI_STATE.json`
- `PROJECT_CONTEXT.md`
- `MEMORY.md`
- `CURRENT_TASK.md`
- `TODO_NOT_FIXED.md`
- `OPTIMIZATION_LOG.md`
- `HANDOVER_FOR_OTHER_AI.md`
- `SOURCE_CHANGE_LEDGER.md`
- `INTERRUPTED_PROGRESS.md`
- `AI_PROMPT_TEMPLATES.md`
- `GITHUB_SYNC_CHECKLIST.md`

## GitHub 持久记忆

- 当前私有仓库：`https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git`
- 当前默认分支：`main`
- 当前跨 AI / 跨窗口的标准记忆入口：
  - `AI_STATE.json`
  - `checkpoints/`
  - 上述 Markdown 文档
- 标准保存入口：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Progress -Task "当前任务名" -Summary "当前已完成的这一步" -Push
```

- 如果对话或任务可能被截断，优先使用：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Interrupt -Task "当前任务名" -Summary "中断前保存当前进度" -IncludeCode -Push
```
