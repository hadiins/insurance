# Phase 2A — Insurance Credit & Risk Management
## سند اجرایی پیاده‌سازی برای Repository: `hadiins/new`

### 1. هدف Phase 2A

هدف این فاز تبدیل امکانات فعلی مربوط به مشتریان، اقساط، بدهی، چک و معوقات به یک سیستم رسمی **Credit & Risk Management** است.

سیستم باید بتواند:

1. وضعیت اعتباری مشتری را محاسبه کند.
2. مشتری را در یکی از سطوح ریسک قرار دهد.
3. دلایل افزایش/کاهش ریسک را توضیح دهد.
4. برای اعتبار پیشنهادی مشتری تصمیم اولیه ارائه کند.
5. موارد پرریسک را برای بررسی دستی جدا کند.
6. سابقه ارزیابی‌های قبلی را نگهداری کند.
7. امکان اضافه‌کردن ML/AI در فازهای بعدی را بدون بازطراحی فراهم کند.

### 2. اصل معماری

فاز 2A باید به این شکل عمل کند:

Customer Data
→ Feature Calculation
→ Rule Engine
→ Weighted Credit Score
→ Risk Classification
→ Decision
→ Audit / Snapshot

AI نباید جایگزین Rule Engine شود.

AI/ML در آینده فقط به عنوان یک لایه تکمیلی اضافه خواهد شد:

Rule Engine + ML Prediction + Business Policy → Final Decision

---

# 3. استفاده از امکانات فعلی پروژه

در Repository فعلی Featureهای جداگانه‌ای برای:

- customers
- installments
- cheques
- collateral
- cashflow
- policies
- reports

وجود دارد.

بنابراین Feature جدید Risk باید از ابتدا ایجاد شود و از Featureهای موجود تغذیه کند؛ نباید اطلاعات مشتری، بدهی و اقساط را دوباره در یک مدل مستقل کپی کند.

ساختار Feature-Based فعلی حفظ شود.

---

# 4. ساختار فولدر پیشنهادی

در `src/features`:

```text
src/features/
├── risk/
│   ├── api/
│   │   ├── riskApi.ts
│   │   ├── creditApi.ts
│   │   └── riskRulesApi.ts
│   │
│   ├── components/
│   │   ├── CreditScoreCard.tsx
│   │   ├── RiskLevelBadge.tsx
│   │   ├── RiskFactorList.tsx
│   │   ├── RiskScoreBreakdown.tsx
│   │   ├── CreditLimitCard.tsx
│   │   ├── DecisionBadge.tsx
│   │   ├── RiskTrendChart.tsx
│   │   ├── RiskWarningCard.tsx
│   │   ├── RiskSummaryCard.tsx
│   │   └── ManualReviewCard.tsx
│   │
│   ├── pages/
│   │   ├── RiskDashboardPage.tsx
│   │   ├── CreditAssessmentPage.tsx
│   │   ├── RiskCustomersPage.tsx
│   │   ├── RiskWarningsPage.tsx
│   │   ├── ManualReviewsPage.tsx
│   │   ├── CreditLimitsPage.tsx
│   │   ├── CreditRulesPage.tsx
│   │   └── RiskReportsPage.tsx
│   │
│   ├── hooks/
│   │   ├── useRiskDashboard.ts
│   │   ├── useCreditAssessment.ts
│   │   ├── useRiskHistory.ts
│   │   └── useRiskWarnings.ts
│   │
│   ├── types/
│   │   ├── risk.types.ts
│   │   ├── credit.types.ts
│   │   └── riskRule.types.ts
│   │
│   └── utils/
│       ├── riskFormatters.ts
│       └── riskLabels.ts
```

از ساختار موجود Feature-Based خارج نشو.

---

# 5. Navigation

در `src/app/navConfig.ts` یک گروه جدید اضافه شود:

```text
اعتبار و ریسک
├── داشبورد ریسک
├── ارزیابی اعتبار
├── پرونده‌های نیازمند بررسی
├── مشتریان پرریسک
├── هشدارهای ریسک
├── حدود اعتبار
├── قوانین اعتبارسنجی
└── گزارش ریسک
```

صفحه فعلی مشتریان پرریسک حذف نشود؛ در صورت وجود قابلیت قبلی، Route آن با صفحه جدید Risk یکپارچه شود.

---

# 6. Risk Dashboard

مسیر پیشنهادی:

```text
/risk
```

صفحه باید این KPIها را داشته باشد:

