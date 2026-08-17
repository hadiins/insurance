# مشخصات همزمانی و قفل رکورد

**نسخه:** ۱.۰ · **تاریخ:** ۲۳ مرداد ۱۴۰۵
**پشته:** ASP.NET Core · EF Core · SQL Server · SignalR
**دامنه:** سطح ۱ (تشخیص تعارض) + سطح ۲ (حضور زنده) + سطح ۳ (قفل انحصاری با آزادسازی)

---

# ۱. سه لایه و نقش هر کدام

| لایه | چه می‌کند | کجا فعال است |
|---|---|---|
| **۱ — `rowversion`** | جلوی بازنویسی بی‌صدا را می‌گیرد | **همهٔ جداول، همیشه** |
| **۲ — حضور زنده** | نشان می‌دهد چه کسی این صفحه را باز کرده | همهٔ صفحات پرونده و فرم |
| **۳ — قفل انحصاری** | فقط یک نفر اجازهٔ ویرایش دارد | عملیات مالی و ویرایش پرونده |

> لایهٔ ۱ حتی وقتی ۲ و ۳ کار نکنند (قطعی شبکه، اشکال فنی) **آخرین خط دفاع** است. هرگز حذفش نکنید.

---

# ۲. لایهٔ ۱ — تشخیص تعارض

## مدل

```csharp
public abstract class AuditableEntity
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }

    [Timestamp]                       // rowversion در SQL Server
    public byte[] RowVersion { get; set; }

    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
```

`[Timestamp]` باعث می‌شود EF Core در هر `UPDATE` شرط `WHERE RowVersion = @original` بگذارد. اگر کسی زودتر تغییر داده باشد، صفر ردیف تحت تأثیر قرار می‌گیرد و `DbUpdateConcurrencyException` پرتاب می‌شود.

## مدیریت تعارض

```csharp
catch (DbUpdateConcurrencyException ex)
{
    var entry   = ex.Entries.Single();
    var current = await entry.GetDatabaseValuesAsync();

    if (current is null)
        return Conflict(new { reason = "deleted",
            message = "این رکورد توسط کاربر دیگری حذف شده است." });

    var conflicts = entry.Properties
        .Where(p => !Equals(p.CurrentValue, current[p.Metadata.Name]))
        .Select(p => new {
            field    = p.Metadata.Name,
            yours    = p.CurrentValue,
            theirs   = current[p.Metadata.Name]
        });

    return Conflict(new {
        reason   = "modified",
        modifiedBy = await ResolveLastEditorAsync(entry),   // از لاگ
        fields   = conflicts
    });
}
```

## در رابط کاربری

**هرگز تغییرات کاربر را دور نریزید.** یک پنجره باز شود:

> **این پرونده توسط علی رضایی تغییر کرده است**
>
> | فیلد | مقدار شما | مقدار جدید |
> |---|---|---|
> | مبلغ قسط | ۱٬۰۰۰٬۰۰۰ | ۱٬۲۰۰٬۰۰۰ |
>
> `[ نگه داشتن مقدار من ]` `[ پذیرفتن مقدار جدید ]` `[ انصراف ]`

## ثبت پرداخت — idempotent

خطرناک‌ترین تعارض واقعی، **ثبت دوبارهٔ یک پرداخت** است.

```sql
CREATE UNIQUE INDEX UX_Payment_Dedupe
  ON Payments (InstallmentId, PaidOn, Amount)
  WHERE IsDeleted = 0;
```

در سرویس، خطای نقض کلید یکتا به‌عنوان **موفقیت** برگردانده شود (همان پرداخت، دو بار ثبت نشد)، نه خطا.

> **دلیل:** اگر کاربر دکمه را دو بار بزند یا دو نفر همزمان ثبت کنند، حساب مشتری نباید خراب شود.

---

# ۳. لایهٔ ۲ — حضور زنده

## مدل داده

