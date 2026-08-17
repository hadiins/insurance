import { Hono } from "hono";
import { zValidator } from "@hono/zod-validator";
import { getPool, sql } from "../db.js";
import {
  createCustomerSchema,
  updateCustomerSchema,
  customerIdParamSchema,
} from "../schemas/customer.js";

const customers = new Hono();

customers.get("/", async (c) => {
  const pool = await getPool();
  const result = await pool
    .request()
    .query(
      `SELECT id, full_name AS fullName, phone, email, address, created_at AS createdAt, updated_at AS updatedAt
       FROM customers ORDER BY created_at DESC`
    );
  return c.json(result.recordset);
});

customers.get("/:id", zValidator("param", customerIdParamSchema), async (c) => {
  const { id } = c.req.valid("param");
  const pool = await getPool();
  const result = await pool
    .request()
    .input("id", sql.Int, id)
    .query(
      `SELECT id, full_name AS fullName, phone, email, address, created_at AS createdAt, updated_at AS updatedAt
       FROM customers WHERE id = @id`
    );
  const customer = result.recordset[0];
  if (!customer) return c.json({ error: "مشتری یافت نشد" }, 404);
  return c.json(customer);
});

customers.post("/", zValidator("json", createCustomerSchema), async (c) => {
  const input = c.req.valid("json");
  const pool = await getPool();
  const result = await pool
    .request()
    .input("fullName", sql.NVarChar, input.fullName)
    .input("phone", sql.NVarChar, input.phone)
    .input("email", sql.NVarChar, input.email || null)
    .input("address", sql.NVarChar, input.address ?? null)
    .query(
      `INSERT INTO customers (full_name, phone, email, address)
       OUTPUT INSERTED.id, INSERTED.full_name AS fullName, INSERTED.phone, INSERTED.email,
              INSERTED.address, INSERTED.created_at AS createdAt, INSERTED.updated_at AS updatedAt
       VALUES (@fullName, @phone, @email, @address)`
    );
  return c.json(result.recordset[0], 201);
});

customers.patch(
  "/:id",
  zValidator("param", customerIdParamSchema),
  zValidator("json", updateCustomerSchema),
  async (c) => {
    const { id } = c.req.valid("param");
    const input = c.req.valid("json");
    if (Object.keys(input).length === 0) {
      return c.json({ error: "هیچ داده‌ای برای به‌روزرسانی ارسال نشده" }, 400);
    }
    const pool = await getPool();
    const request = pool.request().input("id", sql.Int, id);
    const setClauses: string[] = [];
    if (input.fullName !== undefined) {
      request.input("fullName", sql.NVarChar, input.fullName);
      setClauses.push("full_name = @fullName");
    }
    if (input.phone !== undefined) {
      request.input("phone", sql.NVarChar, input.phone);
      setClauses.push("phone = @phone");
    }
    if (input.email !== undefined) {
      request.input("email", sql.NVarChar, input.email || null);
      setClauses.push("email = @email");
    }
    if (input.address !== undefined) {
      request.input("address", sql.NVarChar, input.address);
      setClauses.push("address = @address");
    }
    setClauses.push("updated_at = SYSUTCDATETIME()");
    const result = await request.query(
      `UPDATE customers SET ${setClauses.join(", ")}
       OUTPUT INSERTED.id, INSERTED.full_name AS fullName, INSERTED.phone, INSERTED.email,
              INSERTED.address, INSERTED.created_at AS createdAt, INSERTED.updated_at AS updatedAt
       WHERE id = @id`
    );
    const customer = result.recordset[0];
    if (!customer) return c.json({ error: "مشتری یافت نشد" }, 404);
    return c.json(customer);
  }
);

customers.delete("/:id", zValidator("param", customerIdParamSchema), async (c) => {
  const { id } = c.req.valid("param");
  const pool = await getPool();
  const result = await pool
    .request()
    .input("id", sql.Int, id)
    .query("DELETE FROM customers OUTPUT DELETED.id WHERE id = @id");
  if (result.recordset.length === 0) return c.json({ error: "مشتری یافت نشد" }, 404);
  return c.body(null, 204);
});

export default customers;