```text
کل مشتریان
ریسک پایین
ریسک متوسط
ریسک بالا
ریسک بحرانی
بدهی جاری
کل معوقات
نرخ پرداخت به‌موقع
نرخ نکول
پرونده‌های نیازمند بررسی
```

### نمودارها

1. توزیع مشتریان بر اساس Risk Level
2. روند Risk Score
3. روند معوقات
4. روند پرداخت به‌موقع
5. تعداد مشتریان واردشده به High Risk
6. هشدارهای فعال

از Recharts موجود در پروژه استفاده شود و کتابخانه Chart جدید اضافه نشود.

---

# 7. Customer Credit Profile

به Customer File فعلی یک بخش جدید اضافه شود:

```text
اطلاعات پایه
بیمه‌نامه‌ها
اقساط
پرداخت‌ها
چک‌ها
وثایق
اعتبار و ریسک
```

در Tab «اعتبار و ریسک»:

```text
Credit Score
Risk Level
Probability of Default
Credit Limit
Current Exposure
Payment Behavior
Risk Factors
Risk History
Warnings
Decisions
```

اطلاعات قبلی مشتری نباید دوباره‌سازی شود.

---

# 8. Credit Score

Score اولیه در Phase 2A از 0 تا 1000 باشد.

مثال دسته‌بندی:

```text
800 - 1000  → VERY_LOW
700 - 799   → LOW
550 - 699   → MEDIUM
400 - 549   → HIGH
0 - 399     → CRITICAL
```

این بازه‌ها Configurable باشند و در UI به صورت Hardcoded پخش نشوند.

---

# 9. عوامل امتیازدهی

نسخه اولیه از عوامل زیر استفاده کند:

```text
Payment History              30%
Current Debt                 20%
Late Payment Behavior        20%
Returned Cheques             15%
Customer Tenure              10%
Insurance Behavior            5%
```

وزن‌ها در یک فایل Config قرار گیرند:

```ts
const CREDIT_SCORE_WEIGHTS = {
  paymentHistory: 0.30,
  currentDebt: 0.20,
  latePayment: 0.20,
  returnedCheques: 0.15,
  customerTenure: 0.10,
  insuranceBehavior: 0.05,
};
```

Rule Engine نباید مستقیماً UI را کنترل کند.

---

# 10. Risk Factors

برای هر Score باید عوامل تأثیرگذار ثبت شوند.

نمونه:

```json
{
  "code": "LATE_PAYMENT_FREQUENCY",
  "title": "تأخیر در پرداخت",
  "impact": -42,
  "severity": "HIGH"
}
```

نمونه‌های مثبت:

```text
سابقه پرداخت منظم
سابقه همکاری طولانی
تمدید منظم بیمه‌نامه
عدم وجود نکول
```

نمونه‌های منفی:

```text
افزایش بدهی
تأخیرهای متوالی
چک برگشتی
معوقه قدیمی
عبور از سقف اعتبار
نرخ پایین پرداخت به‌موقع
```

---

# 11. Rule Engine

قوانین در یک بخش مستقل مدیریت شوند.

هر Rule شامل:

```text
id
name
description
status
priority
conditions
action
severity
createdAt
updatedAt
```

مثال:

```json
{
  "name": "Returned Cheques",
  "conditions": [
    {
      "field": "returnedChequeCount",
      "operator": ">=",
      "value": 2
    }
  ],
  "action": {
    "riskLevel": "HIGH"
  },
  "priority": 90,
  "active": true
}
```

### Operatorهای اولیه

```text
=
!=
>
>=
<
<=
IN
NOT_IN
BETWEEN
```

در Phase 2A از Expression Language پیچیده استفاده نشود.

---

# 12. قوانین اولیه سیستم

حداقل این قوانین پیاده شوند:

### Rule 1
اگر تعداد چک برگشتی >= 2:

```text
Risk = HIGH
```

### Rule 2
اگر معوقه شدید وجود دارد:

```text
Risk = HIGH
```

### Rule 3
اگر تأخیر حداکثر >= 60 روز:

```text
Risk = HIGH
```

### Rule 4
اگر سابقه نکول قطعی وجود دارد:

```text
Risk = CRITICAL
```

### Rule 5
اگر بدهی جاری از حد اعتبار عبور کرد:

```text
Risk = REVIEW
```

### Rule 6
اگر پرداخت به‌موقع >= 90% باشد:

```text
Positive Risk Factor
```

این مقادیر باید Configuration باشند و بعداً از UI قابل تنظیم شوند.

---

