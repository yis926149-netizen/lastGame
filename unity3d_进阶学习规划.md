**Unity 3D（U3D）专项学习规划（2026最新版）**

你好！这是一条**专注三维游戏与场景**的系统化路线，从零基础到能独立做出**可玩的 3D 小游戏**。整个计划以**Unity 6**（或稳定版 2022.3 LTS）为核心，采用**模块化 + 实战项目驱动**，每个模块配有**独立小项目**（可单独完成，也可串联成一个大项目）。本路线**不包含 2D / Tilemap / Sprite 专项**，如需 2D 可另学官方 2D Pathway。

**预计学习时间**（每天2-4小时）：  
- 前5个核心基础模块：约4-5周  
- 进阶模块（动画～发布）：约5-6周  
- 最终完整项目：2-4周  
总计 **约2-4个月** 可做出可发布的 3D 小游戏作品集（简历/作品集加分项）。

### 一、准备工作（第1天完成）
1. **安装环境**：
   - 下载 **Unity Hub**（官网 unity.com/get-unity/download）
   - 安装 **Unity 6**（推荐新手先用 2022.3 LTS 更稳定，后续升级到 Unity 6）。同时安装 **Visual Studio 2022**（Community 版免费）。
   - 推荐插件：Unity Input System、TextMeshPro、Cinemachine（Asset Store 免费）。
2. **学习工具**：
   - 官方平台：**Unity Learn**（learn.unity.com 或中文课堂 learn.u3d.cn）→ 优先完成 **Unity Essentials Pathway** 中与 3D、脚本、物理相关的单元（免费、结构化、最推荐）。
   - 中文资源：B站搜索「Unity 6 零基础 3D」或「Unity3D 教程 2026」。
   - 版本控制：立即学会 Git（GitHub Desktop），养成好习惯。
3. **学习心态**：每天动手写代码 + 看官方文档 > 纯看视频。遇到报错先 Google / Unity Forum / B站评论区。

下面按**Unity 3D 核心模块**顺序规划，每个模块包含：
- 学习内容
- 推荐资源
- **对应实战项目**（带具体实现目标、核心知识点、预计时间）

### 模块1：Unity 编辑器基础 & 界面熟悉
**学习内容**：Unity Hub、Editor布局（Scene/Game/Hierarchy/Inspector/Project/Console）、GameObject创建、Transform、材质、灯光、相机、Scene保存、Play模式、Asset Store、打包测试。  
**推荐资源**：Unity Essentials Pathway - Editor Essentials（官方，1-2小时）。B站「Unity 6 编辑器界面讲解」。  
**实战项目**：**「我的第一个3D房间」**  
- 目标：搭建一个简单室内场景（地板、墙壁、桌子、椅子、灯），调整灯光和相机角度，实现可自由漫游。  
- 核心知识点：层级管理、Prefab初步、Play测试。  
- 预计时间：2-3天。完成标志：能打包成EXE并运行。  
- 进阶挑战：导入免费Asset Store模型装饰房间。

### 模块2：C# 脚本基础（MonoBehaviour 生命周期）
**学习内容**：C# 基础（变量、函数、循环、类）、Start/Update/FixedUpdate、public/private、SerializeField、Debug.Log、Transform组件API（position、rotation、Translate、Rotate）。  
**推荐资源**：Unity Essentials - Programming Essentials + B站「Unity C# 零基础脚本教程」。  
**实战项目**：**「会动的立方体」**  
- 目标：写脚本让立方体按WASD移动、空格跳跃、鼠标旋转视角（第一人称简单版）。  
- 核心知识点：Update中输入检测、物理前准备。  
- 预计时间：3-4天。  
- 进阶挑战：添加边界限制和速度变量（Inspector可调）。

### 模块3：GameObject、Component、Prefab、场景管理
**学习内容**：组件系统、Prefab创建/实例化、DontDestroyOnLoad、多场景切换（SceneManager）、父子层级、Tag/Layer。  
**推荐资源**：Unity Learn GameObject & Prefab 部分。  
**实战项目**：**「Prefab 武器库」**  
- 目标：创建3种不同武器Prefab（剑、枪、炸弹），通过脚本在场景中随机生成/切换，并实现「拾取」销毁效果。  
- 核心知识点：Instantiate、Destroy、场景加载。  
- 预计时间：2天。  
- 进阶挑战：做成一个简单「武器选择菜单」场景切换系统。

### 模块4：物理系统（Physics）
**学习内容**：Rigidbody、Collider（Box/Sphere/Mesh）、物理材质、AddForce/AddTorque、Collision/Trigger事件、FixedUpdate。  
**推荐资源**：Unity Essentials - 3D Essentials + 官方 Roll-a-ball 教程（经典！）。  
**实战项目**：**「Roll-a-ball 经典版」（官方改版）**  
- 目标：控制小球在平台上滚动，收集12个金币，碰到陷阱重生，计时结束。  
- 核心知识点：物理移动 vs Transform移动、碰撞检测。  
- 预计时间：4-5天（强烈推荐先跟官方做一遍再自己改）。  
- 进阶挑战：添加跳跃和斜坡物理。

### 模块5：输入系统（Input System）+ 玩家控制
**学习内容**：新 Input System（Action Map、Player Input组件）、键盘/手柄/移动端输入、射线检测（Raycast）。  
**推荐资源**：Unity Learn + B站「Unity新Input System教程」。  
**实战项目**：**「第一人称/第三人称角色控制器」**  
- 目标：基于上一个球项目，换成胶囊体角色，实现平滑移动、跳跃、鼠标视角控制。  
- 核心知识点：Input Action、CharacterController 或 Rigidbody 混合使用。  
- 预计时间：3天。  
- 进阶挑战：添加冲刺和蹲下动作。

