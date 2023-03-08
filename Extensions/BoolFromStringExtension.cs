namespace BroadcastManager2
{
    public static class BoolFromStringExtension
    {
        public static bool StringToBoolean(this string str )
        {
            String[] BooleanStringTrue = { "1", "on", "yes", "true" };

            if (String.IsNullOrEmpty(str))
                return false;
            else if (BooleanStringTrue.Contains(str, StringComparer.InvariantCultureIgnoreCase))
                return true;
            else
                return false;

        }
    }
}
