namespace UsageViewer;

internal sealed class PanelVisibility(Action show,Action hide,Func<IDisposable?> observer)
{
    private IDisposable? subscription;
    internal bool Visible { get; private set; }
    internal bool Observing=>subscription is not null;
    internal void Open()
    {
        if(Visible) return;
        Visible=true;show();subscription=observer();
    }
    internal void Close()
    { Visible=false;subscription?.Dispose();subscription=null;hide(); }
    internal void Toggle() { if(Visible) Close();else Open(); }
    internal void Press(bool insidePanel,bool insideIcon,bool left)
    { if(Visible&&!insidePanel&&!(insideIcon&&left)) Close(); }
}
