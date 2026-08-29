# رانبوک تست بهروزرسانی — سرور اوبونتو (تکسرور)

**هدف:** اجرای کامل «Check تسک ۲۱» از `docs/UPDATE-SYSTEM.md` — یک نسخه را منتشر کن و روی محیط
آزمایشی اعمالش کن: از build ایمیج تا پنل، با پیشرفت زنده و پشتیبانگیری خودکار. این همان آزمایش
واقعی سامانهٔ بهروزرسانی (`Aqsat.Updater` + پنل «بهروزرسانی سیستم») است.

مسیر کلی:

```
استقرار اولیهٔ ۱.۰.۰  ←  کلید امضا  ←  راهاندازی updater  ←  انتشار بستهٔ ۱.۱.۰  ←  اعمال از پنل با OTP  ←  راستیآزمایی  ←  (اختیاری) بازگشت
```

> نسخهٔ مبنا را «۱.۰.۰» و بستهٔ تست را «۱.۱.۰» فرض کردهایم. اگر سرور از قبل استک در حال اجرا دارد،
> فقط مطمئن شوید ایمیجِ در حال اجرا با `APP_VERSION` مهر شده (نسخهٔ درست در پنل نشان داده شود) و
> از گام ۲ ادامه دهید.

---

## ۰. پیشنیازها

- سرور اوبونتو با Docker + افزونهٔ compose (`docker compose version`)
- `openssl`، `jq`، `curl` — همه پیشفرض اوبونتو هستند
- کدِ این شاخه روی سرور (git clone/pull یا rsync) و قرارگرفتن در ریشهٔ مخزن
- پورت ۸۰۸۰ برای پنل؛ برای استفادهٔ واقعی پشت reverse proxy با TLS

## ۱. استقرار اولیهٔ نسخهٔ ۱.۰.۰ (اگر سرور خالی است)

```bash
cp .env.example .env
nano .env   # مقادیر واقعی را پر کنید — جدول متغیرها در README.md
```

مقادیر `.env` برای این تست:

| متغیر | مقدار |
|---|---|
| `SA_PASSWORD` | رمز قوی دلخواه (سیاست رمز SQL Server را رعایت کند) |
| `JWT_KEY` | `openssl rand -base64 48` |
| `NATIONAL_ID_KEY` | کلید AES — نسخهٔ قبلی داشتهاید، همان را بگذارید تا دادهها خوانا بمانند |
| `APP_VERSION` | `1.0.0` — موقع build داخل ایمیج مهر میشود |
| `BOOTSTRAP_SECRET` | یک رشتهٔ تصادفی — فقط برای ساخت اولین `Platform.Owner` |
| `UPDATER_SHARED_TOKEN` | `openssl rand -hex 32` |
| `UPDATER_SIGNING_PUBLIC_KEY_PEM` | گام ۲ — فعلاً خالی |

```bash
# GID گروه مالک docker.sock روی این میزبان (بعداً برای updater لازم میشود):
stat -c '%g' /var/run/docker.sock    # مثلاً 999 — در .env بنویسید: DOCKER_GID=999

docker compose -f docker-compose.prod.yml --env-file .env build
docker compose -f docker-compose.prod.yml --env-file .env up -d

# فقط بار اول: مالکیت پوشهٔ پشتیبانها (README.md §Run)
docker compose -f docker-compose.prod.yml exec -u root sqlserver \
  bash -c "mkdir -p /var/opt/mssql/backup && chown mssql:mssql /var/opt/mssql/backup && chmod 0777 /var/opt/mssql/backup"

# اولین حساب Platform.Owner (کلید عمومی امضا هنوز لازم نیست؛ پنل بدون updater هم بالا میآید):
curl -sf -X POST http://127.0.0.1:8080/api/platform/bootstrap-owner \
  -H "Content-Type: application/json" \
  -d '{"mobile":"09xxxxxxxxx","password":"...","secret":"<BOOTSTRAP_SECRET .env>"}'
# بعد از ساختهشدن حساب، BOOTSTRAP_SECRET را از .env خالی کنید و یکبار:
#   docker compose -f docker-compose.prod.yml --env-file .env up -d api
```

