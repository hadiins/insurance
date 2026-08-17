import { z } from "zod";

export const createCustomerSchema = z.object({
  fullName: z.string().trim().min(1, "نام الزامی است").max(200),
  phone: z
    .string()
    .trim()
    .regex(/^09\d{9}$/, "شماره موبایل معتبر نیست"),
  email: z.string().trim().email("ایمیل معتبر نیست").optional().or(z.literal("")),
  address: z.string().trim().max(500).optional(),
});

export const updateCustomerSchema = createCustomerSchema.partial();

export const customerIdParamSchema = z.object({
  id: z.coerce.number().int().positive(),
});

export type CreateCustomerInput = z.infer<typeof createCustomerSchema>;
export type UpdateCustomerInput = z.infer<typeof updateCustomerSchema>;