```csharp
public class RecordPresence
{
    public long Id { get; set; }                 // BIGINT — حجیم است
    public Guid AgencyId { get; set; }
    public string EntityType { get; set; }       // "Policy" | "Customer" | ...
    public Guid EntityId { get; set; }
    public Guid UserId { get; set; }
    public string UserDisplayName { get; set; }  // در لحظهٔ ثبت، نه در نمایش
    public string ConnectionId { get; set; }     // SignalR
    public DateTimeOffset LastSeenAt { get; set; }
    public bool IsEditing { get; set; }          // فقط مشاهده یا در حال ویرایش
}
```

```sql
CREATE UNIQUE INDEX UX_Presence ON RecordPresence (EntityType, EntityId, UserId);
CREATE INDEX IX_Presence_Sweep ON RecordPresence (LastSeenAt);
```

## مکانیزم

**SignalR** — چون در دات‌نت بومی است و به‌روزرسانی لحظه‌ای می‌دهد:

```csharp
public class PresenceHub : Hub
{
    public async Task Enter(string entityType, Guid entityId)
    {
        var group = $"{entityType}:{entityId}";
        await Groups.AddToGroupAsync(Context.ConnectionId, group);
        await _presence.UpsertAsync(entityType, entityId, CurrentUser, Context.ConnectionId);
        await Clients.Group(group).SendAsync("PresenceChanged",
            await _presence.ListAsync(entityType, entityId));
    }

    public async Task Heartbeat(string entityType, Guid entityId)
        => await _presence.TouchAsync(entityType, entityId, CurrentUser.Id);

    public override async Task OnDisconnectedAsync(Exception? ex)
    {
        await _presence.RemoveByConnectionAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(ex);
    }
}
```

| پارامتر | مقدار پیشنهادی | قابل تنظیم |
|---|---|---|
| فاصلهٔ ضربان (heartbeat) | ۲۰ ثانیه | بله |
| انقضای حضور | ۶۰ ثانیه بدون ضربان | بله |
| پاک‌سازی رکوردهای منقضی | هر ۲ دقیقه (Hangfire) | — |

> **پشتیبان:** اگر SignalR وصل نشد (شبکهٔ ضعیف، پروکسی)، به درخواست دورهای هر ۳۰ ثانیه برگردید. حضور نباید به یک تکنولوژی وابسته باشد.

## در رابط کاربری

بالای پرونده، نواری کوچک:

> 👁 **علی رضایی** این پرونده را باز کرده · ✏️ **مریم حسینی** در حال ویرایش

- **مشاهده** خاکستری · **ویرایش** نارنجی
- با ورود و خروج، بدون رفرش به‌روز شود
- در فهرست‌ها هم: کنار ردیفی که کسی بازش کرده، نقطهٔ کوچک

---

# ۴. لایهٔ ۳ — قفل انحصاری

## کجا فعال باشد

| عملیات | قفل | مدت |
|---|---|---|
| **ثبت پرداخت به شرکت بیمه** | ✅ اجباری | ۵ دقیقه |
| **ویرایش جدول اقساط** | ✅ اجباری | ۱۰ دقیقه |
| ویرایش پروندهٔ مشتری | ✅ اختیاری (تنظیمات) | ۱۰ دقیقه |
| ثبت وثیقه / چک | ✅ اجباری | ۵ دقیقه |
| مشاهدهٔ پرونده | ❌ هرگز | — |
| گزارش‌گیری | ❌ هرگز | — |

> **قاعده:** قفل فقط برای **نوشتن** است، هرگز برای **خواندن**. کاربر دیگر همیشه باید بتواند ببیند.

## مدل داده

```csharp
public class RecordLock
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public Guid LockedByUserId { get; set; }
    public string LockedByDisplayName { get; set; }
    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastRenewedAt { get; set; }

    // آزادسازی اجباری
    public Guid? ForceReleasedByUserId { get; set; }
    public DateTimeOffset? ForceReleasedAt { get; set; }
    public string? ForceReleaseReason { get; set; }
}
```

```sql
-- فقط یک قفل فعال برای هر رکورد
CREATE UNIQUE INDEX UX_Lock_Active
  ON RecordLock (EntityType, EntityId)
  WHERE ForceReleasedAt IS NULL AND ExpiresAt > SYSDATETIMEOFFSET();
```

