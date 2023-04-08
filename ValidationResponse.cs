namespace BroadcastManager2
{
    public class ValidationResponse
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public ValidationResponse( bool IsValid, string Message )
        {
            this.IsValid = IsValid;
            this.Message = Message;
        }
    }
}
