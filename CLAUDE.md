# Code style
- TypeScript strict mode
- Hono backend: هر route باید validation با zod داشته باشه
- React 18 + Ant Design 5، همیشه RTL (direction: rtl)
- تاریخ‌ها با تقویم جلالی

# Workflow
- بعد از هر تغییر: npm run typecheck
- تست تکی: npm run test -- <file>
- برای تست محلی Workers: wrangler dev