### 模块6：动画系统（Animation & Animator）
**学习内容**：Animation Clip、Animator Controller、参数、Blend Tree、IK、Root Motion。  
**推荐资源**：Unity Learn Animation 部分 + 免费 Mixamo 动画资源。  
**实战项目**：**「带动画的角色控制器」**  
- 目标：导入免费人物模型，实现Idle → Walk → Run → Jump 状态切换（参数驱动）。  
- 核心知识点：状态机、Blend Tree、动画事件。  
- 预计时间：3-4天。  
- 进阶挑战：添加攻击动画和过渡。

### 模块7：UI 系统（UGUI / UI Toolkit）
**学习内容**：Canvas、RectTransform、Button、TextMeshPro、Slider、EventSystem、Screen Space vs World Space。  
**推荐资源**：Unity Learn UI 部分。  
**实战项目**：**「完整游戏UI框架」**（接前面的角色控制器）  
- 目标：添加主菜单、暂停菜单、血条/分数HUD、Game Over界面、按钮音效。  
- 核心知识点：UI事件、场景间UI传递、分辨率适配。  
- 预计时间：4天。

### 模块8：音频 & 粒子系统（Audio & VFX）
**学习内容**：Audio Source/Mixer、3D 空间音效（距离衰减、Listener）、粒子系统（Particle System）、VFX Graph 入门。  
**推荐资源**：Unity Essentials - Audio Essentials。  
**实战项目**：**「爆炸与特效演示」**  
- 目标：在碰撞/死亡时触发粒子爆炸 + 音效（爆炸声、背景音乐、脚步声），用Audio Mixer 做音量分组。  
- 核心知识点：One Shot 音效、粒子触发、音效淡入淡出、3D 音效摆放。  
- 预计时间：3天。

### 模块9：3D 渲染 & 光照（Lighting & Rendering）
**学习内容**：URP/HDRP、Lights（Directional/Point/Spot）、Materials/Shader Graph 入门、Post Processing（Bloom、Vignette）、Skybox。  
**推荐资源**：Unity 6 渲染文档。  
**实战项目**：**「美化场景」**（接前面所有项目）  
- 目标：为房间/关卡添加动态光照、实时反射、后处理特效，让画面「高级起来」。  
- 核心知识点：光照烘焙 vs 实时、URP设置。  
- 预计时间：2-3天。

### 模块10：AI & 寻路（NavMesh）
**学习内容**：NavMesh Agent、NavMesh Bake、简单行为树（或代码实现巡逻/追逐）。  
**推荐资源**：Unity Learn AI 部分。  
**实战项目**：**「敌人AI追逐战」**  
- 目标：创建1-3个敌人，巡逻 → 发现玩家 → 追击，碰撞造成伤害。  
- 核心知识点：NavMesh、Agent.destination、简单状态机。  
- 预计时间：4天。

### 模块11：数据持久化 & 高级脚本
**学习内容**：PlayerPrefs、JSON/ScriptableObject、事件系统（UnityEvent）、协程、对象池。  
**推荐资源**：官方文档。  
**实战项目**：**「存档系统」**  
- 目标：保存最高分、当前关卡进度、重新加载后数据不丢失。  
- 核心知识点：数据序列化、Singleton模式。  
- 预计时间：2天。

### 模块12：优化、性能 & 构建发布
**学习内容**：Profiler、Frame Debugger、LOD、Occlusion Culling、构建设置（PC/Web/Android/iOS）、Addressables 入门。  
**推荐资源**：Unity Learn 优化模块。  
**实战项目**：**「打包与优化」**  
- 目标：将前面所有功能整合成一个完整 3D 小游戏，打包成WebGL/PC版，优化到60FPS。  
- 核心知识点：性能分析、构建流水线。  
- 预计时间：3-5天。

### 最终 Capstone 项目（整合所有模块）
**推荐项目**（任选其一，有能力建议都做）：
1. **3D 收集/射击生存游戏**（Roll-a-ball 升级版：AI敌人、武器切换、多关卡）
2. **3D 太空射击或迷宫探险**（B站可搜「Unity 3D 完整教程」作参考）
3. **3D 第一人称探索/解谜小关卡**（侧重场景、光照、简单机关与存档）

完成后上传到 **itch.io** 或 GitHub，做出作品集。

### 学习建议 & 进阶路线
- **每天流程**：30%看教程 → 70%自己敲代码 → 做小项目。
- **常见坑**：版本不一致、Input System没启用、物理和Transform冲突 → 多看Console。
- **进阶模块**（学完本路线后再学）：
  - Shader Graph / Visual Scripting
  - Netcode for GameObjects（多人）
  - DOTS / ECS（高性能）
  - AR Foundation / VR
  - 商业项目框架（StrangeIoC / UniTask 等）
- **推荐完整教程**（B站，优先选带 3D 实战的）：
  - 「【Unity教程】零基础带你从小白到超神」（Gamer飞羽）
  - 「Unity 6 零基础多个实战案例」
  - SiKi / 黑马程序员 Unity 系列（选 3D 项目章节）

坚持做完所有实战项目，你就能**独立开发并发布 3D 小游戏**了！  

有任何模块卡住了、想看具体代码示例、或者需要我帮你细化某个项目的完整步骤，随时告诉我，我可以继续给你拆解甚至提供伪代码框架。  

**开始行动吧！第一步今天就去安装Unity Hub，完成「我的第一个3D房间」项目！** 🚀

加油，你一定能做出属于自己的游戏！如果想让我帮你规划更详细的周计划或推荐具体B站视频链接，随时说。
