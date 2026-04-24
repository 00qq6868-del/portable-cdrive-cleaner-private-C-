# 项目上下文

## 项目是什么
- 项目名称：便携式磁盘清理器 / PortableCDriveCleaner
- 技术栈：WinForms + .NET Windows 桌面程序
- 源码目录：`E:\vscode Claude\PortableCDriveCleaner`
- 实际安装目录：`D:\磁盘清理器`
- 实际运行主程序：`D:\磁盘清理器\磁盘清理器.exe`
- 当前桌面入口：`C:\Users\zero\Desktop\磁盘清理器.lnk`
- 当前桌面入口模式：通过计划任务 `磁盘清理器-高权限启动` 拉起

## 当前交付链
- 源码修改后，使用 `publish.ps1` 重新发布
- 发布脚本会同步：
  - 安装目录 EXE
  - 桌面快捷方式
  - 安装目录内快捷方式
- 当前已确认：安装目录和桌面入口需要始终同步，不能只更新桌面快捷方式

## 当前主要工作方式
- 任何新 AI 或同一 AI 的新窗口，先读这些文件：
  1. `PROJECT_CONTEXT.md`
  2. `MEMORY.md`
  3. `CURRENT_TASK.md`
  4. `TODO_NOT_FIXED.md`
  5. `OPTIMIZATION_LOG.md`
  6. `HANDOVER_FOR_OTHER_AI.md`

## 当前阶段
- 当前仍处于“历史问题收口 + 主窗口垂直布局压缩”阶段
- 最近一次用户最新反馈：
  - 主窗口小窗里仍然“看到的数据太少”
  - 顶部仍然占高太多
- 这意味着：
  - 小窗布局问题尚未结案
  - 当前阶段不能假装完成，必须继续记录与回归

## 当前重要事实
- 当前安装版时间戳：`2026-04-23 02:47:53`
- 当前安装版路径：`D:\磁盘清理器\磁盘清理器.exe`
- 当前项目还没有 GitHub CLI
- 当前项目已经初始化本地 Git 仓库
- 当前 GitHub 私有远程已配置并已完成首次推送
- 当前远程仓库：`origin -> portable-cdrive-cleaner-private-C-`

## 使用规则
- 每次开始新任务前，先更新 `CURRENT_TASK.md`
- 每完成一个子步骤，立即更新：
  - `OPTIMIZATION_LOG.md`
  - `TODO_NOT_FIXED.md`
  - 必要时更新 `MEMORY.md`
- 任何时候如果怀疑会中断，先写交接，再继续做代码
