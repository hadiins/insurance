import { Hono } from "hono";
import { zValidator } from "@hono/zod-validator";
import { sign } from "hono/jwt";
import bcrypt from "bcryptjs";
import { getPool, sql } from "../db.js";
import { registerSchema, loginSchema } from "../schemas/auth.js";
import { getJwtSecret } from "../auth.js";

const auth = new Hono();

const TOKEN_TTL_SECONDS = 60 * 60 * 24 * 7; // 7 days

async function issueToken(user: { id: number; email: string; fullName: string }) {
  const now = Math.floor(Date.now() / 1000);
  return sign(
    {
      sub: String(user.id),
      email: user.email,
      fullName: user.fullName,
      iat: now,
      exp: now + TOKEN_TTL_SECONDS,
    },
    getJwtSecret()
  );
}

auth.post("/register", zValidator("json", registerSchema), async (c) => {
  const { fullName, email, password } = c.req.valid("json");
  const pool = await getPool();

  const existing = await pool
    .request()
    .input("email", sql.NVarChar, email)
    .query("SELECT id FROM users WHERE email = @email");
  if (existing.recordset.length > 0) {
    return c.json({ error: "این ایمیل قبلاً ثبت شده است" }, 409);
  }

  const passwordHash = await bcrypt.hash(password, 10);
  const result = await pool
    .request()
    .input("fullName", sql.NVarChar, fullName)
    .input("email", sql.NVarChar, email)
    .input("passwordHash", sql.NVarChar, passwordHash)
    .query(
      `INSERT INTO users (full_name, email, password_hash)
       OUTPUT INSERTED.id, INSERTED.full_name AS fullName, INSERTED.email
       VALUES (@fullName, @email, @passwordHash)`
    );
  const user = result.recordset[0];
  const token = await issueToken(user);
  return c.json({ token, user }, 201);
});

auth.post("/login", zValidator("json", loginSchema), async (c) => {
  const { email, password } = c.req.valid("json");
  const pool = await getPool();

  const result = await pool
    .request()
    .input("email", sql.NVarChar, email)
    .query(
      "SELECT id, full_name AS fullName, email, password_hash AS passwordHash FROM users WHERE email = @email"
    );
  const user = result.recordset[0];
  if (!user || !(await bcrypt.compare(password, user.passwordHash))) {
    return c.json({ error: "ایمیل یا رمز عبور نادرست است" }, 401);
  }

  const token = await issueToken(user);
  return c.json({ token, user: { id: user.id, fullName: user.fullName, email: user.email } });
});

export default auth;
