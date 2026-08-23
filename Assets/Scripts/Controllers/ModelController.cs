using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ModelController : MonoBehaviour
{
    private Camera mainCamera;
    void Start()
    {
        // 获取主摄像头
        mainCamera = Camera.main;

        // 如果没有找到主摄像头，尝试查找
        if (mainCamera == null)
        {
            mainCamera = FindObjectOfType<Camera>();
        }
    }

}