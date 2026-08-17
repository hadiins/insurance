import { useEffect, useState } from "react";
import { Button, Form, Input, Modal, Popconfirm, Space, Table, message } from "antd";
import { DeleteOutlined, EditOutlined, PlusOutlined } from "@ant-design/icons";
import type { ColumnsType } from "antd/es/table";
import { customersApi, type Customer, type CustomerInput } from "../api/client";

export default function CustomersPage() {
  const [customers, setCustomers] = useState<Customer[]>([]);
  const [loading, setLoading] = useState(false);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingCustomer, setEditingCustomer] = useState<Customer | null>(null);
  const [form] = Form.useForm<CustomerInput>();

  const load = async () => {
    setLoading(true);
    try {
      setCustomers(await customersApi.list());
    } catch {
      message.error("خطا در دریافت مشتریان");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const openCreateModal = () => {
    setEditingCustomer(null);
    form.resetFields();
    setModalOpen(true);
  };

  const openEditModal = (customer: Customer) => {
    setEditingCustomer(customer);
    form.setFieldsValue({
      fullName: customer.fullName,
      phone: customer.phone,
      email: customer.email ?? undefined,
      address: customer.address ?? undefined,
    });
    setModalOpen(true);
  };

  const handleSubmit = async () => {
    try {
      const values = await form.validateFields();
      if (editingCustomer) {
        await customersApi.update(editingCustomer.id, values);
        message.success("مشتری به‌روزرسانی شد");
      } else {
        await customersApi.create(values);
        message.success("مشتری ایجاد شد");
      }
      setModalOpen(false);
      load();
    } catch (err) {
      if (err instanceof Error) message.error("خطا در ذخیره مشتری");
    }
  };

  const handleDelete = async (id: number) => {
    try {
      await customersApi.remove(id);
      message.success("مشتری حذف شد");
      load();
    } catch {
      message.error("خطا در حذف مشتری");
    }
  };

  const columns: ColumnsType<Customer> = [
    { title: "نام", dataIndex: "fullName" },
    { title: "تلفن", dataIndex: "phone" },
    { title: "ایمیل", dataIndex: "email", render: (v: string | null) => v ?? "-" },
    { title: "آدرس", dataIndex: "address", ellipsis: true, render: (v: string | null) => v ?? "-" },
    {
      title: "عملیات",
      render: (_, customer) => (
        <Space>
          <Button icon={<EditOutlined />} size="small" onClick={() => openEditModal(customer)} />
          <Popconfirm title="از حذف مطمئن هستید؟" onConfirm={() => handleDelete(customer.id)}>
            <Button icon={<DeleteOutlined />} size="small" danger />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <Space style={{ marginBottom: 16 }}>
        <Button type="primary" icon={<PlusOutlined />} onClick={openCreateModal}>
          مشتری جدید
        </Button>
      </Space>
      <Table
        rowKey="id"
        columns={columns}
        dataSource={customers}
        loading={loading}
        pagination={{ pageSize: 10 }}
      />
      <Modal
        title={editingCustomer ? "ویرایش مشتری" : "مشتری جدید"}
        open={modalOpen}
        onCancel={() => setModalOpen(false)}
        onOk={handleSubmit}
        okText="ذخیره"
        cancelText="انصراف"
      >
        <Form form={form} layout="vertical">
          <Form.Item
            name="fullName"
            label="نام کامل"
            rules={[{ required: true, message: "نام الزامی است" }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="phone"
            label="تلفن همراه"
            rules={[
              { required: true, message: "شماره تلفن الزامی است" },
              { pattern: /^09\d{9}$/, message: "شماره موبایل معتبر نیست" },
            ]}
          >
            <Input dir="ltr" />
          </Form.Item>
          <Form.Item
            name="email"
            label="ایمیل"
            rules={[{ type: "email", message: "ایمیل معتبر نیست" }]}
          >
            <Input dir="ltr" />
          </Form.Item>
          <Form.Item name="address" label="آدرس">
            <Input.TextArea rows={2} />
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
}
