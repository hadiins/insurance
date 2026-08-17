import "dotenv/config";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";
import { getPool } from "./db.js";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

async function migrate() {
  const schemaPath = path.join(__dirname, "..", "sql", "schema.sql");
  const schema = readFileSync(schemaPath, "utf-8");
  const pool = await getPool();
  const batches = schema.split(/^GO$/im).map((b) => b.trim()).filter(Boolean);
  for (const batch of batches) {
    await pool.request().batch(batch);
  }
  console.log("Migration complete.");
  await pool.close();
}

migrate().catch((err) => {
  console.error("Migration failed:", err);
  process.exit(1);
});
