import { z } from "zod";

export const registerSchema = z.object({
  fullName: z.string().trim().min(1, "نام الزامی است").max(200),
  email: z.string().trim().email("ایمیل معتبر نیست"),
  password: z.string().min(8, "رمز عبور باید حداقل ۸ کاراکتر باشد").max(100),
});

export const loginSchema = z.object({
  email: z.string().trim().email("ایمیل معتبر نیست"),
  password: z.string().min(1, "رمز عبور الزامی است"),
});

export type RegisterInput = z.infer<typeof registerSchema>;
export type LoginInput = z.infer<typeof loginSchema>;
