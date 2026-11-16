# 本地第二玩家阶段验证记录

目标是让本地规则 AI 作为独立第二玩家完成双人流程。本次已验证第一章首领胜利、进入第二章及保留各自资源。完整通关仍未验证。

## 环境与构建

实测平台为 Windows，实际安装游戏为 v0.113.1，游戏提交为 d09d48e5。原目标 v0.113.0 未在本次运行验证。测试使用此前为空的独立 profile2，并保留现有其他模组。此结果不代表纯净模组环境。

游戏启动、输入和截图均针对核对过进程身份的测试窗口。没有修改存档、地图、事件池或战斗结果来推进。正常保存退出后才安装下一构建。

| 测试会话目录 | 已载入 DLL 的 SHA256 | 范围 |
| --- | --- | --- |
| `local-ai-20261116-001149-379b7c41` | `14359B9AECA9C071449A325E46143B74D311B4F66489C526DA02497A76D321A8` | 基线构建的五场战斗、休息、宝箱及鸟巢事件 |
| `local-ai-20261116-051457-cdd83fda` | `3A0DA374B12DFA9AACB438BC08F32B4D3C58C8E833E5C4B5B2C76E7C3E5F3718` | 安装 PR #4、#5 后续局，商店、孵化、VANTOM_BOSS 胜利和第二章先古事件 |

基线源码见 [PR #2](https://github.com/VariianWrynn/STS2-AgentCompanion/pull/2)，验证工具见 [PR #3](https://github.com/VariianWrynn/STS2-AgentCompanion/pull/3)。第二会话使用 PR #5 提交 cb5ddbc，其中包含 PR #4。此文档分支自身不包含两个修复。

## 已观察到的行为

| 流程 | 结果 | 本地证据文件 |
| --- | --- | --- |
| 普通战斗 | 基线会话赢下甲虫、Inklets、Mawler、Burglars。AI 独立出牌并结束回合。 | `beetle-victory`、`inklets-victory`、`mawler-victory`、`burglars-victory` 的图像及 JSON |
| 精英战斗 | ByrdoniElite 中真人第5回合倒地。AI 第6回合继续出牌并获胜，战后真人恢复至7HP。 | `byrdoni-victory`、`game.log` |
| 友军药水 | Burglars 中 AI 使用自己的格挡药水指向真人。真人获得12格挡，自有药水仍保留。 | `ally-block-potion`、`game.log` |
| 休息 | 真人与 AI 分别从7到31、41到65HP。随后两处休息也完成。 | `rest-heal-complete`、`post-rest-checkpoint.json` |
| 宝箱 | 真人获得 ORICHALCUM，AI 获得 RED_MASK。 | `treasure-complete`、`post-treasure-checkpoint.json` |
| 鸟巢事件 | 真人选 TAKE，牌组加入 BYRDONIS_EGG，生命上限80。AI 选 EAT，生命上限升至87，未获得蛋。 | `independent-event-complete`、`post-event-checkpoint.json` |
| 修复构建续局 | 正常保存退出、换装构建和重启后恢复同一测试档。真人在休息点孵化鸟蛋，AI 独立完成 HEAL。 | 第二会话的 `development-plan.json`、`rest-hatch-complete`、`post-hatch-checkpoint.json` |
| 首领战斗 | VANTOM_BOSS 第10回合获胜。AI 独立打出两张牌完成击杀，真人该回合未出牌。 | 第二会话的 `vantom-boss-victory`、`game.log` |
| 首领奖励与跨章 | 双方各自获得 DEMON_FORM，进入第二章。检查点为真人114金币、50HP、22张牌，AI108金币、37HP、16张牌。 | `post-boss-checkpoint.json` |
| 第二章先古事件 | 真人获得 PAELS_GROWTH，DISMANTLE 附魔 CLONE。AI 获得 PAELS_TEARS。双方生命分别恢复80、87，结果已在下一房间存档核对。 | `post-pael-checkpoint.json` |

首领奖励前 AI 在商店消费后剩8金币，领取自己的100金币后为108金币。真人购买四张牌后剩14金币，领取100金币后为114金币。第二章已进入首个战斗，仅证明跨章及继续行动，不代表该战斗已胜利。

基线会话审计为92次提交、92次结算、零错误 actor。其中54次匹配的出牌前后快照显示 AI 资源减少、真人资源不变。第二会话在第二章首战第一回合结束 AI 行动后为48次提交、48次结算、零错误 actor，其中26次匹配资源观察。审计计数对应这些截点，并非所有动作隔离或完整通关的证明。

## 规划修复的检查

[PR #4](https://github.com/VariianWrynn/STS2-AgentCompanion/pull/4) 修正新增敏捷给非格挡动作虚构格挡。协调者独立运行生产方法检查，基线6项失败，修复后10项通过。

[PR #5](https://github.com/VariianWrynn/STS2-AgentCompanion/pull/5) 修正火焰、毒、虚弱等药水被虚构为施加易伤。协调者独立运行生产方法检查，前一构建10项失败，修复后25项通过。该检查将无关评分器替换为中性结果，以避免游戏外加载 Godot 原生接口。被验证的生产规划方法保持原样。

这些检查不等于完整规划器或所有卡牌效果均正确。修复构建已运行并赢下上述首领战，但未在游戏中分别构造敏捷和各类药水的全部回归场景。

## 尚未验证或解决

- 完整通关、其他角色组合、全部事件、共享事件投票和全部远端特殊选牌。
- 药水直接伤害、滑溜和群体状态的完整模拟。
- 伤害与施加易伤的顺序。已有代码审查线索，尚未完成回归复现及修复。
- 零能量时负评分药水与结束回合的选择质量。
- 休息选项可用性和 MEND 远端选牌。当前已测休息没有卡死，不能把未证实风险写成已复现缺陷。

## 复核方法

证据只保留在项目的 `artifacts/verification/`，不上传包含玩家标识的日志、图像或存档。以上文件名均相对于对应会话目录。

按 [玩法验证说明](gameplay-verification.md) 检查实际版本、独立测试档和进程身份，然后运行 `scripts/read-local-ai-audit.ps1 -RunDir <会话目录>`。把审计输出与原生画面、游戏日志和只读存档检查点共同核对。清理记录或主菜单检查不能代替玩法验证。
