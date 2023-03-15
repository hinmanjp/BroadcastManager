using Newtonsoft.Json.Linq;
using OBSWebsocketDotNet.Communication;
using System.Text;
using System.Security.Cryptography;
using System.Data.Common;
using OBSWebsocketDotNet;
using System.Threading;
using System.Collections.Concurrent;
using Websocket.Client;
using System.Net.WebSockets;

namespace BroadcastManager2
{

    internal enum MessageTypes
    {
        Hello = 0,
        Identify = 1,
        Identified = 2,
        ReIdentify = 3,
        Event = 5,
        Request = 6,
        RequestResponse = 7,
        RequestBatch = 8,
        RequestBatchResponse = 9
    }

    public class Obs 
    {


        private const int SUPPORTED_RPC_VERSION = 1;
        private TimeSpan wsTimeout = TimeSpan.FromSeconds(10);
        private string connectionPassword = null;
        private ClientWebSocket wsConnection;

        private delegate void RequestCallback(OBSWebsocket sender, JObject body);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JObject>> responseHandlers;

        // Random should never be created inside a function
        private static readonly Random random = new Random();


        public WebSocketState Connect(Uri uri, CancellationToken? cancellationToken = null)
        {
            if (cancellationToken is null)
                cancellationToken = CancellationToken.None;

            wsConnection = new ClientWebSocket();
            wsConnection.ConnectAsync(uri, (CancellationToken)cancellationToken);
            return wsConnection.State;
        }


        /// <summary>
        /// Sends a message to the websocket API with the specified request type and optional parameters
        /// </summary>
        /// <param name="requestType">obs-websocket request type, must be one specified in the protocol specification</param>
        /// <param name="additionalFields">additional JSON fields if required by the request type</param>
        /// <returns>The server's JSON response as a JObject</returns>
        public JObject SendRequest(string requestType, JObject additionalFields = null)
        {
            return SendRequest(MessageTypes.Request, requestType, additionalFields, true);
        }

        /// <summary>
        /// Internal version which allows to set the opcode
        /// Sends a message to the websocket API with the specified request type and optional parameters
        /// </summary>
        /// <param name="operationCode">Type/OpCode for this messaage</param>
        /// <param name="requestType">obs-websocket request type, must be one specified in the protocol specification</param>
        /// <param name="additionalFields">additional JSON fields if required by the request type</param>
        /// <param name="waitForReply">Should wait for reply vs "fire and forget"</param>
        /// <returns>The server's JSON response as a JObject</returns>
        internal JObject SendRequest(MessageTypes operationCode, string requestType, JObject additionalFields = null, bool waitForReply = true)
        {
            if (wsConnection == null)
            {
                throw new NullReferenceException("Websocket is not initialized");
            }

            // Prepare the asynchronous response handler
            var tcs = new TaskCompletionSource<JObject>();
            JObject message = null;
            do
            {
                // Generate a random message id
                message = MessageFactory.BuildMessage(operationCode, requestType, additionalFields, out string messageId);
                if (!waitForReply || responseHandlers.TryAdd(messageId, tcs))
                {
                    break;
                }
                // Message id already exists, retry with a new one.
            } while (true);
            // Send the message 
            wsConnection.SendAsync(message.ToString(), );
            if (!waitForReply)
            {
                return null;
            }

            // Wait for a response (received and notified by the websocket response handler)
            tcs.Task.Wait(wsTimeout.Milliseconds);

            if (tcs.Task.IsCanceled)
                throw new ErrorResponseException("Request canceled", 0);

            // Throw an exception if the server returned an error.
            // An error occurs if authentication fails or one if the request body is invalid.
            var result = tcs.Task.Result;

            if (!(bool)result["requestStatus"]["result"])
            {
                var status = (JObject)result["requestStatus"];
                throw new ErrorResponseException($"ErrorCode: {status["code"]}{(status.ContainsKey("comment") ? $", Comment: {status["comment"]}" : "")}", (int)status["code"]);
            }

            if (result.ContainsKey("responseData")) // ResponseData is optional
                return result["responseData"].ToObject<JObject>();

            return new JObject();
        }


        /// <summary>
        /// Request authentication data. You don't have to call this manually.
        /// </summary>
        /// <returns>Authentication data in an <see cref="OBSAuthInfo"/> object</returns>
        public OBSAuthInfo GetAuthInfo()
        {
            JObject response = SendRequest("GetAuthRequired");
            return new OBSAuthInfo(response);
        }



        /// <summary>
        /// Authenticates to the Websocket server using the challenge and salt given in the passed <see cref="OBSAuthInfo"/> object
        /// </summary>
        /// <param name="password">User password</param>
        /// <param name="authInfo">Authentication data</param>
        /// <returns>true if authentication succeeds, false otherwise</returns>
        protected void SendIdentify(string password, OBSAuthInfo authInfo = null)
        {
            var requestFields = new JObject
            {
                { "rpcVersion", SUPPORTED_RPC_VERSION }
            };

            if (authInfo != null)
            {
                // Authorization required

                string secret = HashEncode(password + authInfo.PasswordSalt);
                string authResponse = HashEncode(secret + authInfo.Challenge);
                requestFields.Add("authentication", authResponse);
            }

            SendRequest(MessageTypes.Identify, null, requestFields, false);
        }

        /// <summary>
        /// Encode a Base64-encoded SHA-256 hash
        /// </summary>
        /// <param name="input">source string</param>
        /// <returns></returns>
        protected string HashEncode(string input)
        {
            using var sha256 = new SHA256Managed();

            byte[] textBytes = Encoding.ASCII.GetBytes(input);
            byte[] hash = sha256.ComputeHash(textBytes);

            return System.Convert.ToBase64String(hash);
        }

        private void HandleHello(JObject payload)
        {
            if (!wsConnection.IsStarted)
            {
                return;
            }

            OBSAuthInfo authInfo = null;
            if (payload.ContainsKey("authentication"))
            {
                // Authentication required
                authInfo = new OBSAuthInfo((JObject)payload["authentication"]);
            }

            SendIdentify(connectionPassword, authInfo);

            connectionPassword = null;
        }



    }



    internal static class MessageFactory
    {
        internal static JObject BuildMessage(MessageTypes opCode, string messageType, JObject additionalFields, out string messageId)
        {
            messageId = Guid.NewGuid().ToString();
            JObject payload = new JObject()
            {
                { "op", (int)opCode }
            };

            JObject data = new JObject();

            switch (opCode)
            {
                case MessageTypes.Request:
                    data.Add("requestType", messageType);
                    data.Add("requestId", messageId);
                    data.Add("requestData", additionalFields);
                    additionalFields = null;
                    break;
                case MessageTypes.RequestBatch:
                    data.Add("requestId", messageId);
                    break;

            }

            if (additionalFields != null)
            {
                data.Merge(additionalFields);
            }
            payload.Add("d", data);
            return payload;
        }
    }


}
