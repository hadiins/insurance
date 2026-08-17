import { useEffect, useState } from "react";
import {
  Button,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  message,
} from "antd";
import { DeleteOutlined, EditOutlined, PlusOutlined } from "@ant-design/icons";
import type { ColumnsType } from "antd/es/table";
import { tasksApi, customersApi, type Task, type Customer, type TaskInput } from "../api/client";
import JalaliDatePicker from "../components/JalaliDatePicker";
import { toJalaliDisplay } from "../utils/jalali";

const statusLabels: Record<Task["status"], { text: string; color: string }> = {
  todo: { text: "برای انجام", color: "default" },
  in_progress: { text: "در حال انجام", color: "processing" },
  done: { text: "انجام‌شده", color: "success" },
};

export default function TasksPage() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingTask, setEditingTask] = useState<Task | null>(null);
  const [statusFilter, setStatusFilter] = useState<Task["status"]>();
  const [customerFilter, setCustomerFilter] = useState<number>();
  const [form] = Form.useForm<TaskInput>();

  const load = async (filters?: { status?: Task["status"]; customerId?: number }) => {
    setLoading(true);
    try {
      const [taskList, customerList] = await Promise.all([
        tasksApi.list(filters),
        customersApi.list(),
      ]);
      setTasks(taskList);
      setCustomers(customerList);
    } catch {
      message.error("خطا در دریافت اطلاعات");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load({ status: statusFilter, customerId: customerFilter });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter, customerFilter]);

  const openCreateModal = () => {
    setEditingTask(null);
    form.resetFields();
    setModalOpen(true);
  };

  const openEditModal = (task: Task) => {
    setEditingTask(task);
    form.setFieldsValue({
      title: task.title,
      description: task.description ?? undefined,
      status: task.status,
      dueDate: task.dueDate ?? undefined,
      customerId: task.customerId ?? undefined,
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      if (editingTask) {
        await tasksApi.update(editingTask.id, values);
        message.success("تسک به‌روزرسانی شد");
      } else {
        await tasksApi.create(values);
        message.success("تسک ایجاد شد");
      }
      setModalOpen(false);
      load({ status: statusFilter, customerId: customerFilter });
    } catch (err) {
      if (err instanceof Error) message.error("خطا در ذخیره تسک");
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await tasksApi.remove(id);
      message.success("تسک حذف شد");
      load({ status: statusFilter, customerId: customerFilter });
    } catch {
      message.error("خطا در حذف تسک");
    }
  };

  const columns: ColumnsType<Task> = [
    { title: "عنوان", dataIndex: "title" },
    { title: "توضیحات", dataIndex: "description", ellipsis: true },
    {
      title: "وضعیت",
      dataIndex: "status",
      render: (status: Task["status"]) => (
        <Tag color={statusLabels[status].color}>{statusLabels[status].text}</Tag>
      ),
    },
    {
      title: "مشتری",
      dataIndex: "customerId",
      render: (customerId: number | null) =>
        customers.find((c) => c.id === customerId)?.fullName ?? "-",
    },
    { title: "مهلت", dataIndex: "dueDate", render: (d: string | null) => toJalaliDisplay(d) },
    {
      title: "عملیات",
      render: (_, task) => (
        <Space>
          <Button icon={<EditOutlined />} size="small" onClick={() => openEditModal(task)} />
          <Popconfirm title="از حذف مطمئن هستید؟" onConfirm={() => handleDelete(task.id)}>
            <Button icon={<DeleteOutlined />} size="small" danger />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 16 }} wrap>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreateModal}>
          تسک جدید
        </Button>
        <Select
          placeholder="فیلتر وضعیت"
          allowClear
          style={{ width: 160 }}
          value={statusFilter}
          onChange={setStatusFilter}
          options={Object.entries(statusLabels).map(([value, { text }]) => ({
            value,
            label: text,
          }))}
        />
        <Select
          placeholder="فیلتر مشتری"
          allowClear
          style={{ width: 200 }}
          value={customerFilter}
          onChange={setCustomerFilter}
          options={customers.map((c) => ({ value: c.id, label: c.fullName }))}
        />
      </Space>
      <Table
        rowKey="id"
        columns={columns}
        dataSource={tasks}
        loading={loading}
        pagination={{ pageSize: 10 }}
      />
      <Modal
        title={editingTask ? "ویرایش تسک" : "تسک جدید"}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSubmit}
        okText="ذخیره"
        cancelText="انصراف"
      >
        <Form form={form} layout="vertical" initialValues={{ status: "todo" }}>
          <Form.Item name="title" label="عنوان" rules={[{ required: true, message: "عنوان الزامی است" }]}>
            <Input />
          </Form.Item>
          <Form.Item name="description" label="توضیحات">
            <Input.TextArea rows={3} />
          </Form.Item>
          <Form.Item name="status" label="وضعیت">
            <Select
              options={Object.entries(statusLabels).map(([value, { text }]) => ({
                value,
                label: text,
              }))}
            />
          </Form.Item>
          <Form.Item name="customerId" label="مشتری">
            <Select
              allowClear
              options={customers.map((c) => ({ value: c.id, label: c.fullName }))}
            />
          </Form.Item>
          <Form.Item name="dueDate" label="مهلت انجام">
            <JalaliDatePicker />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
