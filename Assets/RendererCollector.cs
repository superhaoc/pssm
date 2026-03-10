using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class RendererCollector : MonoBehaviour
{
    private static List<RendererType> _allTargetRenderers = new List<RendererType>();

    // 提供只读的列表副本（避免外部修改）
    public static IReadOnlyList<RendererType> AllTargetRenderers => _allTargetRenderers.AsReadOnly();

    // 尝试添加一个 Renderer（满足过滤条件才添加）
    public static bool TryAddRenderer(RendererType renderer)
    {
       
        if (!(renderer.render is MeshRenderer)) // 根据需要调整类型
            return false;

        // 防止重复添加（虽然理论上不会，但安全起见）
        if (!_allTargetRenderers.Contains(renderer))
        {
            _allTargetRenderers.Add(renderer);
  
            return true;
        }
        return false;
    }

   
    public static bool RemoveRenderer(RendererType renderer)
    {
        return _allTargetRenderers.Remove(renderer);
    }
}
