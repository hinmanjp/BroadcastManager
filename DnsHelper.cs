using System.Net.NetworkInformation;

namespace BroadcastManager2
{
    public class DnsHelper
    {
        public struct DnsSplit
        {
            public string RecordName { get; set; }
            public string ZoneName { get; set; }
        }

        public static DnsSplit SplitDnsName(string dnsName)
        {
            var regex = new System.Text.RegularExpressions.Regex(@"(?<record>.*)\.(?<zone>[^.]*\..*$)");
            var match = regex.Match(dnsName);
            var recordName = match.Groups["record"].Value;
            var zoneName = match.Groups["zone"].Value;
            DnsSplit split = new DnsSplit { RecordName = recordName, ZoneName = zoneName };
            return split;
        }

        public static string GetLocalIPv4(NetworkInterfaceType _type = NetworkInterfaceType.Unknown)
        {  // Checks your IP adress from the local network connected to a gateway. This to avoid issues with double network cards
            string output = "";  // default output
            foreach (NetworkInterface item in NetworkInterface.GetAllNetworkInterfaces()) // Iterate over each network interface
            {  // Find the network interface which has been provided in the arguments, break the loop if found
                if ((_type == NetworkInterfaceType.Unknown || item.NetworkInterfaceType == _type) && item.OperationalStatus == OperationalStatus.Up)
                {   // Fetch the properties of this adapter
                    IPInterfaceProperties adapterProperties = item.GetIPProperties();
                    // Check if the gateway adress exist, if not its most likley a virtual network or smth
                    if (adapterProperties.GatewayAddresses.FirstOrDefault() != null)
                    {   // Iterate over each available unicast adresses
                        foreach (UnicastIPAddressInformation ip in adapterProperties.UnicastAddresses)
                        {   // If the IP is a local IPv4 adress
                            if (ip.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            {   // we got a match!
                                output = ip.Address.ToString();
                                break;  // break the loop!!
                            }
                        }
                    }
                }
                // Check if we got a result if so break this method
                if (output != "") { break; }
            }
            // Return results
            return output;
        }

    }
}
