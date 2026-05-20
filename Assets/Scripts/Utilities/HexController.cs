using System.Collections;
using System.Collections.Generic;
using UnityEngine;

//****************************************
//创建人：易生
//功能说明：地块控制器
//****************************************

public class HexController : MonoBehaviour
{
    /// <summary>
    /// 返回一个地块需要被设定的材质
    /// </summary>
    /// <param name="hexCellData">该地块的数据类</param>
    /// <param name="mapMaterial">地图的材质组</param>
    public static Material SetHexMaterial(HexCellData hexCellData, Material[] mapMaterial)
    {
        if (hexCellData == null)
        {
            Debug.Log("该地块不存在");
            return null;
        }

        float height = hexCellData.Height;
        switch (height)
        {
            case 0:
                return mapMaterial[2];
            case 1:
                return mapMaterial[1];
            case 2:
                return mapMaterial[0];
            default:
                return mapMaterial[0];
        }
    }
}
