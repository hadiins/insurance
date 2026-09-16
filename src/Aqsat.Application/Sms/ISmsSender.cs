using System.Threading.Tasks;

namespace Aqsat.Application.Sms;

/// <summary>
/// docs/TASKS.md Task 1 named this as one of three interfaces to build behind so development never
/// stalls on external accounts — built now (Task 14) since this is where a real implementation
/// (backed by IApiIrClient.SendSmsAsync) first exists.
/// </summary>
public interface ISmsSender
{
    Task<bool> SendAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default);

    /// <summary>
    /// Delivers a one-time code via api.ir's SmsOTP endpoint (trust level 1), which works even when the
    /// account's bulk SendSms trust level (5) has not yet been granted. The caller owns generating and
    /// verifying the code — this only delivers it. Implemented by ApiIrSmsSender; the default returns
    /// false for fakes/tests that don't need OTP delivery.
    /// </summary>
    Task<bool> SendOtpAsync(string mobile, string code, Guid agencyId, CancellationToken ct = default)
        => Task.FromResult(false);
}
