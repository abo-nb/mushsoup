using System.Text;

namespace Questions;

internal sealed record ImportPoolPromptInfo(string Id, string Name, int AvailableCount);

/// <summary>
/// 只描述「导入需要什么格式」，不规定出什么题、出多少、怎么出。
/// 题目内容由用户和他的智能体商量，这里只保证粘回来的东西能被解析。
/// </summary>
internal static class ImportPromptBuilder
{
    private const string Template = """
## 输出要求

- 只输出一个 JSON 文档，前后不要加解释、总结或追问。
- 可以用 ```json 围栏包住，围栏外不要再写别的文字。
- 单次导入最多 10000 道题，提交内容不超过 50MB。
- 字段名区分大小写，字符串用双引号。导入器能容忍尾逗号、注释和围栏外的少量文字，但不要依赖这些宽松处理。
- 本文只规定输出格式与导入规则，不规定题目内容与数量。

## 整体结构

- 推荐顶层写成对象 `{ "formatVersion": 1, "questions": [ ... ] }`；也接受直接输出一个题目数组，但对象形式便于携带版本号，优先用它。
- `formatVersion`：整数，固定写 `1`。写其他值会按 1 解析并给出警告；不写或不是整数时按 1 处理。
- `questions`：必填数组，每项是一道题（必须是 JSON 对象）；缺失或不是数组会直接报错，数组里的非对象项会按单题报错。
- 顶层对象和题目对象里的未知字段会被忽略，不报错。

## 题目字段

- `clientId`（可选，字符串）：仅用于导入记录对照，不写入题目内容；建议同批唯一，系统不校验重复。
- `type`（必填，字符串）：`single` 单选、`multiple` 多选，忽略大小写和首尾空格。兼容写法：`single_choice`、`singlechoice`、`单选`、`multiple_choice`、`multiplechoice`、`多选`。缺失或无法识别时该题报错，不会导入。
- `stem`（必填，字符串，Markdown）：去掉首尾空白后 1–4000 字符；为空或超长都报错。
- `options`（必填，数组）：
  - 普通题 2–10 项；使用选项池时 1–10 项，且只写正确答案选项。数量越界报错。
  - 每项可写 `{ "key": "A", "text": "选项内容" }`，也可直接写字符串 `"选项内容"`；纯字符串按数组位置自动分配 key：A、B、C…（按位置分配，混用两种写法时同样如此）。
  - `key` 同题内不能重复，重复的项会报错并被丢弃；`text` 去空白后不能为空，为空的项会报错并被丢弃。`key` 和 `text` 的首尾空白会被去掉。
  - 数组里既不是字符串也不是对象的项会被忽略；`options` 缺失或不是数组时按 0 项处理，触发数量不足错误。
- `answer`（必填，字符串数组）：填正确选项的 `key`，不要填选项文本。
  - 不能为空；每个 key 都必须出现在本题自身的 `options` 里，否则报错。
  - 单选：恰好 1 个。
  - 多选：至少 2 个，且严格少于「自身选项数 + 选项池抽取数」；不用选项池时就是少于自身选项数，即不能把所有选项都设为答案。
  - `answer` 里不要重复写同一个 key，系统按数组长度计数。也接受写成单个字符串（会转成单元素数组）；空字符串和空项会被忽略。
- `shuffle`（可选，默认 `follow`）：布尔值 `true` 等同 `always`，`false` 等同 `never`。字符串只认 `follow`（跟随题库设置）、`always`（总是打乱）、`never`（从不打乱），区分大小写；其他写法按 `follow` 处理，不给警告。
- `tags`（可选，字符串数组）：最多 10 个，超出报错。重复项自动去重，空字符串忽略，也可写成单个字符串。尽量用「可用标签」里的原样写法；不在题库已有标签里会给警告，但照常导入（题库目前没有任何标签时不提示）。
- `chapterPath`（可选，字符串数组）：从大章到小章，最多 3 层；每层去空白后不能为空（空层直接丢弃）且不超过 40 字；超过 3 层或某层超长报错。不写、空数组或全是空字符串都表示不归章节。优先用「可用章节」里已有的路径，同一知识点保持路径一致，方便筛选统计。
- `difficulty`（可选，整数 1–5，默认 3）：可写数字，也可写数字字符串（如 `"2"`）。超出 1–5 会按 3 处理并给出警告；无法解析成整数时直接按 3 处理，不给警告。
- `source`（可选，字符串）：来源说明，纯文本，如教材名、章节、页码或链接。
- `explanation`（可选，字符串，Markdown）：不写或只有空白会给「没有写解析」的警告，但不阻止导入；建议每题都写。
- `pool`（可选，对象）：引用选项池，规则见下一节；题库没有可用选项池时不要输出这个字段。

### 使用选项池

- 写法：`{ "id": "pool_xxxxxx", "name": "池子名称", "count": 2 }`。
- `id` 与 `name` 至少写一个，`id` 优先：先按 `id` 精确匹配，找不到再按 `name` 精确匹配（首尾空白忽略）。两者都找不到会报「选项池不存在」，该题不会导入。
- `count` 必填，整数 1–9，可写数字字符串；缺失或越界报错。
- 使用时 `options` 只写正确答案选项（1–10 个），`answer` 只引用这些自身选项的 key；系统每次组卡会从池中随机抽 `count` 个干扰项追加进来，所以同一道题每张卡的干扰项可以不同。
- 自身选项数 + `count` 必须 ≤ 10，否则报错。
- 池子的可用条目必须不少于 `count + 1` 条：与本题自身选项文本规范化后相同的池条目会被排除，所以「可用 N 条」是池子的上限参考，`count` 要留出余量；不足时该题报错。
- 不写 `pool` 的题，用自身 `options` 组成整张卡的选项。

## 导入行为

- 流程是先校验、出预览，再确认导入。报错的题不会导入；只有警告的题照常导入。
- 重复题：题干与选项文本规范化后（忽略空白、标点、全角半角、大小写差异，选项顺序无关）与题库已有题或本批前面的题相同，默认跳过；导入页可勾选「重复题也导入」强制导入。
- 以下情况会给警告，但不会阻止导入：
  - 同一批里至少 5 道单选时，超过 60% 的答案落在同一个位置；
  - 单选题有 3 个以上选项时，正确选项达到 12 字且长度超过其余选项长度中位数的 1.6 倍（正确选项明显更长）；
  - `stem`、`options[].text` 或 `explanation` 里 `$` 或 ``` 出现奇数次（未闭合）；
  - 题目缺少 `explanation`；
  - 标签不在题库已有标签里。

## 文本格式

- `stem`、`options[].text`、`explanation` 按 Markdown 渲染，支持 GFM：列表、表格、代码块、粗体、删除线等。
- 公式用 `$...$`（行内）和 `$$...$$`（独立成行），按 KaTeX 渲染，`$` 必须成对；代码围栏 ``` 也要成对。
- 图片写 `![说明](地址)`。站内已上传图片的地址是 `asset://<资产 id>`，只有确实从站内拿到 id 时才用；不要编造 asset id，不确定时省略图片。
- JSON 字符串里的反斜杠要转义成两个：`\frac` 要写成 `\\frac`，`\alpha` 写成 `\\alpha`；换行写 `\n`，双引号写 `\"`。

## 示例

### 普通单选题

```json
{
  "formatVersion": 1,
  "questions": [
    {
      "clientId": "q1",
      "type": "single",
      "stem": "在 TCP 三次握手中，客户端发送的第一个报文段的标志位是（　）",
      "options": [
        { "key": "A", "text": "SYN" },
        { "key": "B", "text": "ACK" },
        { "key": "C", "text": "FIN" },
        { "key": "D", "text": "RST" }
      ],
      "answer": ["A"],
      "shuffle": "follow",
      "tags": ["计算机网络"],
      "chapterPath": ["计算机网络", "传输层"],
      "difficulty": 2,
      "source": "《计算机网络：自顶向下方法》第 3 章",
      "explanation": "客户端先发送 `SYN` 请求建立连接，服务端回复 `SYN+ACK`，客户端再回 `ACK` 完成握手。"
    }
  ]
}
```

### 普通多选题

```json
{
  "formatVersion": 1,
  "questions": [
    {
      "clientId": "q2",
      "type": "multiple",
      "stem": "下列关于函数 $f(x)=x^2$ 的说法，正确的有（　）",
      "options": [
        { "key": "A", "text": "在 $x=0$ 处取得最小值" },
        { "key": "B", "text": "在 $\\mathbb{R}$ 上单调递增" },
        { "key": "C", "text": "图象关于 $y$ 轴对称" },
        { "key": "D", "text": "导数为 $2x$" }
      ],
      "answer": ["A", "C", "D"],
      "shuffle": true,
      "tags": ["函数", "导数"],
      "chapterPath": ["高等数学", "导数与微分"],
      "difficulty": 3,
      "explanation": "$f(x)=x^2$ 在 $x=0$ 处取最小值 0；$f'(x)=2x$ 在 $x<0$ 时为负，所以不单调递增；图象关于 $y$ 轴对称。"
    }
  ]
}
```

### 使用选项池的单选题（选项池只给正确答案，干扰项由系统抽取）

```json
{
  "formatVersion": 1,
  "questions": [
    {
      "type": "single",
      "stem": "函数 $f(x)=x^3$ 的导数是（　）",
      "options": ["$3x^2$"],
      "answer": ["A"],
      "pool": { "id": "pool_xxxxxx", "count": 2 },
      "tags": ["导数"],
      "chapterPath": ["高等数学", "导数与微分"],
      "difficulty": 2,
      "explanation": "由幂函数求导公式 $(x^n)' = nx^{n-1}$，得 $f'(x)=3x^2$。"
    }
  ]
}
```

### 使用选项池的多选题

```json
{
  "formatVersion": 1,
  "questions": [
    {
      "type": "multiple",
      "stem": "下列函数在 $x=0$ 处连续的有（　）",
      "options": [
        { "key": "A", "text": "$y=\\sin x$" },
        { "key": "B", "text": "$y=|x|$" }
      ],
      "answer": ["A", "B"],
      "pool": { "id": "pool_xxxxxx", "name": "函数性质干扰项", "count": 2 },
      "difficulty": 4,
      "source": "自编",
      "explanation": "$\\sin x$ 与 $|x|$ 在 $x=0$ 处都有定义，且极限等于函数值。"
    }
  ]
}
```

示例里的 `pool_xxxxxx` 只是占位，必须换成「可用选项池」里列出的 id；没有可用的池就删掉 `pool` 字段，按普通题写。示例里的标签和章节用于演示写法，实际以「可用标签」「可用章节」为准。

## 可用标签

@@TAGS@@

## 可用章节

@@CHAPTERS@@

## 可用选项池

@@POOLS@@

## 输出前自检

- 是合法 JSON，或整段包在 ```json 围栏里且围栏外没有多余文字。
- `questions` 是非空数组，单次不超过 10000 题、总量不超过 50MB。
- 每题 `type` 是 `single` / `multiple` 或其兼容写法。
- `stem` 去空白后长度在 1–4000 之间。
- `options` 数量正确：不用池 2–10 项，用池 1–10 项；key 不重复，text 非空。
- `answer` 非空，每个 key 都来自本题 `options`；单选恰好 1 个；多选至少 2 个且少于自身选项数 + 抽取数。
- `pool` 的 `id` / `name` 来自「可用选项池」，`count` 是 1–9 的整数，自身选项数 + `count` ≤ 10，池子排除重复后仍有至少 `count + 1` 条。
- `tags` 不超过 10 个，且尽量取自「可用标签」。
- `chapterPath` 不超过 3 层，每层非空且不超过 40 字。
- `difficulty` 是 1–5 的整数，或省略。
- 反斜杠已转义：`\frac` 写成 `\\frac`，`\alpha` 写成 `\\alpha`。
- 没有未闭合的 `$` 和 ``` 代码围栏。
- 写了 `explanation`；同一批的单选题答案位置已经打散。

""";

