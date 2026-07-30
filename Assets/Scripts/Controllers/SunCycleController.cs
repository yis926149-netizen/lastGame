using UnityEngine;
using Zenject;

public class SunCycleController : ITickable
{
    private const float CycleDuration = 300f;
    private const float HalfCycle = 150f;
    private const float NoonAngle = 90f;
    private const float SunsetAngle = 170f;
    private const float DirectionYAngle = 30f;

    private static readonly Color SunLowColor = new Color(1f, 0.55f, 0.3f);
    private static readonly Color NoonColor = new Color(1f, 0.95f, 0.82f);

    private readonly GameLoop _gameLoop;
    private Light _sunLight;

    public SunCycleController(GameLoop gameLoop)
    {
        _gameLoop = gameLoop;
    }

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
        _sunLight.intensity = Mathf.Lerp(1.2f, 0.4f, d);
    }
}
