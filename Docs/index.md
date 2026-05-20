---
_layout: landing
---

# 游戏服务文档

本文档包含游戏中各个核心服务的说明和使用指南。这些服务通过接口定义，由 Zenject 注入，实现了模块间的解耦，便于测试和维护。

## 项目简介

这是一款基于六边形网格的回合制策略游戏，玩家可以探索地图、建造城市、发展科技文化、指挥单位进行战斗。游戏采用模块化架构，核心逻辑以服务接口形式提供，包括地图数据访问、单位管理、科技文化系统、输入处理、卡牌系统等。本文档旨在帮助开发人员理解各服务的职责、依赖关系和用法。

## 服务列表

- [IMapDataService](services/IMapDataService.md) – 地图数据访问（六边形坐标、地块信息、邻居查询等）
- [IUnitService](services/IUnitService.md) – 单位管理（获取玩家/敌方单位、AI 势力范围等）
- [ITechCultureService](services/ITechCultureService.md) – 科技与文化系统（点数、等级、进度更新）
- [IInputService](services/IInputService.md) – 输入处理（鼠标、键盘、射线检测、UI遮挡）
- [IUnitMovement](services/IUnitMovement.md) – 单位移动接口（移动、攻击移动、路径查询）
- [ICardService](services/ICardService.md) – 卡牌系统核心逻辑（卡槽管理、卡牌生成）
- [IGameStateMachine](services/IGameStateMachine.md) – 回合状态机（当前回合、阶段切换）
- [IUIManagerView](services/IUIManagerView.md) – UI 视图接口（更新科技文化显示、单位信息面板）
- [IUnitRepository](services/IUnitRepository.md) – 单位数据仓库（存储玩家和敌方单位）
- [IMeshGenerator](services/IMeshGenerator.md) – 地图网格生成服务（用于生成地形、河流、迷雾等）