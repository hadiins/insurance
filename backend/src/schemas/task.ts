import { z } from "zod";

export const taskStatusSchema = z.enum(["todo", "in_progress", "done"]);

export const createTaskSchema = z.object({
  title: z.string().trim().min(1, "عنوان الزامی است").max(200),
  description: z.string().trim().max(1000).optional(),
  status: taskStatusSchema.default("todo"),
  dueDate: z.string().date().optional(),
  customerId: z.number().int().positive().optional(),
});

export const updateTaskSchema = createTaskSchema.partial();

export const taskIdParamSchema = z.object({
  id: z.coerce.number().int().positive(),
});

export const listTasksQuerySchema = z.object({
  status: taskStatusSchema.optional(),
  customerId: z.coerce.number().int().positive().optional(),
});

export type CreateTaskInput = z.infer<typeof createTaskSchema>;
export type UpdateTaskInput = z.infer<typeof updateTaskSchema>;
