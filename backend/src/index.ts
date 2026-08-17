import "dotenv/config";
import { serve } from "@hono/node-server";
import { Hono } from "hono";
import { cors } from "hono/cors";
import tasks from "./routes/tasks.js";
import customers from "./routes/customers.js";

const app = new Hono();

app.use(
  "*",
  cors({
    origin: process.env.CORS_ORIGIN ?? "http://localhost:5173",
  })
);

app.get("/health", (c) => c.json({ ok: true }));

app.route("/api/tasks", tasks);
app.route("/api/customers", customers);

app.notFound((c) => c.json({ error: "مسیر یافت نشد" }, 404));

app.onError((err, c) => {
  console.error(err);
  return c.json({ error: "خطای داخلی سرور" }, 500);
});

const port = Number(process.env.PORT ?? 3001);

serve({ fetch: app.fetch, port }, (info) => {
  console.log(`Server listening on http://localhost:${info.port}`);
});