نکتهٔ مهاجرت: کانتینر api موقع بالا آمدن، مهاجرتهای EF Core را خودش اعمال میکند (با قفل
`sp_getapplock`). اگر دیتابیس از قبل دستی مهاجرت شده (مثل LocalDB توسعه)، اینجا هم خودکار تشخیص
داده و فقط مابقی اعمال میشود.

---

## ۲. کلید امضا و راهاندازی updater

```bash
# فقط بار اول (publish-local.sh هم اگر نبود خودش میسازد). اسکریپتها با bash صدا زده میشوند
# تا اجراییبودن فایلها مهم نباشد — گیت آنها را 100644 ذخیره میکند:
bash scripts/release/generate-signing-key.sh secrets

# کلید عمومی بهصورت مقدار چندخطی کوتیشندار به .env اضافه میشود — compose v2 آن را درست میخواند:
printf '\nUPDATER_SIGNING_PUBLIC_KEY_PEM="%s"\n' "$(cat secrets/release-signing-key.pub.pem)" >> .env

# راستیآزمایی — باید ۲ بار (api + updater) دیده شود:
docker compose -f docker-compose.prod.yml --env-file .env config | grep -c "BEGIN PUBLIC KEY"

docker compose -f docker-compose.prod.yml --env-file .env up -d updater
# اگر GID سوکت با پیشفرض ۹۹۹ فرق داشت، اول:
#   DOCKER_GID=$(stat -c '%g' /var/run/docker.sock) docker compose -f docker-compose.prod.yml --env-file .env build updater
```

کلید خصوصی (`secrets/release-signing-key.pem`) هرگز از سرور/مخزن خارج نمیشود — در `.gitignore`
هست؛ از آن یک نسخهٔ پشتیبان آفلاین بگیرید (گمشدنش یعنی چرخش کلید و باطلشدن همهٔ بستهها).

## ۳. انتشار بستهٔ ۱.۱.۰

```bash
export PLATFORM_OWNER_MOBILE="09xxxxxxxxx"
export PLATFORM_OWNER_PASSWORD="..."
bash scripts/release/publish-local.sh --version 1.1.0 \
  --db-migration \
  --notes "تکمیل ویزارد صدور بیمهنامه (جستجوی کد ملی، اقساط، دریافت پیشپرداخت و صدور نهایی)؛ ذخیرهٔ متنی کد ملیها؛ رفع اشکالات سامانهٔ بهروزرسانی."
```

شش گام اسکریپت: کلید امضا (در صورت نبود) ← رجیستری محلی روی `127.0.0.1:5000` ← build ایمیج با
مهر نسخه ← push ← امضای مانیفست (`release-out/manifest-1.1.0.json`) ← ثبت در API با تأیید امضا
سمت سرور. خروجی گام ششم، بستهٔ ثبتشده را نشان میدهد.

## ۴. اعمال از پنل

1. ورود با `Platform.Owner` ← صفحهٔ «بهروزرسانی سیستم» (فقط این نقش آن را میبیند)
2. «درخواست کد» — OTP به موبایل مالک پیامک میشود (رمز بهتنهایی کافی نیست)
3. شروع بهروزرسانی — پنل ۹ مرحله را با درصد زنده نشان میدهد: پیشنیازها ۵٪ ← دانلود ۱۵٪ ← امضا
   و هش ۵٪ ← **پشتیبانگیری دیتابیس ۲۰٪** ← حالت تعمیر ۵٪ ← مهاجرت ۲۰٪ (کانتینر جدید موقع بالا
   آمدن اجرا میکند) ← جابهجایی کانتینر ۱۵٪ ← health check ۱۰٪ ← خروج از حالت تعمیر ۵٪
