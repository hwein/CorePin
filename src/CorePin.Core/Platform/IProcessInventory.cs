namespace CorePin.Core.Platform;

public interface IProcessInventory
{
    /// Handle-free, filtered to the caller's own logon session.
    IReadOnlyList<ProcessEntry> ListOwnSession();
}
