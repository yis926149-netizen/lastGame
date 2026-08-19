using UnityEngine;
using Zenject;

public class SunCycleController : ITickable
{
    private const float NoonAngle = 90f;
    private const float SunsetAngle = 170f;
    private const float DirectionYAngle = 30f;

    private static readonly Color SunLowColor = new Color(1f, 0.55f, 0.3f);
    private static readonly Color NoonColor = new Color(1f, 0.95f, 0.82f);

    private readonly GameLoop _gameLoop;
    private readonly GameFlowConfigProvider _gameFlow;
    private Light _sunLight;

    public SunCycleController(GameLoop gameLoop, GameFlowConfigProvider gameFlow = null)
    {
        _gameLoop = gameLoop;
        _gameFlow = gameFlow;
    }

    // 昼夜周期与光照强度仅读 Excel（阶段6 唯一主源）
    private float CycleDuration => _gameFlow.DayNightCycleSeconds;
    private float HalfCycle => CycleDuration * 0.5f;
    private float NoonIntensity => _gameFlow.NoonLightIntensity;
    private float SunsetIntensity => _gameFlow.SunsetLightIntensity;

    public void Tick()
    {
        if (_gameLoop.IsPaused) return;

        if (_sunLight == null)
        {
            _sunLight = RenderSettings.sun;
            if (_sunLight == null)
            {
                var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var light in lights)
                {
                    if (light.type == LightType.Directional)
                    {
                        _sunLight = light;
                        break;
                    }
                }
            }
            if (_sunLight == null) return;
        }

        float t = _gameLoop.GameTime;
        float xAngle = NoonAngle + (SunsetAngle - NoonAngle) * Mathf.Sin(t * Mathf.PI / HalfCycle);
        _sunLight.transform.rotation = Quaternion.Euler(xAngle, DirectionYAngle, 0f);

        float d = Mathf.Abs(xAngle - NoonAngle) / (SunsetAngle - NoonAngle);
        _sunLight.color = Color.Lerp(NoonColor, SunLowColor, d);
        _sunLight.intensity = Mathf.Lerp(NoonIntensity, SunsetIntensity, d);
    }
}