    public static string Build(
        IReadOnlyList<string> tags,
        IReadOnlyList<IReadOnlyList<string>> chapters,
        IReadOnlyList<ImportPoolPromptInfo> pools)
    {
        var tagList = tags.Count > 0
            ? string.Join("、", tags)
            : "（暂无，可留空）";
        var chapterList = FormatChapters(chapters);
        var poolList = FormatPools(pools);

        return Template
            .Replace("@@POOLS@@", poolList)
            .Replace("@@CHAPTERS@@", chapterList)
            .Replace("@@TAGS@@", tagList);
    }

    private static string FormatPools(IReadOnlyList<ImportPoolPromptInfo> pools)
    {
        if (pools.Count == 0)
        {
            return "（题库还没有选项池，不要输出 pool 字段）";
        }

        var builder = new StringBuilder();
        foreach (var pool in pools)
        {
            var name = string.IsNullOrWhiteSpace(pool.Name) ? pool.Id : pool.Name;
            builder.AppendLine($"- {name}（id: {pool.Id}，可用 {pool.AvailableCount} 条）");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatChapters(IReadOnlyList<IReadOnlyList<string>> chapters)
    {
        if (chapters.Count == 0)
        {
            return "（暂无，可自行按「大章 / 小章」组织）";
        }

        var root = new ChapterNode("");
        foreach (var chapter in chapters)
        {
            var node = root;
            foreach (var segment in chapter)
            {
                node = node.GetOrAdd(segment);
            }
        }

        var builder = new StringBuilder();
        WriteNode(root, 0, builder);
        return builder.ToString().TrimEnd();
    }

    private static void WriteNode(ChapterNode node, int depth, StringBuilder builder)
    {
        foreach (var child in node.Children)
        {
            builder.Append(' ', depth * 2);
            builder.Append("- ").AppendLine(child.Name);
            WriteNode(child, depth + 1, builder);
        }
    }

    private sealed class ChapterNode(string name)
    {
        public string Name { get; } = name;

        public List<ChapterNode> Children { get; } = [];

        public ChapterNode GetOrAdd(string childName)
        {
            var existing = Children.FirstOrDefault(child =>
                string.Equals(child.Name, childName, StringComparison.Ordinal));
            if (existing is not null)
            {
                return existing;
            }

            var created = new ChapterNode(childName);
            Children.Add(created);
            return created;
        }
    }
}
