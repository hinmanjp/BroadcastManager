// credit to: https://github.com/zingz0r/CloudFlareDnsUpdater/

using CloudFlare.Client.Api.Authentication;
using CloudFlare.Client.Api.Zones.DnsRecord;
using CloudFlare.Client.Enumerators;
using CloudFlare.Client;
using System.Security.Cryptography.Xml;

namespace BroadcastManager2
{
    internal class UpdateCloudflareDNS
    {
        //private readonly HttpClient _httpClient;
        //private readonly ILogger _logger;
        private readonly IAuthentication _authentication;
        //private readonly TimeSpan _updateInterval;

        public UpdateCloudflareDNS(string ApiTokenKey)
        {
            _authentication = new ApiTokenAuthentication(ApiTokenKey);
        }


        public async Task<bool> UpdateDnsAsync(string zoneName, string recordName, string IPv4address, CancellationToken cancellationToken)
        {
            string fullName = recordName + "." + zoneName;
            try
            {
                using var client = new CloudFlareClient(_authentication);

                var zones = (await client.Zones.GetAsync(cancellationToken: cancellationToken)).Result;
                var zone = zones.Where(z => z.Name.ToLower() == zoneName.ToLower()).FirstOrDefault();

                if (zone is not null)
                {
                    var records = (await client.Zones.DnsRecords.GetAsync(zone.Id, new DnsRecordFilter { Type = DnsRecordType.A }, null, cancellationToken)).Result;

                    var record = records.Where(r => r.Name.ToLower() == fullName.ToLower()).FirstOrDefault();

                    if ( record is not null )
                    {
                        if ( record.Content == IPv4address ) // update the record if the IP is not what is wanted
                            return true;
                        else
                        {
                            var modified = new ModifiedDnsRecord
                            {
                                Type = DnsRecordType.A,
                                Name = record.Name,
                                Content = IPv4address,
                            };

                            var updateResult = (await client.Zones.DnsRecords.UpdateAsync(zone.Id, record.Id, modified, cancellationToken));

                            if ( updateResult.Success )
                                return true;
                        }
                    }
                    else // can't find an existing record to update - need to create a new record
                    {
                        var newRecord = new NewDnsRecord
                        {
                            Name = recordName,
                            Content = IPv4address,
                            Type = DnsRecordType.A,
                            Proxied = false,
                        };
                        var addResult = await client.Zones.DnsRecords.AddAsync(zone.Id, newRecord, cancellationToken);
                        if ( addResult.Success ) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                var e1 = ex;
            }
            return false;
        }


        public async Task<bool> DeleteDnsAsync(string zoneName, string recordName, CancellationToken cancellationToken)
        {
            string fullName = recordName + "." + zoneName;
            try
            {
                using var client = new CloudFlareClient(_authentication);

                var zones = (await client.Zones.GetAsync(cancellationToken: cancellationToken)).Result;
                var zone = zones.Where(z => z.Name == zoneName).FirstOrDefault();

                if (zone is not null && zone.Name == zoneName)
                {
                    var records = (await client.Zones.DnsRecords.GetAsync(zone.Id, new DnsRecordFilter { Type = DnsRecordType.A }, null, cancellationToken)).Result;

                    var record = records.Where(r => r.Name == fullName).FirstOrDefault();

                    if (record is not null && record.Name == fullName)
                    {
                        var delResult = await client.Zones.DnsRecords.DeleteAsync(zone.Id, record.Id, cancellationToken);
                        if ( delResult.Success ) return true;
                    }
                }
            }
            catch (Exception ex) { var e1 = ex; }
            return false;
        }


    }
}
