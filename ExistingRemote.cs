using Vultr.API;
using Vultr.Models;

namespace BroadcastManager2
{
    public class ExistingRemote
    {
        private static Instance? _currentInstance;

        public static Vultr.Models.Instance CurrentInstance { get => _currentInstance ?? new Instance(); set => _currentInstance = value; }

        public ExistingRemote()
        {
            _currentInstance = FindExistingRemoteServer() ?? new Vultr.Models.Instance();
        }
        public Vultr.Models.Instance FindExistingRemoteServer()
        {
            var vc = new VultrClient(AppSettings.Config["VultrApiKey"] ?? "");
            var instanceResult = vc.Instance.ListInstances();
            if ( instanceResult != null )
            {
                var instance = instanceResult.Instances.FirstOrDefault(i => !string.IsNullOrEmpty(i.label) && i.label == (AppSettings.Config["VultrVmLabel"] ?? ""));
                if ( instance != null )
                {
                    return instance;
                }
            }

            return new Instance();
        }
    }
}
