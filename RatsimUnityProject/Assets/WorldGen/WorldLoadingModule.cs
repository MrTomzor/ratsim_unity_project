using UnityEngine;
using System.Collections.Generic;

public abstract class WorldLoadingModule : MonoBehaviour {
    public static List<WorldLoadingModule> registered = new List<WorldLoadingModule>();

    protected virtual void OnEnable()  { registered.Add(this); }
    protected virtual void OnDisable() { registered.Remove(this); }

    /// Called once per episode after Clear(), before any chunk loading begins.
    /// Use for work that must happen regardless of chunk requests (e.g. spawning agents).
    public virtual void Initialize() { }

    public abstract void OnChunkLoadRequested(int cx, int cz, int lod);
    public abstract void OnChunkUnloadRequested(int cx, int cz, int lod);
    public abstract void Clear();
}