# 13. Decision Engine

خروجی نهایی فقط Score نباشد.

Decisionهای اولیه:

```text
APPROVE
MANUAL_REVIEW
DECLINE
```

مثال:

```text
Score >= 700
و Rule بحرانی فعال نیست
→ APPROVE
```

```text
Score بین 550 و 699
→ MANUAL_REVIEW
```

```text
Score < 400
یا Rule Critical
→ DECLINE
```

اما Ruleهای صریح باید بتوانند Score را Override کنند.

مثلاً:

```text
Score = 760
اما سابقه نکول قطعی = true

Final Decision = DECLINE
```

---

# 14. Credit Limit

سیستم باید حد اعتبار پیشنهادی ایجاد کند.

در Phase 2A فرمول ساده و Configurable باشد:

```text
Base Limit
× Risk Multiplier
= Recommended Credit Limit
```

مثال:

```text
LOW       → 1.00
MEDIUM    → 0.70
HIGH      → 0.35
CRITICAL  → 0
```

فرمول واقعی بعداً بر اساس Business Policy اصلاح خواهد شد.

---

# 15. Manual Review

صفحه:

```text
/risk/manual-reviews
```

برای مواردی که سیستم نمی‌تواند تصمیم قطعی بگیرد.

هر پرونده:

```text
Customer
Score
Risk Level
Current Debt
Overdue
Returned Cheques
Triggered Rules
Recommended Action
Assigned Reviewer
Status
```

Status:

```text
PENDING
IN_REVIEW
APPROVED
REJECTED
REQUEST_MORE_INFO
```

---

# 16. Risk Assessment

Route:

```text
/customers/:id/risk
```

صفحه باید امکان:

```text
[اجرای ارزیابی مجدد]
```

داشته باشد.

بعد از اجرا:

```text
Score
Risk Level
Factors
Triggered Rules
Recommended Credit Limit
Decision
```

نمایش داده شود.

---

# 17. Risk History

برای هر مشتری تاریخچه ارزیابی حفظ شود:

```text
Date
Score
Risk Level
Decision
Credit Limit
Top Risk Factors
Assessment Source
```

مثال:

```text
2026/06/01   780   LOW       APPROVE
2026/07/01   742   LOW       APPROVE
2026/08/01   681   MEDIUM    REVIEW
2026/09/01   612   MEDIUM    REVIEW
```

این قسمت برای Phaseهای ML آینده بسیار مهم است.

---

# 18. Risk Snapshot

در هر Assessment یک Snapshot از وضعیت مشتری ذخیره شود:

```text
customerId
assessmentId
score
riskLevel
currentDebt
overdueAmount
overdueCount
returnedChequeCount
paymentRate
creditExposure
creditLimit
decision
createdAt
```

با این کار، تغییر اطلاعات فعلی باعث خراب‌شدن تاریخچه نمی‌شود.

---

# 19. Early Warning

در Phase 2A یک نسخه ساده اجرا شود.

Warning زمانی ساخته شود که:

```text
Risk Level افزایش یابد
یا
Score افت قابل‌توجه داشته باشد
یا
قسط وارد محدوده هشدار شود
یا
بدهی سریع افزایش یابد
یا
چک برگشتی جدید ثبت شود
```

مثال:

```text
⚠ افزایش سطح ریسک

مشتری: شرکت نمونه

LOW → MEDIUM

دلایل:
• دو تأخیر جدید
• رشد بدهی 27%
• نزدیک‌شدن به سقف اعتبار
```

---

# 20. API Contract

Frontend باید API Layer مشخص داشته باشد.

### Dashboard

```http
GET /api/risk/dashboard
```

### Customer Risk

```http
GET /api/customers/{customerId}/risk
```

### Assessment

```http
POST /api/customers/{customerId}/risk/assess
```

### History

```http
GET /api/customers/{customerId}/risk/history
```

### High Risk

```http
GET /api/risk/high-risk
```

### Warnings

```http
GET /api/risk/warnings
```

### Manual Reviews

```http
GET /api/risk/manual-reviews
```

### Decision

```http
POST /api/risk/assessments/{assessmentId}/decision
```

### Rules

```http
GET /api/risk/rules
POST /api/risk/rules
PUT /api/risk/rules/{id}
DELETE /api/risk/rules/{id}
```

### Credit Limit

```http
GET /api/customers/{customerId}/credit-limit
PUT /api/customers/{customerId}/credit-limit
```

---

# 21. TypeScript Models

نمونه:

