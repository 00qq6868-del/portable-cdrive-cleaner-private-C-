# 可直接复制给新 AI / 新窗口 / 新 API 的模板

## 固定项目识别信息
- GitHub 私有仓库：`https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git`
- 本地源码目录：`<LOCAL_REPO_PATH>`
- 实际安装版：`<INSTALLED_EXE_PATH>`
- 当前项目类型：WinForms 桌面程序，不是 Web 项目

## 模板 1：GitHub 可访问接力模板

```text
这是一个已经接入 GitHub 私有仓库、要求“跨 AI / 跨窗口不中断继续”的 WinForms 项目。

项目固定信息：
- GitHub 私有仓库：https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git
- 本地源码目录：<LOCAL_REPO_PATH>
- 实际安装版：<INSTALLED_EXE_PATH>

请严格按下面流程执行，不要跳步，不要假装已完成。

第一步：如果你能访问本地仓库或 Git 仓库，先执行
git pull --rebase origin main

第二步：读取这些文件，顺序不能乱
1. AI_STATE.json
2. PROJECT_CONTEXT.md
3. MEMORY.md
4. CURRENT_TASK.md
5. TODO_NOT_FIXED.md
6. OPTIMIZATION_LOG.md
7. HANDOVER_FOR_OTHER_AI.md
8. SOURCE_CHANGE_LEDGER.md
9. INTERRUPTED_PROGRESS.md
10. AI_PROMPT_TEMPLATES.md
11. GITHUB_SYNC_CHECKLIST.md

第三步：读取后先输出这 6 项
1. 当前阶段
2. 已完成到哪一步
3. 正在进行但被打断的任务是什么
4. 已修改过哪些源码文件
5. 还没进行的方案有哪些
6. 你准备继续哪一个任务

第四步：开始执行前必须先做
- 先更新 CURRENT_TASK.md
- 如果涉及源码修改方案，更新 SOURCE_CHANGE_LEDGER.md
- 如果任务延续自上一次被打断状态，先更新 INTERRUPTED_PROGRESS.md
- 先写一个 checkpoint，再开始实现

第五步：执行过程中的硬性要求
- 每完成一个部分，就更新 OPTIMIZATION_LOG.md
- 已完成、未完成、待验证、被打断都必须记录
- 每次关键节点使用：
  powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Progress -Task "当前任务名" -Summary "当前已完成的这一步" -Push
- 如果可能被截断，优先执行：
  powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Interrupt -Task "当前任务名" -Summary "中断前保存当前进度" -IncludeCode -Push

第六步：绝对不要做的事
- 不要把“已尝试”说成“已修好”
- 不要只改聊天回答不改项目文档
- 不要忽略历史硬性要求
- 不要丢掉正在进行但被打断的进度
- 不要跳过 AI_STATE.json 和 checkpoints
```

## 模板 2：GitHub 不可访问回退模板

```text
你现在可能无法直接访问这个项目的 GitHub 私有仓库。

项目固定信息：
- GitHub 私有仓库：https://github.com/00qq6868-del/portable-cdrive-cleaner-private-C-.git
- 本地源码目录：<LOCAL_REPO_PATH>
- 实际安装版：<INSTALLED_EXE_PATH>

如果你拿不到私有仓库，请不要假装已经读过。
请明确告诉我：你现在需要我粘贴下面这些文件的完整内容，再继续接手：
1. AI_STATE.json
2. CURRENT_TASK.md
3. SOURCE_CHANGE_LEDGER.md
4. INTERRUPTED_PROGRESS.md
5. TODO_NOT_FIXED.md
6. 最新一个 checkpoints 文件

收到这些内容后，请先输出：
1. 当前阶段
2. 已完成到哪一步
3. 正在进行但被打断的任务
4. 已修改过哪些文件
5. 还没完成什么
6. 你准备继续哪一步

硬性要求：
- 不要因为拿不到仓库就重新从头猜
- 不要把缺失上下文伪装成已确认事实
- 继续工作前，先基于收到的内容写出接手结论
```

## 模板 3：中断前强制保存模板

```text
现在先不要继续实现新功能。

请立即做“中断前保存”，并且优先保留代码与文档检查点：
1. 更新 AI_STATE.json
2. 更新 CURRENT_TASK.md
3. 更新 OPTIMIZATION_LOG.md
4. 更新 TODO_NOT_FIXED.md
5. 如果用户新加了硬性规则，更新 MEMORY.md
6. 更新 SOURCE_CHANGE_LEDGER.md
7. 更新 INTERRUPTED_PROGRESS.md
8. 生成新的 checkpoints 快照

然后执行：
powershell -ExecutionPolicy Bypass -File .\tools\Record-Checkpoint.ps1 -Mode Interrupt -Task "当前任务名" -Summary "中断前保存当前进度" -IncludeCode -Push

结束前必须明确写出：
- 当前做到哪一步
- 哪一步还没做完
- 改了哪些文件
- 下一步必须先做什么
```

