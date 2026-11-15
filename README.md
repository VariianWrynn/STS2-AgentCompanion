# STS2 本地 AI 队友

一个真人和一个本地规则 AI 使用独立玩家实体进行双人游戏。AI 通过游戏动作队列控制自己的手牌与资源，处理奖励、商店和事件。首版固定两名玩家，不需要模型 API。

源码来自固定版本的 coop-mod C# 实现，已移除 LLM、HTTP、人工批准、测试地图和事件池排除。来源及许可见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 当前验证状态

原适配目标为 Windows STS2 v0.113.0。当前已对 v0.113.1 构建和运行验证，继续开发以实际安装版本为准。构建通过不代表玩法已验证。

v0.113.1 已实测创建双人局、两场战斗胜利、独立能量与手牌、奖励、地图投票、商店购物与删牌，以及重启和同进程续局。事件、休息、宝箱、特殊选牌及完整通关尚未完成运行验证。当前产物是 0.1.0 开发版。

## 构建

需要 .NET 9 或更新 SDK、游戏程序集，以及项目声明的 Godot SDK 和编译期 Publicizer 依赖。

```powershell
./scripts/build.ps1 -Sts2DataDir 'E:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64'
```

产物位于 `artifacts/build/LocalAiTeammate/`。构建不会自动安装或启动游戏。

## 安装与进入

关闭游戏后，把 `LocalAiTeammate.dll`、`LocalAiTeammate.json` 和 `config` 文件夹放入游戏的 `mods/LocalAiTeammate/`。在原生游戏设置底部的模组设置中启用 Local AI teammate，再重启游戏。

测试时使用独立空存档。主菜单新增 `Play with AI Teammate` 入口，在设置界面选择真人和 AI 的角色。AI 在自己的行动窗口自动行动。状态提示会说明等待或暂停原因。

开发验证工具将在独立的后续 PR 提供。详细截图、日志和会话记录保留在本地，不随源码发布。

## 设计与边界

本地第二玩家使用同进程 loopback 会话，没有第二个 Steam 客户端。当前实现不提供远程真人与本地 AI 混合组队。

规则 AI 的质量不等于最优策略。未适配的交互必须记录为阻塞，不能靠删事件或修改游戏结果推进。事件、商店、友军目标和续局都需要分别验证，不能以主菜单可见替代。
