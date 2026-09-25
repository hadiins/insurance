using Aqsat.Application.Common;
using Aqsat.Domain;
using Aqsat.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Aqsat.Infrastructure.Customers;

/// <summary>Thrown for every caller-visible customer-creation failure — surfaces as a 400 with its
/// Persian message, never an empty catch (CLAUDE.md rule 15).</summary>
public sealed class CustomerCreationException(string message) : Exception(message);

public sealed record CreateCustomerInput(
    string FullName, string? FirstName, string? LastName, string? NationalId,
    string? Mobile, string? EmergencyMobile, string? PostalCode, string? Address,
    Aqsat.Domain.Enums.CustomerKind Kind = Aqsat.Domain.Enums.CustomerKind.Iranian,
    string? PassportNumber = null, DateOnly? PassportExpiry = null);

/// <summary>
/// The single manual-customer-creation path, shared by the issuance wizard's inline registration
/// and the standalone "مشتری جدید" form (owner decision 2026-09-03: a brand-new customer can be
/// registered and sent a credit-check portal link BEFORE any policy exists). The validation rules
/// are the ones the issuance endpoint has enforced since Task 6.
/// </summary>
public sealed class CustomerCreationService(AppDbContext dbContext, IFieldEncryptor fieldEncryptor)
{
    public async Task<Customer> CreateAsync(Guid agencyId, CreateCustomerInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.FullName))
        {
            throw new CustomerCreationException("نام بیمه‌گذار الزامی است.");
        }

        if (PersonNameValidator.IsDigitsOnly(input.FirstName) || PersonNameValidator.IsDigitsOnly(input.LastName))
        {
            throw new CustomerCreationException(
                "نام و نام خانوادگی نمی‌تواند عدد باشد — کد ملی را در فیلد جداگانهٔ آن وارد کنید.");
        }

        var customer = new Customer
        {
            AgencyId = agencyId,
            // No natural join key for a manually-entered customer (unlike an import row) — a
            // generated code just satisfies the NOT NULL + unique constraint.
            ExternalCode = $"MAN-{Guid.NewGuid():N}"[..12],
            FullName = input.FullName.Trim(),
            FirstName = string.IsNullOrWhiteSpace(input.FirstName) ? null : input.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(input.LastName) ? null : input.LastName.Trim(),
        };

        // Identity rules (owner decision 2026-09-21): an Iranian customer keeps the legacy shape —
        // a null national ID still means an incomplete profile (completion flow fills it later),
        // but a supplied one must pass the mod-11 checksum; a foreign resident needs a 996-series
        // ID; a passport-only customer needs neither — they are identified by passport, and
        // external credit inquiries are never run for them (PolicyVerificationService checks Kind
        // at inquiry time).
        if (input.Kind == Aqsat.Domain.Enums.CustomerKind.ForeignResident
            && string.IsNullOrWhiteSpace(input.NationalId))
        {
            throw new CustomerCreationException(
                "برای اتباع دارای کد ملی، کد ۱۰ رقمی شروع‌شده با ۹۹۶ الزامی است.");
        }

        if (input.Kind == Aqsat.Domain.Enums.CustomerKind.ForeignPassportOnly
            && string.IsNullOrWhiteSpace(input.PassportNumber))
        {
            throw new CustomerCreationException("برای اتباع بدون کد ملی، شمارهٔ پاسپورت الزامی است.");
        }

        if (!string.IsNullOrWhiteSpace(input.Mobile))
        {
            if (!MobileNumberValidator.IsValid(input.Mobile))
            {
                throw new CustomerCreationException("شمارهٔ موبایل بیمه‌گذار نامعتبر است.");
            }

            customer.Mobile = MobileNumberValidator.Normalize(input.Mobile);
        }

        if (!string.IsNullOrWhiteSpace(input.EmergencyMobile))
        {
            if (!MobileNumberValidator.IsValid(input.EmergencyMobile))
            {
                throw new CustomerCreationException("شمارهٔ موبایل اضطراری نامعتبر است.");
            }

            var normalizedEmergency = MobileNumberValidator.Normalize(input.EmergencyMobile);
            if (normalizedEmergency == customer.Mobile)
            {
                throw new CustomerCreationException("موبایل اضطراری نباید با موبایل اصلی یکسان باشد.");
            }

            customer.EmergencyMobile = normalizedEmergency;
        }

        if (!string.IsNullOrWhiteSpace(input.NationalId))
        {
            var normalizedNationalId = DigitNormalizer.ToLatin(input.NationalId).Trim();
            if (input.Kind == Aqsat.Domain.Enums.CustomerKind.Iranian
                && !NationalIdValidator.IsIranian(normalizedNationalId))
            {
                throw new CustomerCreationException("کد ملی وارد شده نامعتبر است.");
            }

            if (input.Kind == Aqsat.Domain.Enums.CustomerKind.ForeignResident
                && !NationalIdValidator.IsForeignResident(normalizedNationalId))
            {
                throw new CustomerCreationException(
                    "کد ملی اتباع باید ۱۰ رقم شروع‌شده با ۹۹۶ باشد.");
            }

            // The wizard looks a national ID up before ever reaching registration, but the
            // standalone form (and any race between two tabs) can land here with a duplicate —
            // a clear Persian message beats a second customer row the RLS screen can't dedupe.
            // The same dedupe now covers the 996 series, which shares the NationalId column.
            var duplicate = await dbContext.Customers.AsNoTracking()
                .AnyAsync(c => c.NationalId == normalizedNationalId, ct);
            if (duplicate)
            {
                throw new CustomerCreationException(
                    "مشتری با این کد ملی قبلاً ثبت شده است — از جستجوی کد ملی در همان فرم استفاده کنید.");
            }

            customer.NationalId = normalizedNationalId;
            customer.NationalIdHash = fieldEncryptor.Hash(normalizedNationalId);
            if (input.Kind == Aqsat.Domain.Enums.CustomerKind.ForeignResident)
            {
                customer.Kind = input.Kind;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.PassportNumber))
        {
            var normalizedPassport = NormalizePassport(input.PassportNumber);
            if (!NationalIdValidator.IsPassport(normalizedPassport))
            {
                throw new CustomerCreationException("شمارهٔ پاسپورت نامعتبر است — حروف لاتین و رقم، حداقل ۵ کاراکتر.");
            }

            var duplicatePassport = await dbContext.Customers.AsNoTracking()
                .AnyAsync(c => c.PassportNumber == normalizedPassport, ct);
            if (duplicatePassport)
            {
                throw new CustomerCreationException(
                    "مشتری با این شمارهٔ پاسپورت قبلاً ثبت شده است — از جستجوی پاسپورت استفاده کنید.");
            }

            customer.PassportNumber = normalizedPassport;
            customer.PassportNumberHash = fieldEncryptor.Hash(normalizedPassport);
            customer.PassportExpiry = input.PassportExpiry;
            if (input.Kind == Aqsat.Domain.Enums.CustomerKind.ForeignPassportOnly)
            {
                customer.Kind = input.Kind;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.PostalCode))
        {
            var normalizedPostalCode = DigitNormalizer.ToLatin(input.PostalCode).Trim();
            if (normalizedPostalCode.Length != 10 || !normalizedPostalCode.All(char.IsAsciiDigit))
            {
                throw new CustomerCreationException("کد پستی باید دقیقاً ۱۰ رقم باشد.");
            }

            customer.PostalCode = normalizedPostalCode;
        }

        if (!string.IsNullOrWhiteSpace(input.Address))
        {
            if (input.Address.Trim().Length < 10)
            {
                throw new CustomerCreationException("آدرس بیمه‌گذار باید حداقل ۱۰ کاراکتر باشد.");
            }

            customer.Address = input.Address.Trim();
        }

        dbContext.Customers.Add(customer);
        await dbContext.SaveChangesAsync(ct);
        return customer;
    }

    /// <summary>Passport numbers are stored uppercase Latin with inner whitespace collapsed —
    /// a single canonical form so lookups and dedupe agree.</summary>
    public static string NormalizePassport(string input) =>
        string.Join(string.Empty, DigitNormalizer.ToLatin(input).Trim().ToUpperInvariant().Split(
            ' ', StringSplitOptions.RemoveEmptyEntries));
}
