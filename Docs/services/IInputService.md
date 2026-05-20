
---

### IInputService.md

```markdown
# IInputService

## 职责描述
封装所有输入相关操作，包括鼠标、键盘、轴输入、射线检测和 UI 遮挡检测。统一输入来源，方便测试和替换。

## 依赖项
- `EventSystem`（用于 UI 遮挡检测）
- `Camera`（主摄像机，用于射线检测）

实现类 `InputService` 通过 Zenject 注入上述依赖。

## 公共方法
请参考 [API 文档](../api/IInputService.html)（DocFX 自动生成）。

## 使用示例
```csharp
public class PlayerInputHandler
{
    private IInputService _input;
    private Camera _mainCamera;

    [Inject]
    public PlayerInputHandler(IInputService input, [Inject(Id = "MainCamera")] Camera mainCamera)
    {
        _input = input;
        _mainCamera = mainCamera;
    }

    public void Tick()
    {
        if (_input.GetMouseButtonDown(0) && !_input.IsPointerOverUI())
        {
            // 处理左键点击
        }

        float scroll = _input.MouseScrollDelta;
        if (scroll != 0)
        {
            // 缩放摄像机
        }
    }
}