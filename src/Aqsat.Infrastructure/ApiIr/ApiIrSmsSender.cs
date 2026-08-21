using Aqsat.Application.ApiIr;
using Aqsat.Application.Sms;

namespace Aqsat.Infrastructure.ApiIr;

public sealed class ApiIrSmsSender(IApiIrClient client) : ISmsSender
{
    public Task<bool> SendAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default) =>
        client.SendSmsAsync(mobile, text, agencyId, ct);
}