## گرفتن قفل — اتمیک

**حتماً در یک دستور، نه خواندن و بعد نوشتن** — وگرنه دو نفر همزمان قفل می‌گیرند:

```sql
MERGE RecordLock WITH (HOLDLOCK) AS t
USING (SELECT @EntityType AS EntityType, @EntityId AS EntityId) AS s
   ON t.EntityType = s.EntityType
  AND t.EntityId   = s.EntityId
  AND t.ExpiresAt  > SYSDATETIMEOFFSET()
  AND t.ForceReleasedAt IS NULL
WHEN MATCHED AND t.LockedByUserId = @UserId THEN
    UPDATE SET ExpiresAt = DATEADD(MINUTE, @Minutes, SYSDATETIMEOFFSET()),
               LastRenewedAt = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (Id, AgencyId, EntityType, EntityId, LockedByUserId,
            LockedByDisplayName, AcquiredAt, ExpiresAt, LastRenewedAt)
    VALUES (@Id, @AgencyId, @EntityType, @EntityId, @UserId,
            @DisplayName, SYSDATETIMEOFFSET(),
            DATEADD(MINUTE, @Minutes, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET())
OUTPUT $action, inserted.*;
```

**قفل مالک، قابل تمدید است** — با هر ضربان، انقضا جلو می‌رود. پس تا وقتی کاربر فعال است، قفل نمی‌پرد.

## آزادسازی — چهار مسیر

| مسیر | چه کسی | نیاز به دلیل |
|---|---|---|
| **عادی** — ذخیره یا انصراف | مالک قفل | ❌ |
| **قطع اتصال** | خودکار (SignalR disconnect) | ❌ |
| **انقضا** | خودکار (Hangfire هر ۲ دقیقه) | ❌ |
| **اجباری** | کاربر با دسترسی `Lock.ForceRelease` | ✅ **اجباری** |

## آزادسازی اجباری — قواعد

```csharp
[Authorize(Policy = "Lock.ForceRelease")]
public async Task<IActionResult> ForceRelease(Guid lockId, string reason)
{
    if (string.IsNullOrWhiteSpace(reason) || reason.Length < 10)
        return BadRequest("دلیل آزادسازی الزامی است (حداقل ۱۰ کاراکتر).");

    var lk = await _locks.GetActiveAsync(lockId);
    if (lk is null) return NotFound();

    // در دامنهٔ دسترسی کاربر باشد — RLS جای این را نمی‌گیرد
    await _scope.EnsureInScopeAsync(lk.AgencyId);

    await _locks.ForceReleaseAsync(lk, CurrentUser.Id, reason);

    // ۱. لاگ تغییرناپذیر
    await _audit.WriteAsync(new AuditEntry {
        Action     = AuditAction.LockForceReleased,
        EntityType = lk.EntityType,
        EntityId   = lk.EntityId,
        PolicyId   = await ResolvePolicyIdAsync(lk),
        Detail     = $"قفل {lk.LockedByDisplayName} توسط {CurrentUser.DisplayName} آزاد شد. دلیل: {reason}"
    });

    // ۲. اطلاع لحظه‌ای به صاحب قفل
    await _hub.Clients.User(lk.LockedByUserId.ToString())
        .SendAsync("LockRevoked", new { lk.EntityType, lk.EntityId, reason,
                                        by = CurrentUser.DisplayName });

    return Ok();
}
```

**سه الزام غیرقابل حذف:**

۱. **دلیل اجباری** — حداقل ۱۰ کاراکتر
۲. **ثبت در لاگ تغییرناپذیر** — با نام هر دو طرف
۳. **اطلاع فوری به صاحب قفل** — نباید بی‌خبر بماند

## وقتی قفل کسی آزاد شد

کاربری که در حال ویرایش بود، بلافاصله می‌بیند:

> ⚠️ **دسترسی ویرایش شما لغو شد**
>
> مدیر (حسن کاظمی) قفل این پرونده را آزاد کرد.
> دلیل: «کاربر دفتر را ترک کرده و پرونده فوری است»
>
> **تغییرات ذخیره‌نشدهٔ شما حفظ شده است.**
> `[ کپی تغییرات من ]` `[ تلاش دوباره برای قفل ]` `[ بستن ]`

