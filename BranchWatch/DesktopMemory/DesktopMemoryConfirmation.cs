namespace BranchWatch;

internal sealed class DesktopMemoryConfirmation
{
    private long _revision;
    private bool _prompting;

    internal void Invalidate() => _revision++;

    internal bool Confirm(Func<bool> eligible, Func<bool> prompt)
    {
        if (_prompting || !eligible()) return false;
        var revision = _revision;
        _prompting = true;
        try
        {
            return prompt() && revision == _revision && eligible();
        }
        finally { _prompting = false; }
    }
}
