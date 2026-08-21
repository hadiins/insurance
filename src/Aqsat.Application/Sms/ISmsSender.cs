namespace Aqsat.Application.Sms;

/// <summary>
/// docs/TASKS.md Task 1 named this as one of three interfaces to build behind so development never
/// stalls on external accounts — built now (Task 14) since this is where a real implementation
/// (backed by IApiIrClient.SendSmsAsync) first exists.
/// </summary>
public interface ISmsSender
{
    Task<bool> SendAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default);
}
