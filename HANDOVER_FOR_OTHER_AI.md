# 给下一个 AI 的交接说明

## 先读顺序
1. `PROJECT_CONTEXT.md`
2. `MEMORY.md`
3. `CURRENT_TASK.md`
4. `TODO_NOT_FIXED.md`
5. `OPTIMIZATION_LOG.md`
6. `AI_PROMPT_TEMPLATES.md`

## 当前必须知道的事实
- 这是 WinForms 项目，不是 Web 项目
- 源码目录：`E:\vscode Claude\PortableCDriveCleaner`
- 实际安装版：`D:\磁盘清理器\磁盘清理器.exe`
- 桌面入口通过计划任务 `磁盘清理器-高权限启动` 拉起
- 最新用户结论不是“差不多好了”，而是：
  - 主窗口小窗里看到的数据仍然太少

## 当前不能做错的事
- 不要只改源码不更新安装版
- 不要只更新桌面快捷方式不更新安装目录
- 不要把“已尝试过”当成“已修好”
- 不要只在聊天里记录进度，必须写回项目文件

## 开始工作前必须做
1. 先更新 `CURRENT_TASK.md`
2. 确认当前真正要解决的只有哪一个问题
3. 做任何较大修改前，先把计划写到 `CURRENT_TASK.md`

## 每完成一个子步骤后必须做
1. 更新 `OPTIMIZATION_LOG.md`
2. 如果还有未结案内容，更新 `TODO_NOT_FIXED.md`
3. 如果用户新加了硬性规则，更新 `MEMORY.md`

## 如果对话快被截断
1. 先不要继续写代码
2. 先更新：
   - `CURRENT_TASK.md`
   - `OPTIMIZATION_LOG.md`
   - `TODO_NOT_FIXED.md`
3. 如果 Git 已配置远程，就优先推送这些文档

## 当前最应该继续的方向
- 继续处理主窗口小窗布局
- 目标不是“有一点改善”，而是“数据区明显变大，用户一眼就看出来”

