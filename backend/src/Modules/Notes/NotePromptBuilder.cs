using Notes.Contracts;

namespace Notes;

/// <summary>
/// 只描述「导入讲义需要什么格式」，不规定讲什么、分几节、写多少。
/// 内容由用户和自己的智能体商量，这里只保证粘回来的 JSON 能被解析。
/// </summary>
internal static class NotePromptBuilder
{
    public static NoteImportPromptDto Build() => new(Prompt);

    private const string Prompt = """
## 输出格式

一份讲义是一个 JSON 对象，不要输出 JSON 以外的解释：

{
  "formatVersion": 1,
  "title": "讲义标题",
  "subtitle": "一句话说明这份讲义的范围",
  "tags": ["数学"],
  "visibility": "public",
  "year": "高一",
  "semester": "上",
  "notice": {
    "tone": "tip",
    "text": "阅读前的提示（Markdown，可省略）"
  },
  "sections": [
    {
      "id": "ch1",
      "label": "CH 01",
      "title": "第一节标题",
      "subtitle": "本节内容概述",
      "blocks": [
        {
          "type": "card",
          "title": "知识点标题",
          "tone": "blue",
          "blocks": [
            { "type": "paragraph", "text": "正文（Markdown）" },
            {
              "type": "callout",
              "tone": "tip",
              "text": "**重点**：……"
            }
          ]
        }
      ]
    }
  ]
}

## 字段规则

- `formatVersion`：必须是 `1`
- `title`：1–120 字
- `subtitle`：可选，≤500 字
- `tags`：字符串数组，最多 30 项，每项 ≤40 字；公开讲义至少需要 1 个标签；建议用学科名，例如 语文、英文、数学、物理、化学、生物、历史、地理、公民、企管
- `visibility`：`public` 或 `private`，缺省 `public`
- `year`：可选，≤20 字，例如 `高一`、`2025-2026`
- `semester`：可选，只能是 `上` 或 `下`
- `notice`：可选；`tone` 是 `tip` / `warn` / `note` / `example`，`text` 是 Markdown
- `sections`：1–60 节；每节 `id` 非空且唯一，`title` 非空，`subtitle` 可选
- 每节 `blocks` 是块数组；块可以嵌套，最多 8 层；整份 JSON ≤ 4 MB

## 块类型

块必须有 `type`，只允许下面这些：

1. `card`：内容卡片
   { "type": "card", "title": "标题", "tone": "blue", "blocks": [] }
   `tone`：`blue`（默认）/ `teal` / `amber` / `purple`

2. `heading`：卡片内小标题
   { "type": "heading", "level": 4, "text": "小标题" }

3. `paragraph`：段落
   { "type": "paragraph", "text": "正文", "size": "normal" }
   `size`：`normal`（默认）/ `small`

4. `list`：列表
   { "type": "list", "ordered": true, "items": ["第一条", "第二条"] }

5. `table`：普通表格
   {
     "type": "table",
     "headers": ["列一", "列二"],
     "widths": [130, null],
     "rows": [["单元格", "单元格"]]
   }
   `widths` 可省略，数字是像素提示，`null` 表示自适应。

6. `table` + `variant: "matrix"`：矩阵表（左侧是行标题）
   {
     "type": "table",
     "variant": "matrix",
     "headers": ["列一", "列二"],
     "rows": [{ "header": "行标题", "cells": ["单元格", "单元格"] }]
   }

7. `flow`：流程箭头
   { "type": "flow", "items": ["第一步", "第二步", "第三步"] }

8. `callout`：提示框
   { "type": "callout", "tone": "tip", "label": null, "text": "内容" }
   `tone`：`tip` / `warn` / `note` / `example`；`example` 建议写 `label: "例"`

9. `quote`：引用
   { "type": "quote", "text": "引文", "source": "出处" }

10. `grid`：并排小格
    { "type": "grid", "columns": 2, "items": [{ "title": "小标题", "blocks": [] }] }
    `columns` 只能是 2 或 3。

11. `quickGroup`：速记小卡
    {
      "type": "quickGroup",
      "label": "CH 01",
      "title": "这一组小卡的主题",
      "items": [{ "title": "卡片标题", "text": "卡片内容", "tone": "teal" }]
    }
    `tone`：`teal`（默认）/ `blue` / `purple` / `amber` / `red` / `green`

12. `summary`：总结卡
    { "type": "summary", "title": "总结标题", "blocks": [] }

13. `figure`：内联 SVG 图
    { "type": "figure", "svg": "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 680 300\">...</svg>", "caption": "图注" }
    `svg` 必须是完整、自包含的 `<svg>…</svg>` 字符串，不要脚本、外部图片或外部字体；`caption` 可选。

14. `chips`：色标图例
    {
      "type": "chips",
      "items": [{ "label": "红灯", "note": "38–45 过热", "tone": "red" }]
    }
    `tone`：`red` / `amber` / `green` / `blue` / `navy`

## 文本格式

- 所有文本字段都是 Markdown：`**粗体**`、`*斜体*`、`` `代码` ``、链接、`$行内公式$`、`$$独立公式$$`
- 除 `figure.svg` 外，不要出现任何 HTML 标签
- 段内换行用「行尾两个空格 + 换行」，不要写 `<br>`
- 表格用 `table` 块表达，不要用 Markdown 表格
- 不要在 JSON 里写注释或尾逗号

## 输出前自检

- 是合法 JSON，能被 `JSON.parse` 解析
- `formatVersion` 是 1
- `sections` 非空，每节 `id` 唯一、`title` 非空
- 所有块的 `type` 都在上面的清单里
- `callout.tone`、`card.tone`、`quickGroup.items[].tone`、`chips.items[].tone` 都是允许值
- `semester` 只能是 `上` 或 `下`
- 公开讲义的 `tags` 至少 1 个
- `figure.svg` 是完整的 `<svg>`，且不含 `<script>`
""";
}
