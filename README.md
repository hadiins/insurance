# مدیریت وظایف و مشتریان

## بک‌اند (Hono + Node.js + SQL Server)

```bash
cd backend
npm install
cp .env.example .env   # مقادیر اتصال SQL Server را تنظیم کنید
npm run db:migrate     # ساخت جداول
npm run dev             # اجرا روی http://localhost:3001
```

## فرانت‌اند (React 18 + Ant Design 5، RTL)

```bash
cd frontend
npm install
npm run dev              # اجرا روی http://localhost:5173
```

فرانت‌اند درخواست‌های `/api` را به `http://localhost:3001` پروکسی می‌کند (`vite.config.ts`).