4. کاربران فعال ۶۰ ثانیه قبل، هشدار SignalR میگیرند؛ درخواستهای جدید به صفحهٔ تعمیر میروند
5. اگر هر مرحله بعد از پشتیبانگیری شکست بخورد، بهطور خودکار به ایمیج قبلی بازمیگردد و وضعیت
   در تاریخچه با خطای فارسی ثبت میشود

## ۵. راستیآزمایی

```bash
curl -s http://127.0.0.1:8080/api/platform/updates/status          # "version":"1.1.0"
docker inspect aqsat-api --format '{{.Config.Image}}'              # 127.0.0.1:5000/aqsat-api:1.1.0
docker logs aqsat-api 2>&1 | grep -i -E "migrat|RemovedNationalId" # مهاجرت RemoveNationalIdEncryption اعمال شده
ls -la /var/lib/docker/volumes/*_sqlserver-backup/_data | grep preupdate   # پشتیبان قبل از بهروزرسانی
```

در پنل: نسخهٔ نمایشدادهشده = ۱.۱.۰، جدول «تاریخچهٔ بهروزرسانیها» یک ردیف موفق.

## ۶. تست بازگشت (اختیاری ولی توصیهشده)

- پنل ← «بازگشت به نسخهٔ قبلی»: به ایمیج ۱.۰.۰ برمیگردد و health check میشود. توجه: بازگشت ایمیج
  مهاجرت را برنمیگرداند — اگر مهاجرت اجرا شده باشد، بازگردانی پشتیبان `preupdate` یک تصمیم دستی
  و آگاهانه است (`docs/UPDATE-SYSTEM.md` §6)
- لغو پیشنهاد یک نسخهٔ معیوب: `POST /api/platform/updates/packages/{id}/yank`

## ۷. عیبیابی

| علامت | علت / اصلاح |
|---|---|
| `Permission denied` روی docker.sock در updater | `DOCKER_GID` با میزبان فرق دارد — با `stat -c '%g' /var/run/docker.sock` بسازید و `up -d updater` |
| «امضای بسته نامعتبر» هنگام ثبت | کلید عمومی `.env` غایب یا متفاوت با `secrets/release-signing-key.pem` — کلید را دوباره بگذارید و `up -d api updater` |
| «هش مطابقت ندارد» در مرحلهٔ pull | ایمیج push نشده یا تگ دیگری است — `publish-local.sh` را تکرار کنید و RepoDigests را چک کنید |
| رجیستری محلی بالا نمیآید | pull شدن `registry:2` از docker.io — mirror/پروکسی داکر را تنظیم و `docker start aqsat-registry` |
| توقف در مرحلهٔ پیشنیازها: فضای دیسک | حداقل ۲ برابر حجم دیتابیس فضای آزاد لازم است |
| «یک بهروزرسانی دیگر در حال اجراست» | حالت تعمیر یا run قبلی — `GET /api/platform/updates/current` و تاریخچه |
| پنجرهٔ زمانی (۲ تا ۶ بامداد) | قاعدهٔ §8 سامانهٔ بهروزرسانی — یا تأیید صریح «اضطراری» در پنل |
| «نیازمند حداقل نسخهٔ …» با وجود نسخهٔ درست | ایمیجِ در حال اجرا بدون مهر نسخه (`unknown`) است — با `APP_VERSION` rebuild کنید |

## ۸. چه چیزی این تست را پوشش نمیدهد

- انتشار مرحلهای چندسروری (آزمایشی ← ۱ نمایندگی ← ۱۰٪ ← همه) — `docs/RELEASE-RUNBOOK.md` §5
- خط CI (`.github/workflows/release.yml`) — نیازمند secrets مخزن و رجیستری بیرونی است
- بازگردانی از پشتیبان — `docs/RESTORE-RUNBOOK.md`
