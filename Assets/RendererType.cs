using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public enum ERenderType
{
    Caster,
    Receiver
}

[RequireComponent(typeof(Renderer))]
public class RendererType : MonoBehaviour
{
    public ERenderType type;
    
    [HideInInspector]
    public Renderer render;
    // Start is called before the first frame update
    void Awake()
    {
        render = GetComponent<Renderer>();
       RendererCollector.TryAddRenderer(GetComponent<RendererType>());
       gameObject.layer = LayerMask.NameToLayer(type.ToString());
    }

    void OnDestroy()
    {
        RendererCollector.RemoveRenderer(GetComponent<RendererType>());
    }
}
