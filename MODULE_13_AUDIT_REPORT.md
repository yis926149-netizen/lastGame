# 13. 音频、动画、特效与结束表现审计报告

## 结论

本轮发现并修复了音频通道未隔离、结束视频实际不会播放、缺失结束表现引用会中断流程，以及卡牌 Tween 在对象禁用后仍可能回调的问题。静态核对未发现其他明确的阻断性表现层缺陷。

## 已修复

### [P1] BGM 与 SFX 实际共用同一个 AudioSource

- `ProjectContext` 的 `bgmSource` 和 `sfxSource` 指向同一组件，导致 BGM 音量额外影响 SFX，`StopBGM()` 也会停止同通道正在播放的音效。
- `AudioManager` 现在启动时校验并强制拆分通道；缺少引用时创建专用组件，并关闭无 Clip 的 Play On Awake。

### [P1] 结束视频组件被禁用

- `EndGameVideo` 虽然设置了 Play On Awake，但其 `VideoPlayer` 组件自身为禁用状态；激活 GameObject 后不会播放视频，只会显示 RenderTexture 的旧帧或空白。
- 场景中的目标 `VideoPlayer` 已启用。

### [P2] 缺失结束表现引用会阻断结束流程

- `EndAnimation` 缺失时原逻辑会抛出空引用，最终结果 UI 永远无法显示；延迟回调也未在组件禁用时取消。
- 现在缺少动画时直接显示结果 UI，缺少结果 UI 时输出明确错误，并在禁用时取消待执行回调。

### [P2] 卡牌 Tween 未绑定对象生命周期

- 卡牌放置成功后会立即禁用并销毁对象，但位移/缩放 Tween 可能仍存活并带有完成回调。
- `CardController` 现在在禁用时终止其 `RectTransform` 上的 Tween。

### [P2] 音频资源缺失可在初始化时触发异常

- 音频列表为 null 或包含 Missing Clip 时，原字典初始化会抛出异常，阻断全局服务启动。
- 现在会跳过无效 Clip，并对空列表、缺失引用和重名 Clip 输出可定位日志；播放查询改为一次 `TryGetValue`。

## 已核对

- `AudioManager` 位于 `ProjectContext`，场景切换时由 Zenject 全局上下文持有；业务场景没有第二个有效 `AudioManager`。
- StartScene 与 GameScene 各有一个主相机 `AudioListener`，正常单场景切换下数量符合设计。
- 单位使用的 Animator Controller 均包含 `isMoving`、`isAttack`、`isAttacked` 和 `isDeath` 参数。
- 单位死亡和普通建筑销毁均有一次性状态保护；胜负检查也会在首次命中结果后锁定。
- 结束逻辑状态在播放结束视频前已经确定，视频缺失不会再阻止最终 UI 出现。

## 验证

- `dotnet build MainGame.csproj --no-restore`：成功，0 error；保留现有 Zenject 注入字段等静态告警。
- `git diff --check`：通过，仅输出仓库既有的行尾转换提示。
- 仍需在 Unity PlayMode 手工验证：开始场景与游戏场景 BGM 切换、连续攻击音效与 BGM 并发、单位死亡一次播放、胜利/失败/平局触发，以及结束视频播放后结果 UI 出现。
