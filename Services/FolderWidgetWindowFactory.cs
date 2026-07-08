namespace TimeWidget.Services;

public sealed class FolderWidgetWindowFactory : IFolderWidgetWindowFactory
{
    public FolderWidgetWindow Create()
    {
        return new FolderWidgetWindow();
    }
}
