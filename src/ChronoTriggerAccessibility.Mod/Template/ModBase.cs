namespace ChronoTriggerAccessibility.Mod.Template;

public class ModBase
{
    public virtual bool CanSuspend() => false;
    public virtual bool CanUnload() => false;
    public virtual void Suspend() { }
    public virtual void Resume() { }
    public virtual void Unload() { }
    public virtual void Disposing() { }
}
