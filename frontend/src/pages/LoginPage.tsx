import { useState } from "react";
import { Button, Card, Form, Input, Segmented, Typography, message } from "antd";
import { useNavigate } from "react-router-dom";
import { useAuth } from "../auth/AuthContext";

interface FormValues {
  fullName?: string;
  email: string;
  password: string;
}

export default function LoginPage() {
  const [mode, setMode] = useState<"login" | "register">("login");
  const [submitting, setSubmitting] = useState(false);
  const { login, register } = useAuth();
  const navigate = useNavigate();

  const handleFinish = async (values: FormValues) => {
    setSubmitting(true);
    try {
      if (mode === "login") {
        await login(values.email, values.password);
      } else {
        await register(values.fullName ?? "", values.email, values.password);
      }
      navigate("/");
    } catch (err) {
      const errMessage =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        "خطا در ورود";
      message.error(errMessage);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div
      style={{
        minHeight: "100vh",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        background: "#f5f5f5",
      }}
    >
      <Card style={{ width: 380 }}>
        <Typography.Title level={3} style={{ textAlign: "center", marginBottom: 24 }}>
          مدیریت وظایف و مشتریان
        </Typography.Title>
        <Segmented
          block
          value={mode}
          onChange={(v) => setMode(v as "login" | "register")}
          options={[
            { label: "ورود", value: "login" },
            { label: "ثبت‌نام", value: "register" },
          ]}
          style={{ marginBottom: 24 }}
        />
        <Form layout="vertical" onFinish={handleFinish}>
          {mode === "register" && (
            <Form.Item
              name="fullName"
              label="نام کامل"
              rules={[{ required: true, message: "نام الزامی است" }]}
            >
              <Input />
            </Form.Item>
          )}
          <Form.Item
            name="email"
            label="ایمیل"
            rules={[{ required: true, type: "email", message: "ایمیل معتبر نیست" }]}
          >
            <Input dir="ltr" />
          </Form.Item>
          <Form.Item
            name="password"
            label="رمز عبور"
            rules={[
              { required: true, message: "رمز عبور الزامی است" },
              ...(mode === "register"
                ? [{ min: 8, message: "رمز عبور باید حداقل ۸ کاراکتر باشد" }]
                : []),
            ]}
          >
            <Input.Password dir="ltr" />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" block loading={submitting}>
              {mode === "login" ? "ورود" : "ثبت‌نام"}
            </Button>
          </Form.Item>
        </Form>
      </Card>
    </div>
  );
}
