# 可直接复制给新 AI 的模板

## 模板 1：新窗口 / 新 AI 接手模板

```text
请先不要猜测，也不要直接开始改代码。

这是一个正在持续优化的 WinForms 项目，请你先读取项目根目录下这些文件，并严格按里面的状态继续：
1. PROJECT_CONTEXT.md
2. MEMORY.md
3. CURRENT_TASK.md
4. TODO_NOT_FIXED.md
5. OPTIMIZATION_LOG.md
6. HANDOVER_FOR_OTHER_AI.md

读取后请先输出：
1. 当前阶段
2. 已确认完成到哪一步
3. 还没完成的关键问题
4. 你准备先处理哪一个问题

硬性要求：
- 不要遗漏历史要求
- 不要把“已尝试”说成“已修好”
- 开始执行前，先把你要做的内容写回 CURRENT_TASK.md
- 每完成一个部分，就更新 OPTIMIZATION_LOG.md
- 如果还有没做完的，更新 TODO_NOT_FIXED.md
- 如果中途可能被截断，优先先保存文档再继续
```

## 模板 2：继续当前任务模板

```text
继续这个项目，不要从头分析。

先读取：
1. CURRENT_TASK.md
2. TODO_NOT_FIXED.md
3. OPTIMIZATION_LOG.md
4. MEMORY.md

然后只继续 CURRENT_TASK.md 里当前正在做的那一项，不要擅自切去别的问题。

执行规则：
- 先记录计划，再动手
- 做完一个小阶段，就更新日志
- 改完源码后，如果项目有安装版同步链路，也要同步安装版
- 最后明确写出：
  - 这次完成了什么
  - 还剩什么
  - 下一个窗口接着做什么
```

## 模板 3：中断前强制保存模板

```text
现在先不要继续实现新功能。

请立即做“中断前保存”：
1. 更新 CURRENT_TASK.md
2. 更新 OPTIMIZATION_LOG.md
3. 更新 TODO_NOT_FIXED.md
4. 如果有新的用户硬性要求，更新 MEMORY.md
5. 在 HANDOVER_FOR_OTHER_AI.md 里写清楚：
   - 当前做到哪一步
   - 哪一步还没做完
   - 改了哪些文件
   - 下一步必须先做什么

如果当前项目已经接了 Git 远程，请优先推送这些文档，再结束当前会话。
```

## 模板 4：检查是否遗漏用户要求模板

```text
请不要直接开始新改动。

先读取：
1. MEMORY.md
2. CURRENT_TASK.md
3. TODO_NOT_FIXED.md
4. OPTIMIZATION_LOG.md

然后帮我检查：
1. 用户的硬性要求是否有遗漏
2. 当前是否有“以为做了但其实没做完”的项目
3. 当前是否有已经回归的问题
4. 下一步最应该优先处理哪一项

请按“已完成 / 未完成 / 回归风险 / 下一步”四部分输出。
```