```ts
export type RiskLevel =
  | "VERY_LOW"
  | "LOW"
  | "MEDIUM"
  | "HIGH"
  | "CRITICAL";

export type RiskDecision =
  | "APPROVE"
  | "MANUAL_REVIEW"
  | "DECLINE";

export interface CreditAssessment {
  id: string;
  customerId: string;
  score: number;
  riskLevel: RiskLevel;
  probabilityOfDefault?: number;
  currentDebt: number;
  overdueAmount: number;
  overdueCount: number;
  returnedChequeCount: number;
  paymentRate: number;
  creditLimit: number;
  decision: RiskDecision;
  factors: RiskFactor[];
  triggeredRules: RiskRuleResult[];
  createdAt: string;
}
```

`probabilityOfDefault` فعلاً Optional باشد و در Phase 2A مقدار واقعی ML در آن قرار نگیرد.

---

# 22. React Query

تمام Server State مربوط به Risk با React Query مدیریت شود.

مثال:

```ts
useQuery({
  queryKey: ["risk", "dashboard"],
  queryFn: getRiskDashboard,
});
```

بعد از Assessment:

```ts
queryClient.invalidateQueries({
  queryKey: ["customer", customerId, "risk"],
});
```

از ایجاد State کپی‌شده و غیرضروری در Zustand خودداری شود.

Zustand فقط برای UI Stateهایی استفاده شود که واقعاً Global هستند.

---

# 23. Loading / Error / Empty State

تمام صفحات Risk باید این سه حالت را داشته باشند:

```text
Loading
Error
Empty
```

مثلاً:

```text
در حال محاسبه وضعیت اعتباری...
```

یا:

```text
اطلاعات کافی برای ارزیابی این مشتری وجود ندارد.
```

هرگز صفحه سفید نمایش داده نشود.

---

# 24. UX Rules

رابط کاربری باید از Design System فعلی پروژه استفاده کند.

Component جدید برای Card/Table/Badge فقط زمانی بساز که معادل آن در Components فعلی وجود نداشته باشد.

رنگ‌ها:

```text
Low      → Success
Medium   → Warning
High     → Danger
Critical → Danger + Strong Emphasis
```

از رنگ قرمز برای تمام موارد استفاده نشود.

---

# 25. Customer Risk Header

در بالای پرونده مشتری:

```text
┌────────────────────────────────────┐
│ محمد رضایی                         │
│ مشتری حقیقی                        │
│                                    │
│ Credit Score       742             │
│ Risk               LOW             │
│ Credit Limit       850M            │
│ Current Exposure   420M            │
│                                    │
│ [ارزیابی مجدد]                     │
└────────────────────────────────────┘
```

این Header باید اولین چیزی باشد که کاربر مالی بعد از ورود به Tab ریسک می‌بیند.

---

# 26. Explainability

هر تصمیم باید قابل توضیح باشد.

مثال:

```text
سطح ریسک: MEDIUM

عوامل افزایش ریسک:
- دو تأخیر در 90 روز اخیر
- افزایش بدهی
- یک چک برگشتی

عوامل کاهش ریسک:
+ سابقه 3 ساله
+ 89% پرداخت به‌موقع
+ تمدید منظم بیمه‌نامه
```

هیچ Score بدون Factors نمایش داده نشود.

---

# 27. Audit Log

تمام عملیات حساس ثبت شوند:

```text
Assessment Created
Rule Triggered
Risk Score Changed
Decision Made
Credit Limit Changed
Manual Review Assigned
Manual Review Closed
Rule Created
Rule Updated
Rule Disabled
```

برای هر Event:

```text
userId
action
entityType
entityId
before
after
timestamp
```

---

# 28. Data Safety

اطلاعات حساس مشتری در Frontend Log نشود.

از:

```text
console.log(customer)
```

برای اطلاعات حساس استفاده نشود.

Token و Credential داخل Source Code قرار نگیرد.

API Error نباید اطلاعات داخلی Backend را در UI نمایش دهد.

---

# 29. تست‌های ضروری

حداقل Unit Test برای Rule Engine:

```text
No overdue → LOW
One minor overdue → LOW/MEDIUM
Two returned cheques → HIGH
Previous default → CRITICAL
Score 750 → LOW
Score 620 → MEDIUM
Score 350 → CRITICAL
Critical rule overrides high score
```

برای Decision Engine نیز تست شود:

```text
Score 750 + no critical rule → APPROVE
Score 620 → MANUAL_REVIEW
Score 350 → DECLINE
Score 800 + default history → DECLINE
```

