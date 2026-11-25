namespace BranchWatch;

internal static class StickyNoteDesktopAffinity
{
    internal static bool Verify(bool hasNativeOwner, Guid? ownerDesktop, bool ownerCurrent,
        Guid? bodyDesktop, bool bodyCurrent, out string reason)
    {
        if (!hasNativeOwner)
        {
            reason = "The passive window does not have the note as its native owner.";
            return false;
        }

        if (!ownerDesktop.HasValue || ownerDesktop == Guid.Empty || !ownerCurrent)
        {
            reason = "The note's virtual desktop could not be verified.";
            return false;
        }

        if (bodyDesktop.HasValue && (bodyDesktop != ownerDesktop || !bodyCurrent))
        {
            reason = "The passive window reports a different or inactive virtual desktop.";
            return false;
        }

        reason = "";
        return true;
    }
}
