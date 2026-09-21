namespace Symbolon.Client;

public enum SeatState
{
    Active,
    GracePeriod,
    Lost,
    Released
}

public sealed class SeatStateChangedEventArgs : EventArgs
{
    public SeatState State { get; }
    public string? Detail { get; }

    public SeatStateChangedEventArgs(SeatState state, string? detail = null)
    {
        State = state;
        Detail = detail;
    }
}