---

# 30. Definition of Done

Phase 2A زمانی کامل است که:

[ ] منوی اعتبار و ریسک اضافه شده باشد.

[ ] Risk Dashboard فعال باشد.

[ ] Customer Risk Profile در Customer File نمایش داده شود.

[ ] Credit Score محاسبه شود.

[ ] Risk Level محاسبه شود.

[ ] عوامل Score نمایش داده شوند.

[ ] Rule Engine فعال باشد.

[ ] Rules از UI قابل مدیریت باشند.

[ ] Manual Review فعال باشد.

[ ] Credit Limit پیشنهادی تولید شود.

[ ] Decision Workflow فعال باشد.

[ ] Risk History ذخیره شود.

[ ] Risk Snapshot ذخیره شود.

[ ] Early Warning پایه فعال باشد.

[ ] Audit Log وجود داشته باشد.

[ ] Loading/Error/Empty State وجود داشته باشد.

[ ] Unit Test برای Rule و Decision نوشته شده باشد.

[ ] Build و Lint بدون Error باشند.

---

# 31. چیزهایی که در Phase 2A نباید انجام شوند

در این مرحله:

- ML واقعی اضافه نشود.
- LLM تصمیم نهایی نگیرد.
- معماری فعلی بازنویسی نشود.
- Customer Entity جدید ساخته نشود.
- کتابخانه UI جدید اضافه نشود مگر ضرورت فنی.
- Design System جدید ساخته نشود.
- اطلاعات مالی در Feature جدید Duplicate نشود.

---

# 32. آماده‌سازی برای Phase 2B

از همین الان Interfaceها طوری طراحی شوند که بعداً بتوانیم اضافه کنیم:

```text
MLScoreProvider
PDProvider
ExternalCreditProvider
IdentityVerificationProvider
```

و بعداً:

```text
RuleScore
+
MLScore
+
ExternalCreditScore
+
BusinessPolicy
=
Final Risk
```

همچنین این فیلدها از الان در Model قابل توسعه باشند:

```text
modelVersion
scoreSource
assessmentVersion
```

---

# 33. آماده‌سازی برای ML

از هر Assessment اطلاعات زیر نگهداری شود:

```text
input snapshot
score
decision
rules
outcome
timestamp
```

در آینده برای Training Dataset:

```text
Assessment
→ Future Payment Behaviour
→ Default / Non-default
```

استفاده خواهد شد.

---

# 34. ترتیب پیاده‌سازی

ترتیب اجرا دقیقاً به این شکل باشد:

### Sprint 1
- Types
- API Layer
- Risk routing
- Navigation
- Basic Customer Risk Profile

### Sprint 2
- Score Engine
- Rule Engine
- Risk Level
- Factors

### Sprint 3
- Risk Dashboard
- High Risk
- Manual Review
- Credit Limit

### Sprint 4
- Risk History
- Snapshot
- Early Warning
- Audit
- Testing
- Polish

---

# 35. دستور نهایی برای AI Coding Agent

قبل از هر تغییر:

1. Repository را کامل بررسی کن.
2. ساختار موجود `src/app`, `src/components`, `src/features` را حفظ کن.
3. Featureهای `customers`, `installments`, `cheques`, `collateral`, `cashflow`, `policies`, `reports` را بررسی کن.
4. هیچ فایل موجودی را بدون دلیل بازنویسی نکن.
5. Componentهای موجود را مجدداً استفاده کن.
6. قبل از ساخت Component جدید، وجود معادل آن را بررسی کن.

سپس Phase 2A را طبق این سند پیاده‌سازی کن.

الزامات:

- React + TypeScript
- Vite
- Tailwind فعلی
- React Query فعلی
- Zustand فعلی در صورت نیاز
- Recharts فعلی
- ساختار Feature-Based فعلی
- عدم اضافه‌کردن dependency غیرضروری

اولویت:

```text
Correctness
Consistency
Explainability
Maintainability
UX
Visual polish
```

پس از هر بخش:

```text
npm run lint
npm run build
```

را اجرا کن و تمام TypeScript/Lint/Build Errorها را برطرف کن.

در پایان گزارش بده:

```text
Files Created
Files Modified
Routes Added
Components Added
API Contracts
Business Rules
Tests Added
Build Result
Known Limitations
```

هر تصمیم معماری که با ساختار فعلی Repository تناقض دارد قبل از تغییر مستند و ساده‌ترین راه سازگار با کد موجود انتخاب شود.