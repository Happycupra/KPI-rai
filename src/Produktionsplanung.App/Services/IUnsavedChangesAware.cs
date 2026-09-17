namespace Produktionsplanung.App.Services;

public interface IUnsavedChangesAware
{
    bool HasUnsavedChanges { get; }
    string UnsavedChangesDescription { get; }
    bool TrySaveChanges();
    void DiscardChanges();
}