> **حیاتی:** تغییرات کاربر **هرگز دور ریخته نشود**. در حافظه بماند تا خودش تصمیم بگیرد.

## سطوح دسترسی

| نقش | `Lock.ForceRelease` |
|---|---|
| کاربر عادی (منشی) | ❌ |
| کاربر ارشد | ❌ |
| **مدیر نمایندگی** | ✅ در دامنهٔ خودش |
| **سرپرستی** | ✅ در نمایندگی‌های زیرمجموعه |
| پشتیبانی شرکت (شما) | ✅ با لاگ ویژه و اطلاع به نمایندگی |

---

# ۵. جریان کامل کاربر

```
کاربر پرونده را باز می‌کند
        ↓
  حضور ثبت می‌شود (لایه ۲) → بقیه می‌بینند «در حال مشاهده»
        ↓
دکمهٔ «ویرایش» را می‌زند
        ↓
   ┌──────────────────────────────┐
   │ تلاش برای گرفتن قفل (لایه ۳) │
   └──────────────────────────────┘
        ↓                    ↓
    موفق                  ناموفق
        ↓                    ↓
 حالت ویرایش      «مریم حسینی از ۳ دقیقه پیش
 قفل هر ۲۰ ثانیه   در حال ویرایش است»
 تمدید می‌شود      [ فقط مشاهده ] [ درخواست آزادسازی ]
        ↓                    ↓
    ذخیره              (اگر مدیر است)
        ↓              [ آزادسازی اجباری + دلیل ]
 rowversion چک
 می‌شود (لایه ۱)
        ↓
   قفل آزاد می‌شود
```

---

# ۶. حالت‌های خطا — همه باید مدیریت شوند

| حالت | رفتار سیستم |
|---|---|
| SignalR وصل نمی‌شود | برگشت به درخواست دوره‌ای · نوار: «به‌روزرسانی لحظه‌ای در دسترس نیست» |
| قفل منقضی شد و کاربر هنوز تایپ می‌کند | هشدار در ۳۰ ثانیهٔ آخر: «قفل شما تا ۳۰ ثانیه دیگر منقضی می‌شود» + دکمهٔ تمدید |
| کاربر مرورگر را بست | قطع SignalR → قفل فوری آزاد |
| برق رفت / لپ‌تاپ خوابید | انقضای خودکار پس از مهلت |
| دو نفر همزمان درخواست قفل | `MERGE` اتمیک — فقط یکی موفق |
| قفل هست ولی حضور نیست | ناسازگاری → پاک‌سازی خودکار قفل |

> **هیچ‌کدام از این‌ها نباید صفحهٔ خالی یا خطای گنگ بدهد** — طبق الزام «حالت‌های خالی و خطا».

---

# ۷. آنچه ساخته نمی‌شود

- **قفل روی خواندن** — هرگز
- **قفل بدون انقضا** — همیشه انقضا داشته باشد
- **قفل دستی و دائمی** توسط کاربر
- **صف انتظار قفل** — پیچیدگی بی‌مورد در این مقیاس

---

# ۸. چک‌لیست پیاده‌سازی

- [ ] `[Timestamp] RowVersion` روی همهٔ موجودیت‌ها
- [ ] `DbUpdateConcurrencyException` در همه‌جا مدیریت شود، با نمایش تفاوت‌ها
- [ ] ایندکس یکتای ضدتکرار روی `Payments`
- [ ] جدول `RecordPresence` + `PresenceHub`
- [ ] جدول `RecordLock` + `MERGE` اتمیک
- [ ] Hangfire: پاک‌سازی حضور و قفل منقضی هر ۲ دقیقه
- [ ] دسترسی `Lock.ForceRelease` در سیستم نقش‌ها
- [ ] آزادسازی اجباری: دلیل + لاگ + اطلاع فوری
- [ ] پشتیبان درخواست دوره‌ای برای SignalR
- [ ] **تست:** دو مرورگر، همان پرونده، همهٔ سناریوهای بخش ۶
