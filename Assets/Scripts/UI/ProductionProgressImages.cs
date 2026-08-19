using UnityEngine;
using UnityEngine.UI;

//****************************************
//功能说明：兵营生产进度帧动画控制器。
//         替代旧的 Slider 倒计时条：BarracksSpawner 每帧传入归一化进度 t ∈ [0,1]，
//         本组件在一个生产周期内按顺序平均切帧（t=0 显示第一帧，t=1 显示最后一帧）。
//         挂载到兵营预制体（barracks_blue.prefab / barracks_red.prefab）上，
//         在 Inspector 中手动拖入用于显示的 Image 节点与帧图片列表即可。
//****************************************
public class ProductionProgressImages : MonoBehaviour
{
    [Header("显示节点")]
    [Tooltip("用于切换帧图片的 Image 节点（如兵营 Canvas 下的生产进度图）。")]
    [SerializeField] private Image targetImage;

    [Header("帧图片序列")]
    [Tooltip("一个生产周期内按顺序展示的帧图片列表，平均分布在整个周期上（顺序即播放顺序）。")]
    [SerializeField] private Sprite[] frames;

    private int _lastIndex = -1;

    /// <summary>是否已配置好显示节点与至少一帧图片。</summary>
    public bool IsConfigured => targetImage != null && frames != null && frames.Length > 0;

    /// <summary>
    /// 根据归一化进度切换帧图片。
    /// normalized ∈ [0,1]：0 显示第一帧，1 显示最后一帧，中间按顺序平均分布。
    /// </summary>
    public void SetProgress(float normalized)
    {
        if (!IsConfigured) return;

        normalized = Mathf.Clamp01(normalized);
        int index = Mathf.FloorToInt(normalized * frames.Length);
        index = Mathf.Clamp(index, 0, frames.Length - 1);

        // 帧未变化则跳过重复赋值
        if (index == _lastIndex) return;
        _lastIndex = index;

        targetImage.sprite = frames[index];
        targetImage.enabled = frames[index] != null;
    }

    /// <summary>重置到第一帧（生产周期开始 / 生成单位后调用）。</summary>
    public void ResetProgress()
    {
        _lastIndex = -1;
        SetProgress(0f);
    }
}
