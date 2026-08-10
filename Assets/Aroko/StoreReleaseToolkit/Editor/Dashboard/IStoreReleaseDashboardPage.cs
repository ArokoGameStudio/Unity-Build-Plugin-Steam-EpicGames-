namespace Aroko.StoreRelease.Editor.Dashboard
{
    internal interface IStoreReleaseDashboardPage
    {
        string Title { get; }
        void OnActivated(StoreReleaseDashboardContext context);
        void Draw(StoreReleaseDashboardContext context);
    }
}
