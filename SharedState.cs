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
}
