import axios from "axios";

export const api = axios.create({
  baseURL: "/api",
});

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
