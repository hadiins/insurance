import { Hono } from "hono";
import { zValidator } from "@hono/zod-validator";
import { getPool, sql } from "../db.js";
import {
  createTaskSchema,
  updateTaskSchema,
  taskIdParamSchema,
  listTasksQuerySchema,
} from "../schemas/task.js";

const tasks = new Hono();

tasks.get("/", zValidator("query", listTasksQuerySchema), async (c) => {
  const { status, customerId } = c.req.valid("query");
  const pool = await getPool();
  const request = pool.request();
  const conditions: string[] = [];
  if (status) {
    request.input("status", sql.NVarChar, status);
    conditions.push("status = @status");
  }
  if (customerId) {
    request.input("customerId", sql.Int, customerId);
    conditions.push("customer_id = @customerId");
  }
  const where = conditions.length ? `WHERE ${conditions.join(" AND ")}` : "";
  const result = await request.query(
    `SELECT id, title, description, status, CONVERT(varchar(10), due_date, 23) AS dueDate, customer_id AS customerId, created_at AS createdAt, updated_at AS updatedAt
     FROM tasks ${where} ORDER BY created_at DESC`
  );
  return c.json(result.recordset);
});

tasks.get("/:id", zValidator("param", taskIdParamSchema), async (c) => {
  const { id } = c.req.valid("param");
  const pool = await getPool();
  const result = await pool
    .request()
    .input("id", sql.Int, id)
    .query(
      `SELECT id, title, description, status, CONVERT(varchar(10), due_date, 23) AS dueDate, customer_id AS customerId, created_at AS createdAt, updated_at AS updatedAt
       FROM tasks WHERE id = @id`
    );
  const task = result.recordset[0];
  if (!task) return c.json({ error: "تسک یافت نشد" }, 404);
  return c.json(task);
});

tasks.post("/", zValidator("json", createTaskSchema), async (c) => {
  const input = c.req.valid("json");
  const pool = await getPool();
  const result = await pool
    .request()
    .input("title", sql.NVarChar, input.title)
    .input("description", sql.NVarChar, input.description ?? null)
    .input("status", sql.NVarChar, input.status)
    .input("dueDate", sql.Date, input.dueDate ?? null)
    .input("customerId", sql.Int, input.customerId ?? null)
    .query(
      `INSERT INTO tasks (title, description, status, due_date, customer_id)
       OUTPUT INSERTED.id, INSERTED.title, INSERTED.description, INSERTED.status,
              CONVERT(varchar(10), INSERTED.due_date, 23) AS dueDate, INSERTED.customer_id AS customerId,
              INSERTED.created_at AS createdAt, INSERTED.updated_at AS updatedAt
       VALUES (@title, @description, @status, @dueDate, @customerId)`
    );
  return c.json(result.recordset[0], 201);
});

tasks.patch(
  "/:id",
  zValidator("param", taskIdParamSchema),
  zValidator("json", updateTaskSchema),
  async (c) => {
    const { id } = c.req.valid("param");
    const input = c.req.valid("json");
    if (Object.keys(input).length === 0) {
      return c.json({ error: "هیچ داده‌ای برای به‌روزرسانی ارسال نشده" }, 400);
    }
    const pool = await getPool();
    const request = pool.request().input("id", sql.Int, id);
    const setClauses: string[] = [];
    if (input.title !== undefined) {
      request.input("title", sql.NVarChar, input.title);
      setClauses.push("title = @title");
    }
    if (input.description !== undefined) {
      request.input("description", sql.NVarChar, input.description);
      setClauses.push("description = @description");
    }
    if (input.status !== undefined) {
      request.input("status", sql.NVarChar, input.status);
      setClauses.push("status = @status");
    }
    if (input.dueDate !== undefined) {
      request.input("dueDate", sql.Date, input.dueDate);
      setClauses.push("due_date = @dueDate");
    }
    if (input.customerId !== undefined) {
      request.input("customerId", sql.Int, input.customerId);
      setClauses.push("customer_id = @customerId");
    }
    setClauses.push("updated_at = SYSUTCDATETIME()");
    const result = await request.query(
      `UPDATE tasks SET ${setClauses.join(", ")}
       OUTPUT INSERTED.id, INSERTED.title, INSERTED.description, INSERTED.status,
              CONVERT(varchar(10), INSERTED.due_date, 23) AS dueDate, INSERTED.customer_id AS customerId,
              INSERTED.created_at AS createdAt, INSERTED.updated_at AS updatedAt
       WHERE id = @id`
    );
    const task = result.recordset[0];
    if (!task) return c.json({ error: "تسک یافت نشد" }, 404);
    return c.json(task);
  }
);

tasks.delete("/:id", zValidator("param", taskIdParamSchema), async (c) => {
  const { id } = c.req.valid("param");
  const pool = await getPool();
  const result = await pool
    .request()
    .input("id", sql.Int, id)
    .query("DELETE FROM tasks OUTPUT DELETED.id WHERE id = @id");
  if (result.recordset.length === 0) return c.json({ error: "تسک یافت نشد" }, 404);
  return c.body(null, 204);
});

export default tasks;
