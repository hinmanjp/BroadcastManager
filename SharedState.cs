namespace BroadcastManager2
{
    public static class SharedState
    {
        public enum BroadcastState
        {
            stopped,
            starting,
            running,
            paused,
            stopping
        }

        public static object LockObj = new();

        public static BroadcastState? CurrentState = null;
    }

    public class StateChangedArgs : EventArgs
    {
        public StateChangedArgs( SharedState.BroadcastState NewState )
        {
            SharedState.CurrentState = NewState;
        }

        public SharedState.BroadcastState BroadcastState { get; private set; }

    }
}
