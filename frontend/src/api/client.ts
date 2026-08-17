import axios from "axios";

export const api = axios.create({
  baseURL: "/api",
});

const TOKEN_STORAGE_KEY = "auth_token";

export function getStoredToken(): string | null {
  return localStorage.getItem(TOKEN_STORAGE_KEY);
}

export function setStoredToken(token: string | null): void {
  if (token) localStorage.setItem(TOKEN_STORAGE_KEY, token);
  else localStorage.removeItem(TOKEN_STORAGE_KEY);
}

api.interceptors.request.use((config) => {
  const token = getStoredToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401 && getStoredToken()) {
      setStoredToken(null);
      localStorage.removeItem("auth_user");
      window.location.href = "/login";
    }
    return Promise.reject(error);
  }
);

export interface AuthUser {
  id: number;
  fullName: string;
  email: string;
}

export interface AuthResponse {
  token: string;
  user: AuthUser;
}

export const authApi = {
  login: (email: string, password: string) =>
    api.post<AuthResponse>("/auth/login", { email, password }).then((r) => r.data),
  register: (fullName: string, email: string, password: string) =>
    api.post<AuthResponse>("/auth/register", { fullName, email, password }).then((r) => r.data),
};

export type TaskStatus = "todo" | "in_progress" | "done";

export interface Task {
  id: number;
  title: string;
  description: string | null;
  status: TaskStatus;
  dueDate: string | null;
  customerId: number | null;
  createdAt: string;
  updatedAt: string;
}

export interface Customer {
  id: number;
  fullName: string;
  phone: string;
  email: string | null;
  address: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface TaskInput {
  title: string;
  description?: string;
  status?: TaskStatus;
  dueDate?: string;
  customerId?: number;
}

export interface CustomerInput {
  fullName: string;
  phone: string;
  email?: string;
  address?: string;
}

export interface TaskFilters {
  status?: TaskStatus;
  customerId?: number;
}

export const tasksApi = {
  list: (filters?: TaskFilters) =>
    api.get<Task[]>("/tasks", { params: filters }).then((r) => r.data),
  create: (input: TaskInput) => api.post<Task>("/tasks", input).then((r) => r.data),
  update: (id: number, input: Partial<TaskInput>) =>
    api.patch<Task>(`/tasks/${id}`, input).then((r) => r.data),
  remove: (id: number) => api.delete(`/tasks/${id}`),
};

export const customersApi = {
  list: () => api.get<Customer[]>("/customers").then((r) => r.data),
  create: (input: CustomerInput) => api.post<Customer>("/customers", input).then((r) => r.data),
  update: (id: number, input: Partial<CustomerInput>) =>
    api.patch<Customer>(`/customers/${id}`, input).then((r) => r.data),
  remove: (id: number) => api.delete(`/customers/${id}`),
};
