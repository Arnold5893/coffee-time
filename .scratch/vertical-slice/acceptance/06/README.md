# 06 票验收 · 做饮品系统（领域层）

纯领域层，不接 UI / 场景表现。拿铁线 4 款 SKU，28 条新用例（全仓 89 条全绿）。

## 用例分布

| 测试类 | 条数 | 覆盖 |
| --- | --- | --- |
| `RecipeConfigTests` | 8 | FR-MAKE-04/05 配置化、四款 SKU、步骤 6–9、判定步数、门槛指向、坏配置拦截 |
| `DrinkMakerTests` | 20 | FR-MAKE-01/03 五种原语、FR-EVAL-01/02/04 偏差与四档、门槛、提前出杯、双驱动 |
| 其余（04/05 票） | 61 | 回归未破 |

`editmode_results.xml` 是完整报告。

## 关键设计（详见 `.scratch/vertical-slice/issues/06-drink-making-domain.md`）

- **配方只写一份**（`Resources/Config/recipes.json`）：手动驱动读它，老板的自动驱动也读它，
  差别只在"谁在推进"——手动要一步步发操作请求，自动按 `cost` 计时。
- **五种原语**：`Hold / Click / Select / Place / Wait`。每次操作请求先过 `Guard()`：
  已出杯 → `Served`；待决定重做 → `Invalid`；原语不符 → `NotCurrent`；门槛没开 → `Blocked`。
- **判定口径**：只统计 `judged=true` 的步骤（等待型一律不判），错误率 = 错误步数 ÷ 判定步数；
  缺任一必需步直接非常差，不参与错误率。
- **门槛不判错**：`Blocked` 不写任何记录，测试用"跳过萃取再取杯"断言 `ErrorSteps` 不变。
- **偏差后可选择**：判偏 → `PendingRetry`，`RetryStep()` 重做（抹掉偏差）或 `Continue()` 带着走。
