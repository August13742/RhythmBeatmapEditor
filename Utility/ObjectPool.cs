using Godot;
using System.Collections.Generic;
namespace ObjectPool;
// Interface for objects that need initialization/reset logic
public interface IPoolable
{
    void OnSpawned();
    void OnDespawned();
}

// The Pool Manager
public class ObjectPool<T> where T : Node
{
    private readonly Node _parent; // Where active objects live
    private readonly Node _poolRoot; // Where inactive objects hide
    private readonly PackedScene _prefab;
    private readonly Stack<T> _stack = new();
    private readonly System.Func<T> _factory;

    // Constructor for Code-based instantiation
    public ObjectPool(System.Func<T> factoryMethod, int prewarm, Node activeParent, Node inactiveParent)
    {
        _factory = factoryMethod;
        _parent = activeParent;
        _poolRoot = inactiveParent;
        for (int i = 0; i < prewarm; i++) _stack.Push(CreateInstance());
    }

    private T CreateInstance()
    {
        // Use Factory if available, else Prefab
        T inst = _factory != null ? _factory.Invoke() : _prefab.Instantiate<T>();
        
        _poolRoot.AddChild(inst); // Init in storage

        if (inst is IPoolStamp stamp) stamp.SetReturnCallback(() => Return(inst));
        if (inst is CanvasItem ci) ci.Visible = false;
        return inst;
    }

    public T Rent()
    {
        var inst = _stack.Count > 0 ? _stack.Pop() : CreateInstance();

        // Reparent to active layer
        // Note: Reparent checks if it's already there to avoid errors
        if (inst.GetParent() != _parent)
        {
            inst.Reparent(_parent, keepGlobalTransform: false);
        }

        if (inst is CanvasItem ci) ci.Visible = true;
        if (inst is IPoolable p) p.OnSpawned();

        return inst;
    }

    public void Return(T inst)
    {
        if (!IsInstanceValid(inst)) return;

        if (inst is IPoolable p) p.OnDespawned();
        if (inst is CanvasItem ci) ci.Visible = false;

        // Move back to storage (keeps scene tree clean)
        if (inst.GetParent() != _poolRoot)
        {
            inst.Reparent(_poolRoot, keepGlobalTransform: false);
        }

        _stack.Push(inst);
    }
    
    // Helper for C# checking validity (Godot specific)
    private bool IsInstanceValid(Node node)
    {
        return node != null && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion();
    }
}

// Helper interface to give objects a "Return to Pool" method without exposing the whole pool
public interface IPoolStamp
{
    void SetReturnCallback(System.Action callback);
}