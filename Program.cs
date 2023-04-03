
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor.Services;
using BroadcastManager2;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthenticationStateProvider>();
builder.Services.AddHttpClient();
builder.Services.AddMudServices();
builder.Services.AddDataProtection()
    .SetApplicationName( "Broadcast Manager" );

var app = builder.Build();

AppSettings.Config = app.Configuration;

// update dns records so that the broadcast manager can be found
var dnsSplit = DnsHelper.SplitDnsName(app.Configuration["LocalServerDnsName"] ?? "");
string ipv4Address = DnsHelper.GetLocalIPv4();

var dns = new UpdateCloudflareDNS(app.Configuration["CloudFlareTokenKey"] ?? "");
await dns.UpdateDnsAsync( dnsSplit.ZoneName, dnsSplit.RecordName, ipv4Address, new CancellationToken() );


if ( !app.Environment.IsDevelopment() )
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseForwardedHeaders( new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
} );

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.MapBlazorHub();
app.MapFallbackToPage( "/_Host" );

app.Run();
