import "dotenv/config";
import { serve } from "@hono/node-server";
import { Hono } from "hono";
import { cors } from "hono/cors";
import { HTTPException } from "hono/http-exception";
import { jwt } from "hono/jwt";
import auth from "./routes/auth.js";
import tasks from "./routes/tasks.js";
import customers from "./routes/customers.js";
import { getJwtSecret } from "./auth.js";

const app = new Hono();

app.use(
  "*",
  cors({
    origin: process.env.CORS_ORIGIN ?? "http://localhost:5173",
  })
);

app.get("/health", (c) => c.json({ ok: true }));

app.route("/api/auth", auth);

app.use("/api/tasks", jwt({ secret: getJwtSecret(), alg: "HS256" }));
app.use("/api/tasks/*", jwt({ secret: getJwtSecret(), alg: "HS256" }));
app.use("/api/customers", jwt({ secret: getJwtSecret(), alg: "HS256" }));
app.use("/api/customers/*", jwt({ secret: getJwtSecret(), alg: "HS256" }));

app.route("/api/tasks", tasks);
app.route("/api/customers", customers);

app.notFound((c) => c.json({ error: "مسیر یافت نشد" }, 404));

app.onError((err, c) => {
  if (err instanceof HTTPException) {
    return c.json({ error: err.message || "دسترسی غیرمجاز" }, err.status);
  }
  console.error(err);
  return c.json({ error: "خطای داخلی سرور" }, 500);
});

const port = Number(process.env.PORT ?? 3001);

serve({ fetch: app.fetch, port }, (info) => {
  console.log(`Server listening on http://localhost:${info.port}`);
});
