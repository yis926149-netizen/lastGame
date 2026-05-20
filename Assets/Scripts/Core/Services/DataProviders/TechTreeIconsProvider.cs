using System.Collections.Generic;
using UnityEngine;

public interface ITechTreeIconsProvider
{
    Sprite GetTechIcon(int techId);
    Sprite GetCultureIcon(int cultureId);
    List<Sprite> GetAllTechIcon();
    List<Sprite> GetAllCultureIcon();
}

public class TechTreeIconsProvider : ITechTreeIconsProvider
{
    private readonly TechTreeIconsSO _techTreeIcons;

    public TechTreeIconsProvider(TechTreeIconsSO techTreeIcons)
    {
        _techTreeIcons = techTreeIcons;
    }

    public Sprite GetTechIcon(int techId) => _techTreeIcons.techIcons[techId];
    public Sprite GetCultureIcon(int cultureId) => _techTreeIcons.cultureIcons[cultureId];
    public List<Sprite> GetAllTechIcon() => _techTreeIcons.techIcons;
    public List<Sprite> GetAllCultureIcon() => _techTreeIcons.cultureIcons;
}