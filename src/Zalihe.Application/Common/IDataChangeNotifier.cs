namespace Zalihe.Application.Common;

/// <summary>
/// Tells a company's open screens that its data changed (SPEC 11.1), so they load it again. Carries no data:
/// what each user may see is decided by the API when the screen reloads, by role as always.
/// </summary>
public interface IDataChangeNotifier
{
    /// <summary>Called after the change is saved (committed); never throws, a missed notice only delays a refresh.</summary>
    Task DataChangedAsync(IReadOnlyCollection<Guid> tenantIds);
}
