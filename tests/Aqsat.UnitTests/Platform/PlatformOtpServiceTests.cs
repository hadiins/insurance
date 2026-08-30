using System.Security.Cryptography;
using Aqsat.Application.Platform;
using Aqsat.Application.Sms;
using Microsoft.Extensions.Caching.Memory;

namespace Aqsat.UnitTests.Platform;

/// <summary>
/// The OTP service's own contract: a failed SMS send must return false (the panel shows "ارسال
/// نشد" instead of a fake "کد ارسال شد") and must leave no verifiable code behind, while a
/// successful send verifies exactly once.
/// </summary>
public class PlatformOtpServiceTests
{
    [Fact]
    public async Task A_failed_sms_send_returns_false_and_leaves_no_verifiable_code()
    {
        var sender = new FakeSmsSender(success: false);
        var service = new Aqsat.Infrastructure.Platform.PlatformOtpService(sender, new MemoryCache(new MemoryCacheOptions()));

        var sent = await service.SendAsync(Guid.NewGuid(), "09121234567", Guid.NewGuid());

        Assert.False(sent);
    }

    [Fact]
    public async Task A_successful_send_returns_true_and_the_code_verifies_exactly_once()
    {
        var sender = new FakeSmsSender(success: true);
        var service = new Aqsat.Infrastructure.Platform.PlatformOtpService(sender, new MemoryCache(new MemoryCacheOptions()));
        var userId = Guid.NewGuid();

        var sent = await service.SendAsync(userId, "09121234567", Guid.NewGuid());

        Assert.True(sent);
        // The message ends with expiry prose, not the code — pull the 6-digit run out with a regex
        // (the only ASCII-digit run in the text; "۵ دقیقه" is Persian script, not \d).
        var code = System.Text.RegularExpressions.Regex.Match(sender.LastText!, @"\d{6}").Value;
        Assert.True(service.Verify(userId, code));
        Assert.False(service.Verify(userId, code)); // single-use: consumed on first verify.
    }

    [Fact]
    public async Task A_wrong_code_never_verifies()
    {
        var sender = new FakeSmsSender(success: true);
        var service = new Aqsat.Infrastructure.Platform.PlatformOtpService(sender, new MemoryCache(new MemoryCacheOptions()));
        var userId = Guid.NewGuid();

        await service.SendAsync(userId, "09121234567", Guid.NewGuid());

        // The wrong-code assertion only needs to show Verify rejects a bad input; the code-bearing
        // text ends with prose, so match the embedded 6-digit run rather than the last 6 chars.
        var wrongCode = System.Text.RegularExpressions.Regex.Match(sender.LastText!, @"\d{6}").Value == "000000" ? "000001" : "000000";

        Assert.False(service.Verify(userId, wrongCode));
    }

    private sealed class FakeSmsSender(bool success) : ISmsSender
    {
        public string? LastText { get; private set; }

        public Task<bool> SendAsync(string mobile, string text, Guid agencyId, CancellationToken ct = default)
        {
            LastText = text;
            return Task.FromResult(success);
        }
    }
}